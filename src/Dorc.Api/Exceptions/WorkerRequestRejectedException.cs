namespace Dorc.Api.Exceptions
{
    // The Windows worker answered 400: the request itself was rejected (e.g. the target
    // machine could not be opened), as opposed to the worker being unavailable. Rendered
    // as a 400 by WorkerUnavailableExceptionFilter so the caller keeps the actionable
    // error the endpoint documented, instead of an opaque 500.
    public class WorkerRequestRejectedException : Exception
    {
        public WorkerRequestRejectedException(string detail)
            : base(string.IsNullOrWhiteSpace(detail) ? "The Windows worker rejected the request." : detail)
        {
        }
    }
}
