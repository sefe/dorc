using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessState
    {
        [Required]
        public int DatabaseId { get; set; }
        [Required]
        public long Revision { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Provider { get; set; } = "sql-server";
        [Required]
        public List<DatabaseAccessPrincipal> Principals { get; set; } = new List<DatabaseAccessPrincipal>();
        [Required]
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        [Required]
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
        // Legacy accounts deliberately left out of migration; changed only through legacy import.
        public List<int> ExcludedLegacyUserIds { get; set; } = new List<int>();
    }

    public class DatabaseAccessPrincipal
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        // Native, DirectoryUser or DirectoryGroup. Name is a database alias, not an AD profile.
        [Required(AllowEmptyStrings = true)]
        public string Kind { get; set; } = "Native";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        [Required]
        public bool Present { get; set; } = true;
        public int? LegacyUserId { get; set; }
    }

    public class DatabaseAccessRole
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        [Required]
        public bool Managed { get; set; }
        [Required]
        public bool Present { get; set; } = true;
    }

    public class DatabaseAccessMembership
    {
        [Required(AllowEmptyStrings = true)]
        public string Principal { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Role { get; set; } = "";
        [Required]
        public bool Present { get; set; } = true;
    }

    public class DatabaseAccessObservedPrincipal
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Kind { get; set; } = "";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        [Required]
        public bool Protected { get; set; }
    }

    public class DatabaseAccessObservation
    {
        [Required]
        public List<DatabaseAccessObservedPrincipal> Principals { get; set; } = new List<DatabaseAccessObservedPrincipal>();
        [Required]
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        [Required]
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
        [Required]
        public List<DatabaseAccessObservedPrincipal> Logins { get; set; } = new List<DatabaseAccessObservedPrincipal>();
    }

    public class DatabaseAccessOperation
    {
        [Required(AllowEmptyStrings = true)]
        public string Action { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Principal { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Role { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Login { get; set; } = "";
    }

    public class DatabaseAccessPreview
    {
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
        [Required]
        public long Revision { get; set; }
        [Required]
        public DatabaseAccessObservation Observed { get; set; } = new DatabaseAccessObservation();
        [Required]
        public List<DatabaseAccessOperation> Operations { get; set; } = new List<DatabaseAccessOperation>();
        [Required]
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class DatabaseAccessConfirmation
    {
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
    }

    public class DatabaseAccessStatus
    {
        [Required]
        public bool Enabled { get; set; }
        [Required]
        public bool ExecutionEnabled { get; set; }
        public bool CanApproveDbOwner { get; set; }
        [Required]
        public string[] Providers { get; set; } = Array.Empty<string>();
        [Required]
        public string[] Limitations { get; set; } = Array.Empty<string>();
    }

    public class DatabaseAccessDirectoryIdentity
    {
        [Required(AllowEmptyStrings = true)]
        public string Id { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string DisplayName { get; set; } = "";
        [Required]
        public bool IsGroup { get; set; }
        public string AccountName { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Status { get; set; } = "Resolved";
    }

    public class DatabaseAccessImportRequest
    {
        [Required]
        public Dictionary<string, string> RoleMappings { get; set; } = new Dictionary<string, string>();
        [Required]
        public Dictionary<int, string> DirectoryMappings { get; set; } = new Dictionary<int, string>();
        public List<int> ExcludedLegacyUserIds { get; set; } = new List<int>();
        public List<int> ApprovedDbOwnerLegacyUserIds { get; set; } = new List<int>();
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
    }

    public class DatabaseAccessImportPreview
    {
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
        [Required]
        public DatabaseAccessState Proposed { get; set; } = new DatabaseAccessState();
        [Required]
        public List<string> Errors { get; set; } = new List<string>();
        [Required]
        public int SourceAssignments { get; set; }
        public List<DatabaseAccessImportExclusion> ExcludedAccounts { get; set; } = new List<DatabaseAccessImportExclusion>();
        public List<int> ApprovedDbOwnerLegacyUserIds { get; set; } = new List<int>();
    }

    public class DatabaseAccessImportExclusion
    {
        [Required]
        public int LegacyUserId { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string LoginName { get; set; } = "";
        [Required]
        public List<string> Permissions { get; set; } = new List<string>();
        [Required]
        public int AssignmentCount { get; set; }
    }

    public class DatabaseAccessAuditApiModel
    {
        [Required]
        public long Id { get; set; }
        [Required]
        public DateTime CreatedUtc { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Actor { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Action { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Status { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Detail { get; set; } = "";
    }
}
