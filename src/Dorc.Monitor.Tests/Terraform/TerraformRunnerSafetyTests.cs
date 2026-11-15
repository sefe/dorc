using Dorc.ApiModel;
using Dorc.Monitor.RunnerProcess;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using NativeRunnerProcess = Dorc.Monitor.RunnerProcess.RunnerProcess;

namespace Dorc.Monitor.Tests.Terraform;

[TestClass]
public class TerraformRunnerSafetyTests
{
    [TestMethod]
    [DataRow(TerraformSourceType.Git)]
    [DataRow(TerraformSourceType.SharedFolder)]
    public void ProjectBackend_DoesNotAdoptLegacyComponents(TerraformSourceType source)
    {
        var group = new ScriptGroup { TerraformSourceType = source };
        TerraformDispatcher.ConfigurePlatformState(group, StateConfiguration(), "component", "prod");
        Assert.IsNull(group.TerraformStateStorageAccount);
        Assert.IsNull(group.TerraformStateKey);
    }

    [TestMethod]
    public void SubmittedBackend_IsMandatoryForCatalog()
    {
        var group = new ScriptGroup { TerraformSourceType = TerraformSourceType.Catalog };
        TerraformDispatcher.ConfigurePlatformState(group, StateConfiguration(), "component", "prod");
        Assert.AreEqual("platform", group.TerraformStateStorageAccount);
        Assert.AreEqual("project/component/prod.tfstate", group.TerraformStateKey);
        Assert.Throws<InvalidOperationException>(() => TerraformDispatcher.ConfigurePlatformState(group,
            new ProjectApiModel { ProjectName = "project" }, "component", "prod"));

        var ephGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(ephGroup, StateConfiguration(), "component", "eph-1", isEphemeral: true, environmentId: 17);
        Assert.AreEqual("platform", ephGroup.TerraformStateStorageAccount);
        Assert.AreEqual("project/component/eph-1.tfstate", ephGroup.TerraformStateKey);
        Assert.Throws<InvalidOperationException>(() =>
            TerraformDispatcher.ConfigurePlatformState(ephGroup, StateConfiguration(), "component", "eph-1", isEphemeral: true, environmentId: 99));

        var ephDefaultBackendProject = new ProjectApiModel
        {
            ProjectName = "project",
            TerraformStateBackends = new List<TerraformStateBackendApiModel>
            {
                new()
                {
                    EnvironmentId = null,
                    StorageAccount = "defaultplat",
                    ContainerName = "tfstate",
                    ResourceGroup = "rg",
                    SubscriptionId = "00000000-0000-0000-0000-000000000001"
                }
            }
        };
        var ephDefaultGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(ephDefaultGroup, ephDefaultBackendProject, "component", "eph-2", isEphemeral: true, environmentId: 99);
        Assert.AreEqual("defaultplat", ephDefaultGroup.TerraformStateStorageAccount);
        Assert.AreEqual("project/component/eph-2.tfstate", ephDefaultGroup.TerraformStateKey);

        var ephMultiBackendProject = new ProjectApiModel
        {
            ProjectName = "project",
            TerraformStateBackends = new List<TerraformStateBackendApiModel>
            {
                new()
                {
                    EnvironmentId = null,
                    StorageAccount = "defaultplat",
                    ContainerName = "tfstate",
                    ResourceGroup = "rg",
                    SubscriptionId = "00000000-0000-0000-0000-000000000001"
                },
                new()
                {
                    EnvironmentId = 99,
                    StorageAccount = "env99plat",
                    ContainerName = "tfstate",
                    ResourceGroup = "rg",
                    SubscriptionId = "00000000-0000-0000-0000-000000000001"
                }
            }
        };
        var ephMultiGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(
            ephMultiGroup,
            ephMultiBackendProject,
            "component",
            "eph-3",
            isEphemeral: true,
            environmentId: 99,
            existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
            {
                new() { ComponentName = "other-comp", StateStorageAccount = "other", StateContainerName = "other" },
                new() { ComponentName = "component", StateKey = "other-proj/component/eph-3.tfstate", StateStorageAccount = "other", StateContainerName = "other" },
                new() { ComponentName = "component", StateStorageAccount = "", StateContainerName = "" },
                new() { ComponentName = "component", StateKey = "project/component/eph-3.tfstate", StateStorageAccount = "env99plat", StateContainerName = "tfstate" }
            });
        Assert.AreEqual("env99plat", ephMultiGroup.TerraformStateStorageAccount);
        Assert.AreEqual("project/component/eph-3.tfstate", ephMultiGroup.TerraformStateKey);

        var renamedGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(
            renamedGroup,
            ephMultiBackendProject,
            "renamed-component",
            "eph-3",
            isEphemeral: true,
            environmentId: 99,
            existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = 1, EnvironmentId = 99, ProjectName = "project", ComponentId = 42, ComponentName = "original-component", StateKey = "project/original-component/eph-3.tfstate", StateStorageAccount = "env99plat", StateContainerName = "tfstate" },
                new() { DeploymentResultId = 2, EnvironmentId = 99, ProjectName = "other-proj", ComponentId = 42, ComponentName = "original-component", StateKey = "other-proj/original-component/eph-3.tfstate", StateStorageAccount = "other", StateContainerName = "other" },
                new() { DeploymentResultId = 3, EnvironmentId = 99, ProjectName = "", ComponentId = 42, ComponentName = "original-component", StateKey = "other-proj/original-component/eph-3.tfstate", StateStorageAccount = "env99plat", StateContainerName = "tfstate" },
                new() { DeploymentResultId = 4, EnvironmentId = 99, ProjectName = "project", ComponentId = 42, ComponentName = "original-component", StateKey = "", StateStorageAccount = "env99plat", StateContainerName = "tfstate" }
            },
            componentId: 42);
        Assert.AreEqual("project/original-component/eph-3.tfstate", renamedGroup.TerraformStateKey);

        var reusedNameGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(
            reusedNameGroup,
            ephMultiBackendProject,
            "original-component",
            "eph-3",
            isEphemeral: true,
            environmentId: 99,
            existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = 1, EnvironmentId = 99, ProjectName = "project", ComponentId = 42, ComponentName = "legacy-name", StateKey = "project/original-component/eph-3.tfstate", StateStorageAccount = "env99plat", StateContainerName = "tfstate" },
                new() { DeploymentResultId = 2, EnvironmentId = 99, ProjectName = "project", ComponentId = 43, ComponentName = "original-component", StateKey = "", StateStorageAccount = "env99plat", StateContainerName = "tfstate" }
            },
            componentId: 98);
        Assert.AreEqual("project/original-component-98/eph-3.tfstate", reusedNameGroup.TerraformStateKey);

        var doubleCollidedGroup = new ScriptGroup { TerraformSourceType = TerraformSourceType.Git };
        TerraformDispatcher.ConfigurePlatformState(
            doubleCollidedGroup,
            ephMultiBackendProject,
            "original-component",
            "eph-3",
            isEphemeral: true,
            environmentId: 99,
            existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
            {
                new() { DeploymentResultId = 1, EnvironmentId = 99, ProjectName = "project", ComponentId = 42, ComponentName = "original-component", StateKey = "project/original-component/eph-3.tfstate", StateStorageAccount = "env99plat", StateContainerName = "tfstate" },
                new() { DeploymentResultId = 2, EnvironmentId = 99, ProjectName = "project", ComponentId = 44, ComponentName = "original-component-99", StateKey = "", StateStorageAccount = "env99plat", StateContainerName = "tfstate" }
            },
            componentId: 99);
        Assert.AreEqual("project/original-component-99-1/eph-3.tfstate", doubleCollidedGroup.TerraformStateKey);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            TerraformDispatcher.ConfigurePlatformState(
                new ScriptGroup { TerraformSourceType = TerraformSourceType.Git },
                ephMultiBackendProject,
                "component",
                "eph-3",
                isEphemeral: true,
                environmentId: 99,
                existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
                {
                    new() { ComponentName = "component", StateStorageAccount = "oldplat", StateContainerName = "tfstate" }
                }));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            TerraformDispatcher.ConfigurePlatformState(
                new ScriptGroup { TerraformSourceType = TerraformSourceType.Git },
                ephMultiBackendProject,
                "component",
                "eph-3",
                isEphemeral: true,
                environmentId: 99,
                existingReceipts: new List<Dorc.PersistentData.Model.TerraformDeploymentReceipt>
                {
                    new() { ComponentName = "component", StateStorageAccount = "env99plat", StateContainerName = "oldcontainer" }
                }));
    }

    [TestMethod]
    public void ScriptGroup_IncludesInheritedSecurePropertyAndConfigMetadata()
    {
        var properties = new Dictionary<string, VariableValue>
        {
            ["UnrelatedDatabasePassword"] = new() { Value = "secret", Type = typeof(string) },
            ["ConfigCredential"] = new() { Value = "config-secret", Type = typeof(string) },
            ["Public"] = new() { Value = "public", Type = typeof(string) }
        };
        var propertySource = Substitute.For<IPropertiesPersistentSource>();
        propertySource.GetAllProperties().Returns(new[]
        {
            new PropertyApiModel { Name = "UnrelatedDatabasePassword", Secure = true },
            new PropertyApiModel { Name = "Public", Secure = false }
        });
        var configSource = Substitute.For<IConfigValuesPersistentSource>();
        configSource.GetAllConfigValues(false).Returns(new[]
        {
            new ConfigValueApiModel { Key = "ConfigCredential", Secure = true },
            new ConfigValueApiModel { Key = "Public", Secure = false }
        });
        var requestsSource = Substitute.For<IRequestsPersistentSource>();
        var dispatcher = new TerraformDispatcher(NullLogger<TerraformDispatcher>.Instance,
            requestsSource, configSource, Substitute.For<IConfigurationSettings>(),
            Substitute.For<IDeploymentRequestProcessesPersistentSource>(), Substitute.For<IScriptGroupPipeServer>(),
            Substitute.For<IAzureStorageAccountWorker>(), Substitute.For<IProjectsPersistentSource>(),
            Substitute.For<IGitHubHostValidator>(), Substitute.For<ICloudResourcesPersistentSource>(),
            Substitute.For<ICloudResourceAuditPersistentSource>(), Substitute.For<IEnvironmentsPersistentSource>(),
            Substitute.For<IDatabasesPersistentSource>(), propertySource);
        var getScriptGroup = typeof(TerraformDispatcher).GetMethod("GetScriptGroup",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var group = (ScriptGroup)getScriptGroup.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel { EnvironmentName = "eph-1", Project = "proj" }, null, null
            })!;
        CollectionAssert.AreEquivalent(new[] { "UnrelatedDatabasePassword", "ConfigCredential" },
            group.SensitivePropertyNames.ToArray());
        Assert.AreSame(properties, group.CommonProperties, "Sensitivity metadata must not change execution inputs.");

        // Cover GetScriptGroup resolving prior request build metadata when request BuildUri and DropLocation are blank
        requestsSource.GetLatestAppliedRequestForEnvironment("eph-1", "proj", 11).Returns(new DeploymentRequestApiModel
        {
            BuildUri = "vstfs:///Build/Build/42",
            DropLocation = "https://dev.azure.com/org/proj/_apis/build/builds/42/artifacts",
            BuildNumber = "20260401.1"
        });
        var backfilledGroup = (ScriptGroup)getScriptGroup.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel { EnvironmentName = "eph-1", Project = "proj", BuildNumber = null },
                new ProjectApiModel { ProjectName = "proj" }, null
            })!;
        Assert.IsNotNull(backfilledGroup);
        requestsSource.GetLatestAppliedRequestForEnvironment("eph-1", "proj", 11).Returns(new DeploymentRequestApiModel
        {
            BuildUri = "vstfs:///Build/Build/42",
            DropLocation = "https://dev.azure.com/org/proj/_apis/build/builds/42/artifacts",
            BuildNumber = null
        });
        var backfilledNullBuildNumGroup = (ScriptGroup)getScriptGroup.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel { EnvironmentName = "eph-1", Project = "proj", BuildNumber = "fallback-num" },
                null, null
            })!;
        Assert.IsNotNull(backfilledNullBuildNumGroup);
        var withDropGroup = (ScriptGroup)getScriptGroup.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel { EnvironmentName = "eph-1", Project = "proj", DropLocation = @"\\drop\share" },
                null, null
            })!;
        Assert.IsNotNull(withDropGroup);
        var withUriGroup = (ScriptGroup)getScriptGroup.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel { EnvironmentName = "eph-1", Project = "proj", BuildUri = "vstfs:///Build/Build/1" },
                null, null
            })!;
        Assert.IsNotNull(withUriGroup);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CancellationDuringWait_KillsContainedRunnerAndDescendants(bool cooperativeSignal)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows process containment test.");
        var root = Path.Join(Directory.GetCurrentDirectory(), "runner-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var childIdPath = Path.Join(root, "child.txt");
        var startMarker = Path.Join(root, "start");
        using var job = RunnerProcessJob.Create();
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add($"while (!(Test-Path '{startMarker}')) {{ Start-Sleep -Milliseconds 50 }}; " +
            $"$p = Start-Process powershell.exe -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 120' -PassThru; " +
            $"Set-Content '{childIdPath}' $p.Id; Start-Sleep -Seconds 120");
        using var process = Process.Start(info)!;
        Process? child = null;
        NativeRunnerProcess? runner = null;
        try
        {
            job.Assign(process.Handle);
            File.WriteAllText(startMarker, "");
            Assert.IsTrue(SpinWait.SpinUntil(() => File.Exists(childIdPath)
                && new FileInfo(childIdPath).Length > 0, TimeSpan.FromSeconds(15)), "Runner did not spawn test child.");
            child = Process.GetProcessById(int.Parse(File.ReadAllText(childIdPath).Trim()));
            Assert.IsTrue(DuplicateHandle(GetCurrentProcess(), process.Handle, GetCurrentProcess(),
                out var duplicate, 0, false, 2));
            runner = new NativeRunnerProcess(duplicate, IntPtr.Zero, (uint)process.Id, job);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var elapsed = Stopwatch.StartNew();
            var signalled = false;
            Assert.ThrowsExactly<OperationCanceledException>(() => runner.WaitForExit(cancel.Token,
                cooperativeSignal ? () => signalled = true : null));
            Assert.AreEqual(cooperativeSignal, signalled);
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(11));
            Assert.IsTrue(process.WaitForExit(5000));
            Assert.IsTrue(child.WaitForExit(5000), "Terraform/provider descendant survived cancellation.");
        }
        finally
        {
            job.Terminate();
            runner?.Dispose();
            child?.Dispose();
            process.WaitForExit(5000);
            Directory.Delete(root, true);
        }
    }

    private static ProjectApiModel StateConfiguration() => new()
    {
        ProjectName = "project",
        TerraformStateBackends =
        [
            new()
        {
            EnvironmentId = 17,
            StorageAccount = "platform",
            ContainerName = "state",
            SubscriptionId = "11111111-1111-1111-1111-111111111111"
        }
        ]
    };

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle,
        IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess, bool inherit, uint options);
}
