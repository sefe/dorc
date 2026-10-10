namespace Dorc.PersistentData.Model;

public class DatabaseAccessConfiguration
{
    public int DatabaseId { get; set; }
    public string Provider { get; set; } = "";
    public long Revision { get; set; }
}

public class DatabaseAccessPrincipalRecord
{
    public int DatabaseId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? LoginName { get; set; }
    public string? DirectoryId { get; set; }
    public bool Present { get; set; }
    public int? LegacyUserId { get; set; }
}

public class DatabaseAccessRoleRecord
{
    public int DatabaseId { get; set; }
    public string Name { get; set; } = "";
    public bool Managed { get; set; }
    public bool Present { get; set; }
}

public class DatabaseAccessMembershipRecord
{
    public int DatabaseId { get; set; }
    public string Principal { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Present { get; set; }
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
