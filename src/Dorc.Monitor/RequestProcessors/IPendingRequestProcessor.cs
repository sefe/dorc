namespace Dorc.Monitor.RequestProcessors
{
    public interface IPendingRequestProcessor
    {
        public void Execute(RequestToProcessDto requestToExecute, CancellationToken cancellationToken);
    }
}
