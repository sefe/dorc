using Dorc.ApiModel;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAnalyticsPersistentSource
    {
        IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetCountDeploymentsPerProjectMonth();
        AnalyticsDeploymentSummaryApiModel GetDeploymentSummary();
        IEnumerable<AnalyticsEnvironmentUsageApiModel> GetEnvironmentUsage(IPrincipal user);
        IEnumerable<AnalyticsUserActivityApiModel> GetUserActivity();
        IEnumerable<AnalyticsTimePatternApiModel> GetTimePatterns();
        IEnumerable<AnalyticsComponentUsageApiModel> GetComponentUsage();
        AnalyticsDurationApiModel GetDeploymentDuration();
        IEnumerable<AnalyticsMonthlyOutcomeApiModel> GetMonthlyOutcomes();
        IEnumerable<AnalyticsEnvironmentWaitApiModel> GetEnvironmentWaitTimes();
        IEnumerable<AnalyticsProjectDurationApiModel> GetProjectDurations();
        IEnumerable<AnalyticsComponentReliabilityApiModel> GetComponentReliability();
        IEnumerable<AnalyticsRecoveryTimeApiModel> GetRecoveryTimes();
    }
}