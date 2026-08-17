using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
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

        private static IDeploymentCredentialSource CredentialSource()
        {
            var source = Substitute.For<IDeploymentCredentialSource>();
            source.Description.Returns("test");
            source.Resolve(Arg.Any<DeploymentTier>())
                .Returns(new DeploymentCredential("svc-dorc", "password"));
            source.Resolve(Arg.Any<DeploymentTier>(), Arg.Any<string?>())
                .Returns(new DeploymentCredential("svc-dorc", "password"));
            return source;
        }

        [TestInitialize]
        public void Setup()
        {
            _scriptRoot = Path.Join(Path.GetTempPath(), "dorc-script-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scriptRoot);
            _scriptFile = Path.Combine(_scriptRoot, "Deploy.ps1");
            File.WriteAllText(_scriptFile, "Write-Host 'deploy'");

            _pipeServer = Substitute.For<IScriptGroupPipeServer>();
            _scripts = Substitute.For<IScriptsPersistentSource>();
            _scripts.RecordContentHash(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<IPrincipal>())
                .Returns(true);

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

        /// <summary>
        /// Drives dispatch to the point where the security context is built and no further, and
        /// returns the group that was published on the way. Building that context is Windows
        /// interop against a real account, which a test host cannot satisfy.
        /// </summary>
        private ScriptGroup DispatchAndCaptureGroup(string? recordedBaseline)
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
                _scripts);

            try
            {
                dispatcher.Dispatch(
                    _scriptRoot,
                    new ScriptApiModel
                    {
                        Id = 7,
                        Path = "Deploy.ps1",
                        PowerShellVersionNumber = "7.4",
                        ContentHash = recordedBaseline
                    },
                    new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: false,
                    environmentName: "SOME-ENV",
                    executionIdentityReference: null,
                    new StringBuilder(),
                    CancellationToken.None);
            }
            catch (Exception)
            {
                // Expected: the security context cannot be built on a test host. The group has
                // been published by then, which is what is under test.
            }

            Assert.IsNotNull(published, "The script group was never published.");
            return published;
        }

        [TestMethod]
        public void CarriesTheRecordedBaselineToTheRunner()
        {
            var group = DispatchAndCaptureGroup(RecordedBaseline);

            Assert.AreEqual(RecordedBaseline, group.ScriptProperties.Single().ExpectedContentHash);
        }

        /// <summary>
        /// Carried rather than read by the runner from its own configuration. A runner reading
        /// its own would let a deployment host with a stale or edited configuration file quietly
        /// execute unverified — and the deployment host is the least trustworthy place to keep
        /// the answer.
        /// </summary>
        [TestMethod]
        [DataRow(ScriptContentVerificationMode.Report)]
        [DataRow(ScriptContentVerificationMode.Enforce)]
        [DataRow(ScriptContentVerificationMode.Off)]
        public void CarriesTheEstatesModeToTheRunner(ScriptContentVerificationMode mode)
        {
            _settings.GetScriptContentVerificationMode().Returns(mode);

            Assert.AreEqual(mode, DispatchAndCaptureGroup(RecordedBaseline).ContentVerification);
        }

        /// <summary>
        /// Trust on first use, and worth being plain about what it is: it does not establish that
        /// the script was legitimate when first seen. What it detects is change AFTER recording,
        /// which is the actual attack — and it is what makes the control adoptable without an
        /// inventory of what every script in the estate currently is.
        /// </summary>
        [TestMethod]
        public void RecordsABaselineFromTheShareWhenNoneHasBeenRecorded()
        {
            var expected = ScriptContentHash.Of(File.ReadAllBytes(_scriptFile));

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            _scripts.Received(1).RecordContentHash(7, expected, Arg.Any<IPrincipal>());
            Assert.AreEqual(expected, group.ScriptProperties.Single().ExpectedContentHash);
        }

        [TestMethod]
        public void DoesNotReRecordABaselineThatAlreadyExists()
        {
            DispatchAndCaptureGroup(RecordedBaseline);

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHash(default, default, default!);
        }

        [TestMethod]
        public void RecordsNothingAndCarriesNothingWhenVerificationIsOff()
        {
            _settings.GetScriptContentVerificationMode().Returns(ScriptContentVerificationMode.Off);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            _scripts.DidNotReceiveWithAnyArgs()
                .RecordContentHash(default, default, default!);
            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        /// <summary>
        /// A share that cannot be read at this moment is a deployment failure moments later, for
        /// its own reasons and with its own message. Failing it here would replace that message
        /// with a worse one, and baseline capture is not what the deployment is for.
        /// </summary>
        [TestMethod]
        public void DoesNotFailTheDispatchWhenTheScriptCannotBeReadToBaselineIt()
        {
            File.Delete(_scriptFile);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }

        /// <summary>
        /// If the baseline could not be stored, carrying it to the runner would mean verifying
        /// against something no later deployment will agree with — a match today and a mismatch
        /// tomorrow, for a script nobody touched.
        /// </summary>
        [TestMethod]
        public void CarriesNoBaselineWhenOneCouldNotBeRecorded()
        {
            _scripts.RecordContentHash(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<IPrincipal>())
                .Returns(false);

            var group = DispatchAndCaptureGroup(recordedBaseline: null);

            Assert.IsNull(group.ScriptProperties.Single().ExpectedContentHash);
        }
    }
}
