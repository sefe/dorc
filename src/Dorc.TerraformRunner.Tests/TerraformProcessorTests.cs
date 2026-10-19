using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.Pipes;
using Dorc.TerraformRunner.State;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace Dorc.TerraformRunner.Tests
{
    [TestClass]
    public class TerraformProcessorTests
    {
        private const string StateSubscription = "11111111-1111-1111-1111-111111111111";
        private const string WorkloadSubscription = "22222222-2222-2222-2222-222222222222";
        private string _root = null!;
        private ScriptGroup _scriptGroup = null!;
        private FakeProcessor _processor = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Join(Directory.GetCurrentDirectory(), "processor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Join(_root, "source"));
            File.WriteAllText(Path.Join(_root, "source", "main.tf"), "# approved source");
            _scriptGroup = new ScriptGroup
            {
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptsLocation = Path.Join(_root, "source"),
                CommonProperties = new Dictionary<string, VariableValue>(),
                TerraformGitBranch = "main",
                TerraformResolvedGitSha = "approved-sha"
            };
            var pipe = new FakePipe(_scriptGroup);
            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(NullLogger.Instance);
            _processor = new FakeProcessor(_root, logger, pipe);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void ConfigureState()
        {
            _scriptGroup.TerraformStateStorageAccount = "stdorc01";
            _scriptGroup.TerraformStateContainerName = "tfstate";
            _scriptGroup.TerraformStateKey = "project/component/env.tfstate";
            _scriptGroup.TerraformStateSubscriptionId = StateSubscription;
        }

        private Task<bool> Plan() => _processor.PreparePlanAsync(
            "pipe", 1, Path.Join(_root, "plan.bin"), Path.Join(_root, "plan.txt"),
            Path.Join(_root, "provider.lock"), CancellationToken.None);

        private Task<bool> Apply() => _processor.ExecuteConfirmedPlanAsync(
            "pipe", 1, Path.Join(_root, "plan.bin"), Path.Join(_root, "provider.lock"),
            null, Path.Join(_root, "deployed.zip"), CancellationToken.None);

        [TestMethod]
        public async Task CatalogWithoutState_FailsBeforeSourceFetchOrTerraform_AndCleansWorkingDirectory()
        {
            _scriptGroup.TerraformSourceType = TerraformSourceType.Catalog;
            Assert.IsFalse(await Plan());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
            Assert.IsFalse(await Apply());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        public async Task PartiallyConfiguredPlatformState_DoesNotFallBackToLegacy()
        {
            _scriptGroup.TerraformStateStorageAccount = "stdorc01";
            Assert.IsFalse(await Plan());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        [DataRow("account")]
        [DataRow("container")]
        [DataRow("key")]
        [DataRow("subscription")]
        public async Task CatalogIncompleteState_FailsBeforeAnyTerraformCommand(string missing)
        {
            ConfigureState();
            _scriptGroup.TerraformSourceType = TerraformSourceType.Catalog;
            switch (missing)
            {
                case "account": _scriptGroup.TerraformStateStorageAccount = " "; break;
                case "container": _scriptGroup.TerraformStateContainerName = " "; break;
                case "key": _scriptGroup.TerraformStateKey = " "; break;
                case "subscription": _scriptGroup.TerraformStateSubscriptionId = "not-a-guid"; break;
            }
            Assert.IsFalse(await Plan());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        public async Task LegacySourceWithoutPlatformState_PreservesOwnBackend()
        {
            File.WriteAllText(Path.Join(_root, "source", "backend.tf"), "terraform { backend \"s3\" {} }");
            Assert.IsTrue(await Plan());
            StringAssert.Contains(_processor.PlannedSource["backend.tf"], "backend \"s3\"");
            Assert.IsFalse(_processor.PlannedSource.ContainsKey(TerraformBackendRenderer.BackendFileName));
        }

        [TestMethod]
        public async Task BackendSubscriptionAndAzureAdAuth_AreIndependentOfWorkloadSubscription()
        {
            ConfigureState();
            _scriptGroup.CommonProperties[TerraformProviderRenderer.SubscriptionIdPropertyName] =
                new VariableValue { Value = WorkloadSubscription, Type = typeof(string) };

            Assert.IsTrue(await Plan());
            var backend = _processor.PlannedSource[TerraformBackendRenderer.BackendFileName];
            StringAssert.Contains(backend, "use_azuread_auth     = true");
            StringAssert.Contains(backend, $"subscription_id      = \"{StateSubscription}\"");
            Assert.IsFalse(backend.Contains(WorkloadSubscription));
            var arm = (IReadOnlyDictionary<string, string>)typeof(TerraformProcessor)
                .GetField("_armEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_processor)!;
            Assert.AreEqual(WorkloadSubscription, arm["ARM_SUBSCRIPTION_ID"]);
        }

        [TestMethod]
        [DataRow("quoted\"secret\\path${value}")]
        [DataRow("\\secret\r\nwith-newline")]
        [DataRow("secret-ü-\"quoted\"")]
        public async Task ApplyAfterSourceRefMoves_UsesApprovedSourceProvenanceAndRedactedTfvars(string secret)
        {
            _scriptGroup.CommonProperties["password"] = new VariableValue { Value = secret, Type = typeof(string) };
            _scriptGroup.SensitivePropertyNames.Add("password");
            Assert.IsTrue(await Plan());
            var archived = File.ReadAllBytes(TerraformSourceArchive.PlanArchivePath(Path.Join(_root, "plan.bin")));
            // The standalone lock is no longer authoritative; the snapshot's
            // provider selection must survive even if this artifact changes.
            File.WriteAllText(Path.Join(_root, "provider.lock"), "# different provider lock");

            File.WriteAllText(Path.Join(_root, "source", "main.tf"), "# branch has moved");
            _scriptGroup.TerraformGitBranch = "different-ref";
            _scriptGroup.TerraformResolvedGitSha = "different-sha";
            // A fresh checkout is not even available when applying.
            Directory.Delete(Path.Join(_root, "source"), true);
            Assert.IsTrue(await Apply());
            Assert.AreEqual("# approved source", _processor.AppliedSource);
            Assert.AreEqual("# provider lock", _processor.AppliedLock);
            CollectionAssert.AreEqual(archived, File.ReadAllBytes(Path.Join(_root, "deployed.zip")));
            using var zip = ZipFile.OpenRead(Path.Join(_root, "deployed.zip"));
            using var provenanceReader = new StreamReader(zip.GetEntry(TerraformSourceArchive.ProvenanceEntryName)!.Open());
            using var provenance = JsonDocument.Parse(provenanceReader.ReadToEnd());
            Assert.AreEqual("approved-sha", provenance.RootElement.GetProperty("GitCommitSha").GetString());
            Assert.AreEqual("main", provenance.RootElement.GetProperty("GitRef").GetString());
            using var tfvarsReader = new StreamReader(zip.GetEntry("terraform.tfvars")!.Open());
            var tfvars = tfvarsReader.ReadToEnd();
            StringAssert.Contains(tfvars, "[REDACTED]");
            Assert.IsFalse(tfvars.Contains("secret"));
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        public async Task ApplyWithoutPlanSource_FailsRatherThanRefetching()
        {
            Assert.IsFalse(await Apply());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        public async Task ApplyWithCorruptPlanSource_FailsBeforeTerraformAndCleansDirectory()
        {
            File.WriteAllText(TerraformSourceArchive.PlanArchivePath(Path.Join(_root, "plan.bin")), "not a zip");
            Assert.IsFalse(await Apply());
            Assert.AreEqual(0, _processor.Commands.Count);
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        [TestMethod]
        public async Task FailedTerraformCommand_CleansWorkingDirectory()
        {
            _processor.FailCommand = true;
            Assert.IsFalse(await Plan());
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
        }

        private sealed class FakePipe(ScriptGroup scriptGroup) : IScriptGroupPipeClient
        {
            public ScriptGroup GetScriptGroupProperties(string pipeName) => scriptGroup;
        }

        private sealed class FakeProcessor : TerraformProcessor
        {
            private readonly string _root;
            public List<TerraformCommand> Commands { get; } = new();
            public Dictionary<string, string> PlannedSource { get; } = new();
            public string? AppliedSource { get; private set; }
            public string? AppliedLock { get; private set; }
            public string LastWorkingDirectory { get; private set; } = "";
            public bool FailCommand { get; set; }

            public FakeProcessor(string root, IRunnerLogger logger, IScriptGroupPipeClient pipe)
                : base(logger, pipe, Substitute.For<ITemplateCatalog>()) => _root = root;

            protected override string CreateTerraformWorkingDirectory(int requestId)
            {
                LastWorkingDirectory = Path.Join(_root, "work-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(LastWorkingDirectory);
                return LastWorkingDirectory;
            }

            protected override Task<string> RunTerraformCommandAsync(
                string workingDir, TerraformCommand command, IReadOnlyList<string> args, CancellationToken cancellationToken)
            {
                Commands.Add(command);
                if (FailCommand) throw new InvalidOperationException("Terraform failed");
                if (command == TerraformCommand.Init)
                {
                    var lockFile = Path.Join(workingDir, ".terraform.lock.hcl");
                    if (!File.Exists(lockFile)) File.WriteAllText(lockFile, "# provider lock");
                }
                if (command == TerraformCommand.PlanDetailedExitCode)
                {
                    File.WriteAllText(args.Single(a => a.StartsWith("-out="))[5..], "binary plan");
                    foreach (var file in Directory.EnumerateFiles(workingDir))
                        PlannedSource[Path.GetFileName(file)] = File.ReadAllText(file);
                }
                if (command == TerraformCommand.Apply)
                {
                    AppliedSource = File.ReadAllText(Path.Join(workingDir, "main.tf"));
                    AppliedLock = File.ReadAllText(Path.Join(workingDir, ".terraform.lock.hcl"));
                }
                return Task.FromResult("plan output");
            }
        }
    }
}
