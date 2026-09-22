namespace Dorc.ApiModel
{
    public class ApiRegistrationApiModel : EnvironmentUiPartBase
    {
        public int Id { set; get; }
        public string Name { set; get; }
        public string BaseUrl { set; get; }
        public string Version { set; get; }
        public string HealthCheckUrl { set; get; }
        public string Tags { set; get; }
    }
}
