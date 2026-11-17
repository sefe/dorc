namespace Dorc.PersistentData.Model
{
    public class EnvironmentTemplate
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DefaultTier { get; set; } = "ephemeral";
        public int? BaseEnvironmentId { get; set; }
        public int DefaultTtlMinutes { get; set; } = 240;
        public int MaxTtlMinutes { get; set; } = 1440;
        public string ProvisionComponentIdsJson { get; set; } = "[]";
        public string ValidationComponentIdsJson { get; set; } = "[]";
        public string? TierParameterOverridesJson { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Project? Project { get; set; }
        public Environment? BaseEnvironment { get; set; }
        public ICollection<Environment> Environments { get; set; } = new List<Environment>();
    }
}
