using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using Dorc.Terraform.Catalog;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class McpToolGroupsTests
{
    [TestMethod]
    public async Task DeploymentRequestTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var readOnlyGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentRequestTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DeploymentRequestTools(gateway, null!));

        var tools = new DeploymentRequestTools(gateway, allowGuard);
        var roTools = new DeploymentRequestTools(gateway, readOnlyGuard);

        // SearchDeploymentRequestsAsync with and without filters
        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=1&limit=100",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRequestStatusesListResponseDto { TotalItems = 1 }));

        var search1 = await tools.SearchDeploymentRequestsAsync(0, 200, "ProjA", "DEV", "Failed", "alice");
        Assert.IsTrue(search1.Succeeded);

        gateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
                "RequestStatuses?page=2&limit=10",
                Arg.Is<PagedDataOperators>(op => op.Filters.Count == 0),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRequestStatusesListResponseDto { TotalItems = 0 }));

        var search2 = await tools.SearchDeploymentRequestsAsync(2, 10);
        Assert.IsTrue(search2.Succeeded);

        // GetDeploymentRequestAsync: success with XML redaction, success with empty XML, and failure
        var xmlWithSecret = "<DeploymentRequestDetail><Properties><PropertyPair><Name>SecretKey</Name><Value>my-secret</Value><IsSensitive>true</IsSensitive></PropertyPair></Properties></DeploymentRequestDetail>";
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 10, RequestDetails = xmlWithSecret }));
        var req10 = await tools.GetDeploymentRequestAsync(10);
        Assert.IsTrue(req10.Succeeded);
        StringAssert.Contains(req10.Data!.RequestDetails, RequestPropertyRedaction.Marker);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=11", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 11, RequestDetails = string.Empty }));
        var req11 = await tools.GetDeploymentRequestAsync(11);
        Assert.IsTrue(req11.Succeeded);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=12", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("err", 404));
        var req12 = await tools.GetDeploymentRequestAsync(12);
        Assert.IsFalse(req12.Succeeded);

        // Status, ComponentResults, Attempts
        gateway.GetAsync<RequestStatusDto>("Request?id=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10, Status = "Complete" }));
        Assert.IsTrue((await tools.GetDeploymentRequestStatusAsync(10)).Succeeded);

        gateway.GetAsync<List<DeploymentResultApiModel>>("ResultStatuses?requestId=10", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeploymentResultApiModel>()));
        Assert.IsTrue((await tools.GetDeploymentComponentResultsAsync(10)).Succeeded);

        gateway.GetAsync<List<DeploymentRequestAttemptApiModel>>("Request/10/attempts", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeploymentRequestAttemptApiModel>()));
        Assert.IsTrue((await tools.GetDeploymentAttemptsAsync(10)).Succeeded);

        // Component log (success & failure)
        gateway.GetStringAsync("ResultStatuses/Log?requestId=10&resultId=20", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("comp-log", 200));
        var compLogOk = await tools.GetDeploymentComponentLogAsync(10, 20);
        Assert.AreEqual("comp-log", compLogOk.Data);

        gateway.GetStringAsync("ResultStatuses/Log?requestId=10&resultId=21", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<string>("missing", 404));
        Assert.IsFalse((await tools.GetDeploymentComponentLogAsync(10, 21)).Succeeded);

        // Attempt log (success & failure)
        gateway.GetStringAsync("Request/10/attempts/log?deploymentResultId=30", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("attempt-log", 200));
        Assert.AreEqual("attempt-log", (await tools.GetDeploymentAttemptLogAsync(10, 30)).Data);

        gateway.GetStringAsync("Request/10/attempts/log?deploymentResultId=31", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<string>("missing", 404));
        Assert.IsFalse((await tools.GetDeploymentAttemptLogAsync(10, 31)).Succeeded);

        // Request log: visibility check fails (with error and with null error), log fetch fails, log fetch succeeds
        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=90", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<DeploymentRequestApiModel>("forbidden", 403));
        Assert.IsFalse((await tools.GetDeploymentRequestLogAsync(90)).Succeeded);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=91", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<DeploymentRequestApiModel>(true, null, null, 200));
        StringAssert.Contains((await tools.GetDeploymentRequestLogAsync(91)).Error!, "not found");

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=92", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 92 }));
        gateway.GetStringAsync("RequestStatuses/Log?requestId=92", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<string>("log error", 500));
        Assert.IsFalse((await tools.GetDeploymentRequestLogAsync(92)).Succeeded);

        gateway.GetAsync<DeploymentRequestApiModel>("RequestStatuses?requestId=93", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new DeploymentRequestApiModel { Id = 93 }));
        gateway.GetStringAsync("RequestStatuses/Log?requestId=93", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("req-log-ok", 200));
        Assert.AreEqual("req-log-ok", (await tools.GetDeploymentRequestLogAsync(93)).Data);

        // Mutations: blocked by guard vs allowed
        Assert.IsFalse((await roTools.CreateDeploymentRequestAsync("P", "DEV", ["C1"])).Succeeded);
        Assert.IsFalse((await roTools.RestartDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.CancelDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.PauseDeploymentRequestAsync(10)).Succeeded);
        Assert.IsFalse((await roTools.ResumeDeploymentRequestAsync(10)).Succeeded);

        gateway.PostAsync<RequestDto, RequestStatusDto>("Request", Arg.Any<RequestDto>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 55, Status = "Pending" }));
        Assert.IsTrue((await tools.CreateDeploymentRequestAsync("P", "DEV", null!, "url", "num", "txt", true, [])).Succeeded);
        Assert.IsTrue((await tools.CreateDeploymentRequestAsync("P", "DEV", ["C1"], null, null, null, false, null)).Succeeded);

        gateway.PostAsync<object?, RequestStatusDto>("Request/restart?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.RestartDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/cancel?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.CancelDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/pause?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.PauseDeploymentRequestAsync(10)).Succeeded);

        gateway.PutAsync<object?, RequestStatusDto>("Request/resume?requestId=10", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new RequestStatusDto { Id = 10 }));
        Assert.IsTrue((await tools.ResumeDeploymentRequestAsync(10)).Succeeded);
    }

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

        gateway.GetAsync<List<DeployComponentDto>>("Request/BundleComponents?projectId=1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployComponentDto>()));
        Assert.IsTrue((await tools.ListBundleComponentsAsync(1, null, null)).Succeeded);

        gateway.GetAsync<List<DeployComponentDto>>("Request/BundleComponents?projectId=1&buildDefinitionName=Def&buildUrl=http%3A%2F%2Fb", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DeployComponentDto>()));
        Assert.IsTrue((await tools.ListBundleComponentsAsync(1, "Def", "http://b")).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<BundledRequestsApiModel>()));
        Assert.IsTrue((await tools.ListBundledRequestsAsync(null!)).Succeeded);
        Assert.IsTrue((await tools.ListBundledRequestsAsync(["  "])).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests?projectNames=P1&projectNames=P2", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<BundledRequestsApiModel>()));
        Assert.IsTrue((await tools.ListBundledRequestsAsync([" P1 ", "P2"])).Succeeded);

        gateway.GetAsync<List<BundledRequestsApiModel>>("BundledRequests/RequestsForBundle?bundleName=B1", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<BundledRequestsApiModel>()));
        Assert.IsTrue((await tools.ListRequestsForBundleAsync("B1")).Succeeded);
    }

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

        Assert.IsFalse((await roTools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj")).Succeeded);

        gateway.PostAsync<CopyEnvBuildDto, CopyEnvBuildResponseDto>("CopyEnvBuild", Arg.Any<CopyEnvBuildDto>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new CopyEnvBuildResponseDto { Success = true }));
        Assert.IsTrue((await tools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj", null)).Succeeded);
        Assert.IsTrue((await tools.CopyEnvironmentBuildAsync("SRC", "DST", "Proj", "CompA;CompB")).Succeeded);
    }

    [TestMethod]
    public async Task PropertyResolutionTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        Assert.ThrowsExactly<ArgumentNullException>(() => new PropertyResolutionTools(null!));

        var tools = new PropertyResolutionTools(gateway);

        // GetPropertyValuesAsync: no filters, both filters, failure
        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PropertyValueDto>()));
        Assert.IsTrue((await tools.GetPropertyValuesAsync(null, null)).Succeeded);

        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues?propertyName=P1&environmentName=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PropertyValueDto>()));
        Assert.IsTrue((await tools.GetPropertyValuesAsync(" P1 ", " DEV ")).Succeeded);

        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues?propertyName=ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<PropertyValueDto>>("fail", 500));
        Assert.IsFalse((await tools.GetPropertyValuesAsync("ERR", null)).Succeeded);

        // SearchPropertyValuesAsync: with and without filters, failure
        gateway.PutAsync<PagedDataOperators, GetScopedPropertyValuesResponseDto>(
                "RefDataSearchPropertyValues?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetScopedPropertyValuesResponseDto { Items = [] }));
        Assert.IsTrue((await tools.SearchPropertyValuesAsync(1, 25, "Prop", "Scope")).Succeeded);
        Assert.IsTrue((await tools.SearchPropertyValuesAsync(1, 25, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetScopedPropertyValuesResponseDto>(
                "RefDataSearchPropertyValues?page=2&limit=10",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<GetScopedPropertyValuesResponseDto>("fail", 500));
        Assert.IsFalse((await tools.SearchPropertyValuesAsync(2, 10)).Succeeded);

        // GetPropertyScopeOptionsAsync: success and failure
        gateway.GetAsync<List<PropertyValueScopeOptionApiModel>>("PropertyValues/ScopeOptions?propertyValueScope=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PropertyValueScopeOptionApiModel>()));
        Assert.IsTrue((await tools.GetPropertyScopeOptionsAsync("DEV")).Succeeded);

        gateway.GetAsync<List<PropertyValueScopeOptionApiModel>>("PropertyValues/ScopeOptions?propertyValueScope=ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<PropertyValueScopeOptionApiModel>>("fail", 500));
        Assert.IsFalse((await tools.GetPropertyScopeOptionsAsync("ERR")).Succeeded);

        // SearchPropertyValuesAuditAsync: with filters, without filters, failure
        gateway.PutAsync<PagedDataOperators, GetPropertyValuesAuditListResponseDto>(
                "PropertyValuesAudit?page=1&limit=25&useAndLogic=true",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetPropertyValuesAuditListResponseDto { Items = [] }));
        Assert.IsTrue((await tools.SearchPropertyValuesAuditAsync(1, 25, "P", "E", "U")).Succeeded);
        Assert.IsTrue((await tools.SearchPropertyValuesAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetPropertyValuesAuditListResponseDto>(
                "PropertyValuesAudit?page=2&limit=10&useAndLogic=true",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<GetPropertyValuesAuditListResponseDto>("fail", 500));
        Assert.IsFalse((await tools.SearchPropertyValuesAuditAsync(2, 10)).Succeeded);
    }

    [TestMethod]
    public async Task TerraformCatalogTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new TerraformCatalogTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new TerraformCatalogTools(gateway, null!));

        var tools = new TerraformCatalogTools(gateway, allowGuard);
        var roTools = new TerraformCatalogTools(gateway, roGuard);

        gateway.GetAsync<List<TerraformTemplateManifest>>("Terraform/templates", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<TerraformTemplateManifest>()));
        Assert.IsTrue((await tools.ListTerraformTemplatesAsync()).Succeeded);

        gateway.GetAsync<TerraformTemplateManifest>("Terraform/templates/redis", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success<TerraformTemplateManifest>(null!));
        Assert.IsTrue((await tools.GetTerraformTemplateAsync("redis", null)).Succeeded);

        gateway.GetAsync<TerraformTemplateManifest>("Terraform/templates/redis/1.0.0", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success<TerraformTemplateManifest>(null!));
        Assert.IsTrue((await tools.GetTerraformTemplateAsync("redis", " 1.0.0 ")).Succeeded);

        gateway.GetAsync<TerraformTemplateResolutionApiModel>("Terraform/templates/redis/1.0.0/resolution?projectId=1&environmentName=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformTemplateResolutionApiModel()));
        Assert.IsTrue((await tools.ResolveTerraformTemplateAsync("redis", "1.0.0", 1, "DEV", null)).Succeeded);

        gateway.GetAsync<TerraformTemplateResolutionApiModel>("Terraform/templates/redis/1.0.0/resolution?projectId=1&environmentName=DEV&componentId=9", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformTemplateResolutionApiModel()));
        Assert.IsTrue((await tools.ResolveTerraformTemplateAsync("redis", "1.0.0", 1, "DEV", 9)).Succeeded);

        gateway.GetAsync<TerraformPlanApiModel>("Terraform/plan/77", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new TerraformPlanApiModel()));
        Assert.IsTrue((await tools.GetTerraformPlanAsync(77)).Succeeded);

        gateway.GetAsync<List<TerraformInventoryProvenanceApiModel>>("Terraform/plan/77/inventory", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<TerraformInventoryProvenanceApiModel>()));
        Assert.IsTrue((await tools.GetTerraformPlanInventoryAsync(77)).Succeeded);

        // ConfirmTerraformPlanAsync: empty hash, blocked by guard, allowed
        Assert.IsFalse((await tools.ConfirmTerraformPlanAsync(77, "  ", "DEV")).Succeeded);
        Assert.IsFalse((await roTools.ConfirmTerraformPlanAsync(77, "sha256", "DEV")).Succeeded);

        gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/77/confirm?planContentSha256=sha256", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("confirmed"));
        Assert.IsTrue((await tools.ConfirmTerraformPlanAsync(77, " sha256 ", "DEV")).Succeeded);

        // DeclineTerraformPlanAsync: blocked by guard, allowed
        Assert.IsFalse((await roTools.DeclineTerraformPlanAsync(77, "DEV")).Succeeded);
        gateway.PostWithoutResponseBodyAsync<object?>("Terraform/plan/77/decline", null, Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("declined"));
        Assert.IsTrue((await tools.DeclineTerraformPlanAsync(77, "DEV")).Succeeded);
    }

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

        // Valid statuses: starting, stopping, restarting
        gateway.PutStringResponseAsync("DaemonStatus", Arg.Any<DaemonStatusApiModel>(), Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success("ok"));

        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "starting")).Succeeded);
        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "stopping")).Succeeded);
        Assert.IsTrue((await tools.ChangeDaemonStatusAsync("DEV", "srv", "svc", "restarting")).Succeeded);
    }
}
