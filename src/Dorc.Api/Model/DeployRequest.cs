namespace Dorc.Api.Model;

public class DeployRequest
{
    public string StageName { set; get; } = string.Empty;
    public string RequestId { set; get; } = string.Empty;
}