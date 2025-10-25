using Microsoft.Extensions.Configuration;
using OpenSearch.Client;
using Microsoft.Extensions.Logging;
using System;

namespace Dorc.Runner.Logger
{
    public class LoggerRegistry
    {
        private string logPath = String.Empty;

        public string LogFileName { get { return logPath; } }

        public IRunnerLogger InitializeLogger(string runnerLogPath, IConfigurationRoot config)
        {
            var openSearchConfig = config.GetSection("OpenSearchSettings");

            var deploymentResultIndex = openSearchConfig["DeploymentResultIndex"];
            if (String.IsNullOrEmpty(deploymentResultIndex))
                throw new Exception("'OpenSearchSettings.DeploymentResultIndex' not set in the Runner appsettings");

            var environmentName = openSearchConfig["Environment"];
            if (String.IsNullOrEmpty(environmentName))
                throw new Exception("'OpenSearchSettings.Environment' not set in the Runner appsettings");

            var environmentTier = openSearchConfig["EnvironmentTier"];
            if (String.IsNullOrEmpty(environmentTier))
                throw new Exception("'OpenSearchSettings.EnvironmentTier' not set in the Runner appsettings");

            return new RunnerLogger(
                InitializeSerilog(runnerLogPath),
                InitializeOpenSearchLogger(openSearchConfig),
                deploymentResultIndex,
                environmentName,
                environmentTier
                );
        }

        private ILogger InitializeSerilog(string runnerLogPath)
        {
            logPath = runnerLogPath;
            
            // Create a simple logger factory for file logging
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });
            
            return loggerFactory.CreateLogger("Runner");
        }

        private IOpenSearchClient InitializeOpenSearchLogger(IConfigurationSection openSearchConfig)
        {
            var connectionUri = openSearchConfig["ConnectionUri"];
            if (String.IsNullOrEmpty(connectionUri))
                throw new Exception("'OpenSearchSettings.ConnectionUri' not set in the DOrc appsettings");

            var userName = openSearchConfig["UserName"];
            if (String.IsNullOrEmpty(userName))
                throw new Exception("'OpenSearchSettings.UserName' not set in the DOrc appsettings");

            var password = openSearchConfig["Password"];
            if (String.IsNullOrEmpty(password))
                throw new Exception("'OpenSearchSettings.Password' not set in the DOrc appsettings");

            var deploymentResultIndex = openSearchConfig["DeploymentResultIndex"];
            if (String.IsNullOrEmpty(deploymentResultIndex))
                throw new Exception("'OpenSearchSettings.DeploymentResultIndex' not set in the DOrc appsettings");

            var openSearchClientSettings = new ConnectionSettings(new Uri(connectionUri))
                .BasicAuthentication(userName, password)
                .DefaultIndex(deploymentResultIndex)
                .PrettyJson();
            var client = new OpenSearchClient(openSearchClientSettings);

            return client;
        }
    }
}