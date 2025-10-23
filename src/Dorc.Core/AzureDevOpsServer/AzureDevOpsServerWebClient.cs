using System.Text.RegularExpressions;
using Dorc.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client.Auth;
using Org.OpenAPITools.Model;

namespace Dorc.Core.AzureDevOpsServer
{
    public class AzureDevOpsServerWebClient : IAzureDevOpsServerWebClient
    {
        private readonly string _serverUrl;
        private readonly ILogger<AzureDevOpsServerWebClient> _log;
        private const string ApiVersion = "6.0";
        private readonly IAuthTokenGenerator _authTokenGenerator;
        private static readonly IConfigurationSection AppSettings = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings");

        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The Tenant is the name or Id of the Azure AD tenant in which this application is registered.
        // The AAD Instance is the instance of Azure, for example public Azure or Azure China.
        // The Authority is the sign-in URL of the tenant.
        // The Scopes are roles set within the App registration.

        private static string aadInstance = AppSettings["AadInstance"];
        private static string tenant = AppSettings["AadTenant"];
        private static string clientId = AppSettings["AadClientId"];
        private static string secret = AppSettings["AadSecret"];
        private static string azureDevOpsOrganizationUrl = AppSettings["AadAdosOrgUrl"];
        private static string[] scopes = { AppSettings["AadScopes"] };
        private static string azureEndpointUrl = AppSettings["AzureEndpoint"];
        

        public AzureDevOpsServerWebClient(string serverUrl, ILogger<AzureDevOpsServerWebClient> log)
        {
            var aadConnectionSettings = new AadConnectionSettings(clientId, aadInstance, azureDevOpsOrganizationUrl, scopes, secret, tenant);
            _log = log;
            _serverUrl = serverUrl;
            // Ideally for speed of queries we only want to retrieve the connection settings once at instantiation time.
            _authTokenGenerator = AuthTokenGeneratorFactory.GetAuthTokenGenerator(aadConnectionSettings);
        }

