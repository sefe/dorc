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
                For<ILoggerFactory>().Use(_ => LoggerFactory.Create(builder => builder.AddConsole()));
                For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("PostRestoreEndurCLI"));

                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
                For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error in CliRegistry: {e}");
                throw;
            }
        }
    }
}