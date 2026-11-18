namespace Dorc.PersistentData.Model;

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
