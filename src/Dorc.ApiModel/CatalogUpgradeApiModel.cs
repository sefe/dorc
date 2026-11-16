using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class CatalogUpgradePreviewApiModel
    {
        public int ComponentId { get; set; }
        public long Revision { get; set; }
        [Required]
        public string CurrentVersion { get; set; }
        [Required]
        public string TargetVersion { get; set; }
        [Required]
        public List<EnvironmentApiModel> Environments { get; set; } = new List<EnvironmentApiModel>();
        [Required]
        public List<string> InputChanges { get; set; } = new List<string>();
        [Required]
        public Dictionary<string, long> ConfigurationRevisions { get; set; } = new Dictionary<string, long>();
        public bool RequiresExistingServiceOwnership { get; set; }
    }

    public class CatalogUpgradeRequestApiModel
    {
        public long ExpectedRevision { get; set; }
        [Required]
        public string ExpectedVersion { get; set; }
        [Required]
        public List<int> EnvironmentIds { get; set; } = new List<int>();
        [Required]
        public Dictionary<string, long> ConfigurationRevisions { get; set; } = new Dictionary<string, long>();
        public bool ConfirmImpact { get; set; }
        public bool PreserveExistingServiceOwnership { get; set; }
    }
}
