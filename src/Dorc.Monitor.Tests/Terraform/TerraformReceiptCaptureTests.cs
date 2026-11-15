using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Terraform;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.Terraform;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests.Terraform;

[TestClass]
public class TerraformReceiptCaptureTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CatalogSnapshot_PreservesFalseAndRequestOverridePrecedenceWithoutMutatingSharedProperties(bool overridden)
    {
        var component = new Dorc.ApiModel.ComponentApiModel { ComponentId = 3, TerraformTemplateVersion = "2.0.0" };
        var original = new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>
        {
            ["create_service"] = new() { Value = "true" }
        };
        var detail = new Dorc.Core.DeploymentRequestDetail();
        if (overridden) detail.Properties.Add(new Dorc.Core.PropertyPair("create_service", "true"));
        var snapshot = new CatalogRequestInputs
        {
            ComponentId = 3, TemplateName = "database", TemplateVersion = "1.0.0",
            InputsJson = "{\"create_service\":\"false\"}"
        };
        var result = TerraformCatalogRequestSnapshot.Apply(component, original,
            new Dorc.Core.DeploymentRequestDetailSerializer().Serialize(detail), snapshot);
        Assert.AreEqual(overridden ? "true" : "false", result["create_service"].Value);
        Assert.AreEqual("true", original["create_service"].Value);
        Assert.AreEqual("1.0.0", component.TerraformTemplateVersion);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Receipt_UsesActualArchiveProvenanceAndRefusesMismatchedIdentity(bool wrongResult)
    {
        var directory = Path.Combine(System.Environment.CurrentDirectory, "receipt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("_dorc_provenance.json").Open()))
                writer.Write(JsonSerializer.Serialize(new
                {
                    RequestId = 7, DeploymentResultId = wrongResult ? 99 : 42,
                    TemplateName = "planned-module", TemplateVersion = "1.0.0",
                    GitCommitSha = new string('a', 40), StateKey = "original/state.tfstate",
                    ArchivedAtUtc = DateTimeOffset.Parse("2026-10-20T10:00:00Z")
                }));
            var planContent = Path.Combine(directory, "plan.txt");
            var plan = Path.Combine(directory, "plan.bin");
            File.WriteAllText(planContent, "reviewed plan");
            File.WriteAllBytes(plan, new byte[] { 1, 2, 3 });
            var receipt = new TerraformDeploymentReceipt { DeploymentRequestId = 7, DeploymentResultId = 42 };
            if (wrongResult)
            {
                Assert.Throws<InvalidDataException>(() => TerraformReceiptCapture.ReadPlannedSource(receipt, path, planContent, plan));
                return;
            }
            TerraformReceiptCapture.ReadPlannedSource(receipt, path, planContent, plan);
            Assert.AreEqual("planned-module", receipt.TemplateName);
            Assert.AreEqual("1.0.0", receipt.TemplateVersion);
            Assert.AreEqual("original/state.tfstate", receipt.StateKey);
            Assert.AreEqual(TerraformPlanArtifactIntegrity.HashPlanContent("reviewed plan"), receipt.PlanContentSha256);
            Assert.AreEqual(64, receipt.SourceSha256.Length);
            Assert.AreEqual(64, receipt.PlanSha256.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void TerraformDispatcher_CreateDestroyPlanAndApplyDestroyPlan_RecordsDestroyReceiptAndDeregistersResources()
    {
        var blobs = new Dictionary<string, byte[]>();
        var storage = Substitute.For<IAzureStorageAccountWorker>();
        storage.When(w => w.SaveFileToBlobs(Arg.Any<string>())).Do(call =>
        {
            var p = call.Arg<string>();
            blobs[Path.GetFileName(p)] = File.ReadAllBytes(p);
        });
        storage.When(w => w.DownloadFileFromBlobs(Arg.Any<string>(), Arg.Any<string>())).Do(call =>
        {
            var blobName = call.ArgAt<string>(0);
            File.WriteAllBytes(call.ArgAt<string>(1), blobs[blobName]);
        });

        var requests = Substitute.For<IRequestsPersistentSource>();
        requests.GetRequest(7).Returns(new DeploymentRequestApiModel { Id = 7, Project = "proj", UserName = "tester" });
        var stateBackends = new List<TerraformStateBackendApiModel>
        {
            new()
            {
                EnvironmentId = 2598,
                StorageAccount = "platform",
                ContainerName = "state",
                SubscriptionId = "11111111-1111-1111-1111-111111111111"
            }
        };
        requests.GetSubmittedProject(7, 1).Returns(new ProjectApiModel
        {
            ProjectId = 12,
            ProjectName = "proj",
            TerraformStateBackends = stateBackends
        });
        requests.GetRequestDefinitions(7).Returns(new List<DeploymentRequestDefinition>
        {
            new() { DeploymentRequestId = 7, ComponentId = 1, DefinitionRevision = "3" }
        });
        var resultId = Random.Shared.Next(100_000, 900_000);
        TerraformDeploymentReceipt? savedReceipt = null;
        requests.When(r => r.SaveTerraformDeploymentReceipt(Arg.Any<TerraformDeploymentReceipt>()))
            .Do(c => savedReceipt = c.Arg<TerraformDeploymentReceipt>());
        requests.GetTerraformDeploymentReceipt(resultId).Returns(_ => savedReceipt);
        requests.GetTerraformPlanApproval(resultId).Returns(_ => new TerraformPlanApproval
        {
            DeploymentResultId = resultId,
            DeploymentRequestId = 7,
            PlanSha256 = savedReceipt?.PlanSha256 ?? string.Empty,
            SourceSha256 = savedReceipt?.SourceSha256 ?? string.Empty,
            PlanContentSha256 = savedReceipt?.PlanContentSha256 ?? string.Empty,
            ConfirmedBy = TerraformPlanApproval.EphemeralAutoApproveActor
        });

        var configValues = Substitute.For<IConfigValuesPersistentSource>();
        configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("runner-pass");

        var projects = Substitute.For<IProjectsPersistentSource>();
        projects.GetProject("proj").Returns(new ProjectApiModel { ProjectId = 12, ProjectName = "proj" });

        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        environments.GetEnvironment("eph-pr-1").Returns(new EnvironmentApiModel
        {
            EnvironmentId = 2598,
            EnvironmentName = "eph-pr-1",
            IsEphemeral = true
        });

        var registrar = Substitute.For<ITerraformCloudResourceRegistrar>();
        var pipeServer = Substitute.For<IScriptGroupPipeServer>();
        var dispatcher = new TerraformDispatcher(
            Substitute.For<ILogger<TerraformDispatcher>>(),
            requests,
            configValues,
            Substitute.For<IConfigurationSettings>(),
            Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
            pipeServer,
            storage,
            projects,
            Substitute.For<IGitHubHostValidator>(),
            Substitute.For<ICloudResourcesPersistentSource>(),
            Substitute.For<ICloudResourceAuditPersistentSource>(),
            environments,
            Substitute.For<IDatabasesPersistentSource>(),
            Substitute.For<IPropertiesPersistentSource>(),
            registrar);

        dispatcher.SecurityContextFactory = _ => null;
        Func<TerraformRunnerProcessStarter, Dorc.Monitor.RunnerProcess.ProcessSecurityContextBuilder.ProcessSecurityContext?, int, string, CancellationToken, uint> defaultRunner = (starter, _, _, _, _) =>
        {
            if (starter.TerraformRunnerOperation is TerraformRunnerOperations.CreatePlan or TerraformRunnerOperations.CreateDestroyPlan)
            {
                File.WriteAllBytes(starter.PlanFilePath, new byte[] { 9, 8, 7 });
                File.WriteAllText(starter.PlanContentFilePath, "destroy plan content");
                File.WriteAllText(starter.LockFilePath, "# lock");
                if (File.Exists(starter.PlanFilePath + ".source.zip"))
                {
                    File.Delete(starter.PlanFilePath + ".source.zip");
                }
                using var archive = ZipFile.Open(starter.PlanFilePath + ".source.zip", ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("_dorc_provenance.json").Open());
                writer.Write(JsonSerializer.Serialize(new
                {
                    RequestId = 7,
                    DeploymentResultId = resultId,
                    DefinitionRevision = "3",
                    TemplateName = "postgres",
                    TemplateVersion = "1.0.0",
                    GitCommitSha = new string('b', 40),
                    StateKey = "proj/eph-pr-1/infra-postgres.tfstate",
                    ArchivedAtUtc = DateTimeOffset.UtcNow
                }));
            }
            else if (starter.TerraformRunnerOperation == TerraformRunnerOperations.ApplyDestroyPlan)
            {
                File.WriteAllBytes(starter.SourceArchiveFilePath, new byte[] { 1, 2, 3 });
            }
            return 0;
        };
        dispatcher.RunnerProcessRunner = defaultRunner;

        var component = new ComponentApiModel
        {
            ComponentId = 1,
            ComponentName = "Infra-Postgres",
            ComponentType = ComponentType.Terraform,
            TerraformSourceType = TerraformSourceType.SharedFolder,
            ScriptPath = System.Environment.CurrentDirectory
        };
        var result = new DeploymentResultApiModel { Id = resultId, RequestId = 7 };
        var props = new Dictionary<string, VariableValue>();

        // 0. CreatePlan on non-ephemeral environment (verifies "Plan" Operation, non-ephemeral path, and stripping DOrcIsEphemeral)
        var nonEphProps = new Dictionary<string, VariableValue>
        {
            [ComponentProcessor.EphemeralPropertyName] = new() { Value = "true" }
        };
        Assert.IsTrue(dispatcher.Dispatch(
            component, result, nonEphProps, 7, false, "non-eph-env",
            new StringBuilder(), TerraformRunnerOperations.CreatePlan, CancellationToken.None));
        Assert.AreEqual("Plan", savedReceipt!.Operation);
        Assert.IsFalse(nonEphProps.ContainsKey(ComponentProcessor.EphemeralPropertyName));

        // 1. CreateDestroyPlan
        var planOk = dispatcher.Dispatch(
            component, result, props, 7, false, "eph-pr-1",
            new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None);

        Assert.IsTrue(planOk);
        Assert.IsNotNull(savedReceipt);
        Assert.AreEqual("Destroy", savedReceipt!.Operation);

        // 2. ApplyDestroyPlan
        var applyOk = dispatcher.Dispatch(
            component, result, props, 7, false, "eph-pr-1",
            new StringBuilder(), TerraformRunnerOperations.ApplyDestroyPlan, CancellationToken.None);

        Assert.IsTrue(applyOk);
        registrar.Received(1).DeregisterEnvironmentResources(2598, resultId);
        registrar.DidNotReceiveWithAnyArgs().RegisterAppliedResources(default!, default!, default!, default);
        requests.Received().SaveTerraformDeploymentOutcome(Arg.Is<TerraformDeploymentOutcome>(o =>
            o.DeploymentResultId == resultId && o.InventoryOutcome == "Deregistered" && o.ArchiveOutcome == "Succeeded"));

        // 3. CompleteApplyDestroyPlan when environment is null and sourceArchive does not exist
        var outcomeMissingArchive = new TerraformDeploymentOutcome { DeploymentResultId = 56 };
        dispatcher.CompleteApplyDestroyPlan(
            component,
            new DeploymentResultApiModel { Id = 56, RequestId = 7 },
            "missing-env",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip"),
            outcomeMissingArchive,
            CancellationToken.None);
        registrar.Received(1).DeregisterEnvironmentResources(0, 56);
        Assert.AreEqual("Deregistered", outcomeMissingArchive.InventoryOutcome);
        Assert.AreEqual("Unavailable", outcomeMissingArchive.ArchiveOutcome);

        // 3b. CompleteApplyDestroyPlan when DeregisterEnvironmentResources throws -> records InventoryOutcome = "Failed" and rethrows
        registrar.When(r => r.DeregisterEnvironmentResources(0, 57))
            .Do(_ => throw new InvalidOperationException("Deregistration failed"));
        var outcomeFailedDereg = new TerraformDeploymentOutcome { DeploymentResultId = 57 };
        Assert.Throws<InvalidOperationException>(() => dispatcher.CompleteApplyDestroyPlan(
            component,
            new DeploymentResultApiModel { Id = 57, RequestId = 7 },
            "missing-env",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip"),
            outcomeFailedDereg,
            CancellationToken.None));
        Assert.AreEqual("Failed", outcomeFailedDereg.InventoryOutcome);
        requests.Received(1).SaveTerraformDeploymentOutcome(outcomeFailedDereg);

        // 4. Runner returning non-zero exit code during ApplyPlan records FailedOrUnknown and returns false (also cover targetEnvironment.EnvironmentIsProd == true when isProduction == false, and isProduction == true)
        environments.GetEnvironment("prod-eph-env").Returns(new EnvironmentApiModel { EnvironmentId = 2599, EnvironmentName = "prod-eph-env", IsEphemeral = true, EnvironmentIsProd = true });
        dispatcher.RunnerProcessRunner = (_, _, _, _, _) => 1;
        Assert.IsFalse(dispatcher.Dispatch(
            component, result, props, 7, false, "prod-eph-env",
            new StringBuilder(), TerraformRunnerOperations.ApplyPlan, CancellationToken.None));
        Assert.IsFalse(dispatcher.Dispatch(
            component, result, props, 7, true, "eph-pr-1",
            new StringBuilder(), TerraformRunnerOperations.ApplyPlan, CancellationToken.None));

            // 5b. Runner returning ProcessTerminatedExitCode throws OperationCanceledException
            dispatcher.RunnerProcessRunner = (_, _, _, _, _) => Dorc.Monitor.RunnerProcess.RunnerProcess.ProcessTerminatedExitCode;
            Assert.Throws<OperationCanceledException>(() => dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));

            // 6. Destroy request pins GitCommitSha and Catalog TemplateVersion from prior TerraformDeploymentReceipt
            dispatcher.RunnerProcessRunner = defaultRunner;
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel
            {
                Id = 7,
                Project = "proj",
                EnvironmentName = "eph-pr-1",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            ScriptGroup? capturedDestroyGroup = null;
            pipeServer.Start(Arg.Any<string>(), Arg.Do<ScriptGroup>(sg => capturedDestroyGroup = sg), Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var gitDestroyComp = new ComponentApiModel
            {
                ComponentId = 21,
                ComponentName = "GitInfra",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Git,
                TerraformGitBranch = "main"
            };
            var catalogDestroyComp = new ComponentApiModel
            {
                ComponentId = 11,
                ComponentName = "CatInfra",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-vnet",
                TerraformTemplateVersion = "2.0.0"
            };
            requests.GetSubmittedProject(7, 21).Returns(new ProjectApiModel { ProjectId = 12, ProjectName = "proj", TerraformGitRepoUrl = "https://github.com/org/repo.git", TerraformStateBackends = stateBackends });
            requests.GetSubmittedProject(7, 11).Returns(new ProjectApiModel { ProjectId = 12, ProjectName = "proj", TerraformStateBackends = stateBackends });
            requests.GetRequestDefinitions(7).Returns(new List<DeploymentRequestDefinition>
            {
                new() { ComponentId = 1, DefinitionRevision = "3" },
                new() { ComponentId = 11, DefinitionRevision = "3" },
                new() { ComponentId = 21, DefinitionRevision = "3" }
            });
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new()
                {
                    DeploymentResultId = 47,
                    ComponentId = 21,
                    ProjectName = "proj",
                    ComponentName = "GitInfra",
                    Operation = "Plan",
                    SourceSubPath = "plan-only/subpath",
                    GitCommitSha = "dddddddddddddddddddddddddddddddddddddddd"
                },
                new()
                {
                    DeploymentResultId = 48,
                    ComponentId = 21,
                    ProjectName = "other-proj",
                    ComponentName = "GitInfra",
                    Operation = "Plan",
                    SourceSubPath = "wrong/subpath",
                    GitCommitSha = "ffffffffffffffffffffffffffffffffffffffff"
                },
                new()
                {
                    DeploymentResultId = 49,
                    ComponentId = 21,
                    ProjectName = "proj",
                    ComponentName = "GitInfra",
                    Operation = "Plan",
                    SourceSubPath = "unapplied/subpath",
                    GitCommitSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
                },
                new()
                {
                    DeploymentResultId = 50,
                    ComponentId = 21,
                    ProjectName = "proj",
                    ComponentName = "GitInfra",
                    Operation = "Plan",
                    SourceSubPath = "envs/pr",
                    GitCommitSha = "0123456789abcdef0123456789abcdef01234567",
                    RequestedSource = "https://github.com/org/pinned-repo.git"
                },
                new()
                {
                    DeploymentResultId = 51,
                    ComponentId = 11,
                    ProjectName = "proj",
                    ComponentName = "Network",
                    Operation = "Plan",
                    SourceSubPath = null,
                    TemplateName = "azure-vnet",
                    TemplateVersion = "0.9.0"
                }
            });
            requests.GetTerraformDeploymentOutcome(48).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 48, ApplyOutcome = "Succeeded" });
            requests.GetTerraformDeploymentOutcome(49).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 49, ApplyOutcome = "FailedOrUnknown" });
            requests.GetTerraformDeploymentOutcome(50).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 50, ApplyOutcome = "Succeeded" });
            requests.GetTerraformDeploymentOutcome(51).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 51, ApplyOutcome = "Succeeded" });
            Assert.IsTrue(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual("0123456789abcdef0123456789abcdef01234567", capturedDestroyGroup!.TerraformGitBranch);
            Assert.AreEqual("https://github.com/org/pinned-repo.git", capturedDestroyGroup.TerraformGitRepoUrl);
            Assert.AreEqual("envs/pr", capturedDestroyGroup.TerraformSubPath);

            Assert.IsTrue(dispatcher.Dispatch(
                catalogDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual("0.9.0", capturedDestroyGroup!.TerraformTemplateVersion);
            Assert.AreEqual("azure-vnet", capturedDestroyGroup.TerraformTemplateName);

            // Also cover realistic Operation="Plan" receipts with FailedOrUnknown outcome, empty fields, null outcome, or SharedFolder receipt
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = 52, ComponentId = 21, ProjectName = "proj", Operation = "Plan", SourceType = "Git", GitCommitSha = "abcdef", RequestedSource = "" },
                new() { DeploymentResultId = 53, ComponentId = 11, ProjectName = "proj", Operation = "Plan", SourceType = "Catalog", TemplateVersion = "", TemplateName = "" },
                new() { DeploymentResultId = 54, ComponentId = 11, ProjectName = "proj", Operation = "Plan", SourceType = "Catalog", TemplateVersion = "0.8.0", TemplateName = "" },
                new() { DeploymentResultId = resultId, ComponentId = 1, ProjectName = "proj", Operation = "Plan", SourceType = "SharedFolder", RequestedSource = @"\\share\pinned", SourceSubPath = "shared/sub" },
                new() { DeploymentResultId = 56, ComponentId = 21, ProjectName = "proj", Operation = "Apply", GitCommitSha = "unrecorded-outcome" }
            });
            requests.GetTerraformDeploymentOutcome(52).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 52, ApplyOutcome = "FailedOrUnknown" });
            requests.GetTerraformDeploymentOutcome(53).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 53, ApplyOutcome = "Succeeded" });
            requests.GetTerraformDeploymentOutcome(54).Returns(new TerraformDeploymentOutcome { DeploymentResultId = 54, ApplyOutcome = "FailedOrUnknown" });
            requests.GetTerraformDeploymentOutcome(resultId).Returns(new TerraformDeploymentOutcome { DeploymentResultId = resultId, ApplyOutcome = "FailedOrUnknown" });
            requests.GetTerraformDeploymentOutcome(56).Returns((TerraformDeploymentOutcome?)null);
            requests.GetCatalogRequestInputs(7, 11).Returns(new CatalogRequestInputs
            {
                DeploymentRequestId = 7,
                ComponentId = 11,
                TemplateName = "azure-vnet",
                TemplateVersion = "1.0.0",
                InputsJson = "{\"vnet_name\":\"pinned-vnet\"}"
            });
            requests.GetProvisionedRequestPropertiesForEnvironment(Arg.Any<int>(), Arg.Any<string?>()).Returns(new[]
            {
                new RequestProperty { PropertyName = "CatInfra:secret_token", PropertyValue = "from-provision", IsSensitive = true },
                new RequestProperty { PropertyName = "OtherComp:secret_token", PropertyValue = "other-secret", IsSensitive = true },
                new RequestProperty { PropertyName = "unqualified_prop", PropertyValue = "unq-val", IsSensitive = false },
                new RequestProperty { PropertyName = TerraformDestroyOperationConstants.IsDestroyPropertyName, PropertyValue = "true", IsSensitive = false }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.IsTrue(dispatcher.Dispatch(
                catalogDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual("from-provision", capturedDestroyGroup!.CommonProperties["secret_token"].Value);
            Assert.AreEqual("unq-val", capturedDestroyGroup.CommonProperties["unqualified_prop"].Value);
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            StringAssert.Contains(capturedDestroyGroup!.ScriptsLocation, $"dorc-destroy-src-{resultId}-");

            // Cover TryStagePriorAppliedSourceArchive when ArchiveOutcome == "Succeeded" (for SharedFolder with retained tfvars archive, AzureArtifact, GitHubArtifact, and download exception fallback)
            var tfvarsDir = Path.Join(Path.GetTempPath(), $"dorc-tfvars-test-{Guid.NewGuid():N}");
            var planStorageTempDir = Path.Join(DorcProgramData.Root, "TerraformPlans", $"dorc-plan-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Join(tfvarsDir, "shared/sub"));
            Directory.CreateDirectory(planStorageTempDir);
            dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.Git, ScriptPath = tfvarsDir },
                resultId,
                planStorageTempDir);
            dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder, ScriptPath = string.Empty },
                resultId,
                planStorageTempDir);
            Assert.ThrowsExactly<ArgumentException>(() =>
                dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                    new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder, ScriptPath = tfvarsDir, TerraformSubPath = "../outside" },
                    resultId,
                    planStorageTempDir));
            dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder, ScriptPath = Path.Join(tfvarsDir, "missing-dir") },
                resultId,
                planStorageTempDir);
            dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder, ScriptPath = tfvarsDir, TerraformSubPath = "shared/sub" },
                resultId,
                planStorageTempDir);
            File.WriteAllText(Path.Join(tfvarsDir, "shared/sub", "terraform.tfvars"), "x = 1");
            dispatcher.ArchiveSharedFolderTfvarsIfPresent(
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder, ScriptPath = tfvarsDir, TerraformSubPath = "shared/sub" },
                resultId,
                planStorageTempDir);
            Assert.IsTrue(blobs.ContainsKey($"{resultId}.shared-tfvars.zip"));
            using (var badZipStream = new MemoryStream())
            using (var badZip = new ZipArchive(badZipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Assert.ThrowsExactly<ArgumentException>(() =>
                    TerraformDispatcher.AddTfvarsFileToZip(badZip, Path.GetFullPath(tfvarsDir), Path.Join(tfvarsDir, "..", "outside.tfvars")));
            }
            Assert.ThrowsExactly<ArgumentException>(() =>
                dispatcher.TryDeleteFile(Path.Join(DorcProgramData.Root, "..", "outside.txt")));
            Assert.ThrowsExactly<ArgumentException>(() =>
                dispatcher.TryDeleteDirectory(Path.GetTempPath()));
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var w1 = new StreamWriter(z.CreateEntry("main.tf").Open())) w1.Write("resource \"null_resource\" \"x\" {}");
                    using (var w2 = new StreamWriter(z.CreateEntry("_dorc_provenance.json").Open())) w2.Write("{}");
                    using (var w3 = new StreamWriter(z.CreateEntry("_dorc_backend.tf").Open())) w3.Write("terraform {}");
                }
                blobs[$"{resultId}.terraform-source.zip"] = ms.ToArray();
            }
            requests.GetTerraformDeploymentOutcome(resultId).Returns(new TerraformDeploymentOutcome
            {
                DeploymentResultId = resultId,
                ApplyOutcome = "Succeeded",
                ArchiveOutcome = "Succeeded"
            });
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Plan", SourceType = "SharedFolder", RequestedSource = tfvarsDir, SourceSubPath = "shared/sub" }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Directory.Delete(tfvarsDir, recursive: true);
            Directory.Delete(planStorageTempDir, recursive: true);
            StringAssert.Contains(capturedDestroyGroup!.ScriptsLocation, $"dorc-destroy-src-{resultId}-");
            Assert.IsFalse(Directory.Exists(capturedDestroyGroup.ScriptsLocation));

            // Also cover RestorePriorAppliedInputs (priorProject null vs non-null with empty/non-empty ArtefactsUrl, and priorRequest.RequestDetails empty vs populated)
            var restoreProps = new Dictionary<string, VariableValue>
            {
                ["existing_key"] = new() { Value = "keep", Type = typeof(string) }
            };
            var restoreProj = new ProjectApiModel { ProjectId = 12, ProjectName = "proj", ArtefactsUrl = "https://initial" };
            requests.GetSubmittedProject(901, 1).Returns((ProjectApiModel?)null);
            dispatcher.RestorePriorAppliedInputs(
                new DeploymentRequestApiModel { Id = 901, RequestDetails = string.Empty },
                1,
                restoreProj,
                restoreProps);
            requests.GetSubmittedProject(902, 1).Returns(new ProjectApiModel { ProjectId = 12, ProjectName = "proj", ArtefactsUrl = "", ArtefactsSubPaths = "sub1", ArtefactsBuildRegex = ".*" });
            dispatcher.RestorePriorAppliedInputs(
                new DeploymentRequestApiModel
                {
                    Id = 902,
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>existing_key</Name><Value>override</Value></PropertyPair><PropertyPair><Name>applied_only_key</Name><Value>applied-val</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                },
                1,
                restoreProj,
                restoreProps);
            Assert.AreEqual("https://initial", restoreProj.ArtefactsUrl);
            Assert.AreEqual("sub1", restoreProj.ArtefactsSubPaths);
            Assert.AreEqual("keep", restoreProps["existing_key"].Value);
            Assert.AreEqual("applied-val", restoreProps["applied_only_key"].Value);
            requests.GetSubmittedProject(903, 1).Returns(new ProjectApiModel { ProjectId = 12, ProjectName = "proj", ArtefactsUrl = "https://pinned-artefacts", ArtefactsSubPaths = "sub2", ArtefactsBuildRegex = "^1\\." });
            dispatcher.RestorePriorAppliedInputs(
                new DeploymentRequestApiModel { Id = 903, RequestDetails = string.Empty },
                1,
                restoreProj,
                restoreProps);
            Assert.AreEqual("https://pinned-artefacts", restoreProj.ArtefactsUrl);

            // Cover TryDeleteDirectory catch block when a file inside the directory is locked
            var lockedDir = Path.Join(DorcProgramData.Root, "TerraformPlans", $"dorc-locked-dir-{Guid.NewGuid():N}");
            Directory.CreateDirectory(lockedDir);
            var lockedFile = Path.Join(lockedDir, "locked.tf");
            File.WriteAllText(lockedFile, "locked");
            using (var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                dispatcher.TryDeleteDirectory(lockedDir);
            }
            dispatcher.TryDeleteDirectory(lockedDir);
            Assert.IsFalse(Directory.Exists(lockedDir));

            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Plan", SourceType = "SharedFolder", RequestedSource = @"\\share\pinned", SourceSubPath = "shared/sub" }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual(TerraformSourceType.SharedFolder, capturedDestroyGroup!.TerraformSourceType);

            // When GetScriptGroup throws after staging the archive, the finally block in Dispatch still deletes the staged directory
            var stagingRoot = Path.GetDirectoryName(capturedDestroyGroup!.ScriptsLocation)!;
            requests.GetSubmittedProject(7, 1).Returns(
                _ => throw new InvalidOperationException("Simulated GetScriptGroup failure after staging"),
                _ => new ProjectApiModel { ProjectId = 12, ProjectName = "proj", TerraformStateBackends = stateBackends });
            Assert.ThrowsExactly<InvalidOperationException>(() => dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual(0, Directory.GetDirectories(stagingRoot, $"dorc-destroy-src-{resultId}-*").Length);

            // Artifact source types (AzureArtifact / GitHubArtifact) keep their artifact source type rather than staging the stripped source archive
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Plan", SourceType = "AzureArtifact", RequestedSource = "https://dev.azure.com/org/proj", SourceSubPath = "shared/sub" }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual(TerraformSourceType.AzureArtifact, capturedDestroyGroup!.TerraformSourceType);
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Plan", SourceType = "AzureArtifact", RequestedSource = "", SourceSubPath = "shared/sub" }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));

            // When blob is missing -> DownloadFileFromBlobs throws KeyNotFoundException -> TryStagePriorAppliedSourceArchive logs warning and falls back to RequestedSource
            blobs.Remove($"{resultId}.terraform-source.zip");
            component.TerraformSourceType = TerraformSourceType.SharedFolder;
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Plan", SourceType = "SharedFolder", RequestedSource = @"\\share\pinned", SourceSubPath = "shared/sub" }
            });
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual(@"\\share\pinned", capturedDestroyGroup!.ScriptsLocation);
            requests.GetTerraformDeploymentOutcome(resultId).Returns((TerraformDeploymentOutcome?)null);
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns(new List<TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = 54, DeploymentRequestId = 70, ComponentId = 11, EnvironmentId = 2598, ProjectName = "proj", Operation = "Apply", TemplateVersion = "", TemplateName = "" },
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Apply", SourceType = "SharedFolder", RequestedSource = @"\\share\pinned" },
                new() { DeploymentResultId = resultId, ComponentId = 1, EnvironmentId = 999, ProjectName = "proj", Operation = "Apply", SourceType = "SharedFolder", RequestedSource = "" },
                new() { DeploymentResultId = 56, ComponentId = 1, EnvironmentId = 2598, ProjectName = "proj", Operation = "Apply", SourceType = "SharedFolder", RequestedSource = "" }
            });
            requests.GetCatalogRequestInputs(70, 11).Returns((CatalogRequestInputs?)null);
            Assert.IsTrue(dispatcher.Dispatch(
                catalogDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            requests.GetCatalogRequestInputs(70, 11).Returns(new CatalogRequestInputs
            {
                DeploymentRequestId = 70,
                ComponentId = 11,
                TemplateName = "azure-vnet",
                TemplateVersion = "1.2.0",
                InputsJson = "{\"vnet_name\":\"fresher-in-flight-vnet\"}"
            });
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel
            {
                Id = 7,
                Project = "proj",
                EnvironmentName = "eph-pr-1",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>CatInfra:secret_token</Name><Value>stale-sub-val</Value></PropertyPair><PropertyPair><Name>unqualified_prop</Name><Value>stale-unq</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            Assert.IsTrue(dispatcher.Dispatch(
                catalogDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            Assert.AreEqual("fresher-in-flight-vnet", capturedDestroyGroup!.CommonProperties["vnet_name"].Value);
            Assert.AreEqual("from-provision", capturedDestroyGroup.CommonProperties["secret_token"].Value);
            Assert.IsTrue(dispatcher.Dispatch(
                component, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            environments.GetEnvironment("eph-pr-1").Returns((EnvironmentApiModel?)null);
            Assert.IsTrue(dispatcher.Dispatch(
                catalogDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));
            requests.GetTerraformReceiptsForEnvironment("eph-pr-1").Returns((IReadOnlyList<TerraformDeploymentReceipt>)null!);
            Assert.IsTrue(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.CreateDestroyPlan, CancellationToken.None));

            // Non-destroy dispatch aborts when request is Cancelling/Cancelled or target environment is TearingDown
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel { Id = 7, Project = "proj", EnvironmentName = "eph-pr-1", Status = "Cancelling" });
            Assert.IsFalse(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.ApplyPlan, CancellationToken.None));
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel { Id = 7, Project = "proj", EnvironmentName = "eph-pr-1", Status = "Cancelled" });
            Assert.IsFalse(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.ApplyPlan, CancellationToken.None));
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel { Id = 7, Project = "proj", EnvironmentName = "eph-pr-1", Status = "Running" });
            environments.GetEnvironment("eph-pr-1").Returns(new EnvironmentApiModel { EnvironmentId = 100, EnvironmentName = "eph-pr-1", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown });
            Assert.IsFalse(dispatcher.Dispatch(
                gitDestroyComp, result, props, 7, false, "eph-pr-1",
                new StringBuilder(), TerraformRunnerOperations.ApplyPlan, CancellationToken.None));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                Dorc.Monitor.Terraform.TerraformCatalogRequestSnapshot.Apply(
                    catalogDestroyComp,
                    props,
                    null,
                    new CatalogRequestInputs { ComponentId = 999 }));
        }
    }
