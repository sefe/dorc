namespace Dorc.ApiModel
{
    public class AnalyticsMonthlyOutcomeApiModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsProd { get; set; }
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
        public int Cancelled { get; set; }
    }
}
