namespace Dorc.PersistentData.Model
{
    public class Api
    {
        public int Id { get; set; }

        public int EnvId { get; set; }

        public Environment? Environment { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Endpoint { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Type { get; set; } = string.Empty;

        public string AuthType { get; set; } = string.Empty;

        public string? HealthCheckPath { get; set; }

        public int? OwnerProjectId { get; set; }

        public Project? OwnerProject { get; set; }

        public string? Tags { get; set; }
    }
}
