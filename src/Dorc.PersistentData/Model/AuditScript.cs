namespace Dorc.PersistentData.Model
{
    public class AuditScript
    {
        public long Id { get; set; }

        public long? ScriptId { get; set; }

        public string? ScriptName { get; set; }

        public string? FromValue { get; set; }

        public string? ToValue { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime UpdatedDate { get; set; }

        public string? Type { get; set; }
        public string? ProjectNames { get; set; }
    }
}