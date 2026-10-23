using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class CatalogUpgradePreviewApiModel
    {
        public int ComponentId { get; set; }
        public long Revision { get; set; }
        public string CurrentVersion { get; set; }
        public string TargetVersion { get; set; }
        public List<EnvironmentApiModel> Environments { get; set; } = new List<EnvironmentApiModel>();
        public List<string> InputChanges { get; set; } = new List<string>();
        public Dictionary<string, long> ConfigurationRevisions { get; set; } = new Dictionary<string, long>();
        public bool RequiresExistingServiceOwnership { get; set; }
    }

    public class CatalogUpgradeRequestApiModel
    {
        public long ExpectedRevision { get; set; }
        public string ExpectedVersion { get; set; }
        public List<int> EnvironmentIds { get; set; } = new List<int>();
        public Dictionary<string, long> ConfigurationRevisions { get; set; } = new Dictionary<string, long>();
        public bool ConfirmImpact { get; set; }
        public bool PreserveExistingServiceOwnership { get; set; }
    }
}
