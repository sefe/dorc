using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface IServiceStatus
    {
        List<ServicesAndStatus> GetServicesAndStatus(string envName, ClaimsPrincipal principal);

        /// <summary>
        ///     Make action with the servicesAndStatus. Actions may be: start, stop, restart. Returns new servicesAndStatus status.
        /// </summary>
        /// <param name="servicesAndStatus"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        ServicesAndStatus? ChangeServiceState(ServicesAndStatus servicesAndStatus, ClaimsPrincipal principal);

        List<ServicesAndStatus> GetServicesAndStatus(int envId);
    }
}