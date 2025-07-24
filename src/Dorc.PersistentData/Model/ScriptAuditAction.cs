namespace Dorc.PersistentData.Model
{
    public class ScriptAuditAction
    {
        public int ScriptAuditActionId { get; set; }

        public ActionType Action { get; set; }

        public virtual ICollection<ScriptAudit> ScriptAudits { get; set; }
    }
}