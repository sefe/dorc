using Dorc.ApiModel;
using Dorc.ApiModel.Extensions;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Services.Common;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources
{
    public class RequestsPersistentSource : IRequestsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RequestsPersistentSource(
            IDeploymentContextFactory contextFactory,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public DeploymentRequestApiModel GetRequest(int requestId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return MapToDeploymentRequestApiModel(context.DeploymentRequests.FirstOrDefault(x => x.Id == requestId));
            }
        }

        public DeploymentRequestApiModel? GetRequestForUser(int requestId, IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserLogin(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            using (var context = _contextFactory.GetContext())
            {
                var reqStatusesQueryable = RequestsStatusPersistentSource
                    .GetDeploymentRequestApiModels(context, username, userSids).Where(req => req.Id == requestId);

                return reqStatusesQueryable.ToList().FirstOrDefault();
            }
        }

        public RequestStatusDto GetRequestStatus(int requestId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var deploymentRequest = context.DeploymentRequests.FirstOrDefault(x => x.Id == requestId);
                return MapToRequestStatusDto(deploymentRequest);
            }
        }

        public IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status, bool isProd)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.DeploymentRequests.AsNoTracking()
                    .Where(r => status.ToString() == r.Status
                        && r.IsProd == isProd)
                    .ToList().Select(MapToDeploymentRequestApiModel)
                    .Where(r => r != null) // Filter out failed mappings
                    .ToList();
            }
        }

        public IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status1, DeploymentRequestStatus status2, bool isProd)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.DeploymentRequests.AsNoTracking()
                    .Where(r => (status1.ToString() == r.Status || status2.ToString() == r.Status)
                        && r.IsProd == isProd)
                    .ToList().Select(MapToDeploymentRequestApiModel)
                    .Where(r => r != null) // Filter out failed mappings
                    .ToList();
            }
        }

        public IEnumerable<DeploymentResultApiModel> GetDeploymentResultsForRequest(int requestId)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.DeploymentResults
                    .Include(result => result.DeploymentRequest)
                    .Include(result => result.Component)
                    .Where(result => result.DeploymentRequest.Id == requestId)
                    .OrderBy(result => result.Id)
                    .Select(MapToDeploymentResultModel).ToList();
            }
        }

        public IEnumerable<DeploymentResultApiModel> GetDeploymentResultsForRequest(int requestId, int componentId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var deploymentResult = context.DeploymentResults
                    .Include(results => results.Component)
                    .Include(results => results.DeploymentRequest)
                    .Where(result => result.DeploymentRequest.Id == requestId
                        && result.Component.Id == componentId)
                    .Select(MapToDeploymentResultModel);

                return deploymentResult.ToList();
            }
        }

        public void ClearAllDeploymentResults(DeploymentRequestApiModel deploymentRequest)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentResults
                    .Where(result => result.DeploymentRequest.Id == deploymentRequest.Id)
                    .ExecuteDelete();
            }
        }

        public void ClearAllDeploymentResults(IList<int> deploymentRequestIds)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentResults
                    .Where(result => deploymentRequestIds.Contains(result.DeploymentRequest.Id))
                    .ExecuteDelete();
            }
        }

        public DeploymentResultApiModel GetDeploymentResults(int resultId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var deploymentResult = context.DeploymentResults
                    .Include(results => results.Component)
                    .Include(results => results.DeploymentRequest)
                    .FirstOrDefault(x => x.Id == resultId);
                if (deploymentResult != null)
                    return MapToDeploymentResultModel(deploymentResult);
            }

            return null;
        }

        public void SaveDeploymentResults(IEnumerable<ComponentApiModel> components, int requestId)
        {
            using (var context = _contextFactory.GetContext())
            {
                foreach (ComponentApiModel component in components)
                {
                    var deploymentResult = new DeploymentResult
                    {
                        Component = context.Components.First(c => c.Id == component.ComponentId),
                        DeploymentRequest = context.DeploymentRequests.First(dr => dr.Id == requestId),
                        Status = DeploymentResultStatus.Pending.ToString()
                    };
                    if (deploymentResult.Component.IsEnabled != null && deploymentResult.Component.IsEnabled == false)
                    {
                        deploymentResult.Status = DeploymentResultStatus.Disabled.ToString();
                    }

                    context.DeploymentResults.Add(deploymentResult);
                }

                context.SaveChanges();
            }
        }

        public DeploymentResultApiModel CreateDeploymentResult(int componentId, int requestId/*, string logFilePath*/)
        {
            using (var context = _contextFactory.GetContext())
            {
                var deploymentResult = new DeploymentResult
                {
                    Component = context.Components.First(c => c.Id == componentId),
                    DeploymentRequest = context.DeploymentRequests.First(dr => dr.Id == requestId),
                    Status = DeploymentResultStatus.Pending.ToString()
                };

                if (deploymentResult.Component.IsEnabled.HasValue
                    && !deploymentResult.Component.IsEnabled.Value)
                {
                    deploymentResult.Status = DeploymentResultStatus.Disabled.ToString();
                }

                context.DeploymentResults.Add(deploymentResult);

                context.SaveChanges();

                return MapToDeploymentResultModel(deploymentResult);
            }
        }

        public string GetRequestLog(int id)
        {
            using (var context = _contextFactory.GetContext())
            {
                return context.DeploymentRequests.FirstOrDefault(r => r.Id == id)?.Log;
            }
        }

        public bool UpdateUncLogPath(DeploymentRequestApiModel deploymentRequest, string uncLogPath)
        {
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.UncLogPath, uncLogPath));

                if (rowsAffected == 0)
                {
                    return false;
                }

                deploymentRequest.UncLogPath = uncLogPath;
                return true;
            }
        }

        public bool UpdateUncLogPath(int requestId, string uncLogPath)
        {
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.UncLogPath, uncLogPath));

                if (rowsAffected == 0)
                {
                    return false;
                }

                return true;
            }
        }


        public void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, DateTimeOffset requestedTime)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.RequestedTime, requestedTime));
            }
        }
        public int ChangeRequestStatus(DeploymentRequestApiModel deploymentRequest, DeploymentRequestStatus status, DateTimeOffset requestedTime)
        {
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id
                        && r.Status == deploymentRequest.Status.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.RequestedTime, requestedTime));

                deploymentRequest.Status = status.ToString();
                deploymentRequest.RequestedTime = requestedTime;

                return rowsAffected;
            }
        }

        public int UpdateNonProcessedRequest(
            DeploymentRequestApiModel deploymentRequest,
            DeploymentRequestStatus status,
            DateTimeOffset requestedTime)
        {
            using (var context = _contextFactory.GetContext())
            {
                int affectedRows = context.DeploymentRequests
                    .Where(request => request.Id == deploymentRequest.Id
                        && request.Status == deploymentRequest.Status.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(request => request.Status, status.ToString())
                        .SetProperty(request => request.RequestedTime, requestedTime));

                if (affectedRows > 0)
                {
                    deploymentRequest.Status = status.ToString();
                    deploymentRequest.RequestedTime = requestedTime;
                }

                return affectedRows;
            }
        }

        public void UpdateRequestStatus(int requestId, DeploymentRequestStatus status)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString()));
            }
        }

        public int ChangeRequestStatus(DeploymentRequestApiModel deploymentRequest, DeploymentRequestStatus status)
        {
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id
                        && r.Status == deploymentRequest.Status.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString()));

                deploymentRequest.Status = status.ToString();

                return rowsAffected;
            }
        }

        public void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, string user)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.UserName, user));
            }
        }

        public int SwitchDeploymentRequestStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus)
        {
            var ids = deploymentRequests.Select(r => r.Id).ToList();
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => ids.Contains(r.Id)
                        && r.Status == fromStatus.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(r => r.Status, toStatus.ToString()));

                deploymentRequests.ForEach(dr => dr.Status = toStatus.ToString());

                return rowsAffected;
            }
        }

        public int SwitchDeploymentRequestStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus, DateTimeOffset requestedTime)
        {
            var ids = deploymentRequests.Select(r => r.Id).ToList();
            using (var context = _contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => ids.Contains(r.Id)
                        && r.Status == fromStatus.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(r => r.Status, toStatus.ToString())
                        .SetProperty(rq => rq.RequestedTime, requestedTime));

                if (rowsAffected > 0)
                {
                    foreach (var dr in deploymentRequests) 
                    { 
                        dr.Status = toStatus.ToString(); 
                        dr.RequestedTime = requestedTime; 
                    };
                }

                return rowsAffected;
            }
        }

        public int SwitchDeploymentResultsStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentResultStatus fromStatus, DeploymentResultStatus toStatus)
        {
            var ids = deploymentRequests.Select(r => r.Id).ToList();
            using (var context = _contextFactory.GetContext())
            {
                var rowsAffected = context.DeploymentResults
                    .Where(r => ids.Contains(r.DeploymentRequest.Id)
                        && r.Status == fromStatus.ToString())
                    .ExecuteUpdate(setters => setters
                        .SetProperty(r => r.Status, toStatus.ToString())
                        .SetProperty(r => r.StartedTime, dr => toStatus == DeploymentResultStatus.Running ? DateTimeOffset.Now : dr.StartedTime)
                        .SetProperty(r => r.CompletedTime, dr => 
                            toStatus == DeploymentResultStatus.Complete ||
                                toStatus == DeploymentResultStatus.Failed ||
                                toStatus == DeploymentResultStatus.Cancelled
                            ? DateTimeOffset.Now : dr.CompletedTime));

                return rowsAffected;
            }
        }

        public bool SetRequestStartStatus(DeploymentRequestApiModel deploymentRequest, DeploymentRequestStatus status, DateTimeOffset startedTime)
        {
            using (var context = _contextFactory.GetContext())
            {
                int affectedRows = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.StartedTime, startedTime));

                if (affectedRows == 0)
                {
                    return false;
                }

                deploymentRequest.Status = status.ToString();
                deploymentRequest.StartedTime = startedTime;
                return true;
            }
        }

        public void SetRequestCompletionStatus(int requestId,
            DeploymentRequestStatus status,
            DateTimeOffset completionTime,
            string? requestLogs = null)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.CompletedTime, completionTime)
                        .SetProperty(b => b.Log, requestLogs));
            }
        }

        public void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, DateTimeOffset requestedTime, string log)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests
                    .Where(r => r.Id == requestId)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.RequestedTime, requestedTime)
                        .SetProperty(b => b.Log, log));
            }
        }

        public bool UpdateResultStatus(DeploymentResultApiModel deploymentResultModel, DeploymentResultStatus status)
        {
            using (var context = _contextFactory.GetContext())
            {
                int updatedResultCount = 0;

                updatedResultCount = context.DeploymentResults
                    .Where(result => result.Id == deploymentResultModel.Id)
                    .ExecuteUpdate(setters => setters
                                            .SetProperty(b => b.Status, status.ToString())
                                            .SetProperty(r => r.StartedTime, dr => status == DeploymentResultStatus.Running ? DateTimeOffset.Now : dr.StartedTime)
                                            .SetProperty(r => r.CompletedTime, dr =>
                                                status == DeploymentResultStatus.Complete ||
                                                    status == DeploymentResultStatus.Failed ||
                                                    status == DeploymentResultStatus.Cancelled
                                                ? DateTimeOffset.Now : dr.CompletedTime));

                if (updatedResultCount == 0)
                {
                    return false;
                }

                deploymentResultModel.Status = status.ToString();

                return true;
            }
        }

        public bool UpdateResultLog(DeploymentResultApiModel deploymentResultModel, string log)
        {
            using (var context = _contextFactory.GetContext())
            {
                int updatedResultCount = 0;

                updatedResultCount = context.DeploymentResults
                    .Where(result => result.Id == deploymentResultModel.Id)
                    .ExecuteUpdate(setters => setters
                                            .SetProperty(b => b.Log, log));

                if (updatedResultCount == 0)
                {
                    return false;
                }

                deploymentResultModel.Log = log;

                return true;
            }
        }        

        public int SubmitRequest(DeploymentRequest deploymentRequest)
        {
            using (var context = _contextFactory.GetContext())
            {
                var envIsProd = from obj in context.Environments.Where(r => r.Name == deploymentRequest.Environment)
                    select obj.IsProd;
                foreach (var val in envIsProd)
                    if (val)
                        deploymentRequest.IsProd = true;

                var request = context.DeploymentRequests.Add(deploymentRequest);
                context.SaveChanges();
                return request.Entity.Id;
            }
        }

        private static DeploymentResultApiModel MapToDeploymentResultModel(DeploymentResult deploymentResult)
        {
            DeploymentResultStatus status;
            if (deploymentResult.Status is null)
            {
                status = DeploymentResultStatus.StatusNotSet;
            }
            else
            {
                status = deploymentResult.Status.ParseToDeploymentResultStatus();
            }

            return new DeploymentResultApiModel
            {
                Id = deploymentResult.Id,
                Status = status.ToString(),
                ComponentName = deploymentResult.Component.Name,
                ComponentId = deploymentResult.Component.Id,
                RequestId = deploymentResult.DeploymentRequest.Id,
                Log = deploymentResult.Log,
                StartedTime = deploymentResult.StartedTime,
                CompletedTime = deploymentResult.CompletedTime
            };
        }

        public static DeploymentRequestApiModel MapToDeploymentRequestApiModel(DeploymentRequest req)
        {
            if (req == null)
                return null;

            try
            {
                var status = (DeploymentRequestStatus)Enum.Parse(typeof(DeploymentRequestStatus), req.Status, true);

                return new DeploymentRequestApiModel
                {
                    BuildNumber = req.BuildNumber,
                    BuildUri = GetSafeProperty(() => req.BuildUri),
                    CompletedTime = req.CompletedTime,
                    Components = req.Components,
                    DropLocation = GetSafeProperty(() => req.DropLocation),
                    EnvironmentName = req.Environment,
                    Id = req.Id,
                    IsProd = req.IsProd,
                    Project = req.Project,
                    Log = req.Log,
                    RequestDetails = req.RequestDetails,
                    RequestedTime = req.RequestedTime,
                    StartedTime = req.StartedTime,
                    Status = status.ToString(),
                    UserName = req.UserName,
                    UncLogPath = req.UncLogPath
                };
            }
            catch (Exception)
            {
                // If mapping fails completely, return null to skip this request
                return null;
            }
        }

        private static string GetSafeProperty(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception)
            {
                // If property access fails (e.g., XML parsing error), return null
                return null;
            }
        }

        private static RequestStatusDto MapToRequestStatusDto(DeploymentRequest req)
        {
            return new RequestStatusDto
            {
                Id = req.Id,
                Status = req.Status
            };
        }
    }
}
