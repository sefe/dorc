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
        private IRunnerLogger _logger = null!;
        private ITemplateCatalog _catalog = null!;

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
            _logger = Substitute.For<IRunnerLogger>();
            _logger.FileLogger.Returns(NullLogger.Instance);
            _catalog = Substitute.For<ITemplateCatalog>();
            _processor = new FakeProcessor(_root, _logger, pipe, _catalog);
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
        public async Task ApplyAfterSourceRefMoves_UsesApprovedSourceProvenanceWithoutVariableFiles(string secret)
        {
            _scriptGroup.CommonProperties["password"] = new VariableValue { Value = secret, Type = typeof(string) };
            _scriptGroup.SensitivePropertyNames.Add("password");
            _scriptGroup.CommonProperties["UnrelatedDatabasePassword"] =
                new VariableValue { Value = "not-flagged-or-used-by-legacy", Type = typeof(string) };
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
            Assert.IsNull(zip.GetEntry("terraform.tfvars"));
            StringAssert.Contains(_processor.PlannedSource["terraform.tfvars"], "not-flagged-or-used-by-legacy");
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

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CatalogInheritedSensitiveScalars_AreEncodedByManifestAndRedactedInErrors(bool useManifestDefault)
        {
            _scriptGroup.TerraformSourceType = TerraformSourceType.Catalog;
            _scriptGroup.TerraformTemplateName = "test";
            _scriptGroup.TerraformTemplateVersion = "1.0.0";
            var secret = "quoted\"\\path\n\r%{if x}${x}\t";
            var parameters = new[]
            {
                new TerraformTemplateParameter("text", TerraformParameterType.String, true,
                    null, useManifestDefault ? secret : null, null, null, null, null, true),
                new TerraformTemplateParameter("count", TerraformParameterType.Number, true,
                    null, null, null, null, null, null),
                new TerraformTemplateParameter("enabled", TerraformParameterType.Bool, true,
                    null, "true", null, null, null, null)
            };
            var manifest = new TerraformTemplateManifest("test", "1.0.0",
                new TerraformTemplateSource("git", "https://example.invalid/test", "v1"), parameters,
                Array.Empty<TerraformTemplateOutput>(), null, Array.Empty<string>(), null,
                new Dictionary<string, string>(), ">=1.9", null, false, null);
            _catalog.GetAsync("test", "1.0.0", Arg.Any<CancellationToken>()).Returns(manifest);
            if (!useManifestDefault)
                _scriptGroup.CommonProperties["text"] = new VariableValue { Value = secret, Type = typeof(string) };
            _scriptGroup.CommonProperties["count"] = new VariableValue { Value = "1.25e2", Type = typeof(string) };
            _scriptGroup.CommonProperties["UnrelatedDatabasePassword"] =
                new VariableValue { Value = "not-a-catalog-input", Type = typeof(string) };
            await (Task)typeof(TerraformProcessor).GetMethod("CollectSensitiveValuesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_processor,
                new object[] { _scriptGroup, CancellationToken.None })!;
            var effective = (IDictionary<string, VariableValue>)typeof(TerraformProcessor).GetMethod("ResolvePlanProperties",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_processor, new object[] { _scriptGroup.CommonProperties })!;
            await (Task)typeof(TerraformProcessor).GetMethod("CreateTerraformVariablesFileAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_processor,
                new object[] { _root, effective, CancellationToken.None })!;
            var variables = File.ReadAllText(Path.Join(_root, "terraform.tfvars"));
            StringAssert.Contains(variables, "count = 125");
            StringAssert.Contains(variables, "enabled = true");
            Assert.IsFalse(variables.Contains("UnrelatedDatabasePassword"));
            StringAssert.Contains(variables, "text = \"quoted\\\"\\\\path\\n\\r%%{if x}$${x}\\t\"");
            var redact = typeof(TerraformProcessor).GetMethod("RedactSensitiveValues",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.AreEqual("[REDACTED]", redact.Invoke(_processor, new object[] { secret }));
            StringAssert.Contains((string)redact.Invoke(_processor, new object[] { variables })!, "text = \"[REDACTED]\"");
            var error = Assert.ThrowsExactly<TargetInvocationException>(() => typeof(TerraformProcessor)
                .GetMethod("InterpretExitCode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_processor, new object[] { TerraformCommand.PlanDetailedExitCode, 1, "error " + secret }));
            Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
            _logger.Received().Error(Arg.Is<string>(message => message.Contains("error [REDACTED]")
                && !message.Contains(secret)));
        }

        [TestMethod]
        public async Task CatalogEffectiveDefaultConflict_FailsBeforeFetchingSourceOrRunningTerraform()
        {
            ConfigureState();
            _scriptGroup.TerraformSourceType = TerraformSourceType.Catalog;
            _scriptGroup.TerraformTemplateName = "test";
            _scriptGroup.TerraformTemplateVersion = "1.0.0";
            var manifest = new TerraformTemplateManifest("test", "1.0.0",
                new TerraformTemplateSource("git", "https://example.invalid/test", "v1"),
                new[]
                {
                    new TerraformTemplateParameter("database_name", TerraformParameterType.String, true,
                        null, null, null, null, null, null),
                    new TerraformTemplateParameter("create_service", TerraformParameterType.Bool, false,
                        null, "true", null, null, null, null)
                },
                Array.Empty<TerraformTemplateOutput>(), null, Array.Empty<string>(), null,
                new Dictionary<string, string>(), ">=1.9", null, false, null,
                new[] { new TerraformParameterConflict("database_name", "defaultdb", "create_service", "true",
                    "Cannot create reserved default database.") });
            _catalog.GetAsync("test", "1.0.0", Arg.Any<CancellationToken>()).Returns(manifest);
            _scriptGroup.CommonProperties["database_name"] = new VariableValue { Value = "defaultdb", Type = typeof(string) };
            Assert.IsFalse(await Plan());
            Assert.AreEqual(0, _processor.Commands.Count);
            _logger.Received().Error(Arg.Is<string>(message => message.Contains("Cannot create reserved default database.")));
        }

        [TestMethod]
        public async Task CancellationDuringPlan_PropagatesAndCleansWorkingDirectory()
        {
            _processor.CancelCommand = true;
            await Assert.ThrowsAsync<OperationCanceledException>(() => Plan());
            Assert.IsFalse(Directory.Exists(_processor.LastWorkingDirectory));
            Assert.IsFalse(File.Exists(TerraformSourceArchive.PlanArchivePath(Path.Join(_root, "plan.bin"))));
        }

        [TestMethod]
        public async Task CancellationMarker_CancelsRunnerOperation()
        {
            var marker = Path.Join(_root, "plan.cancel");
            using var operation = new CancellationTokenSource();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var watch = Program.WatchCancellationAsync(marker, operation, stop.Token);
            File.WriteAllText(marker, string.Empty);
            await watch;
            Assert.IsTrue(operation.IsCancellationRequested);
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
            public bool CancelCommand { get; set; }

            public FakeProcessor(string root, IRunnerLogger logger, IScriptGroupPipeClient pipe, ITemplateCatalog catalog)
                : base(logger, pipe, catalog) => _root = root;

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
                if (CancelCommand) throw new OperationCanceledException();
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
