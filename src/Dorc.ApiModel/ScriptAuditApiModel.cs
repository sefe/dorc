using System;

namespace Dorc.ApiModel
{
    public class ScriptAuditApiModel
    {
        public int ScriptAuditId { get; set; }

        public int ScriptId { get; set; }

        public virtual ScriptApiModel Script { get; set; }

        public int ScriptAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public DateTime Date { get; set; }

        public string Json { get; set; }
    }

    public enum ScriptAuditAction
    {
        Create,
        Update,
        Delete
    }
}