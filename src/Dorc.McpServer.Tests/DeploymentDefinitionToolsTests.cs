using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class DeploymentDefinitionToolsTests
{
    [TestMethod]
    public async Task DeploymentDefinitionTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentDefinitionTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentDefinitionTools(gateway, null!));

        var tools = new DeploymentDefinitionTools(gateway, allowGuard);
        var roTools = new DeploymentDefinitionTools(gateway, roGuard);

        gateway.GetAsync<GetDeploymentDefinitionsResponse>("DeploymentDefinitions?page=1&limit=50", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDeploymentDefinitionsResponse()));
        Assert.IsTrue((await tools.ListDeploymentDefinitionsAsync()).Succeeded);

        gateway.GetAsync<GetDeploymentDefinitionsResponse>(
                "DeploymentDefinitions?page=2&limit=25&projectId=10&componentType=Terraform&sourceType=Catalog&search=redis",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDeploymentDefinitionsResponse()));
        Assert.IsTrue((await tools.ListDeploymentDefinitionsAsync(2, 25, 10, ComponentType.Terraform, TerraformSourceType.Catalog, " redis ")).Succeeded);

        gateway.GetAsync<GetDeploymentDefinitionHistoryResponse>("DeploymentDefinitions/history?page=1&limit=50", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDeploymentDefinitionHistoryResponse()));
        Assert.IsTrue((await tools.GetDeploymentDefinitionHistoryAsync()).Succeeded);

        gateway.GetAsync<GetDeploymentDefinitionHistoryResponse>("DeploymentDefinitions/history?page=1&limit=50&projectId=10&componentId=42", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDeploymentDefinitionHistoryResponse()));
        Assert.IsTrue((await tools.GetDeploymentDefinitionHistoryAsync(1, 50, 10, 42)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetScriptsListResponseDto>(
                "RefDataScripts?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetScriptsListResponseDto()));
        Assert.IsTrue((await tools.SearchScriptsAsync(1, 25, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchScriptsAsync(1, 25, " deploy.ps1 ", " scripts/ ")).Succeeded);

        Assert.IsFalse((await tools.GetReleaseInformationAsync(null!)).Succeeded);
        Assert.IsFalse((await tools.GetReleaseInformationAsync([])).Succeeded);

        gateway.PostAsync<DeploymentRequestIds, List<ReleaseInformationApiModel>>(
                "ReleaseInformation",
                Arg.Any<DeploymentRequestIds>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<ReleaseInformationApiModel>()));
        Assert.IsTrue((await tools.GetReleaseInformationAsync([101, 102])).Succeeded);

        gateway.GetAsync<TemplateApiModel<EnvironmentApiModel>>(
                "RefDataProjectEnvironmentMappings?project=Endur&includeRead=true",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TemplateApiModel<EnvironmentApiModel>()));
        Assert.IsTrue((await tools.GetProjectEnvironmentsAsync(" Endur ", true)).Succeeded);

        // AddProjectEnvironmentMappingAsync
        Assert.IsFalse((await tools.AddProjectEnvironmentMappingAsync("  ", "DEV")).Succeeded);
        Assert.IsFalse((await roTools.AddProjectEnvironmentMappingAsync("Endur", "DEV")).Succeeded);
        gateway.PostWithoutResponseBodyAsync<object?>(
                "RefDataProjectEnvironmentMappings?project=Endur&environment=DEV",
                null,
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(string.Empty));
        Assert.IsTrue((await tools.AddProjectEnvironmentMappingAsync(" Endur ", " DEV ")).Succeeded);

        // RemoveProjectEnvironmentMappingAsync
        Assert.IsFalse((await tools.RemoveProjectEnvironmentMappingAsync("  ", "DEV")).Succeeded);
        Assert.IsFalse((await roTools.RemoveProjectEnvironmentMappingAsync("Endur", "DEV")).Succeeded);
        gateway.DeleteWithoutResponseBodyAsync(
                "RefDataProjectEnvironmentMappings?project=Endur&environment=DEV",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(string.Empty));
        Assert.IsTrue((await tools.RemoveProjectEnvironmentMappingAsync(" Endur ", " DEV ")).Succeeded);
    }
}
