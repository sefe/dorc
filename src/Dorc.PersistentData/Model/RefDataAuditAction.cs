using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    [Table("deploy.RefDataAuditAction")]
    public class RefDataAuditAction
    {
        public int RefDataAuditActionId { get; set; }

        public ActionType Action { get; set; }

        public virtual ICollection<RefDataAudit> RefDataAudits { get; set; }
        public virtual ICollection<ScriptAudit> ScriptAudits { get; set; }
    }
}