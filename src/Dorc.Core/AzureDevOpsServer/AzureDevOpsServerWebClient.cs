using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.Core.Models;
using Dorc.Core.Security;
using Microsoft.Extensions.Logging;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Dorc.AzureDevOps.Client;
using Dorc.AzureDevOps.Client.Auth;
using Org.OpenAPITools.Model;

namespace Dorc.Core.AzureDevOpsServer
{
    public class AzureDevOpsServerWebClient : IAzureDevOpsServerWebClient
    {
        private readonly string _serverUrl;
        private readonly string _configuredAzureEndpointUrl;
        private readonly ILogger _log;
        private readonly Func<bool, HttpMessageHandler>? _httpHandlerFactory;
        private const string ApiVersion = "6.0";

        // Build-filtering patterns come from project configuration
        // (ArtefactsBuildRegex and the caller-supplied filter list), so a
        // catastrophically backtracking pattern is a misconfiguration rather
        // than an attack — but without a bound it still pins a request thread
        // indefinitely against a large build list. Matching fails fast and
        // diagnosably instead.
        private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(5);
        private readonly IAuthTokenGenerator _authTokenGenerator;
        private static readonly IConfigurationSection AppSettings = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build()
            .GetSection("AppSettings");

        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The Tenant is the name or Id of the Azure AD tenant in which this application is registered.
        // The Scopes are roles set within the App registration.

        private static string tenant = AppSettings["AadTenant"];
        private static string clientId = AppSettings["AadClientId"];
        private static string secret = AppSettings["AadSecret"];
        private static string[] scopes = { AppSettings["AadScopes"] };
        private static string azureEndpointUrl = AppSettings["AzureEndpoint"] ?? "dev.azure.com";

        private readonly ISourceHostAllowList _sourceHosts;

        public AzureDevOpsServerWebClient(
            string serverUrl,
            ILogger<AzureDevOpsServerWebClient> log,
            ISourceHostAllowList sourceHosts)
            : this(
                serverUrl,
                azureEndpointUrl,
                AuthTokenGeneratorFactory.GetAuthTokenGenerator(new AadConnectionSettings(clientId, scopes, secret, tenant)),
                log,
                sourceHosts,
                null)
        {
        }

        internal AzureDevOpsServerWebClient(
            string serverUrl,
            string configuredAzureEndpointUrl,
            IAuthTokenGenerator authTokenGenerator,
            ILogger log,
            ISourceHostAllowList sourceHosts,
            Func<bool, HttpMessageHandler>? httpHandlerFactory = null)
        {
            _log = log;
            _serverUrl = serverUrl;
            _configuredAzureEndpointUrl = configuredAzureEndpointUrl;
            _sourceHosts = sourceHosts ?? throw new ArgumentNullException(nameof(sourceHosts));
            _authTokenGenerator = authTokenGenerator ?? throw new ArgumentNullException(nameof(authTokenGenerator));
            _httpHandlerFactory = httpHandlerFactory;
        }

        public List<BuildDefinitionReference> GetBuildDefinitionsForProjects(string collection, string adosProjects, string projectRegex)
        {
            var coll = GetAzureOrgAndUrl(collection, out var azureEndpoint);

            var (config, apiClient) = ConnectTo(azureEndpoint);
            var instance = new DefinitionsApi(apiClient, apiClient, config);

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

                var regexs = new Regex(projectRegex, RegexOptions.IgnoreCase, RegexMatchTimeout);

                output.AddRange(buildDefinitions.Where(x => regexs.IsMatch(x.Name)));
                buildDefinitions.Clear();
            }

            return output;
        }

        /// <summary>
        /// Builds the configuration and API client for one Azure DevOps endpoint.
        ///
        /// The endpoint is derived from project configuration, so credentials are selected only
        /// after comparing parsed hosts and applying the source-host allow-list. The client also
        /// carries the converter that unwraps Azure DevOps's count/value list envelope.
        /// </summary>
        private (Org.OpenAPITools.Client.Configuration Configuration, ApiClient Client) ConnectTo(
           string azureEndpoint)
        {
           Org.OpenAPITools.Client.Configuration configuration;

           if (IsConfiguredAzureEndpoint(azureEndpoint))
           {
               RequireHttpsAzureEndpoint(azureEndpoint);
               configuration = new Org.OpenAPITools.Client.Configuration
               {
                   BasePath = azureEndpoint,
                   AccessToken = _authTokenGenerator.GetToken()
               };
           }
           else
           {
               RequireDefaultCredentialsPermitted(azureEndpoint);
               configuration = new Org.OpenAPITools.Client.Configuration
               {
                   BasePath = azureEndpoint,
                   UseDefaultCredentials = true,
               };
           }

           return (configuration, AzureDevOpsApiClientFactory.Create(configuration.BasePath, _httpHandlerFactory));
        }

