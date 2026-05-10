using Dorc.ApiModel.MonitorRunnerApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class ScriptGroup
    {
        public Guid ID { get; set; }
        public int DeployResultId { get; set; }
        public string PowerShellVersionNumber { get; set; }
        public string ScriptsLocation { get; set; }

        // Terraform source configuration
        public TerraformSourceType TerraformSourceType { get; set; } = TerraformSourceType.SharedFolder;
        public string TerraformGitRepoUrl { get; set; }
        public string TerraformGitBranch { get; set; }
        public string TerraformSubPath { get; set; }
        public string TerraformGitPat { get; set; }
        public string AzureBearerToken { get; set; }
        public string AzureBuildId { get; set; }
        public string AzureProjects { get; set; }
        public string AzureOrganization { get; set; }

        // Platform-rendered Terraform state backend.
        // Set by the Monitor dispatcher; consumed by the runner to write
        // _dorc_backend.tf into the working directory before `terraform init`.
        // When TerraformStateKey is null/empty the runner skips backend
        // rendering (legacy behaviour) - this preserves backward compatibility
        // until the consolidated lifecycle path is the default.
        // Plain `string` (not `string?`) because Dorc.ApiModel targets
        // netstandard2.0 / C# 7.3 - nullable reference types require C# 8+.
        public string TerraformStateStorageAccount { get; set; }
        public string TerraformStateContainerName { get; set; }
        public string TerraformStateKey { get; set; }
        public string TerraformStateResourceGroup { get; set; }

        // Catalog reference. When TerraformSourceType=Catalog these identify
        // the stock template the runner should resolve via ITemplateCatalog
        // (S-008 runtime).
        public string TerraformTemplateName { get; set; }
        public string TerraformTemplateVersion { get; set; }

        public IDictionary<string, VariableValue> CommonProperties { get; set; }
        public IList<ScriptProperties> ScriptProperties { get; set; }
    }
}
