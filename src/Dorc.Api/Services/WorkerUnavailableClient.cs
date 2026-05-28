using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    // Null implementation of IWindowsWorkerClient used on Linux installs (or any
    // host with WindowsWorker:Enabled=false). Every method throws
    // WorkerUnavailableException with an endpoint name matching the route segment
    // exposed on the worker, so WorkerUnavailableExceptionFilter renders the
    // documented 503 body { "error": "windows_worker_unavailable", "endpoint": "..." }.
    public class WorkerUnavailableClient : IWindowsWorkerClient
    {
        public Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(string serverName, CancellationToken cancellationToken = default)
            => throw new WorkerUnavailableException("remote-server/operating-system");
    }
}
