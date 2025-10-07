namespace Dorc.PersistentData.Model
{
    public class EnvironmentHistory
    {
        public int Id { get; set; }

        public int? EnvId { get; set; }
        public Environment? Environment { get; set; }

        public DateTime? UpdateDate { get; set; }

        public string? UpdateType { get; set; }

        public string? FromValue { get; set; }
        public string? ToValue { get; set; }

        public string? UpdatedBy { get; set; }

        public string? Details { get; set; }

        public string? Comment { get; set; }
    }
}