        internal bool IsConfiguredAzureEndpoint(string azureEndpoint)
        {
            // AzureEndpoint is configured as a bare host ("dev.azure.com") by default but may be
            // written as a URL, so it is reduced to a host before comparing. Subdomains of the
            // configured host are the same service reached through another name -
            // myorg.visualstudio.com, vsrm.dev.azure.com, pkgs.dev.azure.com - and take the same
            // AAD token, so the comparison is host-or-subdomain on the parsed authority, never
            // a substring of the URL text.
            var configured = SourceHost.Of(_configuredAzureEndpointUrl) ?? _configuredAzureEndpointUrl?.Trim();
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            if (SourceHost.Is(azureEndpoint, configured))
            {
                return true;
            }

            return string.Equals(configured, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
                && SourceHost.Is(azureEndpoint, "visualstudio.com");
        }

        private static void RequireHttpsAzureEndpoint(string azureEndpoint)
        {
            if (!Uri.TryCreate(azureEndpoint, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to send Azure DevOps bearer token to '{azureEndpoint}': HTTPS is required.");
            }
        }

        private void RequireDefaultCredentialsPermitted(string azureEndpoint)
        {
            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials because no artefact host "
                    + $"allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
            }

            var decision = _sourceHosts.CheckArtefactSource(azureEndpoint);
            if (decision == null || !decision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials to '{azureEndpoint}', because {decision?.Reason ?? "host is not permitted"}");
            }
        }

        private static void RequireSameOriginWithConfiguredEndpoint(string candidateUrl, string configuredOriginUrl)
        {
            if (!SourceHost.HasSameOrigin(candidateUrl, configuredOriginUrl))
            {
                throw new InvalidOperationException(
                    $"Refusing to present default Windows credentials to '{candidateUrl}': origin '{SourceHost.OriginOf(candidateUrl) ?? "<none>"}' does not match configured endpoint origin '{SourceHost.OriginOf(configuredOriginUrl) ?? "<none>"}'.");
            }
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

            var (config, apiClient) = ConnectTo(azureEndpoint);
            var instance = new BuildsApi(apiClient, apiClient, config);

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

            var (config, apiClient) = ConnectTo(azureEndpoint);
            var instance = new BuildsApi(apiClient, apiClient, config);

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
            var regexes = regexList.Select(x => new Regex(x, RegexOptions.None, RegexMatchTimeout)).ToArray();

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
            var (config, apiClient) = ConnectTo(azureEndpoint);
            var instance = new ArtifactsApi(apiClient, apiClient, config);

            string apiVersion = ApiVersion;
            return instance.ArtifactsList(coll, project, buildId, apiVersion);
        }

        public int ExtractBuildId(string buildUri)
        {
            var uri = new Uri(buildUri);
            return Convert.ToInt32(uri.LocalPath.Split('/').Last());
        }

        public async Task<string?> TryReadArtifactFileTextAsync(string artifactDownloadUrl, string fileName)
        {
            if (string.IsNullOrWhiteSpace(artifactDownloadUrl) || string.IsNullOrWhiteSpace(fileName))
                return null;

            var sanitizedFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(sanitizedFileName))
                return null;

            if (!artifactDownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                (artifactDownloadUrl.StartsWith("file", StringComparison.OrdinalIgnoreCase) ||
                 SourceHost.IsRemoteOrUnc(artifactDownloadUrl)))
            {
                if (artifactDownloadUrl.Contains("..", StringComparison.Ordinal) ||
                    fileName.Contains("..", StringComparison.Ordinal) ||
                    fileName.Contains('/', StringComparison.Ordinal) ||
                    fileName.Contains('\\', StringComparison.Ordinal))
                {
                    return null;
                }

                if (SourceHost.IsRemoteOrUnc(artifactDownloadUrl))
                {
                    if (!SourceHost.TryExtractRemoteHost(artifactDownloadUrl, out _))
                    {
                        throw new InvalidOperationException(
                            $"Refusing to present default Windows credentials to '{artifactDownloadUrl}': remote or UNC authority could not be verified.");
                    }

                    RequireDefaultCredentialsPermitted(artifactDownloadUrl);
                }

                var localPath = Security.PathConfinement.Canonicalise(artifactDownloadUrl);
                if (string.IsNullOrWhiteSpace(localPath) || localPath.Contains("..", StringComparison.Ordinal))
                    return null;

                var targetPath = localPath.EndsWith($"\\{sanitizedFileName}", StringComparison.OrdinalIgnoreCase)
                    ? localPath
                    : Path.Combine(localPath, sanitizedFileName);

                var dirInfo = new DirectoryInfo(Path.GetDirectoryName(targetPath) ?? string.Empty);
                if (!dirInfo.Exists)
                    return null;

                var matchingFile = dirInfo.EnumerateFiles(sanitizedFileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (matchingFile == null)
                    return null;

                using var reader = matchingFile.OpenText();
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (!Uri.TryCreate(artifactDownloadUrl, UriKind.Absolute, out var downloadUri) ||
                (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var isCandidateCloudAdo = IsConfiguredAzureEndpoint(artifactDownloadUrl);
            if (isCandidateCloudAdo && (string.IsNullOrWhiteSpace(_serverUrl) || SourceHost.OriginOf(_serverUrl) == null))
            {
                throw new InvalidOperationException(
                    $"Refusing to send Azure DevOps bearer token to '{artifactDownloadUrl}': configured endpoint origin '{_serverUrl ?? "<none>"}' is missing or invalid.");
            }

            var isCloudAdo = isCandidateCloudAdo && IsConfiguredAzureEndpoint(_serverUrl);
            AuthenticationHeaderValue? authorization = null;
            Action<Uri>? onRedirect = null;

            if (isCloudAdo)
            {
                RequireHttpsAzureEndpoint(_serverUrl);
                RequireHttpsAzureEndpoint(artifactDownloadUrl);
                var token = _authTokenGenerator.GetToken();
                if (!string.IsNullOrEmpty(token))
                {
                    authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
            else
            {
                if (_sourceHosts.IsArtefactSourceUnconfigured)
                {
                    throw new InvalidOperationException(
                        $"Refusing to present default Windows credentials because no artefact host "
                        + $"allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
                }

                if (string.IsNullOrWhiteSpace(_serverUrl) || SourceHost.OriginOf(_serverUrl) == null)
                {
                    throw new InvalidOperationException(
                        $"Refusing to present default Windows credentials to '{artifactDownloadUrl}': configured endpoint origin '{_serverUrl ?? "<none>"}' is missing or invalid.");
                }

                RequireDefaultCredentialsPermitted(_serverUrl);
                RequireDefaultCredentialsPermitted(artifactDownloadUrl);
                RequireSameOriginWithConfiguredEndpoint(artifactDownloadUrl, _serverUrl);
                onRedirect = nextUri => RequireSameOriginWithConfiguredEndpoint(nextUri.AbsoluteUri, _serverUrl);
            }

            var handler = _httpHandlerFactory != null
                ? _httpHandlerFactory(!isCloudAdo)
                : new HttpClientHandler
                {
                    UseDefaultCredentials = !isCloudAdo,
                    AllowAutoRedirect = false
                };

            using var httpClient = new HttpClient(handler, disposeHandler: _httpHandlerFactory == null);
            using var response = await OriginBoundHttpSender.GetAsync(
                httpClient,
                downloadUri,
                authorization,
                HttpCompletionOption.ResponseHeadersRead,
                onRedirect: onRedirect).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Failed to download Azure DevOps artifact (HTTP {StatusCode}).", (int)response.StatusCode);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream).ConfigureAwait(false);
            memoryStream.Position = 0;

            return TryExtractTextFromZipOrRawStream(memoryStream, sanitizedFileName);
        }

        internal static string? TryExtractTextFromZipOrRawStream(Stream stream, string targetFileName)
        {
            const long maxEntryBytes = 1024 * 1024; // 1 MB guard for bundle metadata files

            try
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, targetFileName, StringComparison.OrdinalIgnoreCase));

                if (entry == null || entry.Length > maxEntryBytes)
                    return null;

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                // Not a zip archive — ignore unless caller explicitly pointed at a raw file URL.
                return null;
            }
        }
    }
}