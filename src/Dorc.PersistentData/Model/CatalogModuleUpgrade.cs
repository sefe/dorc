namespace Dorc.PersistentData.Model;

public class CatalogModuleUpgrade
{
    public int ComponentId { get; set; }
    public long Revision { get; set; }
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
