using Dorc.Core;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Lamar;
using Microsoft.Extensions.Logging;

namespace Tools.DeployCopyEnvBuildCLI
{
    public class AppRegistry : ServiceRegistry
    {
        public AppRegistry()
        {
            // Configure ILogger from DI container
            For<ILoggerFactory>().Use<LoggerFactory>();
            For(typeof(ILogger<>)).Use(typeof(Logger<>));
            For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("DeployCopyEnvBuildCLI"));
            
            For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
        }
    }
}
