using Dorc.ApiModel;
using System.Security.Principal;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IRequestsPersistentSource
    {
        DeploymentRequestApiModel? GetRequestForUser(int requestId, IPrincipal principal);
        RequestStatusDto GetRequestStatus(int requestId);
        IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status, bool isProd);
        IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status1, DeploymentRequestStatus status2, bool isProd);
        IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status1, DeploymentRequestStatus status2, DeploymentRequestStatus status3, bool isProd);
        IEnumerable<DeploymentRequestApiModel> GetRequestsWithStatus(DeploymentRequestStatus status1, DeploymentRequestStatus status2, DeploymentRequestStatus status3, DeploymentRequestStatus status4, bool isProd);
        IEnumerable<DeploymentResultApiModel> GetDeploymentResultsForRequest(int requestId);
        IEnumerable<DeploymentResultApiModel> GetDeploymentResultsForRequest(int requestId, int componentId);
        string GetRequestLog(int id);

        bool SetRequestStartStatus(DeploymentRequestApiModel deploymentRequest, DeploymentRequestStatus status, DateTimeOffset startedTime);
        void SetRequestCompletionStatus(
            int requestId,
            DeploymentRequestStatus status,
            DateTimeOffset completionTime,
            string? requestLogs = null);
        void UpdateRequestStatus(int requestId, DeploymentRequestStatus status);
        void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, DateTimeOffset requestedTime, string log);
        void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, string user);
        public void UpdateRequestStatus(int requestId, DeploymentRequestStatus status, string user, DateTimeOffset CancelledTime);

        int UpdateNonProcessedRequest(
            DeploymentRequestApiModel deploymentRequest,
            DeploymentRequestStatus newStatus,
            DateTimeOffset requestedTime);
        int SwitchDeploymentRequestStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus);
        int SwitchDeploymentRequestStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentRequestStatus fromStatus, DeploymentRequestStatus toStatus, DateTimeOffset requestedTime);
        int SwitchDeploymentResultsStatuses(IList<DeploymentRequestApiModel> deploymentRequests, DeploymentResultStatus fromStatus, DeploymentResultStatus toStatus);

        DeploymentResultApiModel GetDeploymentResults(int resultId);
        void SaveDeploymentResults(IEnumerable<ComponentApiModel> components, int requestId);
        public DeploymentResultApiModel CreateDeploymentResult(int componentId, int requestId);
        void ClearAllDeploymentResults(DeploymentRequestApiModel deploymentRequest);
        void ClearAllDeploymentResults(IList<int> deploymentRequestIds);
        bool UpdateResultStatus(DeploymentResultApiModel deploymentResultModel, DeploymentResultStatus status);

        /// <summary>
        /// Transitions a deployment result only if it is currently in
        /// <paramref name="expectedCurrentStatus"/>. Returns false when the row has moved
        /// on, so a caller that read the status earlier cannot overwrite a transition made
        /// in between - notably a confirm racing the Monitor, which would otherwise push a
        /// running result back to Confirmed and cause a second apply dispatch.
        /// </summary>
        bool UpdateResultStatus(DeploymentResultApiModel deploymentResultModel, DeploymentResultStatus status,
            DeploymentResultStatus expectedCurrentStatus);
        bool UpdateResultLog(DeploymentResultApiModel deploymentResultModel, string log);
        bool UpdateUncLogPath(DeploymentRequestApiModel deploymentRequest, string uncLogPath);
        bool UpdateUncLogPath(int requestId, string uncLogPath);
        DeploymentRequestApiModel GetRequest(int requestId);
        int SubmitRequest(DeploymentRequest deploymentRequest);
        void UpdateRequestDetails(int requestId, string requestDetails);
        void UpdateEnvironmentOwnerEmail(int requestId, string email);
        void ArchiveCurrentAttempt(int requestId);
        IEnumerable<DeploymentRequestAttemptApiModel> GetAttemptsForRequest(int requestId);
        int GetNextAttemptNumber(int requestId);
    }
}