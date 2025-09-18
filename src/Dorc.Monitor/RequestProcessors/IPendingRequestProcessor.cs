namespace Dorc.Monitor.RequestProcessors
{
    public interface IPendingRequestProcessor
    {
        public Task ExecuteAsync(RequestToProcessDto requestToExecute, CancellationToken cancellationToken);
    }
}
