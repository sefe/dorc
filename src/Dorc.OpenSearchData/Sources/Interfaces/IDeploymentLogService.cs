using Dorc.ApiModel;

namespace Dorc.OpenSearchData.Sources.Interfaces
{
    public interface IDeploymentLogService
    {
        void EnrichDeploymentResultsWithLogs(IEnumerable<DeploymentResultApiModel> deploymentResults);
        
        void EnrichDeploymentResultsWithLimitedLogs(IEnumerable<DeploymentResultApiModel> deploymentResults, int maxLogsPerResult = 3);
        
        string GetLogsForSingleResult(int requestId, int deploymentResultId);
    }
}
