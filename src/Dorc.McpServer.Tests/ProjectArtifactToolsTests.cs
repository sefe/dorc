using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class ProjectArtifactToolsTests
{
    [TestMethod]
    public async Task ProjectArtifactTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        Assert.ThrowsExactly<ArgumentNullException>(() => new ProjectArtifactTools(null!));

        var tools = new ProjectArtifactTools(gateway);

        gateway.GetAsync<List<ProjectApiModel>>("RefDataProjects", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<ProjectApiModel>()));
        Assert.IsTrue((await tools.ListProjectsAsync()).Succeeded);

        gateway.GetAsync<List<DeployComponentDto>>("Request/Components?projectId=1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployComponentDto>()));
        Assert.IsTrue((await tools.ListProjectComponentsAsync(1, null)).Succeeded);

        gateway.GetAsync<List<DeployComponentDto>>("Request/Components?projectId=1&parentId=5", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployComponentDto>()));
        Assert.IsTrue((await tools.ListProjectComponentsAsync(1, 5)).Succeeded);

        gateway.GetAsync<List<DeployArtefactDto>>("Request/BuildDefinitions?projectId=1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployArtefactDto>()));
        Assert.IsTrue((await tools.ListBuildDefinitionsAsync(1)).Succeeded);

        gateway.GetAsync<List<DeployArtefactDto>>("Request/Builds?projectId=1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployArtefactDto>()));
        Assert.IsTrue((await tools.ListBuildsAsync(1, null, null)).Succeeded);

        gateway.GetAsync<List<DeployArtefactDto>>("Request/Builds?projectId=1&environment=DEV&buildDefinitionName=CI", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployArtefactDto>()));
        Assert.IsTrue((await tools.ListBuildsAsync(1, " DEV ", " CI ")).Succeeded);

        gateway.GetAsync<List<string>>("Request/BundleComponents?projectId=1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<string>()));
        Assert.IsTrue((await tools.ListBundleComponentsAsync(1, null, null)).Succeeded);

        gateway.GetAsync<List<string>>("Request/BundleComponents?projectId=1&buildDefinitionName=Def&buildUrl=http%3A%2F%2Fb", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<string>()));
        Assert.IsTrue((await tools.ListBundleComponentsAsync(1, "Def", "http://b")).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<BundledRequestsApiModel>()));
        Assert.IsTrue((await tools.ListBundledRequestsAsync(null!)).Succeeded);
        Assert.IsTrue((await tools.ListBundledRequestsAsync(["  "])).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests?projectNames=P1&projectNames=P2", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<BundledRequestsApiModel>>(true, new List<BundledRequestsApiModel>(), null, null));
        Assert.IsTrue((await tools.ListBundledRequestsAsync([" P1 ", "P2"])).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests?projectNames=FAIL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<BundledRequestsApiModel>>("err", 500));
        Assert.IsFalse((await tools.ListBundledRequestsAsync(["FAIL"])).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests/RequestsForBundle?bundleName=B1", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<BundledRequestsApiModel>>(true, new List<BundledRequestsApiModel>(), null, null));
        Assert.IsTrue((await tools.ListRequestsForBundleAsync("B1")).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests/RequestsForBundle?bundleName=B2", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<BundledRequestsApiModel>(), 200));
        Assert.IsTrue((await tools.ListRequestsForBundleAsync("B2")).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests/RequestsForBundle?bundleName=FAIL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<BundledRequestsApiModel>>("err", 500));
        Assert.IsFalse((await tools.ListRequestsForBundleAsync("FAIL")).Succeeded);
    }
}
