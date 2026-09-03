namespace Dorc.PersistentData.Model
{
    public class DaemonAudit
    {
        public long Id { get; set; }

        public int? DaemonId { get; set; }

        public int RefDataAuditActionId { get; set; }

        public RefDataAuditAction Action { get; set; } = null!;

        public string Username { get; set; } = null!;

        public DateTime Date { get; set; }

        public string? FromValue { get; set; }

        public string? ToValue { get; set; }
    }
}
