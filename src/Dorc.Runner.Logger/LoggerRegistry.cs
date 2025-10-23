using Microsoft.Extensions.Configuration;
using OpenSearch.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Events;
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
            var config = new ConfigurationBuilder()
                .AddJsonFile("loggerSettings.json", optional: false).Build();

            if (bool.TryParse(config["System:EnableLoggingDiagnostics"], out var enableSelfLog) &&
                enableSelfLog)
            {
                Serilog.Debugging.SelfLog.Enable(Console.Error);
            }

            logPath = runnerLogPath;
            string outputTemplate = config["System:outputTemplate"];
            var logLevel = (LogEventLevel)Enum.Parse(typeof(LogEventLevel), config["Serilog:MinimumLevel:Default"] ?? "Debug");

            var seriLogger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                                .WriteTo.File(logPath, outputTemplate: outputTemplate, restrictedToMinimumLevel: logLevel)
                .Enrich.FromLogContext()
                .CreateLogger();
            return seriLogger;
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