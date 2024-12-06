namespace Dorc.PersistentData.Model
{
    public class EnvironmentHistory
    {
        public int Id { get; set; }

        public int EnvId { get; set; }
        public Environment Environment { get; set; } = null!;

        public DateTime? UpdateDate { get; set; }

        public string? UpdateType { get; set; }

        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }

        public string? UpdatedBy { get; set; }

        public string? Action { get; set; }

        public string? Comment { get; set; }
    }
}