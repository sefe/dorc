namespace Dorc.Api.Exceptions
{
    // Thrown by WorkerUnavailableClient to signal that a Windows-only operation was
    // attempted on a host without the Windows worker. The global
    // WorkerUnavailableExceptionFilter catches this and renders the documented 503
    // body { "error": "windows_worker_unavailable", "endpoint": "<name>" }.
    public class WorkerUnavailableException : Exception
    {
        public string Endpoint { get; }

        public WorkerUnavailableException(string endpoint)
            : base($"Operation '{endpoint}' requires the Windows worker, which is not available on this host.")
        {
            Endpoint = endpoint;
        }
    }
}
