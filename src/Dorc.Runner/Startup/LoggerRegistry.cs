using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;

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

            var seriLogger = new LoggerConfiguration()
                  .Enrich.WithExceptionDetails()
                  .WriteTo.Map("PipeName", "Monitor-Default", (name, wt) => wt.File(logPath + $"/{name}.txt",
                      outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RequestId} {DeploymentResultId} {Message:lj}{NewLine}{Exception}"))
                  .ReadFrom.Configuration(config)
                  .Enrich.FromLogContext()
                  .Enrich.WithThreadId()
                  .CreateLogger();

            //logPath = config["System:LogPath"] + $"\\{pipeName}.txt";
            //string outputTemplate = config["System:outputTemplate"];
            //var logLevel = (LogEventLevel)Enum.Parse(typeof(LogEventLevel), config["Serilog:MinimumLevel:Default"] ?? "Debug");

            //var seriLogger = new LoggerConfiguration()
            //    .ReadFrom.Configuration(config)
            //                    .WriteTo.File(logPath, outputTemplate: outputTemplate, restrictedToMinimumLevel: logLevel)

            //    .Enrich.FromLogContext()
            //    .CreateLogger();

            return seriLogger;
        }
    }
}