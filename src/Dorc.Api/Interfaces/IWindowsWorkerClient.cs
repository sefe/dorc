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
    }
}
