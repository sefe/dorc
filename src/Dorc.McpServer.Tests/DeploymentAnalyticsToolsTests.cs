using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DeploymentAnalyticsToolsTests
{
    [TestMethod]
    public async Task DeploymentAnalyticsTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentAnalyticsTools(null!));

        var tools = new DeploymentAnalyticsTools(gateway);

        gateway.GetAsync<List<AnalyticsDeploymentsPerProjectApiModel>>("AnalyticsDeploymentsMonth", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsDeploymentsPerProjectApiModel>()));
        Assert.IsTrue((await tools.GetDeploymentsPerProjectMonthAsync()).Succeeded);

        gateway.GetAsync<AnalyticsDeploymentSummaryApiModel>("AnalyticsDeploymentSummary", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new AnalyticsDeploymentSummaryApiModel()));
        Assert.IsTrue((await tools.GetDeploymentSummaryAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsEnvironmentUsageApiModel>>("AnalyticsEnvironmentUsage", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsEnvironmentUsageApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentUsageAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsUserActivityApiModel>>("AnalyticsUserActivity", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsUserActivityApiModel>()));
        Assert.IsTrue((await tools.GetUserActivityAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsTimePatternApiModel>>("AnalyticsTimePattern", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsTimePatternApiModel>()));
        Assert.IsTrue((await tools.GetTimePatternsAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsComponentUsageApiModel>>("AnalyticsComponentUsage", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsComponentUsageApiModel>()));
        Assert.IsTrue((await tools.GetComponentUsageAsync()).Succeeded);

        gateway.GetAsync<AnalyticsDurationApiModel>("AnalyticsDuration", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new AnalyticsDurationApiModel()));
        Assert.IsTrue((await tools.GetDurationAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsMonthlyOutcomeApiModel>>("AnalyticsMonthlyOutcome", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsMonthlyOutcomeApiModel>()));
        Assert.IsTrue((await tools.GetMonthlyOutcomesAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsEnvironmentWaitApiModel>>("AnalyticsEnvironmentWait", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsEnvironmentWaitApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentWaitTimesAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsProjectDurationApiModel>>("AnalyticsProjectDuration", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsProjectDurationApiModel>()));
        Assert.IsTrue((await tools.GetProjectDurationsAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsComponentReliabilityApiModel>>("AnalyticsComponentReliability", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsComponentReliabilityApiModel>()));
        Assert.IsTrue((await tools.GetComponentReliabilityAsync()).Succeeded);

        gateway.GetAsync<List<AnalyticsRecoveryTimeApiModel>>("AnalyticsRecoveryTime", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<AnalyticsRecoveryTimeApiModel>()));
        Assert.IsTrue((await tools.GetRecoveryTimesAsync()).Succeeded);
    }
}
