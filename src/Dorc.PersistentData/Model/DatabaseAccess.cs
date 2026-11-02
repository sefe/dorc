namespace Dorc.PersistentData.Model;

public class DatabaseAccessConfiguration
{
    public int DatabaseId { get; set; }
    public string Provider { get; set; } = "";
    public long Revision { get; set; }
}

// Shared catalogue: one row per database user identity, reused by every database that grants it access.
public class DatabaseAccessSharedPrincipal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? LoginName { get; set; }
    public string? DirectoryId { get; set; }
}

// Shared catalogue: one row per role name.
public class DatabaseAccessSharedRole
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class DatabaseAccessDatabasePrincipal
{
    public int DatabaseId { get; set; }
    public int PrincipalId { get; set; }
    public bool Present { get; set; }
    public int? LegacyUserId { get; set; }
}

public class DatabaseAccessDatabaseRole
{
    public int DatabaseId { get; set; }
    public int RoleId { get; set; }
    public bool Managed { get; set; }
    public bool Present { get; set; }
}

public class DatabaseAccessDatabaseMembership
{
    public int DatabaseId { get; set; }
    public int PrincipalId { get; set; }
    public int RoleId { get; set; }
    public bool Present { get; set; }
}

public class DatabaseAccessExclusionRecord
{
    public int DatabaseId { get; set; }
    public int LegacyUserId { get; set; }
}

public class DatabaseAccessAudit
{
    public long Id { get; set; }
    public int DatabaseId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Status { get; set; } = "";
    public string Detail { get; set; } = "";
}
