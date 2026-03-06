using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.HighAvailability;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
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
        private readonly ConcurrentDictionary<string, DateTime> environmentLockBackoff = new ConcurrentDictionary<string, DateTime>();
        private static readonly TimeSpan LockBackoffDuration = TimeSpan.FromSeconds(30);
        private const int EnvironmentLockLeaseTimeMs = 300000;

        // Test hook: invoked when a fire-and-forget publish task is created.
        // Null in production (zero overhead). Tests assign a callback to collect tasks
        // for deterministic awaiting instead of fragile Task.Delay.
        internal Action<Task>? OnPublishTaskCreated;

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

        /// <summary>
        /// Publishes a request status changed event in a fire-and-forget manner with error handling.
        /// Logs a warning if the publish fails instead of letting the exception go unobserved.
        /// </summary>
        private void PublishRequestStatusChangedSafe(DeploymentRequestEventData eventData)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await this.eventPublisher.PublishRequestStatusChangedAsync(eventData);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to publish status event for request {RequestId}", eventData.RequestId);
                }
            });
            OnPublishTaskCreated?.Invoke(task);
        }

        // NOTE: AbandonRequests handles truly stale requests (>24 hours in Running state).
        // In a dual-node HA scenario where BOTH monitors crash simultaneously, requests
        // will remain in Running state until this 24-hour threshold is reached. This is a
        // known limitation - accepted as a reasonable trade-off vs. the risk of incorrectly
        // cancelling another node's in-flight deployments.
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

            int updatedCount = SwitchRequestsStatus(
                requestsToCancel,
                Methods.Cancel,
                DeploymentRequestStatus.Cancelling,
                DeploymentRequestStatus.Cancelled,
                requestCancellationSources,
                monitorCancellationToken);

            if (updatedCount > 0)
            {
                SwitchDeploymentResultsStatus(
                    requestsToCancel,
                    Methods.Cancel,
                    DeploymentResultStatus.Pending,
                    DeploymentResultStatus.Cancelled,
                    monitorCancellationToken);
            }
        }

        public void CancelStaleRequests(bool isProduction)
        {
            var staleStatuses = new[] { DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting };

            foreach (var status in staleStatuses)
            {
                var staleRequests = this.requestsPersistentSource
                    .GetRequestsWithStatus(status, isProduction)
                    .ToList();

                if (staleRequests.Count == 0)
                    continue;

                if (distributedLockService.IsEnabled)
                {
                    foreach (var environmentGroup in staleRequests.GroupBy(request => request.EnvironmentName ?? string.Empty))
                    {
                        var requestsForEnvironment = environmentGroup.ToList();
                        var environmentName = environmentGroup.Key;
                        var recoveredIdsString = string.Join(',', requestsForEnvironment.Select(request => request.Id));
                        IDistributedLock? envLock = null;

                        try
                        {
                            envLock = this.distributedLockService
                                .TryAcquireLockAsync($"env:{environmentName}", EnvironmentLockLeaseTimeMs, CancellationToken.None)
                                .GetAwaiter()
                                .GetResult();

                            if (envLock == null)
                            {
                                this.logger.LogInformation(
                                    "Skipping stale request recovery for environment '{Environment}' in status '{Status}' because its distributed lock is still held. IDs [{Ids}]",
                                    environmentName,
                                    status,
                                    recoveredIdsString);
                                continue;
                            }

                            this.logger.LogWarning(
                                "Acquired distributed lock for environment '{Environment}' while recovering stale '{Status}' requests. Cancelling IDs [{Ids}]",
                                environmentName,
                                status,
                                recoveredIdsString);

                            CancelStaleRequests(status, requestsForEnvironment);
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogWarning(
                                ex,
                                "Error recovering stale requests for environment '{Environment}' in status '{Status}'. IDs [{Ids}]",
                                environmentName,
                                status,
                                recoveredIdsString);
                        }
                        finally
                        {
                            if (envLock != null)
                            {
                                envLock.DisposeAsync().AsTask().GetAwaiter().GetResult();
                            }
                        }
                    }
                }
                else
                {
                    CancelStaleRequests(status, staleRequests);
                }
            }
        }

        private void CancelStaleRequests(DeploymentRequestStatus status, List<DeploymentRequestApiModel> staleRequests)
        {
            var ids = staleRequests.Select(r => r.Id).ToArray();
            var idsString = string.Join(',', ids);

            this.logger.LogWarning(
                "Found {Count} stale requests in '{Status}' state from a previous instance. Cancelling IDs [{Ids}]",
                staleRequests.Count, status, idsString);

            int updatedCount = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(
                staleRequests,
                status,
                DeploymentRequestStatus.Cancelled,
                DateTimeOffset.Now);

            if (updatedCount > 0)
            {
                // Also cancel any pending deployment results for these requests
                this.requestsPersistentSource.SwitchDeploymentResultsStatuses(
                    staleRequests,
                    DeploymentResultStatus.Pending,
                    DeploymentResultStatus.Cancelled);

                foreach (var id in ids)
                {
                    TerminateRunnerProcesses(id);
                    PublishRequestStatusChangedSafe(new DeploymentRequestEventData(
                        RequestId: id,
                        Status: DeploymentRequestStatus.Cancelled.ToString(),
                        StartedTime: null,
                        CompletedTime: null,
                        Timestamp: DateTimeOffset.UtcNow
                    ));
                }

                this.logger.LogWarning(
                    "Cancelled {UpdatedCount} stale requests. IDs [{Ids}]",
                    updatedCount, idsString);
            }
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
        private int SwitchRequestsStatus(List<DeploymentRequestApiModel> requests, Methods method, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
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
                    this.logger.LogError($"Cannot {methodName} the request with id '{requests.First(r => r.IsProd).Id}' because request is running on production environment");
                    return 0;
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
                        PublishRequestStatusChangedSafe(new DeploymentRequestEventData(
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
                        PublishRequestStatusChangedSafe(new DeploymentRequestEventData(
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

                return updatedRequestCount;
            }

            return 0;
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
                var ids = requestsToRestart.Select(c => c.Id).ToList();
                var idsString = string.Join(',', ids);

                this.logger.LogInformation($"Going to restart {requestToRestartCount} requests, IDs [{idsString}]");

                var requestsRestartedByThisMonitor = new List<DeploymentRequestApiModel>();

                foreach (var requestToRestart in requestsToRestart)
                {
                    monitorCancellationToken.ThrowIfCancellationRequested();

                    // Switch status per request so we know exactly which restarts this monitor won.
                    // Bulk restart switching only returned a count, which let a losing monitor still
                    // clear results / kill processes / publish events for requests another monitor won.
                    var switched = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(
                        new List<DeploymentRequestApiModel> { requestToRestart },
                        DeploymentRequestStatus.Restarting,
                        DeploymentRequestStatus.Pending);

                    if (switched > 0)
                    {
                        requestsRestartedByThisMonitor.Add(requestToRestart);
                    }
                }

                var pendingRequestCount = requestsRestartedByThisMonitor.Count;

                if (pendingRequestCount == 0)
                {
                    this.logger.LogInformation($"None of the {requestToRestartCount} requests were restarted - likely already processed by another monitor instance. IDs [{idsString}]");
                    return;
                }

                var restartedIds = requestsRestartedByThisMonitor.Select(r => r.Id).ToList();
                var restartedIdsString = string.Join(',', restartedIds);

                // Clear deployment results only for requests we actually restarted.
                try
                {
                    this.logger.LogDebug($"Removing All results for IDs [{restartedIdsString}].");
                    this.requestsPersistentSource.ClearAllDeploymentResults(restartedIds);
                    this.logger.LogDebug($"Finish removing All results for IDs [{restartedIdsString}].");
                }
                catch (Exception exception)
                {
                    this.logger.LogError($"Removing All Results for IDs [{restartedIdsString}] has failed. Exception: {exception}");
                }

                restartedIds.ForEach(id =>
                {
                    TerminateRequestExecution(id, requestCancellationSources);
                    TerminateRunnerProcesses(id);
                    PublishRequestStatusChangedSafe(new DeploymentRequestEventData(
                        RequestId: id,
                        Status: DeploymentRequestStatus.Pending.ToString(),
                        StartedTime: null,
                        CompletedTime: null,
                        Timestamp: DateTimeOffset.UtcNow
                    ));
                });

                if (pendingRequestCount < requestToRestartCount)
                {
                    var skippedCount = requestToRestartCount - pendingRequestCount;
                    this.logger.LogInformation(
                        $"{pendingRequestCount} of {requestToRestartCount} requests restarted. " +
                        $"{skippedCount} were likely already processed by another monitor instance. IDs [{idsString}]");
                }
                else
                {
                    this.logger.LogInformation($"Requests IDs [{idsString}] have been restarted.");
                }
            }
        }

        public Task[] ExecuteRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            // Always fetch Paused requests so they block subsequent requests in the queue,
            // regardless of whether the pause feature flag is enabled.
            // The flag only controls whether users can pause/resume via UI and API.
            var allRelevantRequests = this.requestsPersistentSource
                .GetRequestsWithStatus(
                        DeploymentRequestStatus.Pending,
                        DeploymentRequestStatus.Running,
                        DeploymentRequestStatus.Confirmed,
                        DeploymentRequestStatus.Paused,
                        isProduction)
                .OrderBy(request => request.Id)
                .ToList();

            // Group by environment and filter to only environments without Running requests
            // and where the first request (by ID) is not Paused.
            var environmentRequestGroupsToExecute = allRelevantRequests
                .GroupBy(
                    request => request.EnvironmentName,
                    request => new RequestToProcessDto(
                        request,
                        this.serializer.Deserialize(request.RequestDetails)))
                .Where(environmentRequestGroup =>
                    // No Running requests in this environment
                    environmentRequestGroup.All(envRequest =>
                        envRequest.Request.Status != DeploymentRequestStatus.Running.ToString()) &&
                    // The first (earliest by ID) request must not be Paused.
                    // If Request 3 is Paused, Requests 4,5 are blocked.
                    // But if Request 4 is Paused, Request 3 can still run (it's before the pause).
                    environmentRequestGroup.OrderBy(r => r.Request.Id).First().Request.Status != DeploymentRequestStatus.Paused.ToString());

            IList<Task> requestGroupExecutionTasks = new List<Task>();

            foreach (var requestGroup in environmentRequestGroupsToExecute)
            {
                // We are taking just first request per environment for execution
                // in order to guarantee that requests are executed sequentially withing distinct environment.
                var requestToExecute = requestGroup.First();

                // Skip environment if in lock backoff period (failed to acquire lock recently)
                if (environmentLockBackoff.TryGetValue(requestGroup.Key, out var backoffUntil) && DateTime.UtcNow < backoffUntil)
                {
                    this.logger.LogDebug($"Skipping environment '{requestGroup.Key}' - in lock backoff until {backoffUntil:O}");
                    continue;
                }

                // IMPORTANT: Register the environment as running BEFORE Task.Run to prevent a race condition.
                // If TryAdd is after Task.Run, a fast-completing task (e.g. lock acquisition fails immediately)
                // can execute TryRemove in its finally block BEFORE TryAdd runs, leaving a phantom entry
                // that permanently blocks this environment from being processed.
                if (!environmentRequestIdRunning.TryAdd(requestGroup.Key, requestToExecute.Request.Id))
                {
                    environmentRequestIdRunning.TryGetValue(requestGroup.Key, out var runningRequestId);
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
                            envLock = await distributedLockService.TryAcquireLockAsync(lockKey, EnvironmentLockLeaseTimeMs, monitorCancellationToken);

                            if (envLock == null)
                            {
                                this.logger.LogWarning($"Could not acquire distributed lock for environment '{requestGroup.Key}' - likely being processed by another monitor instance");
                                environmentLockBackoff[requestGroup.Key] = DateTime.UtcNow.Add(LockBackoffDuration);
                                return; // Skip this environment - another monitor is processing it
                            }

                            environmentLockBackoff.TryRemove(requestGroup.Key, out _);
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

                        // Create a linked token source that cancels if:
                        // 1. The monitor service stops (monitorCancellationToken)
                        // 2. The distributed lock is lost (envLock.LockLostToken)
                        // This ensures split-brain scenarios are avoided by terminating execution immediately if the lock is lost.
                        CancellationTokenSource requestCancellationTokenSource;
                        if (envLock != null)
                        {
                            requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(monitorCancellationToken, envLock.LockLostToken);
                        }
                        else
                        {
                            requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(monitorCancellationToken);
                        }

                        // AddOrUpdate is safe here: the semaphore-like environmentRequestIdRunning guard
                        // ensures only one task per environment is active, so no contention on this key.
                        requestCancellationSources!.AddOrUpdate(
                            requestToExecute.Request.Id,
                            requestCancellationTokenSource,
                            (requestId, existingCancellationTokenSource) => requestCancellationTokenSource);
                        this.ExecuteRequest(requestToExecute, requestCancellationTokenSource.Token);

                        // Check lock health after execution completes
                        if (envLock != null && !envLock.IsValid)
                        {
                            this.logger.LogWarning(
                                "Distributed lock for environment '{Environment}' was lost during execution of request {RequestId} (channel closed). " +
                                "The deployment completed but another monitor may have started a concurrent deployment.",
                                requestGroup.Key, requestToExecute.Request.Id);
                        }
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
                        environmentRequestIdRunning.TryRemove(requestGroup.Key, out _);
                        
                        // Release the distributed lock
                        if (envLock != null)
                        {
                            await envLock.DisposeAsync();
                            this.logger.LogDebug($"Released distributed lock for environment '{requestGroup.Key}'");
                        }
                    }
                }, monitorCancellationToken);

                // Safety net: if the token is already cancelled when Task.Run is called (or
                // gets cancelled before the ThreadPool picks up the delegate), the task
                // transitions to Cancelled without executing the delegate body - so the
                // finally block with TryRemove never runs, leaving a phantom entry.
                // ContinueWith ensures cleanup in that edge case. TryRemove is idempotent,
                // so this is harmless if the finally block already ran.
                var envKey = requestGroup.Key;
                task.ContinueWith(_ =>
                {
                    environmentRequestIdRunning.TryRemove(envKey, out int _);
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default);

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

            PublishRequestStatusChangedSafe(new DeploymentRequestEventData(requestToExecute.Request)
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
