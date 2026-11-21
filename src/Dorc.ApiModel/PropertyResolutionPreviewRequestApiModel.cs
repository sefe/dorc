using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class PropertyResolutionPreviewRequestApiModel
    {
        public string EnvironmentName { get; set; }
        public string ProjectName { get; set; }
        public List<string> PropertyNames { get; set; } = new List<string>();
        public List<RequestProperty> RequestOverrides { get; set; }
    }
}
