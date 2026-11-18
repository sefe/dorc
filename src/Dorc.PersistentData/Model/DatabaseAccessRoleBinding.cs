namespace Dorc.PersistentData.Model;

public class DatabaseAccessRoleBinding
{
    public int DatabaseId { get; set; }
    public int RoleId { get; set; }
    public bool Managed { get; set; }
    public bool Present { get; set; }
}
