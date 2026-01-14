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
        #endregion
    }
}