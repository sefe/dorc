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
        string? GetOAuthUiClientId();
        string? GetOAuthUiRequestedScopes();
        string? GetOAuthApiResourceName();
        string? GetOAuthApiGlobalScope();
        string? GetOnePasswordBaseUrl();
        string? GetOnePasswordApiKey();
        string? GetOnePasswordVaultId();
        string? GetOnePasswordItemId();
        string? GetIdentityServerClientId();
        string? GetOnePasswordIdentityServerApiSecretItemId();
        bool GetIsUseAdAsSearcher();
        bool GetIsUseAdSidsForAccessControl();

        string GetAzureEntraTenantId();
        string GetAzureEntraClientId();
        string GetAzureEntraClientSecret();

        string GetAzureStorageAccountTenantId();
        string GetAzureStorageAccountClientId();
        string GetAzureStorageAccountClientSecret();
        string GetAzureStorageAccounUri();
        string GetAzureStorageAccountTerraformBlobsContainerName();
        string GetWindowsApiUrl();
    }
}
