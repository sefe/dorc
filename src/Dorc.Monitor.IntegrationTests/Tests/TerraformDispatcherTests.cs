using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Terraform;
using Dorc.Monitor.RequestProcessors;
using Dorc.Monitor.Terraform;
using Dorc.Monitor.Tests.Init;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Monitor.IntegrationTests.Tests;

[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public class TerraformDispatcherTests : MonitorServiceTestBase
{
    [TestMethod]
    public void TwoPhaseTerraformLifecycle_CreatesPlan_VerifiesArtifactIntegrity_AppliesImmutableSnapshot_AndRegistersCloudResources()
    {
        var ext = RandomNumberGenerator.GetHexString(6);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"dorc-ws4-tf-{ext}");
        var sourceRepoDir = Path.Combine(tempRoot, "module-repo");
        var blobStoreDir = Path.Combine(tempRoot, "blobs");
        var workingRootDir = Path.Combine(tempRoot, "runner-work");
        Directory.CreateDirectory(sourceRepoDir);
        Directory.CreateDirectory(blobStoreDir);
        Directory.CreateDirectory(workingRootDir);

        try
        {
            File.WriteAllText(Path.Combine(sourceRepoDir, "main.tf"), """
                variable "storage_account_name" { type = string }
                variable "access_key" { type = string }
                resource "azurerm_storage_account" "primary" {
                  name = var.storage_account_name
                }
                """);

            var scenario = SeedCatalogScenario(ext, sourceRepoDir);
            var storageWorker = new DirectoryBackedAzureStorageWorker(blobStoreDir);
            var pipeBackedDispatcher = new PipeBackedTerraformDispatcher(
                provider,
                storageWorker,
                workingRootDir,
                sourceRepoDir,
                scenario.SubscriptionId,
                scenario.StorageAccountName);
            SubstituteScopedWith<ITerraformDispatcher>(pipeBackedDispatcher);

            var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
            var pendingProcessor = provider.GetRequiredService<IPendingRequestProcessor>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var serializer = new DeploymentRequestDetailSerializer();

            // Phase 1: Execute Pending request -> CreatePlan -> transitions to WaitingConfirmation
            var request = requestsSource.GetRequest(scenario.RequestId);
            pendingProcessor.Execute(
                new RequestToProcessDto(request, serializer.Deserialize(request.RequestDetails)),
                CancellationToken.None,
                loggerFactory);

            var postPlanRequest = requestsSource.GetRequest(scenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.WaitingConfirmation.ToString(), postPlanRequest.Status);

            var result = requestsSource.GetDeploymentResultsForRequest(scenario.RequestId).Single();
            Assert.AreEqual(DeploymentResultStatus.WaitingConfirmation.ToString(), result.Status);

            var receipt = requestsSource.GetTerraformDeploymentReceipt(result.Id);
            Assert.IsNotNull(receipt);
            Assert.AreEqual("azure-storage-account", receipt.TemplateName);
            Assert.AreEqual("1.0.0", receipt.TemplateVersion);
            Assert.AreEqual("sttfstate01", receipt.StateStorageAccount);
            Assert.AreEqual("tfstate", receipt.StateContainerName);
            StringAssert.EndsWith(receipt.StateKey, ".tfstate");

            var planContent = storageWorker.LoadFileFromBlobs(result.Id.CreateTerraformPlanContentBlobName());
            StringAssert.Contains(planContent, "[REDACTED]");
            Assert.IsFalse(planContent.Contains("SuperSecretAccessKey!99", StringComparison.Ordinal), "Sensitive parameter must be redacted in stored plan text.");

            // Mutate the live module directory after plan creation to prove Apply uses the immutable .source.zip snapshot
            File.WriteAllText(Path.Combine(sourceRepoDir, "main.tf"), "# MUTATED SOURCE THAT MUST NOT BE APPLIED");

            // Confirm the plan with matching artifact SHA-256 digests
            var digests = TerraformPlanArtifactIntegrity.Calculate(storageWorker, result.Id);
            Assert.AreEqual(receipt.PlanSha256, digests.PlanSha256);
            Assert.AreEqual(receipt.SourceSha256, digests.SourceSha256);
            Assert.AreEqual(receipt.PlanContentSha256, digests.PlanContentSha256);

            var confirmed = requestsSource.TryConfirmTerraformPlan(result, new TerraformPlanApproval
            {
                DeploymentResultId = result.Id,
                DeploymentRequestId = scenario.RequestId,
                ApprovedBy = "approver.user@sefe.eu",
                ApprovedAt = DateTimeOffset.UtcNow,
                PlanSha256 = digests.PlanSha256,
                SourceSha256 = digests.SourceSha256,
                PlanContentSha256 = digests.PlanContentSha256
            });
            Assert.IsTrue(confirmed);

            // Tamper check: corrupting the plan blob before Apply must be rejected by TerraformPlanArtifactIntegrity.Verify
            var planBlobPath = Path.Combine(blobStoreDir, result.Id.CreateTerraformPlanBlobName());
            var originalPlanBytes = File.ReadAllBytes(planBlobPath);
            File.WriteAllBytes(planBlobPath, "tampered-binary-plan"u8.ToArray());

            Assert.Throws<InvalidOperationException>(() =>
                TerraformPlanArtifactIntegrity.Verify(
                    requestsSource.GetTerraformPlanApproval(result.Id),
                    scenario.RequestId,
                    result.Id,
                    planBlobPath,
                    Path.Combine(blobStoreDir, result.Id.CreateTerraformPlanBlobName() + ".source.zip")));

            // Restore intact plan binary and execute Phase 2 (ApplyPlan)
            File.WriteAllBytes(planBlobPath, originalPlanBytes);

            var confirmedRequest = requestsSource.GetRequest(scenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Confirmed.ToString(), confirmedRequest.Status);
            pendingProcessor.Execute(
                new RequestToProcessDto(confirmedRequest, serializer.Deserialize(confirmedRequest.RequestDetails)),
                CancellationToken.None,
                loggerFactory);

            var completedRequest = requestsSource.GetRequest(scenario.RequestId);
            Assert.AreEqual(DeploymentRequestStatus.Completed.ToString(), completedRequest.Status);

            var completedResult = requestsSource.GetDeploymentResultsForRequest(scenario.RequestId).Single();
            Assert.AreEqual(DeploymentResultStatus.Complete.ToString(), completedResult.Status);

            // Verify Apply used the planned snapshot (not the mutated live directory)
            StringAssert.Contains(pipeBackedDispatcher.LastAppliedMainTf ?? string.Empty, "azurerm_storage_account");
            Assert.IsFalse((pipeBackedDispatcher.LastAppliedMainTf ?? string.Empty).Contains("MUTATED SOURCE", StringComparison.Ordinal));

            // Verify TerraformDeploymentOutcome and Cloud Resource registration on the target environment
            var outcome = requestsSource.GetTerraformDeploymentOutcome(result.Id);
            Assert.IsNotNull(outcome);
            Assert.AreEqual("Succeeded", outcome.ApplyOutcome);
            Assert.AreEqual("Succeeded", outcome.InventoryOutcome);
            Assert.AreEqual(1, outcome.RegisteredCount);

            var cloudResourcesSource = provider.GetRequiredService<ICloudResourcesPersistentSource>();
            var registeredResources = cloudResourcesSource.GetForEnvironmentId(scenario.EnvironmentId);
            Assert.IsTrue(registeredResources.Any(r =>
                r.Name == scenario.StorageAccountName &&
                r.ResourceType == "azurerm_storage_account" &&
                r.Provider == "Azure"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private void SubstituteScopedWith<T>(T instance) where T : class
    {
        collection.Replace(ServiceDescriptor.Scoped<T>(_ => instance));
        provider = collection.BuildServiceProvider();
    }

    private (int RequestId, int EnvironmentId, string SubscriptionId, string StorageAccountName) SeedCatalogScenario(string ext, string sourceRepoDir)
    {
        var envName = $"tf-env-{ext}";
        var projectName = $"tf-proj-{ext}";
        var compName = $"tf-comp-{ext}";
        var subscriptionId = Guid.NewGuid().ToString();
        var storageAccountName = $"stdorc{ext}".ToLowerInvariant();

        int envId;
        int compId;
        var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
        using (var context = contextFactory.GetContext())
        {
            var env = new Environment
            {
                Name = envName,
                Secure = false,
                IsProd = false,
                Description = "Terraform integration env",
                ObjectId = Guid.NewGuid()
            };
            var project = new Project
            {
                Name = projectName,
                Description = "Terraform catalog project",
                ArtefactsUrl = sourceRepoDir,
                ArtefactsSubPaths = "",
                SourceControlType = SourceControlType.GitHub,
                TerraformStateBackendsJson = JsonSerializer.Serialize(new List<TerraformStateBackendApiModel>
                {
                    new()
                    {
                        SubscriptionId = subscriptionId,
                        ResourceGroup = "rg-tfstate",
                        StorageAccount = "sttfstate01",
                        ContainerName = "tfstate"
                    }
                })
            };
            project.Environments.Add(env);

            var component = new Component
            {
                Name = compName,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-storage-account",
                TerraformTemplateVersion = "1.0.0",
                IsEnabled = true,
                StopOnFailure = true
            };
            component.Projects.Add(project);

            context.Environments.Add(env);
            context.Projects.Add(project);
            context.Components.Add(component);
            context.SaveChanges();

            envId = env.Id;
            compId = component.Id;
        }

        var propertiesSource = provider.GetRequiredService<IPropertiesPersistentSource>();
        var propSource = provider.GetRequiredService<IPropertyValuesPersistentSource>();
        void AddEnvProp(string name, string val, bool isSecure)
        {
            try
            {
                propertiesSource.CreateProperty(new PropertyApiModel { Name = name, Secure = isSecure, IsArray = false }, "test");
            }
            catch
            {
                // Property may already exist in shared DB
            }
            propSource.AddPropertyValue(new PropertyValueDto
            {
                Value = val,
                PropertyValueFilter = envName,
                Property = new PropertyApiModel { Name = name, Secure = isSecure }
            });
        }

        AddEnvProp("TerraformClientId", Guid.NewGuid().ToString(), isSecure: false);
        AddEnvProp("TerraformClientSecret", "spn-client-secret", isSecure: true);
        AddEnvProp("TerraformTenantId", Guid.NewGuid().ToString(), isSecure: false);
        AddEnvProp("TerraformSubscriptionId", subscriptionId, isSecure: false);
        AddEnvProp("access_key", "SuperSecretAccessKey!99", isSecure: true);

        var detail = new DeploymentRequestDetail
        {
            EnvironmentName = envName,
            BuildDetail = new BuildDetail { Project = projectName, BuildNumber = "1.0.0" },
            Components = new List<string> { compName },
            Properties = new List<PropertyPair>
            {
                new("storage_account_name", storageAccountName)
            }
        };
        var serializer = new DeploymentRequestDetailSerializer();
        var requestsSource = provider.GetRequiredService<IRequestsPersistentSource>();
        var requestId = requestsSource.SubmitRequest(new DeploymentRequest
        {
            Project = projectName,
            Environment = envName,
            BuildNumber = "1.0.0",
            Components = compName,
            RequestDetails = serializer.Serialize(detail),
            UserName = "submitter@sefe.eu",
            RequestedTime = DateTimeOffset.UtcNow,
            IsProd = false,
            Status = DeploymentRequestStatus.Pending.ToString()
        });

        using (var context = contextFactory.GetContext())
        {
            var existingInput = context.CatalogRequestInputs.FirstOrDefault(
                c => c.DeploymentRequestId == requestId && c.ComponentId == compId);
            if (existingInput != null)
            {
                existingInput.InputsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["storage_account_name"] = storageAccountName
                });
            }
            else
            {
                context.CatalogRequestInputs.Add(new CatalogRequestInputs
                {
                    DeploymentRequestId = requestId,
                    ComponentId = compId,
                    TemplateName = "azure-storage-account",
                    TemplateVersion = "1.0.0",
                    InputsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["storage_account_name"] = storageAccountName
                    })
                });
            }
            context.SaveChanges();
        }

        return (requestId, envId, subscriptionId, storageAccountName);
    }
}
