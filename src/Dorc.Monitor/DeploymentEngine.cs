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
        private readonly List<Task> _runningTasks = new();

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
                await Task.Delay(iterationDelayMs);
            }

            // Graceful shutdown: wait for all in-progress deployments to complete
            if (_runningTasks.Count > 0)
            {
                var shutdownTimeout = TimeSpan.FromSeconds(configuration.GracefulShutdownTimeoutSeconds);
                logger.LogInformation("Graceful shutdown: waiting up to {TimeoutSeconds}s for {RunningCount} in-progress deployments to complete...",
                    configuration.GracefulShutdownTimeoutSeconds, _runningTasks.Count);
                try
                {
                    var completedInTime = await Task.WhenAll(_runningTasks).WaitAsync(shutdownTimeout, CancellationToken.None)
                        .ContinueWith(t => !t.IsFaulted && !t.IsCanceled);

                    if (completedInTime)
                    {
                        logger.LogInformation("All in-progress deployments completed successfully during graceful shutdown");
                    }
                    else
                    {
                        logger.LogWarning("Graceful shutdown: some deployments finished with errors but all tasks completed");
                    }
                }
                catch (TimeoutException)
                {
                    var remainingCount = _runningTasks.Count(t => !t.IsCompleted);
                    logger.LogWarning(
                        "Graceful shutdown timeout ({TimeoutSeconds}s): {RemainingCount} deployments still running. " +
                        "These will be automatically recovered and re-queued on next service startup.",
                        configuration.GracefulShutdownTimeoutSeconds, remainingCount);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during graceful shutdown while waiting for deployments");
                }
            }
        }
    }
}
