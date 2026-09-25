using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Configuration
{
    public class ConfigurationSettings : IConfigurationSettings
    {
        private readonly IConfigurationRoot _configuration;

        public ConfigurationSettings(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        public string GetConfigurationDomainName()
        {
            return _configuration.GetSection("AppSettings")["DomainName"];
        }

        public string GetConfigurationDomainNameIntra()
        {
            return _configuration.GetSection("AppSettings")["DomainNameIntra"];
        }

        public string[] GetAllowedCorsLocations()
        {
            return _configuration.GetSection("AppSettings")["AllowedCORSLocations"]?.Split(",");
        }

        public string GetDorcConnectionString()
        {
            return _configuration.GetConnectionString("DOrcConnectionString");
        }

        public TimeSpan? GetADUserCacheTimeSpan()
        {
            var adUserCacheTimeMinutesConfig = _configuration.GetSection("AppSettings")["ADUserCacheTimeMinutes"];
            if (!int.TryParse(adUserCacheTimeMinutesConfig, out int adUserCacheTimeMinutes))
            {
                return null;
            }

            return TimeSpan.FromMinutes(adUserCacheTimeMinutes);
        }

        public string? GetAuthenticationScheme()
        {
            return _configuration.GetSection("AppSettings")["AuthenticationScheme"];
        }

        public string? GetOAuthAuthority()
        {
            return _configuration.GetSection("AppSettings:OAuth2")["Authority"];
        }

        public string? GetOAuthUiClientId()
        {
            return _configuration.GetSection("AppSettings:OAuth2")["UiClientId"];
        }

        public string? GetOAuthUiRequestedScopes()
        {
            return _configuration.GetSection("AppSettings:OAuth2")["UiRequestedScopes"];
        }

        public string? GetOAuthApiResourceName()
        {
            return _configuration.GetSection("AppSettings:OAuth2")["ApiResourceName"];
        }

        public string? GetOAuthApiGlobalScope()
        {
            return _configuration.GetSection("AppSettings:OAuth2")["ApiGlobalScope"];
        }

        public string? GetOnePasswordBaseUrl()
        {
            return _configuration.GetSection("AppSettings:OnePassword")["BaseUrl"];
        }

        public string? GetOnePasswordApiKey()
        {
            return _configuration.GetSection("AppSettings:OnePassword")["ApiKey"];
        }

        public string? GetOnePasswordVaultId()
        {
            return _configuration.GetSection("AppSettings:OnePassword")["VaultId"];
        }

        public string? GetOnePasswordItemId()
        {
            return _configuration.GetSection("AppSettings:OnePassword")["ItemId"];
        }

        public string GetAzureEntraTenantId()
        {
            return _configuration.GetSection("AppSettings")["AadTenant"];
        }

        public string GetAzureEntraClientId()
        {
            return _configuration.GetSection("AppSettings")["AadClientId"];
        }

        public string GetAzureEntraClientSecret()
        {
            return _configuration.GetSection("AppSettings")["AadSecret"];
        }

        public string? GetGitHubToken()
        {
            return _configuration.GetSection("AppSettings")["GitHubToken"];
        }

        public string? GetIdentityServerClientId()
        {
            return _configuration.GetSection("AppSettings")["IdentityServerClientId"];
        }

        public string? GetOnePasswordIdentityServerApiSecretItemId()
        {
            return _configuration.GetSection("AppSettings:OnePassword")["IdentityServerApiSecretItemId"];
        }

        public bool GetIsUseAdAsSearcher()
        {
            var isUseIdentityServerAsSearcherConfig = _configuration.GetSection("AppSettings")["IsUseAdAsSearcher"];
            return bool.TryParse(isUseIdentityServerAsSearcherConfig, out bool isUseIdentityServerAsSearcher) && isUseIdentityServerAsSearcher;
        }

        public bool GetIsUseAdSidsForAccessControl()
        {
            var isUseAdSidsForAccessControlConfig = _configuration.GetSection("AppSettings")["IsUseAdSidsForAccessControl"];
            return bool.TryParse(isUseAdSidsForAccessControlConfig, out bool isUseAdSidsForAccessControl) && isUseAdSidsForAccessControl;
        }

        public string GetEnvironment(bool removeSpaces = false)
        {
            var env = _configuration.GetSection("AppSettings")["environment"] ?? "Local";
            if (removeSpaces)
            {
                env = env.Replace(" ", "_");
            }

            return env;
        }

        #region Azure Storage Account
        public string GetAzureStorageAccountTenantId()
        {
            return _configuration.GetSection("AzureStorageAccount")["TenantId"];
        }

        public string GetAzureStorageAccountClientId()
        {
            return _configuration.GetSection("AzureStorageAccount")["ClientId"];
        }

        public string GetAzureStorageAccountClientSecret()
        {
            return _configuration.GetSection("AzureStorageAccount")["ClientSecret"];
        }

        public string GetAzureStorageAccounUri()
        {
            return _configuration.GetSection("AzureStorageAccount")["StorageAccountUri"];
        }

        public string GetAzureStorageAccountTerraformBlobsContainerName()
        {
            return _configuration.GetSection("AzureStorageAccount")["TerraformBlobsContainerName"];
        }

        public bool GetPauseDeploymentEnabled()
        {
            var value = _configuration.GetSection("AppSettings")["PauseDeploymentEnabled"];
            return bool.TryParse(value, out bool enabled) && enabled;
        }

        public bool? GetTerraformSeparateApproverRequired()
        {
            var value = _configuration.GetSection("AppSettings")["TerraformSeparateApproverRequired"];

            // Intentionally not the "TryParse(...) && enabled" pattern used above. That
            // resolves an absent key to false; for this setting an absent key must mean
            // "enabled for production", so absence is reported as null and the decision is
            // left to the caller, which knows the request's tier.
            return bool.TryParse(value, out bool required) ? required : null;
        }

        /// <summary>
        /// Whether a script path that resolves outside the script root refuses the deployment,
        /// or is only reported.
        ///
        /// Absence means report. Enforcement is deliberately opt-in: existing components hold
        /// paths that were never validated, so enforcing on the day this ships would fail every
        /// deployment of an off-share component at once. The report is what identifies them, and
        /// enforcement is turned on once the estate is clean.
        /// </summary>
        public bool GetScriptPathEnforcementEnabled()
        {
            var value = _configuration.GetSection("AppSettings")["EnforceScriptPathConfinement"];

            return bool.TryParse(value, out bool enforce) && enforce;
        }

        public bool GetIsProduction()
        {
            var value = _configuration.GetSection("AppSettings")["IsProduction"];
            return bool.TryParse(value, out bool isProduction) && isProduction;
        }
        #endregion
    }
}