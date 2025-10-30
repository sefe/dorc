using System;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Lamar;
using Microsoft.Extensions.Logging;

namespace Tools.PostRestoreEndurCLI
{
    public class CliRegistry : ServiceRegistry
    {
        public CliRegistry()
        {
            try
            {
                // Configure ILogger from DI container
                For<ILoggerFactory>().Use<LoggerFactory>();
                For(typeof(ILogger<>)).Use(typeof(Logger<>));
                For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("PostRestoreEndurCLI"));
                
                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
                For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            }
            catch (Exception e)
            {
                // Log error using configured logger or fallback to console
                var loggerFactory = TryGetService<ILoggerFactory>();
                if (loggerFactory != null)
                {
                    var logger = loggerFactory.CreateLogger("CliRegistry");
                    logger.LogError(e, "Error in CliRegistry initialization");
                }
                else
                {
                    Console.Error.WriteLine($"Error in CliRegistry: {e}");
                }
                throw;
            }
        }
    }
}