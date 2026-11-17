namespace Dorc.McpServer;

public sealed class DorcApiOptions
{
    public const string SectionName = "DorcApi";

    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "dorc-api.manage";
}
