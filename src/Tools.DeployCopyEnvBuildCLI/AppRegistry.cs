using Dorc.Core.Interfaces;
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
            For<ILoggerFactory>().Use(_ => LoggerFactory.Create(builder => builder.AddConsole()));
            For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("DeployCopyEnvBuildCLI"));
            
            For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            For<IDeploymentEventsPublisher>().Use<NullDeploymentEventsPublisher>();
        }
    }
}
