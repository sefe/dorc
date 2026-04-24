using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Client.Auth;

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

        public TerraformSourceConfigurator(ILogger logger, IConfigurationSettings configurationSettings,
            IGitHubHostValidator gitHubHostValidator)
        {
            _logger = logger;
            _configurationSettings = configurationSettings;
            _gitHubHostValidator = gitHubHostValidator;
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

            scriptGroup.TerraformGitRepoUrl = project.TerraformGitRepoUrl;

            // Determine if this is GitHub or Azure DevOps
            bool isAzureDevOpsRepo = scriptGroup.TerraformGitRepoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                                 scriptGroup.TerraformGitRepoUrl.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);
            if (isAzureDevOpsRepo)
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
