using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dorc.AzureDevOps.Client.Auth;

namespace Dorc.Monitor.TerraformSourceConfig
{
    /// <summary>
    /// Configures ScriptGroup with Terraform source-specific information
    /// </summary>
    public class TerraformSourceConfigurator
    {
        private const string TerraformGitPatPropertyName = "Terraform_Git_PAT";
        
        private readonly ILogger _logger;
        private readonly IConfigurationSettings _configurationSettings;
        private readonly IGitHubHostValidator _gitHubHostValidator;

        // Read directly rather than injected: this type is constructed with `new` by both
        // dispatchers, and the allow-list is process-wide configuration rather than per-request
        // state.
        private readonly ISourceHostAllowList _sourceHosts;

        public TerraformSourceConfigurator(ILogger logger, IConfigurationSettings configurationSettings,
            IGitHubHostValidator gitHubHostValidator,
            ISourceHostAllowList? sourceHosts = null)
        {
            _logger = logger;
            _configurationSettings = configurationSettings;
            _gitHubHostValidator = gitHubHostValidator;
            _sourceHosts = sourceHosts ?? new SourceHostAllowList(
                new ConfigurationBuilder().AddJsonFile("appsettings.json").Build());
        }

        public void ConfigureScriptGroup(
            ScriptGroup scriptGroup,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project,
            IDictionary<string, VariableValue> properties)
        {
            scriptGroup.TerraformSourceType = component.TerraformSourceType;
            scriptGroup.TerraformGitBranch = component.TerraformGitBranch ?? "main";
            scriptGroup.TerraformSubPath = component.TerraformSubPath ?? string.Empty;

            switch (component.TerraformSourceType)
            {
                case TerraformSourceType.Git:
                    ConfigureGitSource(scriptGroup, project, properties);
                    break;

                case TerraformSourceType.AzureArtifact:
                    ConfigureAzureArtifactSource(scriptGroup, request, project);
                    break;

                case TerraformSourceType.GitHubArtifact:
                    ConfigureGitHubArtifactSource(scriptGroup, request, project);
                    break;

                case TerraformSourceType.SharedFolder:
                    // No additional configuration needed for shared folder
                    break;

                default:
                    _logger.LogWarning($"Unknown Terraform source type: {component.TerraformSourceType}");
                    break;
            }
        }

        private void ConfigureGitSource(
            ScriptGroup scriptGroup,
            ProjectApiModel? project,
            IDictionary<string, VariableValue> properties)
        {
            if (project == null)
            {
                _logger.LogWarning("Project information not available for Git source configuration");
                return;
            }

            var repositoryUrl = project.TerraformGitRepoUrl;

            if (!MayReceiveSourceCredentials(repositoryUrl))
            {
                return;
            }

            scriptGroup.TerraformGitRepoUrl = repositoryUrl;

            if (IsAzureDevOpsRepository(repositoryUrl))
            {
                scriptGroup.AzureBearerToken = GetAzureBearerToken();
            }

            // Get PAT token from environment properties
            if (properties.TryGetValue(TerraformGitPatPropertyName, out var patValue))
            {
                scriptGroup.TerraformGitPat = patValue.Value?.ToString() ?? string.Empty;
            }
            else
            {
                _logger.LogWarning($"PAT token not found in properties. Expected property: {TerraformGitPatPropertyName}");
            }
        }

        /// <summary>
        /// Whether this repository may be offered the deployment's Git credentials at all.
        ///
        /// The repository URL is project configuration, settable at per-project modify rights.
        /// Both credentials it can attract are estate-wide: the Terraform PAT, and an Entra
        /// access token issued to DOrc's own application registration. Neither is scoped to a
        /// particular repository, so pointing a project at a host of one's choosing collected
        /// them both.
        /// </summary>
        private bool MayReceiveSourceCredentials(string repositoryUrl)
        {
            if (_sourceHosts.IsTerraformSourceUnconfigured)
            {
                _logger.LogError(
                    "Refusing Terraform Git source because no source host allow-list is configured"
                    + " ('{Setting}').",
                    SourceHostAllowList.TerraformHostsSetting);
                return false;
            }

            if (_sourceHosts.IsTerraformSourceAllowed(repositoryUrl, out var reason))
            {
                return true;
            }

            _logger.LogError(
                "Withholding Git credentials from '{RepositoryUrl}', because {Reason}",
                SingleLine(repositoryUrl),
                SingleLine(reason));
            return false;
        }

        private static string SingleLine(string? value) =>
            value?.Replace("\r", string.Empty).Replace("\n", string.Empty) ?? string.Empty;

