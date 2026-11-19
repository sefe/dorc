using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class MakeLikeProdRequestDto
    {
        public string TargetEnv { get; set; } = string.Empty;
        public string DataBackup { get; set; } = string.Empty;
        public string BundleName { get; set; } = string.Empty;
        public List<RequestProperty> BundleProperties { get; set; } = new List<RequestProperty>();
    }
}
