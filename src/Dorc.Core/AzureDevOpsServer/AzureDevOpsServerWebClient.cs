using System.Net.Http.Headers;
using System.Text.Json;
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
        private readonly ILogger _log;
        private const string ApiVersion = "6.0";
        private readonly IAuthTokenGenerator _authTokenGenerator;
        private static readonly IConfigurationSection AppSettings = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings");

        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The Tenant is the name or Id of the Azure AD tenant in which this application is registered.
        // The Scopes are roles set within the App registration.

        private static string tenant = AppSettings["AadTenant"];
        private static string clientId = AppSettings["AadClientId"];
        private static string secret = AppSettings["AadSecret"];
        private static string[] scopes = { AppSettings["AadScopes"] };
        private static string azureEndpointUrl = AppSettings["AzureEndpoint"] ?? "dev.azure.com";
        

        public AzureDevOpsServerWebClient(string serverUrl, ILogger<AzureDevOpsServerWebClient> log)
        {
            var aadConnectionSettings = new AadConnectionSettings(clientId, scopes, secret, tenant);
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

            var instance = new ArtifactsApi(config);

            string apiVersion = ApiVersion;
            return instance.ArtifactsList(coll, project, buildId, apiVersion);
        }

        public int ExtractBuildId(string buildUri)
        {
            var uri = new Uri(buildUri);
            return Convert.ToInt32(uri.LocalPath.Split('/').Last());
        }

        /// <inheritdoc />
        public async Task<string?> GetFileFromRepoAsync(string collection, string adoProjects, params string[] candidateFileNames)
        {
            if (candidateFileNames == null || candidateFileNames.Length == 0)
            {
                _log.LogWarning("No candidate file names provided for GetFileFromRepoAsync");
                return null;
            }

            var org = GetAzureOrgAndUrl(collection, out var azureEndpoint);

            // Determine auth: OAuth for cloud, default credentials for on-prem
            var useOAuth = azureEndpoint.Contains(azureEndpointUrl);

            using var httpClient = new HttpClient();
            if (useOAuth)
            {
                var token = _authTokenGenerator.GetToken();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _log.LogWarning("Git file fetch is only supported for Azure DevOps Services (cloud). " +
                    "On-prem TFS/Azure DevOps Server is not supported for cr-inputs.json auto-fetch.");
                return null;
            }

            var projects = adoProjects.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var project in projects)
            {
                try
                {
                    // List all Git repositories in this ADO project
                    var reposUrl = $"{azureEndpoint}{org}/{project}/_apis/git/repositories?api-version={ApiVersion}";
                    _log.LogDebug("Listing Git repos at {Url}", reposUrl);

                    var reposResponse = await httpClient.GetAsync(reposUrl);
                    if (!reposResponse.IsSuccessStatusCode)
                    {
                        _log.LogWarning("Failed to list Git repos for project {Project}: {Status}",
                            project, reposResponse.StatusCode);
                        continue;
                    }

                    var reposJson = await reposResponse.Content.ReadAsStringAsync();
                    var reposResult = JsonSerializer.Deserialize<AdoRepoListResponse>(reposJson, jsonOptions);

                    if (reposResult?.Value == null || reposResult.Value.Count == 0)
                    {
                        _log.LogDebug("No Git repos found in project {Project}", project);
                        continue;
                    }

                    // For each repo, get the full file tree and search for matching file names
                    foreach (var repo in reposResult.Value)
                    {
                        try
                        {
                            var treeUrl = $"{azureEndpoint}{org}/{project}/_apis/git/repositories/{repo.Id}" +
                                          $"/items?recursionLevel=Full&api-version={ApiVersion}";
                            _log.LogDebug("Listing full tree for repo {RepoName} in project {Project}", repo.Name, project);

                            var treeResponse = await httpClient.GetAsync(treeUrl);
                            if (!treeResponse.IsSuccessStatusCode)
                            {
                                _log.LogDebug("Failed to list tree for repo {RepoName}: {Status}",
                                    repo.Name, treeResponse.StatusCode);
                                continue;
                            }

                            var treeJson = await treeResponse.Content.ReadAsStringAsync();
                            var treeResult = JsonSerializer.Deserialize<AdoItemsListResponse>(treeJson, jsonOptions);
                            if (treeResult?.Value == null || treeResult.Value.Count == 0)
                                continue;

                            // Find all items whose file name matches a candidate, grouped by priority
                            // candidateFileNames[0] has highest priority (e.g. "cr-inputs-new.json")
                            foreach (var candidateName in candidateFileNames)
                            {
                                var match = treeResult.Value.FirstOrDefault(item =>
                                    !item.IsFolder &&
                                    item.Path.EndsWith("/" + candidateName, StringComparison.OrdinalIgnoreCase));

                                if (match == null)
                                    continue;

                                // Found a match — fetch the file content
                                var itemUrl = $"{azureEndpoint}{org}/{project}/_apis/git/repositories/{repo.Id}/items" +
                                              $"?path={Uri.EscapeDataString(match.Path)}&api-version={ApiVersion}";
                                _log.LogDebug("Fetching {FilePath} from repo {RepoName}", match.Path, repo.Name);

                                var itemResponse = await httpClient.GetAsync(itemUrl);
                                if (itemResponse.IsSuccessStatusCode)
                                {
                                    var content = await itemResponse.Content.ReadAsStringAsync();
                                    _log.LogInformation("Found {FileName} at {FilePath} in repo {RepoName} of project {Project}",
                                        candidateName, match.Path, repo.Name, project);
                                    return content;
                                }

                                _log.LogWarning("File {FilePath} listed in tree but fetch failed: {Status}",
                                    match.Path, itemResponse.StatusCode);
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogDebug(ex, "Error searching tree for repo {RepoName}", repo.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Error searching for files in ADO project {Project}", project);
                }
            }

            _log.LogInformation("None of [{CandidateNames}] found in any repo across ADO projects: {Projects}",
                string.Join(", ", candidateFileNames), adoProjects);
            return null;
        }

        /// <summary>
        /// Response model for ADO Git Repositories List API
        /// </summary>
        private class AdoRepoListResponse
        {
            public List<AdoRepo> Value { get; set; } = new();
            public int Count { get; set; }
        }

        private class AdoRepo
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string DefaultBranch { get; set; } = string.Empty;
        }

        /// <summary>
        /// Response model for ADO Git Items List API (recursionLevel=Full)
        /// </summary>
        private class AdoItemsListResponse
        {
            public List<AdoItem> Value { get; set; } = new();
            public int Count { get; set; }
        }

        private class AdoItem
        {
            public string Path { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
        }
    }
}