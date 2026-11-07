using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class CatalogEnvironmentInputsApiModel
    {
        public long Revision { get; set; }
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

    public class SaveCatalogEnvironmentInputsApiModel
    {
        public long ExpectedRevision { get; set; }
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();
    }
}
