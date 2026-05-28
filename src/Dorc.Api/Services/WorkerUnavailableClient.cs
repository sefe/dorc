using Dorc.Api.Interfaces;

namespace Dorc.Api.Services
{
    // Null implementation of IWindowsWorkerClient used on Linux installs (or any
    // host with WindowsWorker:Enabled=false). Every method on the interface (none
    // yet, see SPEC-S-003 §2.1) throws WorkerUnavailableException, which the
    // WorkerUnavailableExceptionFilter translates to the documented 503 body.
    //
    // Later S-steps that add methods to IWindowsWorkerClient must add matching
    // throwing overrides here. Keep the throw site name (the endpoint argument)
    // identical to the route segment so the 503 body's `endpoint` field is useful.
    public class WorkerUnavailableClient : IWindowsWorkerClient
    {
    }
}
