using Dorc.ApiModel;

namespace Dorc.OpenSearchData.Sources.Interfaces
{
    public interface IDeploymentResultLogOsSource
    {
        void LoadDeploymentResultsLogs(IEnumerable<DeploymentResultApiModel> deploymentResults);
    }
}
