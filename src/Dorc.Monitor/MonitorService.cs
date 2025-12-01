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


        private static readonly int[] TransientErrors = { 596, 4060, 10928, 10929, 40197, 40501, 40613 };

        private static bool IsTransient(SqlException ex) => TransientErrors.Contains(ex.Number);

        private static bool ShouldClearPool(SqlException ex) => ex.Number == 596;

        private async Task<IDeploymentEngine> CreateDeploymentEngineWithRetryAsync(CancellationToken token)
        {
            const int maxAttempts = 5;
            const int delaySeconds = 2;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var engine = serviceProvider.GetService(typeof(IDeploymentEngine)) as IDeploymentEngine;
                    if (engine == null)
                        throw new NullReferenceException("DeploymentEngine is null");

                    return engine;
                }
                catch (SqlException ex) when (IsTransient(ex))
                {
                    if (ShouldClearPool(ex)) SqlConnection.ClearAllPools();

                    logger.LogWarning("Startup SQL failed (attempt {Attempt}/{Max}, code {Code}). Retrying in {Delay}s...",
                        attempt, maxAttempts, ex.Number, delaySeconds);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
            }

            throw new InvalidOperationException("Failed to initialize DeploymentEngine after retries.");
        }


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

                catch (SqlException sqlEx) when (IsTransient(sqlEx))
                {
                    logger.LogWarning("Transient SQL error {ErrorNumber}. Retrying...", sqlEx.Number);


                    if (ShouldClearPool(sqlEx))
                    {
                        SqlConnection.ClearAllPools();
                        logger.LogInformation("Cleared SQL connection pools due to error 596.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), monitorCancellationToken);
                    deploymentEngine = await CreateDeploymentEngineWithRetryAsync(monitorCancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError("Fatal error in MonitorService: {Exception}", ex);
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
