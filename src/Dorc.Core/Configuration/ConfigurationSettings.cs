﻿using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Configuration
{
    public class ConfigurationSettings : IConfigurationSettings
    {
        private readonly IConfigurationRoot _configuration;

        public ConfigurationSettings(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        public string GetConfigurationDomainNameIntra()
        {
            return _configuration.GetSection("AppSettings")["DomainNameIntra"];
        }
    }
}
