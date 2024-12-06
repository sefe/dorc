namespace Dorc.PersistentData.Model
{
    public class SelectDeploymentsByProjectDateResultDbo
    {
        public int Year { get; set; }

        public int Month { get; set; }

        public int Day { get; set; }

        public string ProjectName { get; set; }

        public int CountofDeployments { get; set; }

        public int Failed { get; set; }
    }
}