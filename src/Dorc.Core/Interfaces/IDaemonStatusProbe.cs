using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface IDaemonStatusProbe
    {
        List<DaemonStatus> GetDaemonStatuses(string envName, ClaimsPrincipal principal);
        List<DaemonStatus> GetDaemonStatuses(int envId);

        /// <summary>
        /// Act on a daemon. Actions may be: start, stop, restart. Returns the new daemon status.
        /// </summary>
        DaemonStatus? ChangeDaemonState(DaemonStatus daemonStatus, ClaimsPrincipal principal);

        /// <summary>
        /// Discover all daemons on environment servers and automatically create daemon-server mappings.
        /// Unlike GetDaemonStatuses, this ALWAYS checks ALL daemons, ignoring existing mappings,
        /// and creates new mappings in the database for discovered daemons.
        /// </summary>
        DiscoverDaemonsResult DiscoverAndMapDaemons(string envName, ClaimsPrincipal principal);
    }
}
