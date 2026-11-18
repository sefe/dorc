namespace Dorc.PersistentData.Model;

public class DatabaseAccessRoleMembership
{
    public int DatabaseId { get; set; }
    public int PrincipalId { get; set; }
    public int RoleId { get; set; }
    public bool Present { get; set; }
}
