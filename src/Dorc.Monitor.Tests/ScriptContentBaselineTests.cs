using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Principal;
using System.Text;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// The Monitor's half of script content verification: it puts the baseline into the group the
    /// runner receives, and records one from the share when none exists yet.
    ///
    /// The split between capture here and verification in the runner is the whole point.
    /// Verifying here would be defeated by the design — dispatch happens in this process and the
    /// executed bytes are read later, from a network share, by another one, so anything checked
    /// here can be swapped in the window by exactly the actor the step exists to stop.
    ///
    /// Capturing here, on the other hand, is *better* than capturing in the runner would be. A
    /// baseline read at dispatch and verified at the point of read covers the dispatch-to-read
    /// window on the very first deployment too, where a runner recording what it was about to
    /// execute would be recording the attacker's bytes and reporting a match.
    /// </summary>
    [TestClass]
    public class ScriptContentBaselineTests
    {
        private const string RecordedBaseline =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        private string _scriptRoot = null!;
        private string _scriptFile = null!;
        private IScriptGroupPipeServer _pipeServer = null!;
        private IConfigurationSettings _settings = null!;
        private IScriptsPersistentSource _scripts = null!;
        private IPowerShellEvidenceCapture _evidenceCapture = null!;

        private static IDeploymentCredentialSource CredentialSource()
        {
            var source = Substitute.For<IDeploymentCredentialSource>();
            source.Description.Returns("test");
            source.Resolve(Arg.Any<DeploymentTier>())
                .Returns(DeploymentCredential.FromPlainText("svc-dorc", "password"));
            source.Resolve(Arg.Any<DeploymentTier>(), Arg.Any<string?>())
                .Returns(DeploymentCredential.FromPlainText("svc-dorc", "password"));
            return source;
        }

        [TestInitialize]
        public void Setup()
        {
            _scriptRoot = Path.Join(
                AppContext.BaseDirectory,
                "dorc-script-baseline-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scriptRoot);
            _scriptFile = Path.Join(_scriptRoot, "Deploy.ps1");
            File.WriteAllText(_scriptFile, "Write-Host 'deploy'", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            _pipeServer = Substitute.For<IScriptGroupPipeServer>();
            _evidenceCapture = Substitute.For<IPowerShellEvidenceCapture>();
            _scripts = Substitute.For<IScriptsPersistentSource>();
            _scripts.RecordContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(call => call.ArgAt<string>(1));
            _scripts.RecordContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(call => call.ArgAt<string>(2));

            _settings = Substitute.For<IConfigurationSettings>();
            // Empty domain: the dispatch fails at the security context, which a test host cannot
            // build. Everything under test happens before that point.
            _settings.GetConfigurationDomainNameIntra().Returns(string.Empty);
            _settings.GetScriptContentVerificationMode().Returns(ScriptContentVerificationMode.Report);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_scriptRoot))
            {
                Directory.Delete(_scriptRoot, recursive: true);
            }
        }

        private ScriptGroup DispatchAndCaptureGroup(ScriptApiModel script)
        {
            ScriptGroup? published = null;
            _pipeServer
                .When(server => server.Start(
                    Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>()))
                .Do(call => published = call.ArgAt<ScriptGroup>(1));

            var dispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ScriptDispatcher>>(),
                _pipeServer,
                _settings,
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                CredentialSource(),
                _scripts,
                _evidenceCapture);

            try
            {
                dispatcher.Dispatch(
                    _scriptRoot,
                    script,
                    new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: false,
                    environmentName: "SOME-ENV",
                    executionIdentityReference: null,
                    new RequestExecutionIdentityAdoption(),
                    new StringBuilder(),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex.Message.Contains(
                "'Domain' should be specified.",
                StringComparison.Ordinal))
            {
                // Expected: the security context cannot be built on a test host. The group has
                // been published by then, which is what is under test.
            }

            Assert.IsNotNull(published, "The script group was never published.");
            return published;
        }

        private ScriptGroup DispatchAndCaptureGroup(string? recordedBaseline) =>
            DispatchAndCaptureGroup(new ScriptApiModel
            {
                Id = 7,
                Path = _scriptFile,
                PowerShellVersionNumber = "7.4",
                Revision = "rev-1",
                ContentHash = recordedBaseline
            });

        [TestMethod]
        public void CarriesTheRecordedBaselineToTheRunner()
        {
            File.Delete(_scriptFile);

            var group = DispatchAndCaptureGroup(RecordedBaseline);

            Assert.AreEqual(RecordedBaseline, group.ScriptProperties.Single().ExpectedContentHash);
            _scripts.DidNotReceiveWithAnyArgs()
                .GetScriptsByPage(default, default, default!);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!, default!);
        }

        [TestMethod]
        [DataRow(ScriptContentVerificationMode.Report)]
        [DataRow(ScriptContentVerificationMode.Enforce)]
        [DataRow(ScriptContentVerificationMode.Off)]
        public void CarriesTheEstatesModeToTheRunner(ScriptContentVerificationMode mode)
        {
            _settings.GetScriptContentVerificationMode().Returns(mode);

            Assert.AreEqual(mode, DispatchAndCaptureGroup(RecordedBaseline).ContentVerification);
        }

        [TestMethod]
        public void RecordsABaselineFromTheSharePinnedToExpectedPathWhenNoneHasBeenRecorded()
        {
            var expected = ScriptContentHash.ComputeFromFile(_scriptFile);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            _scripts.Received(1).RecordContentHashIfUnrecorded(
                7, _scriptFile, expected, Arg.Any<IPrincipal>());
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHash(default, default, default!);
            Assert.AreEqual(expected, group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void RecordsABaselineForConfinedDoubleDotFileNameLikeDeployDotDotPs1()
        {
            var doubleDotDir = Path.Join(_scriptRoot, "sub");
            Directory.CreateDirectory(doubleDotDir);
            var doubleDotScript = Path.Join(doubleDotDir, "Deploy..ps1");
            File.WriteAllText(
                doubleDotScript,
                "Write-Host 'deploy-double-dot'",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var expected = ScriptContentHash.ComputeFromFile(doubleDotScript);

            var group = DispatchAndCaptureGroup(new ScriptApiModel
            {
                Id = 71,
                Path = doubleDotScript,
                PowerShellVersionNumber = "7.4",
                Revision = "rev-double-dot",
                ContentHash = null
            });

            _scripts.Received(1).RecordContentHashIfUnrecorded(
                71,
                doubleDotScript,
                expected,
                Arg.Any<IPrincipal>());
            Assert.AreEqual(expected, group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void DoesNotAbortDispatchInReportModeWhenLegacyPathThrowsArgumentExceptionDuringBaselineCapture()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(false);
            _settings.GetScriptContentVerificationMode().Returns(ScriptContentVerificationMode.Report);

            var group = DispatchAndCaptureGroup(new ScriptApiModel
            {
                Id = 72,
                Path = "Deploy\0.ps1",
                PowerShellVersionNumber = "7.4",
                Revision = "rev-malformed-legacy",
                ContentHash = null
            });

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!, default!);
            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void FallsBackToLiveScriptLookupOnlyForLegacyPreSnapshotRequestsWithoutScriptId()
        {
            var expected = ScriptContentHash.ComputeFromFile(_scriptFile);
            _scripts.GetScriptsByPage(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<PagedDataOperators>())
                .Returns(new GetScriptsListResponseDto
                {
                    Items = new List<ScriptApiModel>
                    {
                        new()
                        {
                            Id = 19,
                            Path = _scriptFile,
                            PowerShellVersionNumber = "7.4",
                            ContentHash = null
                        }
                    }
                });

            var group = DispatchAndCaptureGroup(new ScriptApiModel
            {
                Id = 0,
                Path = _scriptFile,
                PowerShellVersionNumber = "7.4",
                Revision = "rev-legacy",
                ContentHash = null
            });

            _scripts.Received(1).GetScriptsByPage(Arg.Any<int>(), 1, Arg.Any<PagedDataOperators>());
            _scripts.Received(1).RecordContentHashIfUnrecorded(
                19, _scriptFile, expected, Arg.Any<IPrincipal>());
            Assert.AreEqual(expected, group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void DoesNotReRecordABaselineThatAlreadyExists()
        {
            DispatchAndCaptureGroup(RecordedBaseline);

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!, default!);
        }

        [TestMethod]
        public void RecordsNothingAndCarriesNothingWhenVerificationIsOff()
        {
            _settings.GetScriptContentVerificationMode().Returns(ScriptContentVerificationMode.Off);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!, default!);
            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void DoesNotFailTheDispatchWhenTheScriptCannotBeReadToBaselineIt()
        {
            File.Delete(_scriptFile);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void CarriesNoBaselineWhenOneCouldNotBeRecorded()
        {
            _scripts.RecordContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns((string?)null);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void CarriesAConcurrentWinnersBaselineInsteadOfTheLocalCandidate()
        {
            _settings.GetScriptContentVerificationMode()
                .Returns(ScriptContentVerificationMode.Report);
            _scripts.RecordContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(RecordedBaseline);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);
            var diskHash = ScriptContentHash.ComputeFromFile(_scriptFile);

            Assert.AreEqual(
                RecordedBaseline,
                group.ScriptProperties.Single().ExpectedContentHash);
            var decision = ScriptContentGate.Evaluate(
                ScriptContentVerificationMode.Enforce,
                group.ScriptProperties.Single().ExpectedContentHash!,
                diskHash);
            Assert.IsFalse(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Mismatched, decision.Verdict);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHash(default, default, default!);
        }

        [TestMethod]
        public void EnforceMode_DoesNotAutoRecordFirstSeenBaselineFromShare()
        {
            _settings.GetScriptContentVerificationMode()
                .Returns(ScriptContentVerificationMode.Enforce);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);
            var diskHash = ScriptContentHash.ComputeFromFile(_scriptFile);

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!);
            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHashIfUnrecorded(default, default!, default!, default!);
            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);

            var decision = ScriptContentGate.Evaluate(
                group.ContentVerification,
                group.ScriptProperties.Single().ExpectedContentHash!,
                diskHash);
            Assert.IsFalse(decision.MayExecute);
            Assert.AreEqual(ScriptContentVerdict.Unrecorded, decision.Verdict);
        }

        [TestMethod]
        public void ReportMode_DoesNotReadOutsideScriptRootWhenPathConfinementIsReporting()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(false);
            _settings.GetScriptContentVerificationMode()
                .Returns(ScriptContentVerificationMode.Report);

            var outsideDir = Path.Join(
                AppContext.BaseDirectory,
                "dorc-script-outside-root",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            try
            {
                var outsideScript = Path.Join(outsideDir, "Outside.ps1");
                File.WriteAllText(outsideScript, "Write-Host 'outside'");

                var group = DispatchAndCaptureGroup(new ScriptApiModel
                {
                    Id = 77,
                    Path = outsideScript,
                    PowerShellVersionNumber = "7.4",
                    Revision = "rev-outside",
                    ContentHash = null
                });

                _scripts.DidNotReceiveWithAnyArgs()
                    .RecordContentHashIfUnrecorded(default, default!, default!);
                _scripts.DidNotReceiveWithAnyArgs()
                    .RecordContentHashIfUnrecorded(default, default!, default!, default!);
                Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
            }
            finally
            {
                if (Directory.Exists(outsideDir))
                {
                    Directory.Delete(outsideDir, recursive: true);
                }
            }
        }

        [TestMethod]
        public void UnexpectedBaselinePersistenceFailuresAreNotHidden()
        {
            _scripts.RecordContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IPrincipal>())
                .Returns(_ => throw new InvalidOperationException("Database unavailable."));

            Assert.ThrowsExactly<InvalidOperationException>(
                () => DispatchAndCaptureGroup(recordedBaseline: null));
        }

        [TestMethod]
        public void MonitorDependencyInjectionResolvesScriptDispatcherWithPersistentSourcesRegistry()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:ScriptContentVerification"] = "Report",
                    ["AppSettings:DeploymentCredentialsFromVault"] = "False"
                })
                .Build();

            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IConfigurationRoot>(configuration);
            services.AddSingleton(Substitute.For<IPropertyEncryptor>());
            services.AddSingleton(Substitute.For<IDeploymentContextFactory>());
            services.AddSingleton(Substitute.For<IClaimsPrincipalReader>());
            services.AddSingleton(Substitute.For<IScriptGroupPipeServer>());
            services.AddSingleton(Substitute.For<IDeploymentCredentialSource>());
            PersistentSourcesRegistry.Register(services);
            services.AddTransient<IConfigurationSettings>(
                sp => new ConfigurationSettings(sp.GetService<IConfiguration>() ?? configuration));
            services.AddTransient<IScriptDispatcher, ScriptDispatcher>();

            using var provider = services.BuildServiceProvider();
            Assert.IsNotNull(provider.GetRequiredService<IScriptsPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<IScriptDispatcher>());
        }
    }
}
