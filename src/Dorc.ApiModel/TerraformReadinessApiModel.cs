using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class TerraformReadinessApiModel
    {
        public string AzureCredentialMode { get; set; }
        public string AzureTargetSource { get; set; }
        public string WorkloadTarget { get; set; }
        public string AivenCredentialMode { get; set; }
        public string BackendStatus { get; set; } = "Not configured: select a project/environment Terraform state backend in Project Ref Data.";
        public string AccessStatus { get; set; } = "Not checked: workload and state-container permissions have not been probed.";
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> InvalidInputs { get; set; } = new List<string>();
    }
}
