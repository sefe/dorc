using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Orchestration
{
    /// <summary>
    /// Orchestrates Windows service status operations
    /// </summary>
    public class WindowsServicesOrchestrator
    {
        private readonly IServiceStatus _serviceStatus;

        public WindowsServicesOrchestrator(IServiceStatus serviceStatus)
        {
            _serviceStatus = serviceStatus;
        }

        public List<ServiceStatusApiModel> GetEnvDaemonsStatuses(string envName, ClaimsPrincipal principal)
        {
            return _serviceStatus.GetServicesAndStatus(envName, principal).Select(MapToServiceStatusApiModel).ToList();
        }

        public List<ServiceStatusApiModel> GetEnvDaemonsStatuses(int envId)
        {
            return _serviceStatus.GetServicesAndStatus(envId).Select(MapToServiceStatusApiModel).ToList();
        }

        public ServiceStatusApiModel ChangeServiceState(ServiceStatusApiModel daemon, ClaimsPrincipal principal)
        {
            return MapToServiceStatusApiModel(_serviceStatus.ChangeServiceState(MapToServicesAndStatus(daemon), principal));
        }

        private ServicesAndStatus MapToServicesAndStatus(ServiceStatusApiModel ss)
        {
            return new ServicesAndStatus
            {
                EnvName = ss.EnvName,
                ServerName = ss.ServerName,
                ServiceName = ss.ServiceName,
                ServiceStatus = ss.ServiceStatus
            };
        }

        private ServiceStatusApiModel MapToServiceStatusApiModel(ServicesAndStatus ss)
        {
            return new ServiceStatusApiModel
            {
                EnvName = ss.EnvName,
                ServerName = ss.ServerName,
                ServiceName = ss.ServiceName,
                ServiceStatus = ss.ServiceStatus
            };
        }
    }
}
