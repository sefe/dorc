using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Exceptions;

namespace Dorc.Runner.Startup
{
    public class LoggerRegistry
    {
        private string logPath = String.Empty;

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
            string outputTemplate = config["System:outputTemplate"];

            var seriLogger = new LoggerConfiguration()
                .WriteTo.Map("PipeName", "Monitor-Default", (name, wt) => wt.File(logPath + $"/{name}.txt", outputTemplate: outputTemplate))
                .ReadFrom.Configuration(config)
                .Enrich.FromLogContext()
                .CreateLogger();

            return seriLogger;
        }
    }
}