namespace Dorc.ApiModel
{
    public class ContainerApiModel : EnvironmentUiPartBase
    {
        public int Id { set; get; }
        public string Name { set; get; }
        public string Image { set; get; }
        public string Registry { set; get; }
        public string HostServerName { set; get; }
        public string Tags { set; get; }
    }
}