        public List<BuildDefinitionReference> GetBuildDefinitionsForProjects(string collection, string adosProjects, string projectRegex)
        {
            var coll = GetAzureOrgAndUrl(collection, out var azureEndpoint);

            Org.OpenAPITools.Client.Configuration config;

            if (azureEndpoint.Contains(azureEndpointUrl))
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    AccessToken = _authTokenGenerator.GetToken()
                };
            }
            else
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    UseDefaultCredentials = true,
                };
            }

            var instance = new DefinitionsApi(config);

            var projects = adosProjects.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            var output = new List<BuildDefinitionReference>();

            var buildDefinitions = new List<BuildDefinitionReference>();

            foreach (var project in projects)
            {
                string? continuationToken = null;
                do
                {
                    var response = instance.DefinitionsListWithHttpInfoAsync(coll, project, ApiVersion, continuationToken: continuationToken, includeLatestBuilds: true);

                    foreach (var header in response.Result.Headers)
                    {
                        if (header.Key.Equals("X-MS-ContinuationToken", StringComparison.OrdinalIgnoreCase))
                        {
                            continuationToken = header.Value.First();
                            break;
                        }
                    }

                    buildDefinitions.AddRange(response.Result.Data);

                } while (!string.IsNullOrEmpty(continuationToken));

                var regexs = new Regex(projectRegex, RegexOptions.IgnoreCase);

                output.AddRange(buildDefinitions.Where(x => regexs.IsMatch(x.Name)));
                buildDefinitions.Clear();
            }

            return output;
        }

        private static bool IsBuildDefinitionCompletedSuccessfully(BuildDefinitionReference buildDefinition)
        {
            var latestBuild = buildDefinition.LatestBuild;
            if (latestBuild == null)
                return true;
            return latestBuild.Status == Build.StatusEnum.Completed &&
                (latestBuild.Result == Build.ResultEnum.Succeeded || latestBuild.Result == Build.ResultEnum.PartiallySucceeded);
        }

        private static string GetAzureOrgAndUrl(string collection, out string azureEndpoint)
        {
            if (collection.EndsWith("/"))
            {
                collection = collection.Remove(collection.Length - 1);
            }

            var lastIndexOf = collection.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase);
            var coll = collection.Substring(lastIndexOf + 1);
            azureEndpoint = collection.Substring(0, lastIndexOf + 1);
            return coll;
        }

        public async Task<List<Build>> GetBuildsFromDefinitionsAsync(string collection, List<BuildDefinitionReference> buildDefinitions, int requestSize = 10)
        {
            var coll = GetAzureOrgAndUrl(collection, out var azureEndpoint);

            Org.OpenAPITools.Client.Configuration config;

            if (azureEndpoint.Contains(azureEndpointUrl))
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    AccessToken = _authTokenGenerator.GetToken()
                };
            }
            else
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    UseDefaultCredentials = true,
                };
            }

            var instance = new BuildsApi(config);

            var projects = buildDefinitions.Select(def => def.Project.Name).Distinct().ToList();

            var builds = new List<Build>();

            foreach (var project in projects)
            {
                var defsForProject = buildDefinitions.Where(def => def.Project.Name.Equals(project));
                var defIds = defsForProject.Select(d => d.Id);

                string? continuationToken = null;
                do
                {
                    var response = instance.BuildsListWithHttpInfoAsync(coll, project, ApiVersion,
                        definitions: string.Join(",", defIds),
                        continuationToken: continuationToken);

                    foreach (var header in response.Result.Headers)
                    {
                        if (header.Key.Equals("X-MS-ContinuationToken", StringComparison.OrdinalIgnoreCase))
                        {
                            continuationToken = header.Value.First();
                            break;
                        }
                    }

                    builds.AddRange(response.Result.Data);

                } while (!string.IsNullOrEmpty(continuationToken));
            }
            return builds;
        }


        public async Task<List<Build>> GetBuildsFromBuildNumberAsync(string collection, string buildNumber, string projectName, int requestSize = 10)
        {
            var coll = GetAzureOrgAndUrl(collection, out var azureEndpoint);

            Org.OpenAPITools.Client.Configuration config;

            if (azureEndpoint.Contains(azureEndpointUrl))
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    AccessToken = _authTokenGenerator.GetToken()
                };
            }
            else
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    UseDefaultCredentials = true,
                };
            }

            var instance = new BuildsApi(config);

            var builds = new List<Build>();

            var buildStatusResultFilters = new List<BuildApiFilter>
                {
                    new BuildApiFilter(null, Build.ResultEnum.Succeeded ),
                    new BuildApiFilter(null, Build.ResultEnum.PartiallySucceeded),
                    new BuildApiFilter(Build.StatusEnum.InProgress, null)
                };

            foreach (var filter in buildStatusResultFilters)
            {
                string? continuationToken = null;
                do
                {
                    var response = instance.BuildsListWithHttpInfoAsync(coll, projectName, ApiVersion,
                        buildNumber: buildNumber + "*",
                        statusFilter: filter.Status != null ? filter.Status.ToString() : null,
                        resultFilter: filter.Result != null ? filter.Result.ToString() : null,
                        continuationToken: continuationToken);

                    foreach (var header in response.Result.Headers)
                    {
                        if (header.Key.Equals("X-MS-ContinuationToken", StringComparison.OrdinalIgnoreCase))
                        {
                            continuationToken = header.Value.First();
                            break;
                        }
                    }

                    builds.AddRange(response.Result.Data);

                } while (!string.IsNullOrEmpty(continuationToken));
            }
            return builds;
        }

        public List<Build> FilterBuildsByRegex(List<string> regexList, List<Build> buildsForProject)
        {
            var regexes = regexList.Select(x => new Regex(x)).ToArray();

            var buildNames = buildsForProject.Select(x => x.BuildNumber).Where(x => regexes.Any(r => r.IsMatch(x)));

            var buildValues = buildsForProject.AsParallel().Where(x => buildNames.Contains(x.BuildNumber));

            var buildValuesList = buildValues.ToList();
            return buildValuesList;
        }

        public List<BuildArtifact> GetBuildArtifacts(string collection, string project, string buildUri)
        {
            return GetBuildArtifacts(collection, project, ExtractBuildId(buildUri));
        }

        public List<BuildArtifact> GetBuildArtifacts(string collection, string project, int buildId)
        {
            var coll = GetAzureOrgAndUrl(collection, out var azureEndpoint);
            Org.OpenAPITools.Client.Configuration config;

            if (azureEndpoint.Contains("dev.azure.com"))
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    AccessToken = _authTokenGenerator.GetToken()
                };
            }
            else
            {
                config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = azureEndpoint,
                    UseDefaultCredentials = true,
                };
            }

            var instance = new ArtifactsApi(config);

            string apiVersion = ApiVersion;
            return instance.ArtifactsList(coll, project, buildId, apiVersion);
        }

        public int ExtractBuildId(string buildUri)
        {
            var uri = new Uri(buildUri);
            return Convert.ToInt32(uri.LocalPath.Split('/').Last());
        }
    }
}