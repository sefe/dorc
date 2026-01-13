namespace Dorc.PersistentData.Model
{
    public class AnalyticsUserActivity
    {
        public int Id { get; set; }
        public string UserName { get; set; } = null!;
        public int TotalDeployments { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
    }
}
