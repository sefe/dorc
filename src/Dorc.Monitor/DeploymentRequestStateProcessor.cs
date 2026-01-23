using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.HighAvailability;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.Security;
using System.Collections.Concurrent;

namespace Dorc.Monitor
{
    internal class DeploymentRequestStateProcessor : IDeploymentRequestStateProcessor
    {
        private readonly ILogger logger;
        private readonly IServiceProvider serviceProvider;
        private readonly IDeploymentRequestProcessesPersistentSource processesPersistentSource;
        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IDeploymentEventsPublisher eventPublisher;
        private readonly IDistributedLockService distributedLockService;

        private DeploymentRequestDetailSerializer serializer = new DeploymentRequestDetailSerializer();

        private bool disposedValue;
        private ConcurrentDictionary<string, int> environmentRequestIdRunning = new ConcurrentDictionary<string, int>();

        private enum Methods
        {
            Cancel,
            Abandon
        }

        public DeploymentRequestStateProcessor(
            ILogger<DeploymentRequestStateProcessor> logger,
            IServiceProvider serviceProvider,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IRequestsPersistentSource requestsPersistentSource,
            IDeploymentEventsPublisher eventPublisher,
            IDistributedLockService distributedLockService)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.processesPersistentSource = processesPersistentSource;
            this.requestsPersistentSource = requestsPersistentSource;
            this.eventPublisher = eventPublisher;
            this.distributedLockService = distributedLockService;
        }

