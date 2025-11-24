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

        public DeploymentEngine(
            ILogger<DeploymentEngine> logger,
            IDeploymentRequestStateProcessor deploymentRequestStateProcessor
            )
        {
            this.logger = logger;
            this.deploymentRequestStateProcessor = deploymentRequestStateProcessor;
        }

        public async Task ProcessDeploymentRequestsAsync(
            bool isProduction,
            ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources,
            CancellationToken monitorCancellationToken,
            int iterationDelayMs)
        {
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

                    await deploymentRequestStateProcessor.ExecuteRequests(isProduction, requestCancellationSources, monitorCancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogError(ex, "Deployment Requests processing is cancelled.");
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
        }
    }
}
