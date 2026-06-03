namespace Dorc.ApiModel
{
    public class DaemonStatusApiModel
    {
        public string ServerName { get; set; }
        public string DaemonName { get; set; }
        public string Status { get; set; }
        public string EnvName { get; set; }
        public string ErrorMessage { get; set; }
    }
}
