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
        var dispatcher = new TerraformDispatcher(NullLogger<TerraformDispatcher>.Instance,
            Substitute.For<IRequestsPersistentSource>(), configSource, Substitute.For<IConfigurationSettings>(),
            Substitute.For<IDeploymentRequestProcessesPersistentSource>(), Substitute.For<IScriptGroupPipeServer>(),
            Substitute.For<IAzureStorageAccountWorker>(), Substitute.For<IProjectsPersistentSource>(),
            Substitute.For<IGitHubHostValidator>(), Substitute.For<ICloudResourcesPersistentSource>(),
            Substitute.For<ICloudResourceAuditPersistentSource>(), Substitute.For<IEnvironmentsPersistentSource>(),
            Substitute.For<IDatabasesPersistentSource>(), propertySource);
        var group = (ScriptGroup)typeof(TerraformDispatcher).GetMethod("GetScriptGroup",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dispatcher, new object?[]
            {
                "source", properties, 1, new ComponentApiModel { TerraformSourceType = TerraformSourceType.SharedFolder },
                new DeploymentRequestApiModel(), null
            })!;
        CollectionAssert.AreEquivalent(new[] { "UnrelatedDatabasePassword", "ConfigCredential" },
            group.SensitivePropertyNames.ToArray());
        Assert.AreSame(properties, group.CommonProperties, "Sensitivity metadata must not change execution inputs.");
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
