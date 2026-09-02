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

        // GitHub Actions source configuration
        public string GitHubToken { get; set; }
        public string GitHubOwner { get; set; }
        public string GitHubRepo { get; set; }
        public string GitHubRunId { get; set; }
        public string GitHubApiBaseUrl { get; set; }

        /// <summary>
        /// How far the runner takes content verification.
        ///
        /// Carried rather than read by the runner from its own configuration. The Monitor is
        /// where the estate's setting lives, and a runner reading its own would let a host with
        /// a stale or edited configuration file quietly execute unverified — which is the
        /// deployment host, the least trustworthy place to keep the answer.
        /// </summary>
        public ScriptContentVerificationMode ContentVerification { get; set; }
            = ScriptContentVerificationMode.Report;

        public IDictionary<string, VariableValue> CommonProperties { get; set; }
        public IList<ScriptProperties> ScriptProperties { get; set; }
    }
}
