namespace Dorc.ApiModel
{
    public class CloudResourceApiModel : EnvironmentUiPartBase
    {
        public int Id { set; get; }
        public string Name { set; get; }
        public string Provider { set; get; }
        public string ResourceType { set; get; }
        public string ResourceIdentifier { set; get; }
        public string Subscription { set; get; }
        public string Tags { set; get; }
    }
}
