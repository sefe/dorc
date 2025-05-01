using Dorc.PersistData.Dapper;
using Microsoft.Extensions.Configuration;
using OpenSearch.Client;
using Serilog;
using Serilog.Events;
using System;

namespace Dorc.Runner.Logger
{
    public class LoggerRegistry
    {
        private string logPath = String.Empty;

        public string LogFileName { get { return logPath; } }

        public IRunnerLogger InitializeLogger(string pipeName, IConfigurationRoot config)
        {
            return new RunnerLogger(
                InitializeSerilog(pipeName),
                InitializeDapper(config),
                InitializeOpenSearchLogger(config),
                config.GetSection("OpenSearchSettings")["DeploymentResultIndex"]
                );
        }

        private ILogger InitializeSerilog(string pipeName)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("loggerSettings.json", optional: false).Build();

            if (bool.TryParse(config["System:EnableLoggingDiagnostics"], out var enableSelfLog) &&
                enableSelfLog)
            {
                Serilog.Debugging.SelfLog.Enable(Console.Error);
            }

            logPath = config["System:LogPath"] + $"\\{pipeName}.txt";
            string outputTemplate = config["System:outputTemplate"];
            var logLevel = (LogEventLevel)Enum.Parse(typeof(LogEventLevel), config["Serilog:MinimumLevel:Default"] ?? "Debug");

            var seriLogger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                                .WriteTo.File(logPath, outputTemplate: outputTemplate, restrictedToMinimumLevel: logLevel)
                .Enrich.FromLogContext()
                .CreateLogger();
            return seriLogger;
        }

        private IDapperContext InitializeDapper(IConfigurationRoot config)
        {
            var connectionString = config.GetSection("ConnectionStrings")["DOrcConnectionString"];

            var dapperContext = new DapperContext(connectionString);

            return dapperContext;
        }

        private IOpenSearchClient InitializeOpenSearchLogger(IConfigurationRoot config)
        {
            var openSearchConfigSection = config.GetSection("OpenSearchSettings");
            var elasticClientSettings = new ConnectionSettings(new Uri(openSearchConfigSection["ConnectionUri"]))
                .BasicAuthentication(openSearchConfigSection["UserName"], openSearchConfigSection["Password"])
                .DefaultIndex(openSearchConfigSection["DeploymentResultIndex"])
                .PrettyJson();
            var client = new OpenSearchClient(elasticClientSettings);

            return client;
        }
    }
}