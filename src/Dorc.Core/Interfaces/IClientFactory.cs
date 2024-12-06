namespace Dorc.Core.Interfaces
{
    public interface IClientFactory
    {
        ApiCaller GetClient(string url);
    }
}
