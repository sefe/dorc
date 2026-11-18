namespace Dorc.McpServer;

public sealed class DorcMcpOptions
{
    public const string SectionName = "DorcMcp";

    public bool ReadOnly { get; set; } = true;
    public bool AllowProd { get; set; }
    public string Transport { get; set; } = "Stdio";
    public string EndpointPath { get; set; } = "/mcp";
}
