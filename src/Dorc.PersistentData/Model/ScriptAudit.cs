using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    [Table("deploy.ScriptAudit")]
    public class ScriptAudit
    {
        public int ScriptAuditId { get; set; }

        public int ScriptId { get; set; }

        public virtual Script Script { get; set; } = null!;

        public int ScriptAuditActionId { get; set; }

        public ScriptAuditAction Action { get; set; } = null!;

        public string? Username { get; set; }

        public DateTime Date { get; set; }

        public string? Json { get; set; }
    }
}