using System;

namespace Dorc.ApiModel
{
    public class RefDataAuditApiModel
    {
        public int RefDataAuditId { get; set; }

        public int ProjectId { get; set; }

        public virtual ProjectApiModel Project { get; set; }

        public int RefDataAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public DateTime Date { get; set; }

        public string Json { get; set; }
    }

    public enum RefDataAuditAction
    {
        Create,
        Update,
        Delete
    }
}