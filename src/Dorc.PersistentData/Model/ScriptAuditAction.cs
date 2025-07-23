using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    [Table("deploy.ScriptAuditAction")]
    public class ScriptAuditAction
    {
        public int ScriptAuditActionId { get; set; }

        public ActionType Action { get; set; }

        public virtual ICollection<ScriptAudit> ScriptAudits { get; set; }
    }
}