using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    public sealed class MonitorService : BackgroundService
    {
        private readonly ILogger logger;

        private bool isProduction;
        private readonly IServiceProvider serviceProvider;
        private readonly IMonitorConfiguration configuration;

        private ConcurrentDictionary<int, CancellationTokenSource>? requestCancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

        private bool disposedValue;
        private int requestProcessingIterationDelayMs;

        public MonitorService(
            ILogger<MonitorService> logger,
            IServiceProvider serviceProvider,
            IMonitorConfiguration configuration
            )
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.configuration = configuration;
        }

        public override Task StartAsync(CancellationToken monitorCancellationToken)
        {
            isProduction = configuration.IsProduction;
            requestProcessingIterationDelayMs = configuration.RequestProcessingIterationDelayMs;

            return base.StartAsync(monitorCancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken monitorCancellationToken)
        {
            logger.LogInformation("Deployment Monitor service is started.");

            var deploymentEngine = serviceProvider.GetService(typeof(IDeploymentEngine)) as IDeploymentEngine;
            if (deploymentEngine == null)
            {
                throw new NullReferenceException("DeploymentEngine was not issued and equals null");
            }

            // Recover orphaned deployment requests from a previous service instance
            // that may have crashed or been restarted while deployments were in progress
            try
            {
                var stateProcessor = serviceProvider.GetService(typeof(IDeploymentRequestStateProcessor)) as IDeploymentRequestStateProcessor;
                if (stateProcessor != null)
                {
                    await stateProcessor.RecoverOrphanedRequestsAsync(isProduction);
                }
                else
                {
                    logger.LogWarning("Could not resolve IDeploymentRequestStateProcessor for startup recovery.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover orphaned deployment requests during startup. " +
                    "Continuing with normal operation. Orphaned requests will be abandoned after 1 day.");
            }

            while (!monitorCancellationToken.IsCancellationRequested)
            {
                try
                {
                    await deploymentEngine.ProcessDeploymentRequestsAsync(isProduction, requestCancellationSources!, monitorCancellationToken, requestProcessingIterationDelayMs);
                }
                catch (OperationCanceledException operationCanceledException) when (monitorCancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Monitor process is cancelled. " + operationCanceledException.Message);
                }
                catch (SqlException sqlException)
                {
                    logger.LogWarning("Transient SQL failure, retrying in 10s. Exception: " + sqlException);
                    await Task.Delay(TimeSpan.FromSeconds(10), monitorCancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogError("Monitor process is failed. Exception: " + exception);
                    throw;
                }
            }

            logger.LogInformation("Deployment Monitor service is stopping.");
        }

        public override void Dispose()
        {
            if (!disposedValue)
            {

                foreach (var requestCancellationSource in requestCancellationSources!)
                {
                    requestCancellationSource.Value.Dispose();
                }

                requestCancellationSources = null;
                disposedValue = true;
            }

            base.Dispose();
        }
    }
}
