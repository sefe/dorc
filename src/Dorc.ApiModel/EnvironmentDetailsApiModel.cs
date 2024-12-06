namespace Dorc.ApiModel
{
    public class EnvironmentDetailsApiModel
    {
        public string LastUpdated { set; get; }
        public string EnvironmentOwner { set; get; }
        public string RestoredFromSourceDb { set; get; }
        public string Description { set; get; }
        public string FileShare { set; get; }
        public string ThinClient { set; get; }
        public string Notes { set; get; }
    }
}