namespace Dorc.Core.Configuration
{
    public interface IOAuthClientConfiguration
    {
        string BaseUrl { get; }
        string ClientId { get; }
        string ClientSecret { get; }
        string Scope { get; }
    }
}
