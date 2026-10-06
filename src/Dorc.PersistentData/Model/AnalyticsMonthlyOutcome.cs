namespace Dorc.PersistentData.Model
{
    public class AnalyticsMonthlyOutcome
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsProd { get; set; }
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
        public int Cancelled { get; set; }
    }
}
