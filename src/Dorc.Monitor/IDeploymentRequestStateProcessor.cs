using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    public interface IDeploymentRequestStateProcessor
    {
        public void AbandonRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public void CancelRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public void RestartRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public Task<Task[]> ExecuteRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);
    }
}
