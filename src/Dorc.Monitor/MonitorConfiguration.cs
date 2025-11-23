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

        public bool DisableSignalR
        {
            get
            {
                return bool.Parse(configurationRoot.GetSection(appSettings)["DisableSignalR"] ?? "false");
            }
        }

        public bool HighAvailabilityEnabled
        {
            get
            {
                return bool.Parse(configurationRoot.GetSection(appSettings)["HighAvailability:Enabled"] ?? "false");
            }
        }

        public string RabbitMqHostName
        {
            get
            {
                var hostName = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:HostName"];
                if (HighAvailabilityEnabled && string.IsNullOrWhiteSpace(hostName))
                {
                    throw new InvalidOperationException("RabbitMQ HostName is required when HighAvailability is enabled (AppSettings:HighAvailability:RabbitMQ:HostName).");
                }
                return hostName ?? "localhost";
            }
        }

        public int RabbitMqPort
        {
            get
            {
                var portStr = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:Port"];
                if (int.TryParse(portStr, out int port))
                {
                    return port;
                }
                return 5672; // Default RabbitMQ port
            }
        }

        public string? RabbitMqVirtualHost
        {
            get
            {
                return configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:VirtualHost"];
            }
        }

        public string RabbitMqOAuthClientId
        {
            get
            {
                var clientId = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:OAuth:ClientId"];
                if (HighAvailabilityEnabled && string.IsNullOrWhiteSpace(clientId))
                {
                    throw new InvalidOperationException("RabbitMQ OAuth ClientId is required when HighAvailability is enabled (AppSettings:HighAvailability:RabbitMQ:OAuth:ClientId).");
                }
                return clientId ?? "";
            }
        }

        public string RabbitMqOAuthClientSecret
        {
            get
            {
                var clientSecret = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:OAuth:ClientSecret"];
                if (HighAvailabilityEnabled && string.IsNullOrWhiteSpace(clientSecret))
                {
                    throw new InvalidOperationException("RabbitMQ OAuth ClientSecret is required when HighAvailability is enabled (AppSettings:HighAvailability:RabbitMQ:OAuth:ClientSecret).");
                }
                return clientSecret ?? "";
            }
        }

        public string RabbitMqOAuthTokenEndpoint
        {
            get
            {
                var tokenEndpoint = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:OAuth:TokenEndpoint"];
                if (HighAvailabilityEnabled && string.IsNullOrWhiteSpace(tokenEndpoint))
                {
                    throw new InvalidOperationException("RabbitMQ OAuth TokenEndpoint is required when HighAvailability is enabled (AppSettings:HighAvailability:RabbitMQ:OAuth:TokenEndpoint).");
                }
                return tokenEndpoint ?? "";
            }
        }

        public string RabbitMqOAuthScope
        {
            get
            {
                var scope = configurationRoot.GetSection(appSettings)["HighAvailability:RabbitMQ:OAuth:Scope"];
                // Scope is optional, can be empty
                return scope ?? "";
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
