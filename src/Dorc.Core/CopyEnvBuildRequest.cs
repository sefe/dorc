namespace Dorc.Core;

public class CopyEnvBuildRequest
{
    public string SourceEnvironmentName { get; set; }
    public string ProjectName { get; set; }
    public List<int> Components { get; set; }
}