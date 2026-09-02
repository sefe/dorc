using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.RequestProcessors
{
    public interface IPendingRequestProcessor
    {
        public void Execute(RequestToProcessDto requestToExecute, CancellationToken cancellationToken, ILoggerFactory loggerFactory);
    }
}
