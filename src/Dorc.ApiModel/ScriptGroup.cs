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
        public string AzureProject { get; set; }
        public string AzureOrganization { get; set; }

        public IDictionary<string, VariableValue> CommonProperties { get; set; }
        public IList<ScriptProperties> ScriptProperties { get; set; }
    }
}
