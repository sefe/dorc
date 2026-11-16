using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class CatalogEnvironmentInputsApiModel
    {
        public long Revision { get; set; }
        [Required]
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

    public class SaveCatalogEnvironmentInputsApiModel
    {
        public long ExpectedRevision { get; set; }
        [Required]
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();
    }
}
