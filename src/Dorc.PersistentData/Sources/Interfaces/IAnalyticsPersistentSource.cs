using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAnalyticsPersistentSource
    {
        IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectMonth();
        IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectDate();
    }
}