namespace Dorc.PersistentData.Model
{
    public class ApiRegistration
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public string BaseUrl { get; set; } = null!;

        public string? Version { get; set; }

        public string? HealthCheckUrl { get; set; }

        public string? Tags { get; set; }

        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}
