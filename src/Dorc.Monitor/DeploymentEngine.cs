using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    public interface IDeploymentEngine
    {
        Task ProcessDeploymentRequestsAsync(
            bool isProduction,
            ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources,
            CancellationToken monitorCancellationToken,
            int iterationDelayMs);
    }

    public class DeploymentEngine : IDeploymentEngine
    {
        private readonly ILogger logger;
        private readonly IDeploymentRequestStateProcessor deploymentRequestStateProcessor;
        private readonly IMonitorConfiguration configuration;
        /// <summary>
        /// Tracks in-flight deployment tasks. Only accessed from the single ProcessDeploymentRequestsAsync
        /// loop (no concurrent callers), so a plain List is safe here - no synchronization needed.
        /// </summary>
        private readonly List<Task> _runningTasks = new();

        /// <summary>
        /// After this many consecutive unexpected (non-transient) iteration
        /// failures, the loop rethrows so the host stops and orchestration can
        /// restart it, rather than spinning forever on a genuine fault.
        /// </summary>
        internal const int MaxConsecutiveUnexpectedFailures = 5;
        private static readonly TimeSpan TransientRetryBackoff = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MaxUnexpectedBackoff = TimeSpan.FromSeconds(60);

        public DeploymentEngine(
            ILogger<DeploymentEngine> logger,
            IDeploymentRequestStateProcessor deploymentRequestStateProcessor,
            IMonitorConfiguration configuration
            )
        {
            this.logger = logger;
            this.deploymentRequestStateProcessor = deploymentRequestStateProcessor;
            this.configuration = configuration;
        }

        public async Task ProcessDeploymentRequestsAsync(
            bool isProduction,
            ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources,
            CancellationToken monitorCancellationToken,
            int iterationDelayMs)
        {
            var maxConcurrent = configuration.MaxConcurrentDeployments;
            var consecutiveUnexpectedFailures = 0;

            while (!monitorCancellationToken.IsCancellationRequested)
            {
                try
                {
                    monitorCancellationToken.ThrowIfCancellationRequested();

                    deploymentRequestStateProcessor.AbandonRequests(isProduction, requestCancellationSources, monitorCancellationToken);

                    monitorCancellationToken.ThrowIfCancellationRequested();

                    deploymentRequestStateProcessor.CancelRequests(isProduction, requestCancellationSources, monitorCancellationToken);

                    monitorCancellationToken.ThrowIfCancellationRequested();

                    deploymentRequestStateProcessor.RestartRequests(isProduction, requestCancellationSources, monitorCancellationToken);

                    monitorCancellationToken.ThrowIfCancellationRequested();

                    // Remove completed tasks before starting new ones
                    _runningTasks.RemoveAll(t => t.IsCompleted);

                    // Implement backpressure: if at max capacity, wait for at least one task to complete
                    if (maxConcurrent > 0 && _runningTasks.Count >= maxConcurrent)
                    {
                        logger.LogDebug("At max concurrent deployments ({MaxConcurrent}), waiting for a deployment to complete", maxConcurrent);
                        await Task.WhenAny(_runningTasks);
                        _runningTasks.RemoveAll(t => t.IsCompleted);
                    }

                    // Execute new requests and track the returned tasks
                    var newTasks = deploymentRequestStateProcessor.ExecuteRequests(isProduction, requestCancellationSources, monitorCancellationToken);
                    if (newTasks.Length > 0)
                    {
                        _runningTasks.AddRange(newTasks);
                        logger.LogDebug("Started {NewTaskCount} new deployment tasks. Total running: {TotalRunning}",
                            newTasks.Length, _runningTasks.Count);
                    }

                    // A clean iteration resets the unexpected-failure circuit breaker.
                    consecutiveUnexpectedFailures = 0;
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogInformation(ex, "Deployment Requests processing is cancelled. Waiting for {RunningCount} in-progress deployments to complete...",
                        _runningTasks.Count);
                    break;
                }
                catch (Exception ex) when (IsTransientException(ex))
                {
                    // Transient data-access failures (e.g. a SqlException, or a
                    // RetryLimitExceededException wrapping one after EF retries are
                    // exhausted) must NOT stop the whole service — log and continue.
                    // Handling this in the inner loop preserves the _runningTasks
                    // state for in-flight deployments.
                    logger.LogWarning(ex, "Transient failure during deployment request processing; retrying after {BackoffSeconds}s.",
                        TransientRetryBackoff.TotalSeconds);
                    await Task.Delay(TransientRetryBackoff, monitorCancellationToken);
                    continue;
                }
                catch (Exception ex)
                {
                    consecutiveUnexpectedFailures++;
                    if (consecutiveUnexpectedFailures >= MaxConsecutiveUnexpectedFailures)
                    {
                        logger.LogCritical(ex,
                            "Deployment request processing failed {Count} times consecutively; stopping the service so it can be restarted.",
                            consecutiveUnexpectedFailures);
                        throw;
                    }

                    var backoff = ComputeUnexpectedBackoff(consecutiveUnexpectedFailures);
                    logger.LogError(ex,
                        "Unexpected failure during deployment request processing ({Count}/{Max}); backing off {BackoffSeconds}s and continuing.",
                        consecutiveUnexpectedFailures, MaxConsecutiveUnexpectedFailures, backoff.TotalSeconds);
                    await Task.Delay(backoff, monitorCancellationToken);
                    continue;
                }

                // Manual garbage collecting between deployment requests necessary to unload stored resources that remains after Roslyn
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                await Task.Delay(iterationDelayMs);
            }

            // Graceful shutdown: wait for all in-flight deployments to complete.
            // The host's ShutdownTimeout (30s) is the single controlling timeout — no internal
            // deadline is set here. If the host forces exit before all tasks finish, S-004 recovers
            // any requests still in Running state on the next startup.
            await WaitForInFlightDeploymentsAsync();
        }

        private static TimeSpan ComputeUnexpectedBackoff(int consecutiveFailures)
        {
            // Exponential backoff capped at MaxUnexpectedBackoff: 5s, 10s, 20s, ...
            var seconds = 5.0 * Math.Pow(2, Math.Max(0, consecutiveFailures - 1));
            var capped = Math.Min(seconds, MaxUnexpectedBackoff.TotalSeconds);
            return TimeSpan.FromSeconds(capped);
        }

        /// <summary>
        /// Classifies an exception as a transient data-access failure that the
        /// processing loop should retry rather than fatally stop on. Walks the
        /// inner-exception chain so a wrapper (e.g. RetryLimitExceededException,
        /// which EF throws with a SqlException inner once retries are exhausted) is
        /// correctly recognised — the previous code only caught a bare SqlException
        /// and let the wrapper stop the entire service.
        /// </summary>
        internal static bool IsTransientException(Exception? ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is SqlException || current is TimeoutException)
                {
                    return true;
                }

                var typeName = current.GetType().Name;
                if (typeName == "RetryLimitExceededException" || typeName == "SqlException")
                {
                    return true;
                }
            }

            return false;
        }

        private async Task WaitForInFlightDeploymentsAsync()
        {
            if (_runningTasks.Count > 0)
            {
                var shutdownCount = _runningTasks.Count;
                logger.LogInformation("Graceful shutdown: waiting for {RunningCount} in-progress deployment(s) to complete...", shutdownCount);
                try
                {
                    await Task.WhenAll(_runningTasks);
                    logger.LogInformation("Graceful shutdown: all {Count} in-progress deployment(s) completed.", shutdownCount);
                }
                catch (Exception ex)
                {
                    var stillRunning = _runningTasks.Count(t => !t.IsCompleted);
                    logger.LogWarning(ex,
                        "Graceful shutdown: {Completed} of {Total} deployment(s) completed; {StillRunning} still running.",
                        shutdownCount - stillRunning, shutdownCount, stillRunning);
                }
            }
        }
    }
}
