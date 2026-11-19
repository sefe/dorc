using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class EnvironmentCatalogToolsTests
{
    [TestMethod]
    public async Task EnvironmentCatalogTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new EnvironmentCatalogTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new EnvironmentCatalogTools(gateway, null!));

        var tools = new EnvironmentCatalogTools(gateway, allowGuard);
        var roTools = new EnvironmentCatalogTools(gateway, roGuard);

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel>()));
        Assert.IsTrue((await tools.ListEnvironmentsAsync(null)).Succeeded);

        gateway.GetAsync<List<EnvironmentApiModel>>("RefDataEnvironments?env=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentApiModel>()));
        Assert.IsTrue((await tools.ListEnvironmentsAsync(" DEV ")).Succeeded);

        gateway.GetAsync<List<string>>("RefDataEnvironments/GetAllEnvironmentNames", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<string>()));
        Assert.IsTrue((await tools.ListAllEnvironmentNamesAsync()).Succeeded);

        gateway.GetAsync<EnvironmentContentApiModel>("RefDataEnvironmentsDetails/5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentContentApiModel()));
        Assert.IsTrue((await tools.GetEnvironmentDetailsAsync(5)).Succeeded);

        gateway.GetAsync<List<ContainerApiModel>>("RefDataContainers/ByEnvId/5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<ContainerApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentContainersAsync(5)).Succeeded);

        gateway.GetAsync<List<CloudResourceApiModel>>("RefDataCloudResources/ByEnvId/5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<CloudResourceApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentCloudResourcesAsync(5)).Succeeded);

        gateway.GetAsync<EnvironmentOwnerApiModel>("RefDataEnvironmentsUsers/owner/5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new EnvironmentOwnerApiModel()));
        Assert.IsTrue((await tools.GetEnvironmentOwnerAsync(5)).Succeeded);

        gateway.GetAsync<List<EnvironmentHistoryApiModel>>("RefDataEnvironmentsHistory?id=5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvironmentHistoryApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentHistoryAsync(5)).Succeeded);

        gateway.GetAsync<List<EnvBuildsApiModel>>("RefDataProjectBuilds?id=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<EnvBuildsApiModel>()));
        Assert.IsTrue((await tools.GetEnvironmentComponentStatusesAsync(" DEV ")).Succeeded);

        Assert.IsFalse((await roTools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj")).Succeeded);

        gateway.PostAsync<CopyEnvBuildDto, CopyEnvBuildResponseDto>("CopyEnvBuild", Arg.Any<CopyEnvBuildDto>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new CopyEnvBuildResponseDto { Success = true }));
        Assert.IsTrue((await tools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj", null)).Succeeded);
        Assert.IsTrue((await tools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj", "CompA;CompB")).Succeeded);
    }
}
