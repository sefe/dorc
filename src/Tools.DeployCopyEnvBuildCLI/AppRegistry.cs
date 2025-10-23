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
            For<ILog>().Use(LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType));
            For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
        }
    }
}
