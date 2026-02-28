namespace Dorc.Api.Windows.Model;

public class MakeLikeProdResponse
{
    public MakeLikeProdResponse()
    {
        Items = new List<DeployRequest>();
    }
    public IEnumerable<DeployRequest> Items { set; get; }
}