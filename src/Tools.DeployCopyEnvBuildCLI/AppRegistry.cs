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
            // TODO: Configure ILogger from DI container
            For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
        }
    }
}
