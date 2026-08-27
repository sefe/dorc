using Dorc.ApiModel;

namespace Dorc.Api.Interfaces
{
    // Seam for the Linux-incompatible Windows-worker calls. Per HLPS-api-split D-1/D-3
    // and SPEC-S-003. Concrete methods are added by later S-steps as endpoints move:
    //   S-004 — registry/remote-server probing (this contract)
    //   S-005 — WMI service status
    //   S-006 — password reset impersonation
    //
    // Two implementations exist:
    //   - HttpWindowsWorkerClient: real HTTP loopback caller (Windows installs;
    //     WindowsWorker:Enabled=true).
    //   - WorkerUnavailableClient: throws WorkerUnavailableException so the global
    //     WorkerUnavailableExceptionFilter translates it to a documented 503 body
    //     (Linux installs; WindowsWorker:Enabled=false).
    public interface IWindowsWorkerClient
    {
        // S-004 (HLPS Scope D — registry probe move). Reads the target server's
        // ProductName + CurrentVersion from its remote Windows registry. Worker-side
        // implementation lives in Dorc.Api.WindowsWorker/Controllers/RemoteServerController.cs.
        Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(string serverName, CancellationToken cancellationToken = default);

        // S-005 (HLPS Scope D — WMI/service-control move). Probes service status for the
        // supplied daemons, impersonated as the deploy credential when one is present.
        // Worker-side implementation lives in Dorc.Api.WindowsWorker/Controllers/DaemonsController.cs.
        Task<List<WorkerDaemonApiModel>> ProbeDaemonStatusesAsync(WorkerDaemonProbeRequestApiModel request, CancellationToken cancellationToken = default);

        // S-005. Starts/stops/restarts a daemon (request.Daemon.Status carries the action)
        // and returns the resulting status.
        Task<WorkerDaemonApiModel> ChangeDaemonStateAsync(WorkerDaemonStateChangeRequestApiModel request, CancellationToken cancellationToken = default);

        // S-005. Reboots the target server via WMI (moved from WmiUtil in Dorc.Api).
        Task RebootServerAsync(string serverName, CancellationToken cancellationToken = default);

        // S-006 (HLPS Scope D — password-reset impersonation move). Resets the SQL login's
        // password on the target server as the worker's own service account. Worker-side
        // implementation lives in Dorc.Api.WindowsWorker/Controllers/PasswordResetController.cs.
        Task<ApiBoolResult> ResetAppPasswordAsync(WorkerPasswordResetRequestApiModel request, CancellationToken cancellationToken = default);
    }
}
