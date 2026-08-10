namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class VariableValueServers
    {
        public string Name { get; set; }
        public string OsName { get; set; }
        public string Tags { get; set; }
        public VariableValueDaemons[] Services { get; set; }
    }
}
