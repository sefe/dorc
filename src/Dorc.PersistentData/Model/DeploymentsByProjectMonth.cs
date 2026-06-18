namespace Dorc.PersistentData.Model
{
    public class DeploymentsByProjectMonth
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string ProjectName { get; set; } = null!;
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
    }
}
