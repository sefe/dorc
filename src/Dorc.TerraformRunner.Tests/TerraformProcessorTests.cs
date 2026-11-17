using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.Pipes;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.State;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
        public async Task CatalogInheritedSensitiveScalars_AreEncodedAndRedactedBeforePlanEmission(bool useManifestDefault)
        {
            ConfigureState();
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
            _processor.ShowOutput = "plain=" + secret + "\njson=" + JsonSerializer.Serialize(secret)
                + "\nhcl=\"quoted\\\"\\\\path\\n\\r%%{if x}$${x}\\t\"";
            Assert.IsTrue(await Plan());
            var variables = _processor.PlannedSource["terraform.tfvars"];
            StringAssert.Contains(variables, "count = 125");
            StringAssert.Contains(variables, "enabled = true");
            Assert.IsFalse(variables.Contains("UnrelatedDatabasePassword"));
            StringAssert.Contains(variables, "text = \"quoted\\\"\\\\path\\n\\r%%{if x}$${x}\\t\"");
            Assert.AreEqual("plain=[REDACTED]\njson=\"[REDACTED]\"\nhcl=\"[REDACTED]\"",
                File.ReadAllText(Path.Join(_root, "plan.txt")));
            Assert.AreEqual("binary plan", File.ReadAllText(Path.Join(_root, "plan.bin")));
            _processor.FailCommand = true;
            _processor.FailureMessage = "error " + secret;
            Assert.IsFalse(await Plan());
            _logger.Received().Error(Arg.Is<string>(message => message.Contains("error [REDACTED]")
                && !message.Contains(secret)));
        }

        [TestMethod]
        [DataRow(" ")]
        [DataRow("\t")]
        public async Task Plan_WhitespaceSensitiveValue_IsStillMasked(string secret)
        {
            _scriptGroup.CommonProperties["password"] = new VariableValue { Value = secret, Type = typeof(string) };
            _scriptGroup.SensitivePropertyNames.Add("password");
            _processor.ShowOutput = "password=\"" + secret + "\"";

            Assert.IsTrue(await Plan());
            Assert.AreEqual("password=\"[REDACTED]\"", File.ReadAllText(Path.Join(_root, "plan.txt")));
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

        [TestMethod]
        public async Task DestroyPlanAndApply_UsesDestroyFlagRecordsDigestAndCleansStateBlob()
        {
            ConfigureState();
            var cleaner = Substitute.For<ITerraformStateBlobCleaner>();
            cleaner.DeleteStateBlobIfConfiguredAsync(_scriptGroup, Arg.Any<CancellationToken>()).Returns(true);
            _processor = new FakeProcessor(_root, _logger, new FakePipe(_scriptGroup), _catalog, cleaner);

            var planPath = Path.Join(_root, "destroy-plan.bin");
            var planTextPath = Path.Join(_root, "destroy-plan.txt");
            var lockPath = Path.Join(_root, "destroy-provider.lock");

            Assert.IsTrue(await _processor.PreparePlanAsync(
                "pipe", 1, planPath, planTextPath, lockPath, CancellationToken.None, isDestroy: true));

            var planArgs = _processor.CommandArgs[1];
            CollectionAssert.Contains(planArgs.ToList(), "-destroy");
            CollectionAssert.Contains(planArgs.ToList(), "-input=false");
            CollectionAssert.Contains(planArgs.ToList(), "-no-color");
            Assert.IsTrue(File.Exists(planPath));
            Assert.IsTrue(File.Exists(planTextPath));

            using (var archive = ZipFile.OpenRead(TerraformSourceArchive.PlanArchivePath(planPath)))
            using (var reader = new StreamReader(archive.GetEntry(TerraformSourceArchive.ProvenanceEntryName)!.Open()))
            using (var doc = JsonDocument.Parse(reader.ReadToEnd()))
            {
                Assert.AreEqual("Destroy", doc.RootElement.GetProperty("Operation").GetString());
                var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(planPath))).ToLowerInvariant();
                Assert.AreEqual(expectedSha, doc.RootElement.GetProperty("PlanSha256").GetString());
            }

            _processor.Commands.Clear();
            _processor.CommandArgs.Clear();
            Assert.IsTrue(await _processor.ExecuteConfirmedPlanAsync(
                "pipe", 1, planPath, lockPath, Path.Join(_root, "unused-applied.json"),
                Path.Join(_root, "destroy-deployed.zip"), CancellationToken.None, isDestroy: true));

            var applyArgs = _processor.CommandArgs[1];
            CollectionAssert.Contains(applyArgs.ToList(), "-input=false");
            CollectionAssert.Contains(applyArgs.ToList(), "-no-color");
            CollectionAssert.Contains(applyArgs.ToList(), planPath);
            await cleaner.Received(1).DeleteStateBlobIfConfiguredAsync(_scriptGroup, Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task PreparePlan_WhenPlanFileNotWritten_SetsEmptyPlanSha256InProvenance()
        {
            _processor.WritePlanFile = false;
            var planPath = Path.Join(_root, "missing-plan.bin");
            Assert.IsTrue(await _processor.PreparePlanAsync(
                "pipe", 1, planPath, Path.Join(_root, "plan.txt"), null, CancellationToken.None, isDestroy: false));

            using var archive = ZipFile.OpenRead(TerraformSourceArchive.PlanArchivePath(planPath));
            using var reader = new StreamReader(archive.GetEntry(TerraformSourceArchive.ProvenanceEntryName)!.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            Assert.AreEqual("Plan", doc.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(string.Empty, doc.RootElement.GetProperty("PlanSha256").GetString());
        }

        [TestMethod]
        public async Task ProgramDispatchOperation_RoutesAllTerraformRunnerOperations()
        {
            var processor = Substitute.For<ITerraformProcessor>();
            processor.PreparePlanAsync("p", 7, "plan.bin", "plan.txt", "lock.hcl", Arg.Any<CancellationToken>(), false).Returns(true);
            processor.PreparePlanAsync("p", 7, "plan.bin", "plan.txt", "lock.hcl", Arg.Any<CancellationToken>(), true).Returns(true);
            processor.ExecuteConfirmedPlanAsync("p", 7, "plan.bin", "lock.hcl", "applied.json", "src.zip", Arg.Any<CancellationToken>(), false).Returns(true);
            processor.ExecuteConfirmedPlanAsync("p", 7, "plan.bin", "lock.hcl", "applied.json", "src.zip", Arg.Any<CancellationToken>(), true).Returns(true);

            Options MakeOptions(TerraformRunnerOperations op) => new()
            {
                PipeName = "p",
                LogPath = "log",
                Operation = (int)op,
                PlanFilePath = "plan.bin",
                PlanContentFilePath = "plan.txt",
                LockFilePath = "lock.hcl",
                AppliedResourcesFilePath = "applied.json",
                SourceArchiveFilePath = "src.zip"
            };

            Assert.IsTrue(await Program.DispatchOperationAsync(processor, MakeOptions(TerraformRunnerOperations.CreatePlan), 7, CancellationToken.None));
            Assert.IsTrue(await Program.DispatchOperationAsync(processor, MakeOptions(TerraformRunnerOperations.ApplyPlan), 7, CancellationToken.None));
            Assert.IsTrue(await Program.DispatchOperationAsync(processor, MakeOptions(TerraformRunnerOperations.CreateDestroyPlan), 7, CancellationToken.None));
            Assert.IsTrue(await Program.DispatchOperationAsync(processor, MakeOptions(TerraformRunnerOperations.ApplyDestroyPlan), 7, CancellationToken.None));
            Assert.IsFalse(await Program.DispatchOperationAsync(processor, MakeOptions(TerraformRunnerOperations.None), 7, CancellationToken.None));
        }

        [TestMethod]
        public async Task StateBlobCleaner_HandlesMissingConfigCredentialsCustomDelegateAndExceptions()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new TerraformStateBlobCleaner(null!));

            var defaultCleaner = new TerraformStateBlobCleaner(_logger);
            Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(null));
            Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(new ScriptGroup()));
            Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(new ScriptGroup
            {
                TerraformStateStorageAccount = "st01"
            }));
            Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(new ScriptGroup
            {
                TerraformStateStorageAccount = "st01",
                TerraformStateContainerName = "tfstate"
            }));

            // Untrusted CommonProperties fallback is ignored when ScriptGroup state fields are omitted
            var untrustedPropsOnlyGroup = new ScriptGroup
            {
                CommonProperties = new Dictionary<string, VariableValue>
                {
                    [TerraformStateBlobCleaner.StorageAccountPropertyName] = new() { Value = "st01" },
                    [TerraformStateBlobCleaner.ContainerNamePropertyName] = new() { Value = "tfstate" },
                    [TerraformStateBlobCleaner.KeyPropertyName] = new() { Value = "proj/comp/env.tfstate" }
                }
            };
            Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(untrustedPropsOnlyGroup));

            // Invalid state blob keys are rejected before any delete request
            foreach (var invalidKey in new[]
            {
                "proj/comp/env.txt",
                "proj/env.tfstate",
                "proj\\comp\\env.tfstate",
                "proj/../comp/env.tfstate",
                "proj/./comp/env.tfstate",
                "proj//env.tfstate",
                "   "
            })
            {
                Assert.IsFalse(TerraformStateBlobCleaner.IsValidStateBlobKey(invalidKey), $"Key '{invalidKey}' should be invalid");
                var invalidKeyGroup = new ScriptGroup
                {
                    TerraformStateStorageAccount = "st01",
                    TerraformStateContainerName = "tfstate",
                    TerraformStateKey = invalidKey
                };
                Assert.IsFalse(await defaultCleaner.DeleteStateBlobIfConfiguredAsync(invalidKeyGroup));
            }
            Assert.IsFalse(TerraformStateBlobCleaner.IsValidStateBlobKey(null));
            Assert.IsTrue(TerraformStateBlobCleaner.IsValidStateBlobKey("proj/comp/env.TFSTATE"));
            Assert.AreEqual("az.cmd", TerraformStateBlobCleaner.ResolveAzCliExecutableForPlatform(true));
            Assert.AreEqual("az", TerraformStateBlobCleaner.ResolveAzCliExecutableForPlatform(false));
            Assert.IsFalse(string.IsNullOrWhiteSpace(TerraformStateBlobCleaner.DefaultAzCliExecutableResolver()));

            var originalAzResolver = TerraformStateBlobCleaner.DefaultAzCliExecutableResolver;
            TerraformStateBlobCleaner.DefaultAzCliExecutableResolver = () => "nonexistent-az-binary-for-unit-tests";
            try
            {
            // Default cleaner without credentials when IMDS returns 404 or throws fails closed (returns false)
            var groupWithState = new ScriptGroup
            {
                TerraformStateStorageAccount = "st01",
                TerraformStateContainerName = "tfstate",
                TerraformStateKey = "proj/comp/env.tfstate",
                CommonProperties = new Dictionary<string, VariableValue>()
            };
            var imdsSkipCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))),
                _ => null);
            Assert.IsFalse(await imdsSkipCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var imdsThrowSkipCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((_, _) => throw new HttpRequestException("IMDS unreachable"))),
                _ => null);
            Assert.IsFalse(await imdsThrowSkipCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Direct ARM_OIDC_TOKEN assertion path
            var directOidcCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                {
                    if (req.RequestUri!.Host.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"access_token\":\"direct-oidc-tok\"}", Encoding.UTF8, "application/json")
                        });
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                })),
                key => key switch
                {
                    "ARM_TENANT_ID" => "tenant-oidc",
                    "ARM_CLIENT_ID" => "client-oidc",
                    "ARM_OIDC_TOKEN" => "inline-jwt-assertion",
                    _ => null
                });
            Assert.IsTrue(await directOidcCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Cover TryAcquireCliTokenFromProcessAsync timeout/cancellation killing the process
            using var cancelledCts = new CancellationTokenSource();
            cancelledCts.Cancel();
            Assert.IsNull(await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("dotnet", "--info", cancelledCts.Token));

            using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            Assert.IsNull(await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("ping", "127.0.0.1 -n 10", shortCts.Token));

            // Explicit ARM_USE_MSI=true when IMDS returns 404 fails closed (false)
            var imdsFailCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))),
                key => key == "ARM_USE_MSI" ? "true" : null);
            Assert.IsFalse(await imdsFailCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Direct ARM_ACCESS_TOKEN and cliTokenProvider / ARM_USE_CLI paths
            var directTokenCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))),
                key => key == "ARM_ACCESS_TOKEN" ? "direct-storage-tok" : null);
            Assert.IsTrue(await directTokenCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var cliDelegateCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                    Task.FromResult(new HttpResponseMessage(req.RequestUri!.Host == "169.254.169.254" ? HttpStatusCode.NotFound : HttpStatusCode.OK)))),
                _ => null,
                _ => Task.FromResult<string?>("cli-delegate-token"));
            Assert.IsTrue(await cliDelegateCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var cliEmptyDelegateCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))),
                key => key switch
                {
                    "ARM_TENANT_ID" => "arm-tenant",
                    "ARM_CLIENT_ID" => "arm-client",
                    "ARM_CLIENT_SECRET" => "arm-secret",
                    _ => null
                },
                _ => Task.FromResult<string?>("   "));
            Assert.IsFalse(await cliEmptyDelegateCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var cliEnvCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                    Task.FromResult(new HttpResponseMessage(req.RequestUri!.Host == "169.254.169.254" ? HttpStatusCode.NotFound : HttpStatusCode.OK)))),
                key => key switch
                {
                    "ARM_USE_CLI" => "true",
                    "DORC_AZ_CLI_PATH" => "nonexistent-az-binary-xyz",
                    _ => null
                });
            Assert.IsFalse(await cliEnvCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var cliDefaultExeCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                    Task.FromResult(new HttpResponseMessage(req.RequestUri!.Host == "169.254.169.254" ? HttpStatusCode.NotFound : HttpStatusCode.OK)))),
                key => key == "ARM_USE_CLI" ? "true" : null);
            _ = await cliDefaultExeCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState);

            // Cover TryAcquireCliTokenFromProcessAsync: success (dotnet --version), non-zero exit code, blank output, and missing binary
            var dotnetToken = await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("dotnet", "--version", CancellationToken.None);
            Assert.IsFalse(string.IsNullOrWhiteSpace(dotnetToken));
            Assert.IsNull(await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("dotnet", "nonexistent-subcommand-xyz", CancellationToken.None));
            Assert.IsNull(await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("cmd.exe", "/c echo.", CancellationToken.None));
            Assert.IsNull(await TerraformStateBlobCleaner.TryAcquireCliTokenFromProcessAsync("nonexistent-cli-exe-xyz", "", CancellationToken.None));

            // Cover HasRemainingResourcesInState
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState(null));
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState("   "));
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState("{\"version\":4,\"resources\":[]}"));
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState("{\"version\":4}"));
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState("{\"version\":4,\"resources\":\"none\"}"));
            Assert.IsFalse(TerraformStateBlobCleaner.HasRemainingResourcesInState("[]"));
            Assert.IsTrue(TerraformStateBlobCleaner.HasRemainingResourcesInState("{\"version\":4,\"resources\":[{\"type\":\"azurerm_rg\"}]}"));
            Assert.IsTrue(TerraformStateBlobCleaner.HasRemainingResourcesInState("{not-valid-json"));

            // IMDS Managed Identity (system-assigned and user-assigned via ARM_CLIENT_ID) + Ambient ARM_CLIENT_SECRET paths
            var imdsSuccessCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                {
                    if (req.RequestUri!.Host == "169.254.169.254" && req.Headers.Contains("Metadata"))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"access_token\":\"imds-tok\"}", Encoding.UTF8, "application/json")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                })),
                key => key == "ARM_CLIENT_ID" ? "user-assigned-mi-id" : null);
            Assert.IsTrue(await imdsSuccessCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var envSpnCleaner = new TerraformStateBlobCleaner(
                _logger,
                null,
                new HttpClient(new StubHttpMessageHandler((req, _) =>
                {
                    if (req.RequestUri!.Host.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"access_token\":\"env-spn-tok\"}", Encoding.UTF8, "application/json")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                })),
                key => key switch
                {
                    "ARM_TENANT_ID" => "arm-tenant",
                    "ARM_CLIENT_ID" => "arm-client",
                    "ARM_CLIENT_SECRET" => "arm-secret",
                    _ => null
                });
            Assert.IsTrue(await envSpnCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Ambient Azure Workload Identity / OIDC and App Service Managed Identity paths
            var tempTokenFile = Path.Combine(_root, $"dorc-oidc-{Guid.NewGuid():N}.jwt");
            await File.WriteAllTextAsync(tempTokenFile, "  federated-jwt-assertion  ");
            try
            {
                // 1) OIDC Workload Identity success -> HTTP 202 Accepted on blob delete
                var oidcCleaner = new TerraformStateBlobCleaner(
                    _logger,
                    null,
                    new HttpClient(new StubHttpMessageHandler((req, _) =>
                    {
                        if (req.RequestUri!.Host.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                        {
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("{\"access_token\":\"oidc-tok\"}", Encoding.UTF8, "application/json")
                            });
                        }

                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
                    })),
                    key => key switch
                    {
                        "AZURE_TENANT_ID" => "tenant-oidc",
                        "AZURE_CLIENT_ID" => "client-oidc",
                        "AZURE_FEDERATED_TOKEN_FILE" => tempTokenFile,
                        _ => null
                    });
                Assert.IsTrue(await oidcCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

                // 2) OIDC Workload Identity configured, but token file missing -> fails closed (false)
                var oidcMissingFileCleaner = new TerraformStateBlobCleaner(
                    _logger,
                    null,
                    new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))),
                    key => key switch
                    {
                        "AZURE_TENANT_ID" => "tenant-oidc",
                        "AZURE_CLIENT_ID" => "client-oidc",
                        "AZURE_FEDERATED_TOKEN_FILE" => tempTokenFile + ".missing",
                        _ => null
                    });
                Assert.IsFalse(await oidcMissingFileCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

                // 3) OIDC Workload Identity configured, but token file empty -> fails closed (false)
                var emptyTokenFile = tempTokenFile + ".empty";
                await File.WriteAllTextAsync(emptyTokenFile, "   ");
                try
                {
                    var oidcEmptyFileCleaner = new TerraformStateBlobCleaner(
                        _logger,
                        null,
                        new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))),
                        key => key switch
                        {
                            "AZURE_TENANT_ID" => "tenant-oidc",
                            "AZURE_CLIENT_ID" => "client-oidc",
                            "AZURE_FEDERATED_TOKEN_FILE" => emptyTokenFile,
                            _ => null
                        });
                    Assert.IsFalse(await oidcEmptyFileCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));
                }
                finally
                {
                    File.Delete(emptyTokenFile);
                }

                // 4) OIDC Workload Identity HTTP failure -> fails closed (false)
                var oidcHttpFailCleaner = new TerraformStateBlobCleaner(
                    _logger,
                    null,
                    new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)))),
                    key => key switch
                    {
                        "AZURE_TENANT_ID" => "tenant-oidc",
                        "AZURE_CLIENT_ID" => "client-oidc",
                        "AZURE_FEDERATED_TOKEN_FILE" => tempTokenFile,
                        _ => null
                    });
                Assert.IsFalse(await oidcHttpFailCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

                // 5) Managed Identity (IDENTITY_ENDPOINT with existing query parameter & IDENTITY_HEADER) success
                var miCleaner = new TerraformStateBlobCleaner(
                    _logger,
                    null,
                    new HttpClient(new StubHttpMessageHandler((req, _) =>
                    {
                        if (req.Headers.Contains("X-IDENTITY-HEADER"))
                        {
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("{\"access_token\":\"mi-tok\"}", Encoding.UTF8, "application/json")
                            });
                        }

                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                    })),
                    key => key switch
                    {
                        "IDENTITY_ENDPOINT" => "http://169.254.169.254/metadata/identity/oauth2/token?custom=1",
                        "IDENTITY_HEADER" => "secret-header",
                        _ => null
                    });
                Assert.IsTrue(await miCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

                // 6) Managed Identity without query string in IDENTITY_ENDPOINT, returning HTTP error -> fails closed (false)
                var miFailCleaner = new TerraformStateBlobCleaner(
                    _logger,
                    null,
                    new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))),
                    key => key switch
                    {
                        "IDENTITY_ENDPOINT" => "http://169.254.169.254/metadata/identity/oauth2/token",
                        "IDENTITY_HEADER" => "secret-header",
                        _ => null
                    });
                Assert.IsFalse(await miFailCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));
            }
            finally
            {
                File.Delete(tempTokenFile);
            }

            groupWithState.CommonProperties[TerraformArmCredentials.ClientIdPropertyName] = new() { Value = Guid.NewGuid().ToString() };
            groupWithState.CommonProperties[TerraformArmCredentials.ClientSecretPropertyName] = new() { Value = "secret" };
            groupWithState.CommonProperties[TerraformArmCredentials.TenantIdPropertyName] = new() { Value = Guid.NewGuid().ToString() };

            // Token endpoint failure returns false
            var tokenFailCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)))));
            Assert.IsFalse(await tokenFailCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Token endpoint missing access_token property or empty access_token returns false
            var missingTokenCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token_type\":\"Bearer\"}", Encoding.UTF8, "application/json")
                }))));
            Assert.IsFalse(await missingTokenCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var blankTokenCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"   \"}", Encoding.UTF8, "application/json")
                }))));
            Assert.IsFalse(await blankTokenCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            // Also cover TerraformProcessor.IsEphemeral and TerraformStateConfiguration.Resolve for ephemeral non-catalog components
        Assert.IsFalse(TerraformProcessor.IsEphemeral(null));
        Assert.IsFalse(TerraformProcessor.IsEphemeral(new Dictionary<string, VariableValue>
        {
            [TerraformProcessor.IsEphemeralPropertyName] = null!
        }));
        Assert.IsFalse(TerraformProcessor.IsEphemeral(new Dictionary<string, VariableValue>
        {
            [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "false" }
        }));
        Assert.IsTrue(TerraformProcessor.IsEphemeral(new Dictionary<string, VariableValue>
        {
            [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true" }
        }));
        Assert.IsNull(TerraformStateConfiguration.Resolve(new ScriptGroup
        {
            TerraformSourceType = TerraformSourceType.SharedFolder
        }));
        Assert.Throws<InvalidOperationException>(() => TerraformStateConfiguration.Resolve(new ScriptGroup
        {
            TerraformSourceType = TerraformSourceType.SharedFolder,
            CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true" }
            }
        }));

        // Blob DELETE 202 Accepted and 404 NotFound return true; 500 returns false
            foreach (var status in new[] { HttpStatusCode.Accepted, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
            {
                HttpRequestMessage? deleteRequest = null;
                var httpCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                {
                    if (req.Method == HttpMethod.Post)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"access_token\":\"tok-123\"}", Encoding.UTF8, "application/json")
                        });
                    }

                    if (req.Method == HttpMethod.Get)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"version\":4,\"resources\":[]}", Encoding.UTF8, "application/json")
                        });
                    }

                    deleteRequest = req;
                    return Task.FromResult(new HttpResponseMessage(status));
                })));

                var deleted = await httpCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState);
                Assert.AreEqual(status != HttpStatusCode.InternalServerError, deleted);
                Assert.IsNotNull(deleteRequest);
                Assert.AreEqual("https://st01.blob.core.windows.net/tfstate/proj/comp/env.tfstate", deleteRequest!.RequestUri!.ToString());
                Assert.AreEqual("Bearer", deleteRequest.Headers.Authorization!.Scheme);
                Assert.AreEqual("tok-123", deleteRequest.Headers.Authorization.Parameter);
                Assert.IsTrue(deleteRequest.Headers.Contains("x-ms-version"));
                Assert.IsTrue(deleteRequest.Headers.Contains("x-ms-delete-snapshots"));
            }

            // Cover InspectLeaseAndDeleteBlobAsync: GET 404 (true), GET 500 (false), GET with active resources (false),
            // GET with ETag + lease 409 (false), GET with ETag + lease without header + DELETE 200 (true),
            // GET with ETag + lease with x-ms-lease-id + DELETE 500 (releases lease and returns false)
            var get404Cleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(req.Method == HttpMethod.Post
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)))));
            Assert.IsTrue(await get404Cleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var get500Cleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(req.Method == HttpMethod.Post
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)))));
            Assert.IsFalse(await get500Cleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var nonEmptyStateCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
                Task.FromResult(req.Method == HttpMethod.Post
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[{\"type\":\"res\"}]}", Encoding.UTF8, "application/json") }))));
            Assert.IsFalse(await nonEmptyStateCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var leaseConflictCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
            {
                if (req.Method == HttpMethod.Post)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") });
                if (req.Method == HttpMethod.Get)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[]}", Encoding.UTF8, "application/json") };
                    resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x8D1\"");
                    return Task.FromResult(resp);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            })));
            Assert.IsFalse(await leaseConflictCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var leaseNoHeaderCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
            {
                if (req.Method == HttpMethod.Post)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") });
                if (req.Method == HttpMethod.Get)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[]}", Encoding.UTF8, "application/json") };
                    resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x8D1\"");
                    return Task.FromResult(resp);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })));
            Assert.IsTrue(await leaseNoHeaderCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));

            var leaseReleased = false;
            var leaseDeleteFailCleaner = new TerraformStateBlobCleaner(_logger, new HttpClient(new StubHttpMessageHandler((req, _) =>
            {
                if (req.Method == HttpMethod.Post)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json") });
                if (req.Method == HttpMethod.Get)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[]}", Encoding.UTF8, "application/json") };
                    resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x8D1\"");
                    return Task.FromResult(resp);
                }
                if (req.Method == HttpMethod.Put)
                {
                    if (req.Headers.TryGetValues("x-ms-lease-action", out var actions) && actions.Contains("release"))
                    {
                        leaseReleased = true;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                    }
                    var leaseResp = new HttpResponseMessage(HttpStatusCode.Created);
                    leaseResp.Headers.TryAddWithoutValidation("x-ms-lease-id", "lease-guid-1");
                    return Task.FromResult(leaseResp);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            })));
            Assert.IsFalse(await leaseDeleteFailCleaner.DeleteStateBlobIfConfiguredAsync(groupWithState));
            Assert.IsTrue(leaseReleased);

            // Explicit ScriptGroup fields work with custom delegate
            var explicitGroup = new ScriptGroup
            {
                TerraformStateStorageAccount = "st02",
                TerraformStateContainerName = "container02",
                TerraformStateKey = "proj/comp/key02.tfstate"
            };
            var customCleaner = new TerraformStateBlobCleaner(_logger, (sa, c, k, creds, ct) =>
                Task.FromResult(sa == "st02" && c == "container02" && k == "proj/comp/key02.tfstate" && creds is null));
            Assert.IsTrue(await customCleaner.DeleteStateBlobIfConfiguredAsync(explicitGroup));

            // Exception inside delegate is caught and logged as warning, returning false
            var throwingCleaner = new TerraformStateBlobCleaner(_logger, (_, _, _, _, _) =>
                throw new InvalidOperationException("Storage unreachable"));
            Assert.IsFalse(await throwingCleaner.DeleteStateBlobIfConfiguredAsync(explicitGroup));
            _logger.Received().Warning(Arg.Is<string>(m => m.Contains("Storage unreachable")));
            }
            finally
            {
                TerraformStateBlobCleaner.DefaultAzCliExecutableResolver = originalAzResolver;
            }
        }

        [TestMethod]
        public async Task DestroyApply_WhenStateBlobCleanupFailsOrStateUnconfigured_HandlesResultCorrectly()
        {
            ConfigureState();
            var cleaner = Substitute.For<ITerraformStateBlobCleaner>();
            cleaner.DeleteStateBlobIfConfiguredAsync(_scriptGroup, Arg.Any<CancellationToken>()).Returns(false);
            _processor = new FakeProcessor(_root, _logger, new FakePipe(_scriptGroup), _catalog, cleaner);

            var planPath = Path.Join(_root, "destroy-plan-fail.bin");
            var planTextPath = Path.Join(_root, "destroy-plan-fail.txt");
            var lockPath = Path.Join(_root, "destroy-provider-fail.lock");

            Assert.IsTrue(await _processor.PreparePlanAsync(
                "pipe", 1, planPath, planTextPath, lockPath, CancellationToken.None, isDestroy: true));

            // When state is configured and DeleteStateBlobIfConfiguredAsync returns false, ExecuteConfirmedPlanAsync returns false
            Assert.IsFalse(await _processor.ExecuteConfirmedPlanAsync(
                "pipe", 1, planPath, lockPath, Path.Join(_root, "unused-applied.json"),
                Path.Join(_root, "destroy-deployed.zip"), CancellationToken.None, isDestroy: true));
            _logger.Received().Error(Arg.Is<string>(m => m.Contains("Post-destroy state blob cleanup failed")));

            // When state is not configured on ScriptGroup, destroy succeeds even if cleaner returns false
            _scriptGroup.TerraformStateStorageAccount = "";
            _scriptGroup.TerraformStateContainerName = "";
            _scriptGroup.TerraformStateKey = "";
            _scriptGroup.TerraformStateSubscriptionId = "";
            Assert.IsTrue(await _processor.ExecuteConfirmedPlanAsync(
                "pipe", 1, planPath, lockPath, Path.Join(_root, "unused-applied.json"),
                Path.Join(_root, "destroy-deployed-nostate.zip"), CancellationToken.None, isDestroy: true));
        }

        [TestMethod]
        public async Task NonCatalogPlan_WhenEphemeralEnvironment_EnforcesTerminationProtectionFalse()
        {
            ConfigureState();
            File.WriteAllText(
                Path.Join(_root, "source", "defaults.auto.tfvars"),
                "termination_protection = true\nservice_termination_protection = \"true\"\nother = true\n");
            _scriptGroup.TerraformSourceType = TerraformSourceType.SharedFolder;
            _scriptGroup.CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true", Type = typeof(string) },
                ["termination_protection"] = new() { Value = "true", Type = typeof(string) },
                ["service_termination_protection"] = new() { Value = "true", Type = typeof(string) },
                ["TF_VAR_termination_protection"] = new() { Value = "true", Type = typeof(string) },
                ["TF_VAR_service_termination_protection"] = new() { Value = "true", Type = typeof(string) },
                ["OtherVar"] = new() { Value = "val", Type = typeof(string) }
            };

            Assert.IsTrue(await Plan());
            Assert.AreEqual("false", _scriptGroup.CommonProperties["termination_protection"].Value);
            Assert.AreEqual("false", _scriptGroup.CommonProperties["service_termination_protection"].Value);
            Assert.IsFalse(_scriptGroup.CommonProperties.ContainsKey("TF_VAR_termination_protection"));
            Assert.IsFalse(_scriptGroup.CommonProperties.ContainsKey("TF_VAR_service_termination_protection"));
            var variables = _processor.PlannedSource["terraform.tfvars"];
            StringAssert.Contains(variables, "termination_protection = false");
            StringAssert.Contains(variables, "service_termination_protection = false");
            var scrubbedAutoTfvars = _processor.PlannedSource["defaults.auto.tfvars"];
            StringAssert.Contains(scrubbedAutoTfvars, "termination_protection = false");
            StringAssert.Contains(scrubbedAutoTfvars, "service_termination_protection = false");
            StringAssert.Contains(scrubbedAutoTfvars, "other = true");
            // Because main.tf does not declare termination_protection or service_termination_protection, -var flags are NOT passed on CLI
            CollectionAssert.DoesNotContain(_processor.CommandArgs[1].ToList(), "-var=termination_protection=false");
            CollectionAssert.DoesNotContain(_processor.CommandArgs[1].ToList(), "-var=service_termination_protection=false");

            // When a non-catalog ephemeral module does NOT declare termination_protection in properties or .tf files, -var is omitted
            File.Delete(Path.Join(_root, "source", "defaults.auto.tfvars"));
            _scriptGroup.CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true", Type = typeof(string) },
                ["OtherVar"] = new() { Value = "val", Type = typeof(string) }
            };
            _processor.CommandArgs.Clear();
            Assert.IsTrue(await Plan());
            CollectionAssert.DoesNotContain(_processor.CommandArgs[1].ToList(), "-var=termination_protection=false");
            CollectionAssert.DoesNotContain(_processor.CommandArgs[1].ToList(), "-var=service_termination_protection=false");

            // When .tf file declares termination_protection and service_termination_protection (even if not in properties, or preceded by "/*" in string literals), it is enforced;
            // and terraform.tfvars.json is also scrubbed alongside *.auto.tfvars*
            File.WriteAllText(
                Path.Join(_root, "source", "variables.tf"),
                "variable \"path_glob\" { default = \"/api/*\" }\nvariable \"termination_protection\" { default = true }\nvariable \"end_glob\" { default = \"*/\" }\nvariable \"service_termination_protection\" { default = true }\n");
            File.WriteAllText(
                Path.Join(_root, "source", "terraform.tfvars.json"),
                "{\"termination_protection\": true, \"service_termination_protection\": \"true\"}");
            _scriptGroup.CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true", Type = typeof(string) }
            };
            _processor.CommandArgs.Clear();
            Assert.IsTrue(await Plan());
            CollectionAssert.Contains(_processor.CommandArgs[1].ToList(), "-var=termination_protection=false");
            CollectionAssert.Contains(_processor.CommandArgs[1].ToList(), "-var=service_termination_protection=false");
            StringAssert.Contains(_processor.PlannedSource["terraform.tfvars.json"], "\"termination_protection\": false");
            StringAssert.Contains(_processor.PlannedSource["terraform.tfvars.json"], "\"service_termination_protection\": false");
            File.Delete(Path.Join(_root, "source", "variables.tf"));
            File.Delete(Path.Join(_root, "source", "terraform.tfvars.json"));

            // When .tf file only references termination_protection as a resource attribute (or in comments or heredoc strings, including hyphenated delimiters), -var=termination_protection=false is NOT passed
            File.WriteAllText(
                Path.Join(_root, "source", "aiven.tf"),
                "# variable \"termination_protection\" { default = true }\n/* variable \"termination_protection\" { default = true } */\nlocals {\n  doc = <<-EOF-TEXT\nvariable \"termination_protection\" { default = true }\nEOF-TEXT\n}\nvariable \"service_termination_protection\" { default = true }\nresource \"aiven_pg\" \"db\" {\n  termination_protection = var.service_termination_protection\n}\n");
            _scriptGroup.CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true", Type = typeof(string) }
            };
            _processor.CommandArgs.Clear();
            Assert.IsTrue(await Plan());
            CollectionAssert.DoesNotContain(_processor.CommandArgs[1].ToList(), "-var=termination_protection=false");
            CollectionAssert.Contains(_processor.CommandArgs[1].ToList(), "-var=service_termination_protection=false");
            File.Delete(Path.Join(_root, "source", "aiven.tf"));

            // When .tf.json declares root variable (both object with preceding variable and array form, plus file without variable key), it is detected and enforced
            File.WriteAllText(
                Path.Join(_root, "source", "variables.tf.json"),
                "{\"variable\": {\"region\": {\"default\": \"uksouth\"}, \"termination_protection\": {\"default\": true}}}");
            File.WriteAllText(
                Path.Join(_root, "source", "array_vars.tf.json"),
                "{\"variable\": [{\"other\": {\"type\": \"string\"}}, {\"service_termination_protection\": {\"default\": true}}]}");
            File.WriteAllText(
                Path.Join(_root, "source", "outputs.tf.json"),
                "{\"output\": {\"id\": {\"value\": \"1\"}}}");
            _scriptGroup.CommonProperties = new Dictionary<string, VariableValue>
            {
                [TerraformProcessor.IsEphemeralPropertyName] = new() { Value = "true", Type = typeof(string) }
            };
            _processor.CommandArgs.Clear();
            Assert.IsTrue(await Plan());
            CollectionAssert.Contains(_processor.CommandArgs[1].ToList(), "-var=termination_protection=false");
            CollectionAssert.Contains(_processor.CommandArgs[1].ToList(), "-var=service_termination_protection=false");
            File.Delete(Path.Join(_root, "source", "variables.tf.json"));
            File.Delete(Path.Join(_root, "source", "array_vars.tf.json"));
            File.Delete(Path.Join(_root, "source", "outputs.tf.json"));

            // Non-.tf/.tf.json files such as prod.tfvars, files inside .terraform/modules, string descriptions, and equality comparisons (== false) do not trigger the hard-coded termination_protection check,
            // and disabling termination_protection (true -> false) in terraform show plan output is allowed
            File.WriteAllText(
                Path.Join(_root, "source", "prod.tfvars"),
                "termination_protection = true\n");
            var dotTerraformModuleDir = Path.Join(_root, "source", ".terraform", "modules", "vendor");
            Directory.CreateDirectory(dotTerraformModuleDir);
            File.WriteAllText(
                Path.Join(dotTerraformModuleDir, "vendor.tf"),
                "resource \"aiven_pg\" \"vendor\" { termination_protection = true }\n");
            File.WriteAllText(
                Path.Join(_root, "source", "valid_expressions.tf"),
                "variable \"tp_check\" {\n  type = bool\n  default = false\n  description = \"Whether termination_protection: true is blocked\"\n  validation {\n    condition = var.tp_check == false\n    error_message = \"termination_protection = true is not allowed\"\n  }\n}\nlocals {\n  computed_tp = {\n    termination_protection = true && var.tp_check\n  }\n}\n");
            _processor.ShowOutput = "  ~ termination_protection = true -> false";
            Assert.IsTrue(await Plan());
            File.Delete(Path.Join(_root, "source", "prod.tfvars"));
            File.Delete(Path.Join(_root, "source", "valid_expressions.tf"));
            Directory.Delete(Path.Join(_root, "source", ".terraform"), recursive: true);

            // When a resource or module in .tf hard-codes termination_protection = true on a non-destroy ephemeral plan, Plan fails closed
            File.WriteAllText(
                Path.Join(_root, "source", "hardcoded.tf"),
                "# comment\n/* block */\nlocals {\n  heredoc = <<-EOF\ntermination_protection = true\nEOF\n}\nresource \"aiven_pg\" \"db\" {\n  termination_protection = (true)\n}\n");
            Assert.IsFalse(await Plan());
            File.Delete(Path.Join(_root, "source", "hardcoded.tf"));

            // When terraform show plan output enables termination_protection (including false -> true, sensitive value, or known after apply) on a non-destroy ephemeral plan, Plan fails closed
            _processor.ShowOutput = "  ~ termination_protection = false -> true";
            Assert.IsFalse(await Plan());
            _processor.ShowOutput = "  + termination_protection = (sensitive value)";
            Assert.IsFalse(await Plan());
            _processor.ShowOutput = "  + termination_protection = (known after apply)";
            Assert.IsFalse(await Plan());
            _processor.ShowOutput = "plan output";
        }

        [TestMethod]
        public async Task CatalogPlan_WhenEphemeralEnvironment_EnforcesTerminationProtectionFalse()
        {
            ConfigureState();
            File.WriteAllText(
                Path.Join(_root, "source", "catalog.auto.tfvars"),
                "termination_protection = true\n");
            _scriptGroup.TerraformSourceType = TerraformSourceType.Catalog;
            _scriptGroup.TerraformTemplateName = "pg";
            _scriptGroup.TerraformTemplateVersion = "1.0.0";
            var manifest = new TerraformTemplateManifest(
                "pg",
                "1.0.0",
                new TerraformTemplateSource("git", "https://example.invalid/pg", "v1"),
                new[]
                {
                    new TerraformTemplateParameter("database_name", TerraformParameterType.String, true, null, null, null, null, null, null),
                    new TerraformTemplateParameter("termination_protection", TerraformParameterType.Bool, false, null, "true", null, null, null, null),
                    new TerraformTemplateParameter("service_termination_protection", TerraformParameterType.Bool, false, null, "true", null, null, null, null)
                },
                Array.Empty<TerraformTemplateOutput>(),
                null,
                Array.Empty<string>(),
                null,
                new Dictionary<string, string>(),
                ">=1.9",
                null,
                false,
                null);
            _catalog.GetAsync("pg", "1.0.0", Arg.Any<CancellationToken>()).Returns(manifest);
            _scriptGroup.CommonProperties["database_name"] = new VariableValue { Value = "eph_db", Type = typeof(string) };
            _scriptGroup.CommonProperties["termination_protection"] = new VariableValue { Value = "true", Type = typeof(string) };
            _scriptGroup.CommonProperties["service_termination_protection"] = new VariableValue { Value = "true", Type = typeof(string) };
            _scriptGroup.CommonProperties[TerraformProcessor.IsEphemeralPropertyName] = new VariableValue { Value = "true", Type = typeof(string) };

            Assert.IsTrue(await Plan());
            var variables = _processor.PlannedSource["terraform.tfvars"];
            StringAssert.Contains(variables, "termination_protection = false");
            StringAssert.Contains(variables, "service_termination_protection = false");
            StringAssert.Contains(_processor.PlannedSource["catalog.auto.tfvars"], "termination_protection = false");
        }

        private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => handler(request, cancellationToken);
        }

        private sealed class FakePipe(ScriptGroup scriptGroup) : IScriptGroupPipeClient
        {
            public ScriptGroup GetScriptGroupProperties(string pipeName) => scriptGroup;
        }

        private sealed class FakeProcessor : TerraformProcessor
        {
            private readonly string _root;
            public List<TerraformCommand> Commands { get; } = new();
            public List<IReadOnlyList<string>> CommandArgs { get; } = new();
            public Dictionary<string, string> PlannedSource { get; } = new();
            public string? AppliedSource { get; private set; }
            public string? AppliedLock { get; private set; }
            public string LastWorkingDirectory { get; private set; } = "";
            public bool FailCommand { get; set; }
            public bool CancelCommand { get; set; }
            public bool WritePlanFile { get; set; } = true;
            public string FailureMessage { get; set; } = "Terraform failed";
            public string ShowOutput { get; set; } = "plan output";

            public FakeProcessor(
                string root,
                IRunnerLogger logger,
                IScriptGroupPipeClient pipe,
                ITemplateCatalog catalog,
                ITerraformStateBlobCleaner? stateBlobCleaner = null)
                : base(logger, pipe, catalog, LocalCodeSource(logger), null, stateBlobCleaner) => _root = root;

            private static ITerraformCodeSourceProviderFactory LocalCodeSource(IRunnerLogger logger)
            {
                var factory = Substitute.For<ITerraformCodeSourceProviderFactory>();
                factory.GetProvider(Arg.Any<TerraformSourceType>()).Returns(new SharedFolderCodeSourceProvider(logger));
                return factory;
            }

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
                CommandArgs.Add(args);
                if (CancelCommand) throw new OperationCanceledException();
                if (FailCommand) throw new InvalidOperationException(FailureMessage);
                if (command == TerraformCommand.Init)
                {
                    var lockFile = Path.Join(workingDir, ".terraform.lock.hcl");
                    if (!File.Exists(lockFile)) File.WriteAllText(lockFile, "# provider lock");
                }
                if (command == TerraformCommand.PlanDetailedExitCode)
                {
                    if (WritePlanFile)
                        File.WriteAllText(args.Single(a => a.StartsWith("-out="))[5..], "binary plan");
                    foreach (var file in Directory.EnumerateFiles(workingDir))
                        PlannedSource[Path.GetFileName(file)] = File.ReadAllText(file);
                }
                if (command == TerraformCommand.Apply)
                {
                    AppliedSource = File.ReadAllText(Path.Join(workingDir, "main.tf"));
                    AppliedLock = File.ReadAllText(Path.Join(workingDir, ".terraform.lock.hcl"));
                }
                return Task.FromResult(command == TerraformCommand.Show ? ShowOutput : "plan output");
            }
        }

        [TestMethod]
        public async Task Program_WatchCancellationAsync_CancelsWhenMarkerAppears_OrStopsWhenCancelled()
        {
            var markerPath = Path.Join(_root, "plan.tfplan.cancel");
            using var opCts = new CancellationTokenSource();
            using var stopCts = new CancellationTokenSource();

            File.WriteAllText(markerPath, "cancel");
            await Program.WatchCancellationAsync(markerPath, opCts, stopCts.Token);
            Assert.IsTrue(opCts.IsCancellationRequested);

            var missingMarker = Path.Join(_root, "missing.cancel");
            using var opCts2 = new CancellationTokenSource();
            using var stopCts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Program.WatchCancellationAsync(missingMarker, opCts2, stopCts2.Token);
            Assert.IsFalse(opCts2.IsCancellationRequested);

            using var preStopped = new CancellationTokenSource();
            preStopped.Cancel();
            await Program.WatchCancellationAsync(missingMarker, opCts2, preStopped.Token);
            Assert.IsFalse(opCts2.IsCancellationRequested);
        }
    }
}
