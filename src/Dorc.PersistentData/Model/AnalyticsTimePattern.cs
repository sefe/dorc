namespace Dorc.PersistentData.Model
{
    public class AnalyticsTimePattern
    {
        public int Id { get; set; }
        public int HourOfDay { get; set; }
        public int DayOfWeek { get; set; }
        public int DeploymentCount { get; set; }
    }
}
