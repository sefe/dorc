﻿using Microsoft.Extensions.Configuration;
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
            var openSearchConfig = config.GetSection("OpenSearchSettings");
            return new RunnerLogger(
                InitializeSerilog(pipeName),
                InitializeOpenSearchLogger(config),
                openSearchConfig["DeploymentResultIndex"],
                openSearchConfig["Environment"],
                openSearchConfig["EnvironmentTier"]
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