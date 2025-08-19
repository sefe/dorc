﻿using log4net;
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
        private readonly ILog logger;
        private readonly IDeploymentRequestStateProcessor deploymentRequestStateProcessor;
        private readonly IServiceProvider serviceProvider;

        public DeploymentEngine(
            ILog logger,
            IDeploymentRequestStateProcessor deploymentRequestStateProcessor,
            IServiceProvider serviceProvider
            )
        {
            this.logger = logger;
            this.deploymentRequestStateProcessor = deploymentRequestStateProcessor;
            this.serviceProvider = serviceProvider;
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

                    deploymentRequestStateProcessor.ExecuteRequests(isProduction, requestCancellationSources, monitorCancellationToken);

                    monitorCancellationToken.ThrowIfCancellationRequested();

                    // Process confirmed Terraform plans
                    //var terraformConfirmedPlanProcessor = serviceProvider.GetService(typeof(TerraformConfirmedPlanProcessor)) as TerraformConfirmedPlanProcessor;
                    //if (terraformConfirmedPlanProcessor != null)
                    //{
                    //    await terraformConfirmedPlanProcessor.ProcessConfirmedPlansAsync(monitorCancellationToken);
                    //}
                }
                catch (OperationCanceledException ex)
                {
                    logger.Error("Deployment Requests processing is cancelled. Exception: ", ex);
                    break;
                }
                catch (Exception ex)
                {
                    logger.Error("Deployment Requests processing is failed. Exception: ", ex);
                    throw;
                }

                // Manual garbage collecting between deployment requests necessary to unload stored resources that remains after Roslyn
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                await Task.Delay(iterationDelayMs);
            }
        }
    }
}
