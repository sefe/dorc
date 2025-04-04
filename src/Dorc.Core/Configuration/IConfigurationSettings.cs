namespace Dorc.Core.Configuration
{
    public interface IConfigurationSettings
    {
        string GetConfigurationDomainName();
        string GetConfigurationDomainNameIntra();
        string[] GetAllowedCorsLocations();
        string GetDorcConnectionString();
        TimeSpan? GetADUserCacheTimeSpan();
        string? GetAuthenticationScheme();
        string? GetOAuthAuthority();
        string? GetOnePasswordBaseUrl();
        string? GetOnePasswordApiKey();
        string? GetOnePasswordVaultId();
        string? GetOnePasswordItemId();
    }
}
