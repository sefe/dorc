using Dorc.ApiModel;

namespace Dorc.OpenSearchData.Sources.Interfaces
{
    public interface IDeploymentLogService
    {
        void LoadDeploymentResultsLogs(IEnumerable<DeploymentResultApiModel> deploymentResults);
    }
}
