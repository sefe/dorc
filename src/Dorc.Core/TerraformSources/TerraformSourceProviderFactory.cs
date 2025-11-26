using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.TerraformSources
{
    /// <summary>
    /// Factory for creating appropriate Terraform source providers
    /// </summary>
    public class TerraformSourceProviderFactory
    {
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IAzureDevOpsServerWebClient _azureDevOpsClient;
        private readonly ILogger _logger;

        public TerraformSourceProviderFactory(
            IConfigValuesPersistentSource configValuesPersistentSource,
            IAzureDevOpsServerWebClient azureDevOpsClient,
            ILogger logger)
        {
            _configValuesPersistentSource = configValuesPersistentSource ?? throw new ArgumentNullException(nameof(configValuesPersistentSource));
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ITerraformSourceProvider Create(ComponentApiModel component, string scriptRoot)
        {
            switch (component.TerraformSourceType)
            {
                case TerraformSourceType.SharedFolder:
                    // Legacy behavior: use script path from shared folder
                    var fullScriptPath = string.IsNullOrEmpty(scriptRoot)
                        ? component.ScriptPath
                        : Path.Combine(scriptRoot, component.ScriptPath);
                    return new SharedFolderSourceProvider(fullScriptPath, _logger);

                case TerraformSourceType.Git:
                    if (string.IsNullOrEmpty(component.TerraformGitRepoUrl))
                    {
                        throw new InvalidOperationException("Git repository URL is required for Git source type");
                    }
                    if (string.IsNullOrEmpty(component.TerraformGitBranch))
                    {
                        throw new InvalidOperationException("Git branch is required for Git source type");
                    }

                    // Get Git credentials from configuration
                    var gitUsername = _configValuesPersistentSource.GetConfigValue("TerraformGitUsername");
                    var gitPassword = _configValuesPersistentSource.GetConfigValue("TerraformGitPassword");

                    return new GitSourceProvider(
                        component.TerraformGitRepoUrl,
                        component.TerraformGitBranch,
                        component.TerraformGitPath,
                        gitUsername,
                        gitPassword,
                        _logger);

                case TerraformSourceType.AzureArtifact:
                    if (!component.TerraformArtifactBuildId.HasValue)
                    {
                        throw new InvalidOperationException("Build ID is required for Azure Artifact source type");
                    }

                    // Get Azure DevOps collection and project from configuration or component
                    // These should be configured in DOrc settings
                    var collection = _configValuesPersistentSource.GetConfigValue("TerraformAzureDevOpsCollection");
                    var project = _configValuesPersistentSource.GetConfigValue("TerraformAzureDevOpsProject");

                    if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(project))
                    {
                        throw new InvalidOperationException("Azure DevOps collection and project must be configured for Azure Artifact source type");
                    }

                    return new AzureArtifactSourceProvider(
                        component.TerraformArtifactBuildId.Value,
                        collection,
                        project,
                        _azureDevOpsClient,
                        _logger);

                default:
                    throw new NotSupportedException($"Terraform source type '{component.TerraformSourceType}' is not supported");
            }
        }
    }
}
