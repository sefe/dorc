namespace Dorc.ApiModel
{
    public class AnalyticsTimePatternApiModel
    {
        public int HourOfDay { get; set; }
        public int DayOfWeek { get; set; }
        public string DayOfWeekName { get; set; }
        public int CountOfDeployments { get; set; }
    }
}