        /// <summary>
        /// Compared on the parsed host, not by substring. A test for "dev.azure.com" anywhere in
        /// the URL is satisfied by https://dev.azure.com.attacker.net/repo.git, which then
        /// received an Entra access token issued to DOrc.
        ///
        /// visualstudio.com is matched on its suffix because organisations live at
        /// myorg.visualstudio.com; that is a suffix of the authority, not of the URL text, so it
        /// cannot be satisfied by an attacker's host that merely ends in the same characters
        /// somewhere in a path.
        /// </summary>
        internal static bool IsAzureDevOpsRepository(string repositoryUrl)
        {
            var host = SourceHostAllowList.HostOf(repositoryUrl);

            if (host == null)
            {
                return false;
            }

            return string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "visualstudio.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
        }

        private void ConfigureAzureArtifactSource(
            ScriptGroup scriptGroup,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            // For Azure artifacts, use existing build information from the request
            if (!string.IsNullOrEmpty(request.BuildUri))
            {
                // Extract build ID from BuildUri with proper validation
                try
                {
                    var uri = new Uri(request.BuildUri);
                    scriptGroup.AzureBuildId = uri.LocalPath.Split('/').Last();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to parse BuildUri: {request.BuildUri}");
                }
            }

            scriptGroup.ScriptsLocation = request.DropLocation;

            // Get bearer token for Azure DevOps
            scriptGroup.AzureBearerToken = GetAzureBearerToken();

            if (project == null)
            {
                _logger.LogWarning("Project information not available for Azure artifact source configuration");
                return;
            }

            scriptGroup.AzureProjects = project.ArtefactsSubPaths;

            if (!string.IsNullOrEmpty(project.ArtefactsUrl))
            {
                // Extract organization from ArtefactsUrl
                var match = System.Text.RegularExpressions.Regex.Match(
                    project.ArtefactsUrl, 
                    @"https://(?:dev\.azure\.com/([^/]+)|([^/]+)\.visualstudio\.com)");
                if (match.Success)
                {
                    scriptGroup.AzureOrganization = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                }
            }
        }

        private void ConfigureGitHubArtifactSource(
            ScriptGroup scriptGroup,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            if (project == null)
            {
                _logger.LogWarning("Project information not available for GitHub artifact source configuration");
                return;
            }

            // Parse owner/repo from ArtefactsUrl (e.g., "https://api.github.com/repos/{owner}/{repo}")
            if (!string.IsNullOrEmpty(project.ArtefactsUrl))
            {
                try
                {
                    var uri = new Uri(project.ArtefactsUrl);
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var reposIndex = Array.IndexOf(segments, "repos");
                    if (reposIndex >= 0 && reposIndex + 2 < segments.Length)
                    {
                        scriptGroup.GitHubOwner = segments[reposIndex + 1];
                        scriptGroup.GitHubRepo = segments[reposIndex + 2];
                    }

                    // Derive API base URL with host validation to prevent SSRF/token exfiltration
                    scriptGroup.GitHubApiBaseUrl = _gitHubHostValidator.GetApiBase(project.ArtefactsUrl);
                }
                catch (Exception ex) when (ex is UriFormatException
                                           or FormatException
                                           or ArgumentException
                                           or InvalidOperationException)
                {
                    _logger.LogWarning(ex, "Failed to parse GitHub ArtefactsUrl for project");
                }
            }

            // BuildUri contains the run ID for GitHub Actions
            if (!string.IsNullOrEmpty(request.BuildUri))
            {
                scriptGroup.GitHubRunId = request.BuildUri;
            }

            // Get GitHub token from configuration
            var gitHubToken = _configurationSettings.GetGitHubToken();
            if (!string.IsNullOrEmpty(gitHubToken))
            {
                scriptGroup.GitHubToken = gitHubToken;
            }
            else
            {
                _logger.LogWarning("GitHub token not configured. Set 'GitHubToken' in AppSettings to download GitHub Actions artifacts.");
            }
        }

        private string GetAzureBearerToken()
        {
            var tenant = _configurationSettings.GetAzureEntraTenantId();
            var clientId = _configurationSettings.GetAzureEntraClientId();
            var secret = _configurationSettings.GetAzureEntraClientSecret();
            
            try
            {
                var aadConnectionSettings = new AadConnectionSettings(
                    clientId, new string[] { }, secret, tenant);
                var authTokenGenerator = AuthTokenGeneratorFactory
                    .GetAuthTokenGenerator(aadConnectionSettings);
                return authTokenGenerator.GetToken();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Azure bearer token");
                return string.Empty;
            }
        }
    }
}
