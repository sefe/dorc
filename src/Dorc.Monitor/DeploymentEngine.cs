using Dorc.Core.Events;
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
        private readonly IRequestPollSignal pollSignal;
        /// <summary>
        /// Tracks in-flight deployment tasks. Only accessed from the single ProcessDeploymentRequestsAsync
        /// loop (no concurrent callers), so a plain List is safe here - no synchronization needed.
        /// </summary>
        private readonly List<Task> _runningTasks = new();

        public DeploymentEngine(
            ILogger<DeploymentEngine> logger,
            IDeploymentRequestStateProcessor deploymentRequestStateProcessor,
            IMonitorConfiguration configuration,
            IRequestPollSignal pollSignal
            )
        {
            this.logger = logger;
            this.deploymentRequestStateProcessor = deploymentRequestStateProcessor;
            this.configuration = configuration;
            this.pollSignal = pollSignal;
        }

        public async Task ProcessDeploymentRequestsAsync(
            bool isProduction,
            ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources,
            CancellationToken monitorCancellationToken,
            int iterationDelayMs)
        {
            var maxConcurrent = configuration.MaxConcurrentDeployments;

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
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogInformation(ex, "Deployment Requests processing is cancelled. Waiting for {RunningCount} in-progress deployments to complete...",
                        _runningTasks.Count);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Deployment Requests processing is failed.");
                    throw;
                }

                // Manual garbage collecting between deployment requests necessary to unload stored resources that remains after Roslyn
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                // Wait on the request-poll signal capped at the configured
                // iteration delay. The Kafka request-lifecycle consumer raises
                // the signal on incoming events so this wait short-circuits.
                try
                {
                    await pollSignal.WaitAsync(TimeSpan.FromMilliseconds(iterationDelayMs), monitorCancellationToken);
                }
                catch (OperationCanceledException) { /* shutdown — handled by while-loop check */ }
            }

            // Graceful shutdown: wait for all in-flight deployments to complete.
            // The host's ShutdownTimeout (30s) is the single controlling timeout — no internal
            // deadline is set here. If the host forces exit before all tasks finish, S-004 recovers
            // any requests still in Running state on the next startup.
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
