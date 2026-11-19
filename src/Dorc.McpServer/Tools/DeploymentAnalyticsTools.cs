using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class DeploymentAnalyticsTools
{
    private readonly IDorcApiGateway _apiGateway;

    public DeploymentAnalyticsTools(IDorcApiGateway apiGateway)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
    }

    [McpServerTool(Name = "dorc_get_analytics_deployments_month", ReadOnly = true)]
    [Description("Gets deployment counts and failure counts per project per month/day.")]
    public Task<McpToolOutcome<List<AnalyticsDeploymentsPerProjectApiModel>>> GetDeploymentsPerProjectMonthAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsDeploymentsPerProjectApiModel>>(
            "AnalyticsDeploymentsMonth",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_deployment_summary", ReadOnly = true)]
    [Description("Gets executive summary statistics for deployments (total volume, failure percentage, daily average, and top projects).")]
    public Task<McpToolOutcome<AnalyticsDeploymentSummaryApiModel>> GetDeploymentSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<AnalyticsDeploymentSummaryApiModel>(
            "AnalyticsDeploymentSummary",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_environment_usage", ReadOnly = true)]
    [Description("Gets deployment activity and usage statistics per environment.")]
    public Task<McpToolOutcome<List<AnalyticsEnvironmentUsageApiModel>>> GetEnvironmentUsageAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsEnvironmentUsageApiModel>>(
            "AnalyticsEnvironmentUsage",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_user_activity", ReadOnly = true)]
    [Description("Gets deployment activity statistics grouped by user.")]
    public Task<McpToolOutcome<List<AnalyticsUserActivityApiModel>>> GetUserActivityAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsUserActivityApiModel>>(
            "AnalyticsUserActivity",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_time_patterns", ReadOnly = true)]
    [Description("Gets deployment volume and failure patterns by hour of day and day of week.")]
    public Task<McpToolOutcome<List<AnalyticsTimePatternApiModel>>> GetTimePatternsAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsTimePatternApiModel>>(
            "AnalyticsTimePattern",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_component_usage", ReadOnly = true)]
    [Description("Gets deployment frequency statistics per component.")]
    public Task<McpToolOutcome<List<AnalyticsComponentUsageApiModel>>> GetComponentUsageAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsComponentUsageApiModel>>(
            "AnalyticsComponentUsage",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_duration", ReadOnly = true)]
    [Description("Gets overall deployment duration statistics (average, min, max, p50, p90, p95 minutes).")]
    public Task<McpToolOutcome<AnalyticsDurationApiModel>> GetDurationAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<AnalyticsDurationApiModel>(
            "AnalyticsDuration",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_monthly_outcomes", ReadOnly = true)]
    [Description("Gets monthly deployment outcomes (volume, failures, cancellations) split by production and non-production.")]
    public Task<McpToolOutcome<List<AnalyticsMonthlyOutcomeApiModel>>> GetMonthlyOutcomesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsMonthlyOutcomeApiModel>>(
            "AnalyticsMonthlyOutcome",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_environment_wait_times", ReadOnly = true)]
    [Description("Gets deployment queue wait time statistics per environment to identify bottlenecks.")]
    public Task<McpToolOutcome<List<AnalyticsEnvironmentWaitApiModel>>> GetEnvironmentWaitTimesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsEnvironmentWaitApiModel>>(
            "AnalyticsEnvironmentWait",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_project_durations", ReadOnly = true)]
    [Description("Gets deployment duration statistics grouped by project.")]
    public Task<McpToolOutcome<List<AnalyticsProjectDurationApiModel>>> GetProjectDurationsAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsProjectDurationApiModel>>(
            "AnalyticsProjectDuration",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_component_reliability", ReadOnly = true)]
    [Description("Gets component reliability statistics (failure rates and retry counts per component).")]
    public Task<McpToolOutcome<List<AnalyticsComponentReliabilityApiModel>>> GetComponentReliabilityAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsComponentReliabilityApiModel>>(
            "AnalyticsComponentReliability",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_analytics_recovery_times", ReadOnly = true)]
    [Description("Gets mean time to recovery (MTTR) statistics from failed deployments to the next successful deployment per project.")]
    public Task<McpToolOutcome<List<AnalyticsRecoveryTimeApiModel>>> GetRecoveryTimesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<AnalyticsRecoveryTimeApiModel>>(
            "AnalyticsRecoveryTime",
            cancellationToken);
    }
}
