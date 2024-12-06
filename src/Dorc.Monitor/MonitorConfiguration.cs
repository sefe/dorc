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
    }
}
