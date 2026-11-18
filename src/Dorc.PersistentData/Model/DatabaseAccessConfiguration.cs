namespace Dorc.PersistentData.Model;

public class DatabaseAccessConfiguration
{
    public int DatabaseId { get; set; }
    public string Provider { get; set; } = "";
    public long Revision { get; set; }
}
