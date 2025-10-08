using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    public sealed class MonitorService : BackgroundService
    {
        private readonly ILog logger;

        private bool isProduction;
        private readonly IServiceProvider serviceProvider;
        private readonly IMonitorConfiguration configuration;

        private ConcurrentDictionary<int, CancellationTokenSource>? requestCancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

        private bool disposedValue;
        private int requestProcessingIterationDelayMs;

        public MonitorService(
            ILog logger,
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
            logger.Info("Deployment Monitor service is started.");

            try
            {
                var deploymentEngine = serviceProvider.GetService(typeof(IDeploymentEngine)) as IDeploymentEngine;
                if (deploymentEngine == null)
                {
                    throw new NullReferenceException("DeploymentEngine was not issued and equals null");
                }

                await deploymentEngine.ProcessDeploymentRequestsAsync(isProduction, requestCancellationSources!, monitorCancellationToken, requestProcessingIterationDelayMs);
            }
            catch (OperationCanceledException operationCanceledException) when (monitorCancellationToken.IsCancellationRequested)
            {
                logger.Warn("Monitor process is cancelled. " + operationCanceledException.Message);
            }
            catch (Exception exception)
            {
                logger.Error("Monitor process is failed. Exception: " + exception);
                throw;
            }
            finally
            {
                logger.Info("Deployment Monitor service is stopping.");
            }
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
