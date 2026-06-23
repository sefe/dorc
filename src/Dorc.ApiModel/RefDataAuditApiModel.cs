using System;

namespace Dorc.ApiModel
{
    public class RefDataAuditApiModel
    {
        public int RefDataAuditId { get; set; }

        public int? ProjectId { get; set; }

        public virtual ProjectApiModel Project { get; set; }

        public int RefDataAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public DateTime Date { get; set; }

        public string Json { get; set; }

        // The Json from the chronologically-prior audit row for the same
        // project, when one exists. Null for the very first audit of a
        // project, or for orphaned (project-deleted) audit rows.
        // Frontend uses this to render a line-level diff.
        public string PriorJson { get; set; }
    }

    public enum RefDataAuditAction
    {
        Create,
        Update,
        Delete
    }
}