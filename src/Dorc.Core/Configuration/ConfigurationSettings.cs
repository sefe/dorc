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
    }
}
