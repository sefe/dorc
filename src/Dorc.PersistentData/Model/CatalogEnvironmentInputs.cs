namespace Dorc.PersistentData.Model;

public class CatalogEnvironmentInputs
{
    public int ComponentId { get; set; }
    public int EnvironmentId { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string InputsJson { get; set; } = "{}";
}

public class CatalogRequestInputs
{
    public int DeploymentRequestId { get; set; }
    public int ComponentId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = string.Empty;
    public long ConfigurationRevision { get; set; }
    public string InputsJson { get; set; } = "{}";
}
