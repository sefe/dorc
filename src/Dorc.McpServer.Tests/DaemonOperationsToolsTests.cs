using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DaemonOperationsToolsTests
{
    [TestMethod]
    public async Task DaemonOperationsTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new DaemonOperationsTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DaemonOperationsTools(gateway, null!));

        var tools = new DaemonOperationsTools(gateway, allowGuard);
        var roTools = new DaemonOperationsTools(gateway, roGuard);

        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/3", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonApiModel>()));
        Assert.IsTrue((await tools.GetServerDaemonsAsync(3)).Succeeded);

        gateway.GetAsync<List<DaemonStatusApiModel>>("DaemonStatus?id=4", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonStatusApiModel>()));
        Assert.IsTrue((await tools.GetDaemonStatusesByEnvironmentIdAsync(4)).Succeeded);

        gateway.GetAsync<List<DaemonStatusApiModel>>("DaemonStatus/DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonStatusApiModel>()));
        Assert.IsTrue((await tools.GetDaemonStatusesByEnvironmentNameAsync("DEV")).Succeeded);

        // Invalid statuses
        Assert.IsFalse((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "")).Succeeded);
        Assert.IsFalse((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "Running")).Succeeded);

        // Guard block
        Assert.IsFalse((await roTools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "Starting")).Succeeded);

        // Set up environment, server, and daemon mapping for DEV
        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel> { new() { EnvironmentId = 1, EnvironmentName = "DEV" } }));
        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel
            {
                AppServers = [new ServerApiModel { ServerId = 3, Name = "srv", EnvironmentNames = ["DEV"] }]
            }));
        gateway.GetAsync<List<DaemonApiModel>>("ServerDaemons/3", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonApiModel> { new() { Name = "svc" } }));

        // Valid statuses: starting, stopping, restarting
        gateway.PutStringResponseAsync("DaemonStatus", Arg.Any<DaemonStatusApiModel>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("ok"));

        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "starting")).Succeeded);
        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "Stopping")).Succeeded);
        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "RESTARTING")).Succeeded);
    }
}
