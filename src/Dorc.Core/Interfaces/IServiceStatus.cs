using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface IServiceStatus
    {
        List<DaemonStatus> GetDaemonStatuses(string envName, ClaimsPrincipal principal);

        List<DaemonStatus> GetDaemonStatuses(int envId);

        /// <summary>
        ///     Make action on a daemon. Actions may be: start, stop, restart. Returns the new daemon status.
        /// </summary>
        DaemonStatus? ChangeDaemonState(DaemonStatus daemonStatus, ClaimsPrincipal principal);
    }
}
