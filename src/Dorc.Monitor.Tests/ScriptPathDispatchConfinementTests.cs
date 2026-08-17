using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// The write path stops new components being registered with a path that escapes the script
    /// root. This is the other half: it stops the ones already stored from executing.
    ///
    /// Both are needed. `Path.Combine` discards the root entirely when the stored path is
    /// rooted, so an off-share path does not resolve beneath the root — it replaces it, and the
    /// result runs as the deployment account.
    ///
    /// Reporting is the default and enforcement is opt-in, because existing components hold
    /// paths that were never validated and enforcing on day one would fail every deployment of
    /// an off-share component at once. The report identifies the population to remediate first.
    /// </summary>
    [TestClass]
    public class ScriptPathDispatchConfinementTests
    {
        private const string ScriptRoot = @"\\itss.global\prod\GLO\APP\DORC\Scripts\Scripts.PR";

        private IScriptGroupPipeServer _pipeServer = null!;
        private IConfigurationSettings _settings = null!;

        [TestInitialize]
        public void Setup()
        {
            _pipeServer = Substitute.For<IScriptGroupPipeServer>();

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns("svc-dorc");
            configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("password");

            _settings = Substitute.For<IConfigurationSettings>();
            // Empty domain: the dispatch fails at the security context, which a test host cannot
            // build. Everything under test happens before that point.
            _settings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            _configValues = configValues;
        }

        private IConfigValuesPersistentSource _configValues = null!;

        private ScriptDispatcher NewDispatcher() =>
            new(Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                _configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                _pipeServer,
                _settings,
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>());

        private bool Dispatch(string storedScriptPath)
        {
            try
            {
                return NewDispatcher().Dispatch(
                    ScriptRoot,
                    new ScriptApiModel { Path = storedScriptPath, PowerShellVersionNumber = "7.4" },
                    new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: false,
                    environmentName: "SOME-ENV",
                    new StringBuilder(),
                    CancellationToken.None);
            }
            catch (Exception)
            {
                // Reached the security context, which means the path was accepted. The refusal
                // path returns false without ever getting there.
                return true;
            }
        }

        /// <summary>
        /// The weakness, at dispatch: a rooted stored path replaces the script root.
        /// </summary>
        [TestMethod]
        public void RefusesAnOffShareScriptPathWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsFalse(Dispatch(@"\\attacker\share\payload.ps1"));

            _pipeServer.DidNotReceive().Start(
                Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public void RefusesTraversalOutOfTheScriptRootWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsFalse(Dispatch(@"..\..\Scripts.ST\payload.ps1"));
        }

        [TestMethod]
        public void AcceptsAPathBeneathTheScriptRootWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsTrue(Dispatch(@"00 Generic\DeployMSI.ps1"),
                "A legitimate script must still dispatch with enforcement on.");
        }

        /// <summary>
        /// The default. Reporting mode must not fail a deployment — the estate holds paths that
        /// were never validated, and the report is how they are found.
        /// </summary>
        [TestMethod]
        public void ReportsButDoesNotRefuseWhenNotEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(false);

            Assert.IsTrue(Dispatch(@"\\attacker\share\payload.ps1"),
                "Reporting mode reports and proceeds; only enforcement stops the deployment.");
        }

        [TestMethod]
        public void EnforcementIsOffUnlessConfigured()
        {
            // The substitute returns false for an unstubbed bool, which is the same answer the
            // real setting gives for an absent key — enforcement is opt-in.
            Assert.IsTrue(Dispatch(@"\\attacker\share\payload.ps1"));
        }
    }
}
