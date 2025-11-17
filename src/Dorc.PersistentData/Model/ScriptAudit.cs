namespace Dorc.PersistentData.Model
{
    public class ScriptAudit
    {
        public int ScriptAuditId { get; set; }

        public int ScriptId { get; set; }

        public virtual Script Script { get; set; } = null!;

        public int RefDataAuditActionId { get; set; }

        public RefDataAuditAction Action { get; set; } = null!;

        public string? Username { get; set; }

        public DateTime Date { get; set; }

        public string? Json { get; set; }
    }
}