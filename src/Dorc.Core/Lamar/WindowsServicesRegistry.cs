using System.Runtime.Versioning;
using Dorc.Core.Interfaces;
using Lamar;

namespace Dorc.Core.Lamar
{
    [SupportedOSPlatform("windows")]
    public class WindowsServicesRegistry : ServiceRegistry
    {
        public WindowsServicesRegistry()
        {
            For<IServiceStatus>().Use<ServiceStatus>();
        }
    }
}
