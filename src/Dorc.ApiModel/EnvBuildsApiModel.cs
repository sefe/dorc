namespace Dorc.ApiModel
{
    public class EnvBuildsApiModel
    {
        public string ComponentName { set; get; }
        public string RequestBuildNumber { set; get; }
        public int RequestId { set; get; }
        public string UpdateDate { set; get; }
        public string Status { set; get; }
    }
}