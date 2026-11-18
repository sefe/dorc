namespace Dorc.PersistentData.Model;

public class DatabaseAccessPrincipalBinding
{
    public int DatabaseId { get; set; }
    public int PrincipalId { get; set; }
    public bool Present { get; set; }
    public int? LegacyUserId { get; set; }
}
