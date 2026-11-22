namespace Dorc.Core.Interfaces
{
    public interface IClientFactory
    {
        DorcApiClient GetClient(string url);
    }
}
