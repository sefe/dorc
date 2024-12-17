using System;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Exceptions;

namespace Dorc.NetFramework.Runner.Startup
{
    public class LoggerRegistry
    {
        public string logPath;

        public ILogger InitialiseLogger()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("loggerSettings.json", optional: false).Build();

            if (bool.TryParse(config["System:EnableLoggingDiagnostics"], out var enableSelfLog) &&
                enableSelfLog)
            {
                Serilog.Debugging.SelfLog.Enable(Console.Error);
            }

            logPath = config["System:LogPath"];

          var  seriLogger = new LoggerConfiguration()
                .Enrich.WithExceptionDetails()
                .WriteTo.Map("PipeName", "Monitor-Default", (name, wt) => wt.File(logPath + $"/{name}.txt",
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RequestId} {Message:lj}{NewLine}{Exception}"))
                .ReadFrom.Configuration(config)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .CreateLogger();

            return seriLogger;
        }
    }
}