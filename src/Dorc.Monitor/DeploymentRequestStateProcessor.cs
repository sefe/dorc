using log4net;
using System.Collections.Concurrent;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Monitor
{
    internal class DeploymentRequestStateProcessor : IDeploymentRequestStateProcessor
    {
        private readonly ILog logger;
        private readonly IServiceProvider serviceProvider;
        private readonly IDeploymentRequestProcessesPersistentSource processesPersistentSource;
        private readonly IRequestsPersistentSource requestsPersistentSource;

        private DeploymentRequestDetailSerializer serializer = new DeploymentRequestDetailSerializer();

        private bool disposedValue;
        private ConcurrentDictionary<string, int> environmentRequestIdRunning = new ConcurrentDictionary<string, int>();

        private enum Methods
        {
            Cancel,
            Abandon
        }

        public DeploymentRequestStateProcessor(
            ILog logger,
            IServiceProvider serviceProvider,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IRequestsPersistentSource requestsPersistentSource)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.processesPersistentSource = processesPersistentSource;
            this.requestsPersistentSource = requestsPersistentSource;
        }

        public void AbandonRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            var requestsToAbandon = this.requestsPersistentSource
                .GetRequestsWithStatus([DeploymentRequestStatus.Running,], isProduction)
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
                .GetRequestsWithStatus([DeploymentRequestStatus.Cancelling], isProduction)
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

        private void SwitchRequestsStatus(List<DeploymentRequestApiModel> requests, Methods method, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            int requestToSwitchCount = requests.Count();
            var methodName = method.ToString();
            if (requestToSwitchCount > 0)
            {
                monitorCancellationToken.ThrowIfCancellationRequested();
                var ids = requests.Select(r => r.Id).ToArray();
                var idsString = string.Join(',', ids);

                this.logger.Info($"Going to {methodName} the requests: [{idsString}]");

                if (requests.Any(r => r.IsProd))
                {
                    this.logger.Error($"Cannot {methodName} the request with id '{requests.First(r => r.IsProd).Id}' because request is running on PR environment");
                    return;
                }

                foreach (var id in ids)
                {
                    TerminateRequestExecution(id, requestCancellationSources);
                };

                int cancelledRequestCount = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(
                    requests,
                    fromStatus,
                    toStatus,
                    DateTimeOffset.Now);

                if (cancelledRequestCount > 0)
                {
                    foreach (var id in ids)
                    {
                        TerminateRunnerProcesses(id);
                    }

                    this.logger.Info($"Requests with ids [{idsString}] are {methodName}ed.");
                }
                else
                {
                    this.logger.Error($"{requestToSwitchCount - cancelledRequestCount} request from {requestToSwitchCount} are NOT {methodName}ed. IDs [{idsString}]");
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

                this.logger.Info($"Going to {methodName} the deployment results for the requests: [{idsString}]");

                int cancelledDeploymentResultsCount = this.requestsPersistentSource.SwitchDeploymentResultsStatuses(
                    requests,
                    fromStatus,
                    toStatus);

                this.logger.Info($"Deployment results for requests with ids [{idsString}] are {methodName}ed.");
            }
        }

        public void RestartRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            var requestsToRestart = this.requestsPersistentSource
                .GetRequestsWithStatus([DeploymentRequestStatus.Restarting], isProduction)
                .OrderBy(restartingRequest => restartingRequest.Id)
                .Take(10) // since it's bulk, more than 10 will take too much time
                .ToList();

            int requestToRestartCount = requestsToRestart.Count();
            if (requestToRestartCount > 0)
            {
                this.logger.Info($"Going to restart {requestToRestartCount} requests");

                var ids = requestsToRestart.Select(c => c.Id).ToList();
                var idsString = string.Join(',', ids);
                try
                {
                    this.logger.Debug($"Removing All results for IDs [{idsString}].");

                    this.requestsPersistentSource.ClearAllDeploymentResults(ids);

                    this.logger.Debug($"Finish removing All results for IDs [{idsString}].");
                }
                catch (Exception exception)
                {
                    this.logger.Error($"Removing All Results for IDs [{idsString}] has failed. Exception: {exception}");
                }

                monitorCancellationToken.ThrowIfCancellationRequested();

                this.logger.Info($"Restarting All requests, IDs [{idsString}]");

                var pendingRequestCount = this.requestsPersistentSource.SwitchDeploymentRequestStatuses(requestsToRestart, DeploymentRequestStatus.Restarting, DeploymentRequestStatus.Pending);

                if (pendingRequestCount == requestToRestartCount)
                {
                    ids.ForEach(id =>
                    {
                        TerminateRequestExecution(id, requestCancellationSources);
                        TerminateRunnerProcesses(id);
                    });

                    this.logger.Info($"Requests IDs [{idsString}] have been restarted.");
                }
                else
                    this.logger.Error($"{requestToRestartCount - pendingRequestCount} requests from {requestToRestartCount} have NOT been restarted. IDs [{idsString}]");
            }
        }

        public Task[] ExecuteRequests(bool isProduction, ConcurrentDictionary<int, CancellationTokenSource> requestCancellationSources, CancellationToken monitorCancellationToken)
        {
            // Select only Pending requests for each of environments that do not have any Running requests.
            var environmentRequestGroupsToExecute = this.requestsPersistentSource
                .GetRequestsWithStatus(
                    [
                        DeploymentRequestStatus.Pending,
                        DeploymentRequestStatus.Running
                    ],
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
                    this.logger.Debug($"skipping processing deployment request for Env:{requestGroup.Key} user:{requestToExecute.Request.UserName} id: {runningRequestId}, as some request is being processed already for that env");
                    continue;
                }
                var task = Task.Run(() =>
                {
                    try
                    {
                        monitorCancellationToken.ThrowIfCancellationRequested();

                        var requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(monitorCancellationToken);
                        requestCancellationSources!.AddOrUpdate(
                            requestToExecute.Request.Id,
                            requestCancellationTokenSource,
                            (requestId, existingCancellationTokenSource) => requestCancellationTokenSource);
                        this.ExecuteRequest(requestToExecute, requestCancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException) 
                    {
                        this.logger.Error($"Canceled processing deployment request. Environment: {requestGroup.Key}, id: {requestToExecute.Request.Id}");
                    }
                    catch (Exception exc)
                    {
                        this.logger.Error($"Error while processing deployment request. Environment: {requestGroup.Key}, id: {requestToExecute.Request.Id}, exception: {exc}");
                    }
                    finally
                    {
                        this.RemoveCancellationTokenSource(requestToExecute.Request.Id, requestCancellationSources);
                        environmentRequestIdRunning.TryRemove(requestGroup.Key, out runningRequestId);
                    }
                },
                monitorCancellationToken);
                environmentRequestIdRunning.TryAdd(requestGroup.Key, requestToExecute.Request.Id);
                task.ConfigureAwait(false);
                requestGroupExecutionTasks.Add(task);
            }

            return requestGroupExecutionTasks.ToArray();
        }

        private void ExecuteRequest(RequestToProcessDto requestToExecute, CancellationToken requestCancellationToken)
        {
            this.logger.Debug($"---------------begin execution of request {requestToExecute.Request.Id}---------------");

            int upratedRequestCount = this.requestsPersistentSource.UpdateNonProcessedRequest(
                    requestToExecute.Request,
                DeploymentRequestStatus.Requesting,
                    DateTimeOffset.Now);

            if (upratedRequestCount == 0)
            {
                this.logger.InfoFormat("The request with ID {0} can NOT be processed.", requestToExecute.Request.Id);
                return;
            }

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
                this.logger.Error($"Execution of the request with id '{requestToExecute.Request.Id}' has failed. Exception: {exception}");
            }

            this.logger.Debug($"---------------end execution of request {requestToExecute.Request.Id}---------------");
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
            if (requestCancellationSources!.TryRemove(requestId, out CancellationTokenSource? removedCancellationTokenSource))
            {
                this.logger.ErrorFormat("Removal of CancellationTokenSource for the request '{0}' failed.", requestId);
            }

            removedCancellationTokenSource?.Dispose();
        }

        /// <summary>
        /// Runner process ids are stored in DB for the durability reason.
        /// If DOrc crashes the handle to the processes can be restored and closed appropriately later.
        /// </summary>
        /// <param name="requestId"></param>
        private void TerminateRunnerProcesses(int requestId)
        {
            var processIds = this.processesPersistentSource.GetAssociatedRunnerProcessIds(requestId);
            this.logger.Info($"Processes for the {requestId} that should be killed {String.Join(",", processIds)}");

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
                        this.logger.Info($"Process {processId} is killed");
                    }
                    catch (Exception exception)
                    {
                        this.logger.ErrorFormat($"Termination of the process with id '{processId}' failed. Exception: {exception}");
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
