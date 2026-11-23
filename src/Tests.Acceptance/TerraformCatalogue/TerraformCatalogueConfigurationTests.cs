using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.TerraformCatalogue
{
    [TestClass]
    [TestCategory("LiveEnvironment")]
    [TestCategory("TerraformCatalogue")]
    public class TerraformCatalogueConfigurationTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void GetAndConfigureTerraformTemplate_StockCatalogueResolutionAndOptimisticConcurrency_ValidatesManifestInputsAndRevision()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);
            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);

            var templatesResp = caller.ExecuteRaw("Terraform/templates", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, templatesResp.StatusCode, templatesResp.Content);
            StringAssert.Contains(templatesResp.Content ?? string.Empty, "service-bus");
            StringAssert.Contains(templatesResp.Content ?? string.Empty, "kafka-topic");

            var detailResp = caller.ExecuteRaw("Terraform/templates/service-bus", Method.Get, new Dictionary<string, string>
            {
                ["version"] = "1.0.0"
            });
            Assert.AreEqual(HttpStatusCode.OK, detailResp.StatusCode, detailResp.Content);
            StringAssert.Contains(detailResp.Content ?? string.Empty, "namespace_name");
            StringAssert.Contains(detailResp.Content ?? string.Empty, "queue_name");

            var tempComponentName = $"DOrc_Live_TF_{Guid.NewGuid():N}"[..24];
            int? createdComponentId = null;
            try
            {
                var instantiateResp = caller.ExecuteRaw("Terraform/templates/service-bus/1.0.0/instantiate", Method.Post, body: new TerraformTemplateInstantiateRequestApiModel
                {
                    ProjectId = project.ProjectId,
                    ComponentName = tempComponentName
                });
                Assert.AreEqual(HttpStatusCode.OK, instantiateResp.StatusCode, $"Instantiate failed: {instantiateResp.Content}");
                var instantiated = DorcApiClient.Deserialize<TerraformTemplateInstantiateResponseApiModel>(instantiateResp.Content);
                Assert.IsNotNull(instantiated);
                Assert.IsNotNull(instantiated.Component);
                var componentId = instantiated.Component.ComponentId;
                Assert.IsTrue(componentId.HasValue, "Instantiated Terraform component ID should not be null.");
                var createdComponentIdValue = componentId.Value;
                createdComponentId = createdComponentIdValue;
                Assert.IsGreaterThan(0, createdComponentIdValue);

                var resolutionResp = caller.ExecuteRaw("Terraform/templates/service-bus/1.0.0/resolution", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["environmentName"] = LiveChildEnvName,
                    ["componentId"] = createdComponentIdValue.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resolutionResp.StatusCode, resolutionResp.Content);
                var resolution = DorcApiClient.Deserialize<TerraformTemplateResolutionApiModel>(resolutionResp.Content);
                Assert.IsNotNull(resolution);
                Assert.AreEqual(LiveProjectName, resolution.ProjectName);
                Assert.AreEqual(LiveChildEnvName, resolution.EnvironmentName);
                Assert.IsNotEmpty(resolution.Parameters);

                var configQuery = new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["componentId"] = createdComponentIdValue.ToString(),
                    ["environmentName"] = LiveChildEnvName
                };
                var invalidConfigResp = caller.ExecuteRaw(
                    "Terraform/templates/service-bus/1.0.0/configuration",
                    Method.Put,
                    configQuery,
                    body: new SaveCatalogEnvironmentInputsApiModel
                    {
                        ExpectedRevision = resolution.SavedConfiguration?.Revision ?? 0,
                        Inputs = new Dictionary<string, string>
                        {
                            ["namespace_name"] = "bad!"
                        }
                    });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    invalidConfigResp.StatusCode,
                    $"Expected 400 BadRequest for invalid namespace_name pattern, got {invalidConfigResp.StatusCode}: {invalidConfigResp.Content}");

                var staleConfigResp = caller.ExecuteRaw(
                    "Terraform/templates/service-bus/1.0.0/configuration",
                    Method.Put,
                    configQuery,
                    body: new SaveCatalogEnvironmentInputsApiModel
                    {
                        ExpectedRevision = 999999,
                        Inputs = new Dictionary<string, string>
                        {
                            ["namespace_name"] = "dorc-live-sb-valid01",
                            ["location"] = "westeurope"
                        }
                    });
                Assert.AreEqual(
                    HttpStatusCode.Conflict,
                    staleConfigResp.StatusCode,
                    $"Expected 409 Conflict for stale ExpectedRevision, got {staleConfigResp.StatusCode}: {staleConfigResp.Content}");

                // Verify GET /Terraform/templates/{name} and GET /Terraform/templates/{name}/{version}
                var latestTplResp = caller.ExecuteRaw("Terraform/templates/service-bus", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, latestTplResp.StatusCode, $"GET /Terraform/templates/service-bus failed: {latestTplResp.Content}");
                StringAssert.Contains(latestTplResp.Content ?? string.Empty, "service-bus");

                var versionedTplResp = caller.ExecuteRaw("Terraform/templates/service-bus/1.0.0", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, versionedTplResp.StatusCode, $"GET /Terraform/templates/service-bus/1.0.0 failed: {versionedTplResp.Content}");
                StringAssert.Contains(versionedTplResp.Content ?? string.Empty, "1.0.0");

                // Verify GET & POST /Terraform/components/{componentId}/upgrade/{name}/{version}?projectId={projectId}
                // Upgrading to the same version must return 400 BadRequest ("Choose a newer stable version of this module.")
                var upgradeSameVersionPreviewResp = caller.ExecuteRaw(
                    $"Terraform/components/{createdComponentIdValue}/upgrade/service-bus/1.0.0",
                    Method.Get,
                    new Dictionary<string, string> { ["projectId"] = project.ProjectId.ToString() });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    upgradeSameVersionPreviewResp.StatusCode,
                    $"Expected 400 BadRequest when previewing upgrade to same version 1.0.0, got {upgradeSameVersionPreviewResp.StatusCode}: {upgradeSameVersionPreviewResp.Content}");

                var upgradeUnconfirmedResp = caller.ExecuteRaw(
                    $"Terraform/components/{createdComponentIdValue}/upgrade/service-bus/1.0.0",
                    Method.Post,
                    new Dictionary<string, string> { ["projectId"] = project.ProjectId.ToString() },
                    body: new CatalogUpgradeRequestApiModel
                    {
                        ConfirmImpact = false,
                        EnvironmentIds = new List<int> { childEnv.EnvironmentId },
                        ConfigurationRevisions = new Dictionary<string, long>()
                    });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    upgradeUnconfirmedResp.StatusCode,
                    $"Expected 400 BadRequest when ConfirmImpact=false on POST /Terraform/components/.../upgrade, got {upgradeUnconfirmedResp.StatusCode}: {upgradeUnconfirmedResp.Content}");

                // Verify GET /Projects/{projectId}/terraform-state-backend-cutovers/preview?environmentId={envId}
                var cutoverPreviewResp = caller.ExecuteRaw(
                    $"Projects/{project.ProjectId}/terraform-state-backend-cutovers/preview",
                    Method.Get,
                    new Dictionary<string, string> { ["environmentId"] = childEnv.EnvironmentId.ToString() });
                Assert.IsTrue(
                    cutoverPreviewResp.StatusCode == HttpStatusCode.OK || cutoverPreviewResp.StatusCode == HttpStatusCode.BadRequest,
                    $"Unexpected status from terraform-state-backend-cutovers/preview: {cutoverPreviewResp.StatusCode} {cutoverPreviewResp.Content}");

                // Verify Terraform plan lifecycle endpoints guard against non-existent deploymentResultId
                var missingPlanResp = caller.ExecuteRaw("Terraform/plan/999999999", Method.Get);
                Assert.IsTrue(
                    missingPlanResp.StatusCode == HttpStatusCode.NotFound || missingPlanResp.StatusCode == HttpStatusCode.BadRequest,
                    $"Expected 404/400 for missing Terraform plan 999999999, got {missingPlanResp.StatusCode}");
                var missingInventoryResp = caller.ExecuteRaw("Terraform/plan/999999999/inventory", Method.Get);
                Assert.IsTrue(
                    missingInventoryResp.StatusCode == HttpStatusCode.NotFound || missingInventoryResp.StatusCode == HttpStatusCode.BadRequest,
                    $"Expected 404/400 for missing Terraform plan inventory 999999999, got {missingInventoryResp.StatusCode}");
            }
            finally
            {
                if (createdComponentId is int componentIdToDelete)
                {
                    var refResp = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
                    if (refResp.StatusCode == HttpStatusCode.OK)
                    {
                        var refData = DorcApiClient.Deserialize<RefDataApiModel>(refResp.Content);
                        if (refData?.Components != null)
                        {
                            refData.Components = refData.Components
                                .Where(c => c.ComponentId != componentIdToDelete &&
                                            !string.Equals(c.ComponentName, tempComponentName, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            caller.ExecuteRaw("RefData", Method.Put, body: refData);
                        }
                    }
                }
            }
        }
    }
}
