namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class VariableValueServers
    {
        public string Name { get; set; }
        public string OsName { get; set; }
        public string ApplicationServerName { get; set; }
        public VariableValueDaemons[] Services { get; set; }
    }
}
