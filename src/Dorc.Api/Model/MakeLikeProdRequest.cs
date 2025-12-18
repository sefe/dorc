using Dorc.ApiModel;

namespace Dorc.Api.Model
{
    public class MakeLikeProdRequest
    {
        public string TargetEnv { get; set; } = string.Empty;
        public string DataBackup { get; set; } = string.Empty;
        public string BundleName { get; set; } = string.Empty;
        public ICollection<RequestProperty> BundleProperties { set; get; } = new List<RequestProperty>();
    }
}