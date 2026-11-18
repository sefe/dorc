namespace Dorc.PersistentData.Model;

public class DatabaseAccessPrincipalCatalog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? LoginName { get; set; }
    public string? DirectoryId { get; set; }
}
