using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Monitor
{
    internal class MonitorConfiguration : IMonitorConfiguration
    {
        private IConfigurationRoot configurationRoot;
        private readonly string appSettings = "AppSettings";

        public MonitorConfiguration(IConfigurationRoot configurationRoot)
        {
            this.configurationRoot = configurationRoot;
        }

        public bool IsProduction
        {
            get
            {
                return bool.Parse(configurationRoot
                .GetSection(appSettings)["IsProduction"] ?? "false");
            }
        }
        public int RequestProcessingIterationDelayMs
        {
            get
            {
                int delay = 1000;
                int.TryParse(configurationRoot.GetSection(appSettings)["requestProcessingIterationDelayMs"], out delay);
                return delay;
            }
        }

        public string ServiceName
        {
            get
            {
                var serviceName = configurationRoot.GetSection(appSettings)["ServiceName"];
                if (string.IsNullOrEmpty(serviceName))
                {
                    throw new InvalidOperationException("Service name is not specified in configuration file 'appsettings.json'.");
                }

                return serviceName;
            }
        }

        public string DOrcConnectionString
        {
            get
            {
                var connectionString = configurationRoot.GetConnectionString("DOrcConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("DB connection string is not specified in configuration file 'appsettings.json'.");
                }

                return connectionString;
            }
        }

        public string RefDataApiUrl
        {
            get
            {
                var url = configurationRoot.GetSection("AppSettings")["RefDataApiUrl"];
                if (string.IsNullOrEmpty(url))
                {
                    throw new InvalidOperationException("RefData API URL is not specified in configuration file 'appsettings.json'.");
                }
                return url;
            }
        }

        public string DorcApiClientId
        {
            get
            {
                var id = configurationRoot.GetSection(appSettings)["DorcApi:ClientId"];
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException("OAuth ClientId is not configured (AppSettings:DorcApi:ClientId).");
                }
                return id;
            }
        }

        public string DorcApiClientSecret
        {
            get
            {
                var secret = configurationRoot.GetSection(appSettings)["DorcApi:ClientSecret"];
                if (string.IsNullOrWhiteSpace(secret))
                {
                    throw new InvalidOperationException("OAuth ClientSecret is not configured (AppSettings:DorcApi:ClientSecret).");
                }
                return secret;
            }
        }

        public string DorcApiScope
        {
            get
            {
                var scope = configurationRoot.GetSection(appSettings)["DorcApi:Scope"];
                if (string.IsNullOrWhiteSpace(scope))
                {
                    throw new InvalidOperationException("OAuth Scope is not configured (AppSettings:DorcApi:Scope).");
                }
                return scope;
            }
        }
    }

    internal class OAuthClientConfiguration : IOAuthClientConfiguration
    {
        public string BaseUrl { get; set; }

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string Scope { get; set; }

        public static IOAuthClientConfiguration FromMonitorConfiguration(IMonitorConfiguration config)
        {
            return new OAuthClientConfiguration
            {
                BaseUrl = config.RefDataApiUrl,
                ClientId = config.DorcApiClientId,
                ClientSecret = config.DorcApiClientSecret,
                Scope = config.DorcApiScope
            };
        }
    }
}
