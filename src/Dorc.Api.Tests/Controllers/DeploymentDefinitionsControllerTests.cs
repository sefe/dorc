using System.Net;
using System.Net.Http.Json;
using Dorc.ApiModel;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class DeploymentDefinitionsControllerTests
{
    private static DorcApiFixture _fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _) => _fixture = new DorcApiFixture("dorc_api_tf_cat");

    [ClassCleanup]
    public static void Cleanup() => _fixture?.Dispose();

    [TestMethod]
    public async Task TerraformCatalog_TemplateDiscovery_Instantiation_SavedConfiguration_AndDeploymentDefinitions_WorkEndToEnd()
    {
        using var adminClient = _fixture.CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        var templates = await adminClient.GetFromJsonAsync<List<Dorc.Terraform.Catalog.TerraformTemplateManifest>>(
            "/Terraform/templates", _fixture.JsonOptions);
        Assert.IsNotNull(templates);
        Assert.IsTrue(templates.Any(t => t.Name == "azure-storage-account" && t.Version == "1.0.0"));

        var latestTemplate = await adminClient.GetFromJsonAsync<Dorc.Terraform.Catalog.TerraformTemplateManifest>(
            "/Terraform/templates/azure-storage-account", _fixture.JsonOptions);
        Assert.IsNotNull(latestTemplate);
        Assert.AreEqual("1.0.0", latestTemplate.Version);

        var projectName = "CatProj_" + Guid.NewGuid().ToString("N")[..8];
        var envName = "CatEnv_" + Guid.NewGuid().ToString("N")[..8];

        var envResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, envResp.StatusCode);
        var env = await envResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(env);

        var createProjResp = await adminClient.PostAsJsonAsync("/RefData", new RefDataApiModel
        {
            Project = new ProjectApiModel
            {
                ProjectName = projectName,
                ProjectDescription = "Catalog E2E project",
                ArtefactsUrl = "https://github.com/sefe/dorc-terraform-modules",
                ArtefactsSubPaths = ".github/workflows/ci.yml",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.GitHub,
                TerraformStateBackends = new List<TerraformStateBackendApiModel>
                {
                    new()
                    {
                        SubscriptionId = Guid.NewGuid().ToString(),
                        ResourceGroup = "rg-tfstate",
                        StorageAccount = "sttfstate01",
                        ContainerName = "tfstate"
                    }
                }
            },
            Components = new List<ComponentApiModel>()
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createProjResp.StatusCode);

        var mapResp = await adminClient.PostAsync(
            $"/RefDataProjectEnvironmentMappings?project={Uri.EscapeDataString(projectName)}&environment={Uri.EscapeDataString(envName)}",
            null);
        Assert.AreEqual(HttpStatusCode.OK, mapResp.StatusCode);

        int projectId;
        using (var context = _fixture.ContextFactory.GetContext())
        {
            projectId = context.Projects.Single(p => p.Name == projectName).Id;
        }

        var instantiateResp = await adminClient.PostAsJsonAsync(
            "/Terraform/templates/azure-storage-account/1.0.0/instantiate",
            new TerraformTemplateInstantiateRequestApiModel
            {
                ProjectId = projectId,
                ComponentName = "storage_acct_comp"
            }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, instantiateResp.StatusCode);
        var instantiated = await instantiateResp.Content.ReadFromJsonAsync<TerraformTemplateInstantiateResponseApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(instantiated);
        Assert.IsNotNull(instantiated.Component);
        Assert.IsTrue(instantiated.Component.ComponentId > 0);
        Assert.AreEqual(ComponentType.Terraform, instantiated.Component.ComponentType);
        Assert.AreEqual(TerraformSourceType.Catalog, instantiated.Component.TerraformSourceType);

        var componentId = instantiated.Component.ComponentId!.Value;

        var saveConfigResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 0,
                Inputs = new Dictionary<string, string>
                {
                    ["storage_account_name"] = "stdorc" + Guid.NewGuid().ToString("N")[..10],
                    ["account_replication_type"] = "GRS"
                }
            }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, saveConfigResp.StatusCode);
        var savedConfig = await saveConfigResp.Content.ReadFromJsonAsync<CatalogEnvironmentInputsApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(savedConfig);
        Assert.AreEqual(1, savedConfig.Revision);

        var staleSaveResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 0,
                Inputs = new Dictionary<string, string> { ["storage_account_name"] = "stdorcstale01" }
            }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.Conflict, staleSaveResp.StatusCode);

        var sensitiveSaveResp = await adminClient.PutAsJsonAsync(
            $"/Terraform/templates/azure-storage-account/1.0.0/configuration?projectId={projectId}&componentId={componentId}&environmentName={Uri.EscapeDataString(envName)}",
            new SaveCatalogEnvironmentInputsApiModel
            {
                ExpectedRevision = 1,
                Inputs = new Dictionary<string, string> { ["access_key"] = "plain-secret-key" }
            }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, sensitiveSaveResp.StatusCode);

        var resolutionResp = await adminClient.GetFromJsonAsync<TerraformTemplateResolutionApiModel>(
            $"/Terraform/templates/azure-storage-account/1.0.0/resolution?projectId={projectId}&environmentName={Uri.EscapeDataString(envName)}&componentId={componentId}",
            _fixture.JsonOptions);
        Assert.IsNotNull(resolutionResp);
        Assert.AreEqual(1, resolutionResp.SavedConfiguration.Revision);
        var storageNameParam = resolutionResp.Parameters.Single(p => p.Name == "storage_account_name");
        Assert.AreEqual(TerraformParameterResolutionStatus.SavedConfiguration, storageNameParam.Status);

        var definitionsResp = await adminClient.GetFromJsonAsync<GetDeploymentDefinitionsResponse>(
            $"/DeploymentDefinitions?projectId={projectId}&componentType=Terraform&sourceType=Catalog", _fixture.JsonOptions);
        Assert.IsNotNull(definitionsResp);
        Assert.AreEqual(1, definitionsResp.TotalItems);
        Assert.AreEqual("Catalog azure-storage-account@1.0.0", definitionsResp.Items.Single().SourceSummary);

        var historyResp = await adminClient.GetFromJsonAsync<GetDeploymentDefinitionHistoryResponse>(
            $"/DeploymentDefinitions/history?projectId={projectId}", _fixture.JsonOptions);
        Assert.IsNotNull(historyResp);
        Assert.IsTrue(historyResp.TotalItems >= 1);
    }
}
