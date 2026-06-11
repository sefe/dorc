using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAnalyticsPersistentSource
    {
        IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectMonth();
        AnalyticsDeploymentSummaryApiModel GetDeploymentSummary();
        IEnumerable<AnalyticsEnvironmentUsageApiModel> GetEnvironmentUsage();
        IEnumerable<AnalyticsUserActivityApiModel> GetUserActivity();
        IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns();
        IEnumerable<AnalyticsComponentUsageApiModel> GetComponentUsage();
        AnalyticsDurationApiModel GetDeploymentDuration();
    }
}