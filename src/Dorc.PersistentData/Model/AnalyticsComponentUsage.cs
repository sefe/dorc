namespace Dorc.PersistentData.Model
{
    public class AnalyticsComponentUsage
    {
        public int Id { get; set; }
        public string ComponentName { get; set; } = null!;
        public int DeploymentCount { get; set; }
    }
}
