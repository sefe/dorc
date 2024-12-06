namespace Dorc.ApiModel
{
    public class ServiceStatusApiModel
    {
        public string ServerName { set; get; }
        public string ServiceName { set; get; }
        public string ServiceStatus { set; get; }
        public string EnvName { get; set; }
    }
}