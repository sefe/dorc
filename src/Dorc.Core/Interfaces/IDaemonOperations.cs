using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// The Windows-only half of the daemon-status path (S-005): service-status probing and
    /// service control, both of which need ServiceController and LogonUser impersonation.
    /// The platform-neutral orchestration (building the daemon list from the database,
    /// recording observations, discovery mapping) stays in DaemonStatusProbe; this seam is
    /// implemented in Dorc.Api by a client that delegates to the Windows worker.
    /// </summary>
    public interface IDaemonOperations
    {
        /// <summary>
        /// Probe service status for each daemon. A null credential probes as the current
        /// process identity; otherwise the probe runs impersonated as the deploy account.
        /// Returns only the daemons that answered (unreachable servers are omitted), in a
        /// stable order.
        /// </summary>
        List<DaemonStatus> ProbeStatuses(WorkerDaemonCredentialApiModel? credential, List<DaemonStatus> daemons);

        /// <summary>
        /// Start/stop/restart a daemon (daemonStatus.Status carries the requested action)
        /// impersonated as the deploy account, and return the resulting status.
        /// </summary>
        DaemonStatus? ChangeState(WorkerDaemonCredentialApiModel? credential, DaemonStatus daemonStatus);
    }
}
