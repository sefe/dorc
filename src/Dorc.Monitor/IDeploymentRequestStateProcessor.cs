using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    public interface IDeploymentRequestStateProcessor
    {
        /// <summary>
        /// Recovers deployment requests that were left in Running or Requesting state
        /// from a previous service instance that crashed or was restarted.
        /// Resets them to Pending so they will be re-processed.
        /// </summary>
        Task RecoverOrphanedRequestsAsync(bool isProduction);

        public void AbandonRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public void CancelRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public void RestartRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);

        public Task[] ExecuteRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken);
    }
}
