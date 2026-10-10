using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Dorc.ApiModel
{
    public class ComponentApiModel
    {
        [JsonInclude]
        public IList<ComponentApiModel> Children;
        public int? ComponentId { get; set; }
        public string ComponentName { get; set; }
        public string ScriptPath { get; set; }
        public bool NonProdOnly { get; set; }
        public bool StopOnFailure { get; set; }
        public int ParentId { get; set; }
        public bool IsEnabled { set; get; }
        public string PSVersion { set; get; }
        public ComponentType ComponentType { get; set; } = ComponentType.PowerShell;
        public TerraformSourceType TerraformSourceType { get; set; } = TerraformSourceType.SharedFolder;
        public string TerraformGitBranch { get; set; }
        public string TerraformSubPath { get; set; }

        // / optional catalog reference. When set, the runner
        // resolves a stock template from the catalog by (name, version)
        // instead of cloning a Git repo / consuming a shared folder. The
        // catalog reference is mutually exclusive with ScriptPath at
        // component-save time (validated by TerraformExclusivityValidator
        // in Dorc.Api).
        public string TerraformTemplateName { get; set; }
        public string TerraformTemplateVersion { get; set; }
    }
}