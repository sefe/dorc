namespace Dorc.Core;

public class CopyEnvBuildRequest
{
    public string SourceEnvironmentName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public List<int> Components { get; set; } = new();
}