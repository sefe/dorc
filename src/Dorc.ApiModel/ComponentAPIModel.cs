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
    }
}