        public void AbandonRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            var requestsToAbandon = this.requestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, isProduction)
                .Where(runningRequest => runningRequest.RequestedTime != null
                    && (DateTimeOffset.Now - runningRequest.RequestedTime).Value.Days > 1)
                .OrderBy(runningRequest => runningRequest.Id)
                .Take(10)
                .ToList();

            SwitchRequestsStatus(
                requestsToAbandon,
                Methods.Abandon,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Abandoned,
                requestCancellationSources,
                monitorCancellationToken);
        }

        public void CancelRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            var requestsToCancel = this.requestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, isProduction)
                .OrderBy(cancellingRequest => cancellingRequest.Id)
                .Take(10)
                .ToList();

            SwitchRequestsStatus(
                requestsToCancel,
                Methods.Cancel,
                DeploymentRequestStatus.Cancelling,
                DeploymentRequestStatus.Cancelled,
                requestCancellationSources,
                monitorCancellationToken);

            SwitchDeploymentResultsStatus(
                requestsToCancel,
                Methods.Cancel,
                DeploymentResultStatus.Pending,
                DeploymentResultStatus.Cancelled,
                monitorCancellationToken);
        }

        /// <summary>
        /// Switches deployment request statuses using optimistic concurrency.
        ///
        /// Optimistic Concurrency Pattern:
        /// The database operation uses a WHERE clause with 'fromStatus' to ensure only requests
        /// in the expected state are updated. In a multi-monitor environment, if another monitor
        /// already processed a request (changed its status), the WHERE clause won't match and
        /// that request won't be updated. This is the expected behavior - these operations are
        /// idempotent (cancelling/abandoning a request twice has the same effect as once).
        /// When fewer requests are updated than expected, it typically means another monitor
        /// instance already processed them, which is not an error condition.
        /// </summary>
        private void SwitchRequestsStatus(List<DeploymentRequestApiModel> requests, Methods method, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            int requestToSwitchCount = requests.Count();
            var methodName = method.ToString();
            if (requestToSwitchCount > 0)
            {
                monitorCancellationToken.ThrowIfCancellationRequested();
                var ids = requests.Select(r => r.Id).ToArray();
                var idsString = string.Join(',', ids);

                this.logger.LogInformation($"Going to {methodName} the requests: [{idsString}]");

                if (requests.Any(r => r.IsProd))
                {
                    this.logger.LogError($"Cannot {methodName} the request with id '{requests.First(r => r.IsProd).Id}' because request is running on PR environment");
                    return;
                }

                foreach (var id in ids)
                {
                    TerminateRequestExecution(id, requestCancellationSources);
                };

                // Uses optimistic concurrency: only updates requests still in 'fromStatus'
                int updatedRequestCount = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(
                    requests,
                    fromStatus,
                    toStatus,
                    DateTimeOffset.Now);

                if (updatedRequestCount == requestToSwitchCount)
                {
                    // All requests were successfully updated
                    foreach (var id in ids)
                    {
                        TerminateRunnerProcesses(id);
                        // publish request status change
                        _ = this.eventPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(
                            RequestId: id,
                            Status: toStatus.ToString(),
                            StartedTime: null,
                            CompletedTime: null,
                            Timestamp: DateTimeOffset.UtcNow
                        ));
                    }
                    this.logger.LogInformation($"Requests with ids [{idsString}] are {methodName}ed.");
                }
                else if (updatedRequestCount > 0)
                {
                    // Partial success: some requests were already processed by another monitor
                    foreach (var id in ids)
                    {
                        TerminateRunnerProcesses(id);
                        _ = this.eventPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(
                            RequestId: id,
                            Status: toStatus.ToString(),
                            StartedTime: null,
                            CompletedTime: null,
                            Timestamp: DateTimeOffset.UtcNow
                        ));
                    }
                    var skippedCount = requestToSwitchCount - updatedRequestCount;
                    this.logger.LogInformation(
                        $"{updatedRequestCount} of {requestToSwitchCount} requests {methodName}ed. " +
                        $"{skippedCount} were likely already processed by another monitor instance. IDs [{idsString}]");
                }
                else
                {
                    // All requests were already processed by another monitor (optimistic concurrency)
                    this.logger.LogInformation(
                        $"None of the {requestToSwitchCount} requests were {methodName}ed - likely already processed by another monitor instance. IDs [{idsString}]");
                }
            }
        }

        private void SwitchDeploymentResultsStatus(List<DeploymentRequestApiModel> requests, Methods method, DeploymentResultStatus fromStatus, DeploymentResultStatus toStatus, CancellationToken monitorCancellationToken)
        {
            int requestToSwitchCount = requests.Count();
            var methodName = method.ToString();
            if (requestToSwitchCount > 0)
            {
                monitorCancellationToken.ThrowIfCancellationRequested();
                var ids = requests.Select(r => r.Id).ToArray();
                var idsString = string.Join(',', ids);

                this.logger.LogInformation($"Going to {methodName} the deployment results for the requests: [{idsString}]");

                int cancelledDeploymentResultsCount = this.requestsPersistentSource.SwitchDeploymentResultsStatuses(
                    requests,
                    fromStatus,
                    toStatus);

                this.logger.LogInformation($"Deployment results for requests with ids [{idsString}] are {methodName}ed.");
            }
        }

        public void RestartRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            var requestsToRestart = this.requestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, isProduction)
                .OrderBy(restartingRequest => restartingRequest.Id)
                .Take(10) // since it's bulk, more than 10 will take too much time
                .ToList();

            int requestToRestartCount = requestsToRestart.Count();
            if (requestToRestartCount > 0)
            {
                this.logger.LogInformation($"Going to restart {requestToRestartCount} requests");

                var ids = requestsToRestart.Select(c => c.Id).ToList();
                var idsString = string.Join(',', ids);
                try
                {
                    this.logger.LogDebug($"Removing All results for IDs [{idsString}].");

                    this.requestsPersistentSource.ClearAllDeploymentResults(ids);

                    this.logger.LogDebug($"Finish removing All results for IDs [{idsString}].");
                }
                catch (Exception exception)
                {
                    this.logger.LogError($"Removing All Results for IDs [{idsString}] has failed. Exception: {exception}");
                }

                monitorCancellationToken.ThrowIfCancellationRequested();

                this.logger.LogInformation($"Restarting All requests, IDs [{idsString}]");

                var pendingRequestCount = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(requestsToRestart, DeploymentRequestStatus.Restarting, DeploymentRequestStatus.Pending);

                if (pendingRequestCount == requestToRestartCount)
                {
                    ids.ForEach(id =>
                    {
                        TerminateRequestExecution(id, requestCancellationSources);
                        TerminateRunnerProcesses(id);
                        _ = this.eventPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(
                            RequestId: id,
                            Status: DeploymentRequestStatus.Pending.ToString(),
                            StartedTime: null,
                            CompletedTime: null,
                            Timestamp: DateTimeOffset.UtcNow
                        ));
                    });

                    this.logger.LogInformation($"Requests IDs [{idsString}] have been restarted.");
                }
                else
                    this.logger.LogError($"{requestToRestartCount - pendingRequestCount} requests from {requestToRestartCount} have NOT been restarted. IDs [{idsString}]");
            }
        }

        public Task[] ExecuteRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            // Select only Pending and Confirmed requests for each of environments that do not have any Running requests.
            var environmentRequestGroupsToExecute = this.requestsPersistentSource
                .GetRequestsWithStatus(
                        DeploymentRequestStatus.Pending,
                        DeploymentRequestStatus.Running,
                        DeploymentRequestStatus.Confirmed,
                        isProduction)
                .OrderBy(pendingOrRunningRequest => pendingOrRunningRequest.Id)
                .GroupBy(
                    pendingOrRunningRequest => pendingOrRunningRequest.EnvironmentName,
                    pendingOrRunningRequest => new RequestToProcessDto(
                        pendingOrRunningRequest,
                        this.serializer.Deserialize(pendingOrRunningRequest.RequestDetails)))
                .Where(environmentRequestGroup => environmentRequestGroup.All(environmentRequest =>
                    environmentRequest.Request.Status != DeploymentRequestStatus.Running.ToString()));

            IList<Task> requestGroupExecutionTasks = new List<Task>();

            foreach (var requestGroup in environmentRequestGroupsToExecute)
            {
                // We are taking just first request per environment for execution
                // in order to guarantee that requests are executed sequentially withing distinct environment.
                var requestToExecute = requestGroup.First();

                int runningRequestId;
                // if some request is already running for that env, just skip (that should not happen if DB would be quick enough)
                if (environmentRequestIdRunning.TryGetValue(requestGroup.Key, out runningRequestId))
                {
                    this.logger.LogDebug($"skipping processing deployment request for Env:{requestGroup.Key} user:{requestToExecute.Request.UserName} id: {runningRequestId}, as some request is being processed already for that env");
                    continue;
                }

                // Try to acquire distributed lock for this environment BEFORE creating the task
                // This ensures only one monitor instance processes this environment at a time
                // NOTE: This lambda is async because we need to await distributed lock acquisition and disposal.
                // Other usages of Task.Run in this codebase may be synchronous, but here async is required for proper lock handling.
                var task = Task.Run(async () =>
                {
                    // Acquire distributed lock inside the task to avoid blocking ExecuteRequests method
                    // and to prevent closure over loop variable
                    IDistributedLock? envLock = null;
                    try
                    {
                        monitorCancellationToken.ThrowIfCancellationRequested();

                        if (distributedLockService.IsEnabled)
                        {
                            var lockKey = $"env:{requestGroup.Key}";
                            // Lock lease time is longer than typical request duration to handle long deployments
                            // The lock will auto-release if the monitor crashes
                            envLock = await distributedLockService.TryAcquireLockAsync(lockKey, 300000, monitorCancellationToken);

                            if (envLock == null)
                            {
                                this.logger.LogDebug($"Could not acquire distributed lock for environment '{requestGroup.Key}' - likely being processed by another monitor instance");
                                return; // Skip this environment - another monitor is processing it
                            }

                            this.logger.LogInformation($"Acquired distributed lock for environment '{requestGroup.Key}' to process request {requestToExecute.Request.Id}");

                            // Re-validate request status after acquiring lock
                            // The request status may have changed while waiting for the lock
                            // (e.g., cancelled by user, or already picked up by another monitor before lock was held)
                            var currentRequest = this.requestsPersistentSource.GetRequest(requestToExecute.Request.Id);
                            if (currentRequest == null ||
                                (currentRequest.Status != DeploymentRequestStatus.Pending.ToString() &&
                                 currentRequest.Status != DeploymentRequestStatus.Confirmed.ToString()))
                            {
                                this.logger.LogInformation(
                                    $"Request {requestToExecute.Request.Id} status changed to '{currentRequest?.Status ?? "null"}' while waiting for lock. Skipping execution.");
                                return; // Lock will be released in finally block
                            }
                        }

                        var requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(monitorCancellationToken);
                        requestCancellationSources!.AddOrUpdate(
                            requestToExecute.Request.Id,
                            requestCancellationTokenSource,
                            (requestId, existingCancellationTokenSource) => requestCancellationTokenSource);
                        this.ExecuteRequest(requestToExecute, requestCancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        this.logger.LogError($"Canceled processing deployment request. Environment: {requestGroup.Key}, id: {requestToExecute.Request.Id}");
                    }
                    catch (Exception exc)
                    {
                        this.logger.LogError($"Error while processing deployment request. Environment: {requestGroup.Key}, id: {requestToExecute.Request.Id}, exception: {exc}");
                    }
                    finally
                    {
                        this.RemoveCancellationTokenSource(requestToExecute.Request.Id, requestCancellationSources);
                        environmentRequestIdRunning.TryRemove(requestGroup.Key, out runningRequestId);
                        
                        // Release the distributed lock
                        if (envLock != null)
                        {
                            await envLock.DisposeAsync();
                            this.logger.LogDebug($"Released distributed lock for environment '{requestGroup.Key}'");
                        }
                    }
                }, monitorCancellationToken);
                environmentRequestIdRunning.TryAdd(requestGroup.Key, requestToExecute.Request.Id);
                task.ConfigureAwait(false);
                requestGroupExecutionTasks.Add(task);
            }

            return requestGroupExecutionTasks.ToArray();
        }

        private void ExecuteRequest(RequestToProcessDto requestToExecute, CancellationToken requestCancellationToken)
        {
            this.logger.LogDebug($"---------------begin execution of request {requestToExecute.Request.Id}---------------");

            int upratedRequestCount = this.requestsPersistentSource.UpdateNonProcessedRequest(
                    requestToExecute.Request,
                DeploymentRequestStatus.Requesting,
                    DateTimeOffset.Now);

            if (upratedRequestCount == 0)
            {
                this.logger.LogInformation("The request with ID {RequestId} can NOT be processed.", requestToExecute.Request.Id);
                return;
            }

            _ = this.eventPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
            {
                Status = DeploymentRequestStatus.Requesting.ToString(),
            });

            try
            {
                // we have to create every request new provider object because of common PropertyValuesPersistentSource and VariableResolver
                var pendingRequestProcessor = this.serviceProvider.GetService(typeof(IPendingRequestProcessor)) as IPendingRequestProcessor;
                if (pendingRequestProcessor == null)
                    throw new ArgumentNullException(nameof(pendingRequestProcessor));
                pendingRequestProcessor.Execute(requestToExecute, requestCancellationToken);
            }
            catch (Exception exception)
            {
                this.logger.LogError($"Execution of the request with id '{requestToExecute.Request.Id}' has failed. Exception: {exception}");
            }

            this.logger.LogDebug($"---------------end execution of request {requestToExecute.Request.Id}---------------");
        }

        private void TerminateRequestExecution(int requestId, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources)
        {
            if (!requestCancellationSources!.TryGetValue(requestId, out CancellationTokenSource? requestCancellationTokenSource))
            {
                return;
            }

            if (requestCancellationTokenSource == null)
            {
                return;
            }

            requestCancellationTokenSource.Cancel();

            this.RemoveCancellationTokenSource(requestId, requestCancellationSources);
        }

        private void RemoveCancellationTokenSource(int requestId, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources)
        {
            if (!requestCancellationSources!.TryRemove(requestId, out CancellationTokenSource? removedCancellationTokenSource))
            {
                this.logger.LogDebug("CancellationTokenSource for request '{RequestId}' was not found (may have been removed already).", requestId);
            }

            if (removedCancellationTokenSource is not null)
            {
                try
                {
                    removedCancellationTokenSource?.Dispose();
                }
                catch { } // safe dispose as it may be already disposed
            }
        }

        /// <summary>
        /// Runner process ids are stored in DB for the durability reason.
        /// If DOrc crashes the handle to the processes can be restored and closed appropriately later.
        /// </summary>
        /// <param name="requestId"></param>
        private void TerminateRunnerProcesses(int requestId)
        {
            var processIds = this.processesPersistentSource.GetAssociatedRunnerProcessIds(requestId);
            this.logger.LogInformation($"Processes for the {requestId} that should be killed {String.Join(",", processIds)}");

            if (processIds == null
                || !processIds.Any())
            {
                return;
            }

            foreach (var processId in processIds)
            {
                var procById = GetProcByID(processId);
                if (procById != null)
                {
                    try
                    {
                        procById.Kill();
                        this.logger.LogInformation($"Process {processId} is killed");
                    }
                    catch (Exception exception)
                    {
                        this.logger.LogError(exception, "Termination of the process with id '{ProcessId}' failed.", processId);
                    }
                }
                this.processesPersistentSource.RemoveProcess(processId);
            }
        }

        private static System.Diagnostics.Process? GetProcByID(int id)
        {
            System.Diagnostics.Process[] processlist = System.Diagnostics.Process.GetProcesses();
            return processlist.FirstOrDefault(pr => pr.Id == id);
        }
    }
}
