﻿using Dorc.PersistData.Dapper;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Dorc.Runner.Logger
{
    public class LoggerRegistry
    {
        private string logPath = String.Empty;

        public string LogFileName { get { return logPath; } }

        public IRunnerLogger InitializeLogger(string pipeName)
        {
            return new RunnerLogger(
                InitializeSerilog(pipeName),
                InitializeDapper(),
                InitializeElasticLogger()
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

        private IDapperContext InitializeDapper()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("loggerSettings.json", optional: false).Build();

            var connectionString = config.GetSection("ConnectionStrings")["DOrcConnectionString"];

            var dapperContext = new DapperContext(connectionString);

            return dapperContext;
        }

        private ElasticsearchClient InitializeElasticLogger()
        {
            var elasticClientSettings = new ElasticsearchClientSettings(new Uri(""))
                .Authentication(new BasicAuthentication("", ""))
                .DefaultIndex("test");
            var client = new ElasticsearchClient(elasticClientSettings);

            return client;
        }
    }
}