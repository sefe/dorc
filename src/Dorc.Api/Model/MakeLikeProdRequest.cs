using Dorc.ApiModel;

namespace Dorc.Api.Model
{
    public class MakeLikeProdRequest
    {
        public string TargetEnv { get; set; }
        public string DataBackup { get; set; }
        public string BundleName { get; set; }
        public ICollection<RequestProperty> BundleProperties { set; get; }
    }
}