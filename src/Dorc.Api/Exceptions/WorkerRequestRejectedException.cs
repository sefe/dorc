namespace Dorc.Api.Exceptions
{
    public class WorkerRequestRejectedException : Exception
    {
        public WorkerRequestRejectedException(string detail)
            : base(string.IsNullOrWhiteSpace(detail)
                ? "The Windows worker rejected the request."
                : detail)
        {
        }
    }
}
