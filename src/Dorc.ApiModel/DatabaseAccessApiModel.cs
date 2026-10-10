using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class DatabaseAccessState
    {
        public int DatabaseId { get; set; }
        public long Revision { get; set; }
        public string Provider { get; set; } = "sql-server";
        public List<DatabaseAccessPrincipal> Principals { get; set; } = new List<DatabaseAccessPrincipal>();
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
    }

    public class DatabaseAccessPrincipal
    {
        public string Name { get; set; } = "";
        // Native, DirectoryUser or DirectoryGroup. Name is a database alias, not an AD profile.
        public string Kind { get; set; } = "Native";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        public bool Present { get; set; } = true;
        public int? LegacyUserId { get; set; }
    }

    public class DatabaseAccessRole
    {
        public string Name { get; set; } = "";
        public bool Managed { get; set; }
        public bool Present { get; set; } = true;
    }

    public class DatabaseAccessMembership
    {
        public string Principal { get; set; } = "";
        public string Role { get; set; } = "";
        public bool Present { get; set; } = true;
    }

    public class DatabaseAccessObservedPrincipal
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        public bool Protected { get; set; }
    }

    public class DatabaseAccessObservation
    {
        public List<DatabaseAccessObservedPrincipal> Principals { get; set; } = new List<DatabaseAccessObservedPrincipal>();
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
        public List<DatabaseAccessObservedPrincipal> Logins { get; set; } = new List<DatabaseAccessObservedPrincipal>();
    }

    public class DatabaseAccessOperation
    {
        public string Action { get; set; } = "";
        public string Principal { get; set; } = "";
        public string Role { get; set; } = "";
        public string Login { get; set; } = "";
    }

    public class DatabaseAccessPreview
    {
        public string Token { get; set; } = "";
        public long Revision { get; set; }
        public DatabaseAccessObservation Observed { get; set; } = new DatabaseAccessObservation();
        public List<DatabaseAccessOperation> Operations { get; set; } = new List<DatabaseAccessOperation>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class DatabaseAccessConfirmation
    {
        public string Token { get; set; } = "";
    }

    public class DatabaseAccessStatus
    {
        public bool Enabled { get; set; }
        public bool ExecutionEnabled { get; set; }
        public string[] Providers { get; set; } = Array.Empty<string>();
        public string[] Limitations { get; set; } = Array.Empty<string>();
    }

    public class DatabaseAccessDirectoryIdentity
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsGroup { get; set; }
        public string Status { get; set; } = "Resolved";
    }

    public class DatabaseAccessImportRequest
    {
        public Dictionary<string, string> RoleMappings { get; set; } = new Dictionary<string, string>();
        public Dictionary<int, string> DirectoryMappings { get; set; } = new Dictionary<int, string>();
        public string Token { get; set; } = "";
    }

    public class DatabaseAccessImportPreview
    {
        public string Token { get; set; } = "";
        public DatabaseAccessState Proposed { get; set; } = new DatabaseAccessState();
        public List<string> Errors { get; set; } = new List<string>();
        public int SourceAssignments { get; set; }
    }

    public class DatabaseAccessAuditApiModel
    {
        public long Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Actor { get; set; } = "";
        public string Action { get; set; } = "";
        public string Status { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
