namespace Dorc.PersistentData.Model
{
    public class DeploymentsByProjectDate
    {
        public long Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public string ProjectName { get; set; } = null!;
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
    }
}
