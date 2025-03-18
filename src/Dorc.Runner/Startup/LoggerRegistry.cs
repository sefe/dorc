﻿using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Dorc.Runner.Startup
{
    public class LoggerRegistry
    {
        private string logPath = String.Empty;

        public string LogFileName { get { return logPath; } }

        public ILogger InitialiseLogger(string pipeName)
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
    }
}