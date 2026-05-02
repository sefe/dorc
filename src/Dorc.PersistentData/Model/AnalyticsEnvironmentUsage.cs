namespace Dorc.PersistentData.Model
{
    public class AnalyticsEnvironmentUsage
    {
        public int Id { get; set; }
        public string EnvironmentName { get; set; } = null!;
        public int TotalDeployments { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
    }
}
