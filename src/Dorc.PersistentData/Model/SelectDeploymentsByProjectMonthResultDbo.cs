namespace Dorc.PersistentData.Model
{
    public class SelectDeploymentsByProjectMonthResultDbo
    {
        public int Year { get; set; }

        public int Month { get; set; }

        public string ProjectName { get; set; }

        public int CountofDeployments { get; set; }

        public int Failed { get; set; }
    }
}