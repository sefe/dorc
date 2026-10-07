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

        string? GetGitHubToken();

        string GetAzureStorageAccountTenantId();
        string GetAzureStorageAccountClientId();
        string GetAzureStorageAccountClientSecret();
        string GetAzureStorageAccounUri();
        string GetAzureStorageAccountTerraformBlobsContainerName();

        bool GetPauseDeploymentEnabled();

        /// <summary>
        /// Whether confirming a Terraform plan requires an approver other than the person
        /// who submitted the request.
        ///
        /// Returns null when the setting is absent or unparseable. Callers must treat null
        /// as "enabled for production-tier requests, disabled otherwise" - deliberately NOT
        /// the codebase's usual bool.TryParse(...) &amp;&amp; enabled pattern, which resolves an
        /// absent key to false and would ship this control silently off.
        /// </summary>
        bool? GetTerraformSeparateApproverRequired();
        bool GetIsProduction();
    }
}
