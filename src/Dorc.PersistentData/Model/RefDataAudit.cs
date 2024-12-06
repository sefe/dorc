using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    [Table("deploy.RefDataAudit")]
    public class RefDataAudit
    {
        public int RefDataAuditId { get; set; }

        public int ProjectId { get; set; }

        public virtual Project Project { get; set; } = null!;

        public int RefDataAuditActionId { get; set; }

        public RefDataAuditAction Action { get; set; } = null!;

        public string? Username { get; set; }

        public DateTime Date { get; set; }

        public string? Json { get; set; }
    }
}