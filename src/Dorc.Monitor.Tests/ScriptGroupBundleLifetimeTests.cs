using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Configuration;
using Dorc.Monitor;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// The script group handed to a Runner carries every resolved deployment property,
    /// secrets included. Two things follow, and neither held before.
    ///
    /// It must reach the account the Runner is started as - which is resolved per deployment
    /// from the environment's production flag, not the account the Monitor happens to run
    /// under. Granting only the Monitor's own identity worked solely because the two coincide
    /// in the current deployment topology.
    ///
    /// And it must not outlive that Runner. Expiry belongs on the failure path as much as the
    /// success path: a deployment that fails is exactly when a host gets looked at by someone
    /// who is not the deployment account.
    /// </summary>
    [TestClass]
    public class ScriptGroupBundleLifetimeTests
    {
        private const string Domain = "CORP";
        private const string ProdAccount = "svc-dorc-prod";
        private const string NonProdAccount = "svc-dorc-nonprod";

        private IScriptGroupPipeServer _pipeServer = null!;
        private IConfigValuesPersistentSource _configValues = null!;
        private IConfigurationSettings _configurationSettings = null!;
        private IScriptScopeConfigValues _scriptScopeConfigValues = null!;

        [TestInitialize]
        public void Setup()
        {
            _pipeServer = Substitute.For<IScriptGroupPipeServer>();

            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _configValues.GetConfigValue("DORC_ProdDeployUsername").Returns(ProdAccount);
            _configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("prod-password");
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns(NonProdAccount);
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("nonprod-password");

            _configurationSettings = Substitute.For<IConfigurationSettings>();
            _scriptScopeConfigValues = Substitute.For<IScriptScopeConfigValues>();
            _configurationSettings.GetConfigurationDomainNameIntra().Returns(Domain);
        }

        private ScriptDispatcher NewDispatcher() =>
            new(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                _configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                _pipeServer,
                _configurationSettings,
                Substitute.For<IRequestsPersistentSource>(),
                _scriptScopeConfigValues);

        /// <summary>
        /// Dispatch is driven to the point where the Runner's security context is built and no
        /// further. Building it is Windows interop against a real account, which a test host
        /// cannot satisfy - so the credential is deliberately incomplete and the resulting
        /// failure is the vehicle for observing what happened before it.
        ///
        /// The exception TYPE is not asserted on purpose. Which one surfaces depends on how
        /// far the host gets before refusing, and on what ProcessSecurityContextBuilder
        /// happens to throw today; pinning it would make these tests fail for reasons that
        /// have nothing to do with the bundle lifetime they exist to cover.
        /// </summary>
        private static Exception DispatchExpectingFailure(ScriptDispatcher dispatcher, bool isProduction)
        {
            try
            {
                dispatcher.Dispatch(
                    @"\\host\share\Scripts",
                    new ScriptApiModel { Path = "Deploy.ps1", PowerShellVersionNumber = "7.4" },
                    new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: isProduction,
                    environmentName: "SOME-ENV",
                    new StringBuilder(),
                    CancellationToken.None);
            }
            catch (Exception dispatchFailure)
            {
                return dispatchFailure;
            }

            throw new AssertFailedException(
                "Dispatch was expected to fail while building the Runner's security context. If it now" +
                " succeeds on this host, these tests are no longer observing what they claim to.");
        }

        [TestMethod]
        [DataRow(true, ProdAccount)]
        [DataRow(false, NonProdAccount)]
        public void TheBundleIsMadeReadableByTheAccountTheRunnerWillBeStartedAs(bool isProduction, string expectedAccount)
        {
            // No domain: ProcessSecurityContextBuilder refuses an anonymous context, which
            // fails the dispatch after the bundle has been published but before any interop.
            _configurationSettings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            DispatchExpectingFailure(NewDispatcher(), isProduction);

            _pipeServer.Received(1).Start(
                Arg.Any<string>(),
                Arg.Any<ScriptGroup>(),
                Arg.Is<ScriptGroupReaderIdentity>(identity => identity.UserName == expectedAccount),
                Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public void TheReaderIdentityCarriesTheConfiguredDomain()
        {
            // With a domain present the dispatch fails later, in the logon interop itself. The
            // identity has still been handed over by then, which is what is under test.
            DispatchExpectingFailure(NewDispatcher(), isProduction: true);

            _pipeServer.Received(1).Start(
                Arg.Any<string>(),
                Arg.Any<ScriptGroup>(),
                Arg.Is<ScriptGroupReaderIdentity>(identity =>
                    identity.QualifiedAccountName == $@"{Domain}\{ProdAccount}"),
                Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public void TheBundleIsExpiredWhenTheDispatchFails()
        {
            _configurationSettings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            DispatchExpectingFailure(NewDispatcher(), isProduction: true);

            _pipeServer.Received(1).Expire(Arg.Any<string>());
        }

        [TestMethod]
        public void TheExpiredBundleIsTheOneThatWasPublished()
        {
            _configurationSettings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            string? published = null;
            _pipeServer
                .When(server => server.Start(
                    Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>()))
                .Do(call => published = call.ArgAt<string>(0));

            DispatchExpectingFailure(NewDispatcher(), isProduction: true);

            Assert.IsNotNull(published);
            _pipeServer.Received(1).Expire(published);
        }
    }

    [TestClass]
    public class ScriptGroupReaderIdentityTests
    {
        [TestMethod]
        public void QualifiesABareUserNameWithTheConfiguredDomain()
        {
            Assert.AreEqual(@"CORP\svc-dorc",
                new ScriptGroupReaderIdentity("CORP", "svc-dorc").QualifiedAccountName);
        }

        /// <summary>
        /// A configured user name that already names its own domain must be used as it stands.
        /// Prefixing it again would resolve to a different account - or to nothing - and the
        /// bundle would be granted to the wrong principal or fail to be granted at all.
        /// </summary>
        [TestMethod]
        [DataRow(@"OTHER\svc-dorc")]
        [DataRow("svc-dorc@other.example.com")]
        public void LeavesAnAlreadyQualifiedUserNameAlone(string userName)
        {
            Assert.AreEqual(userName, new ScriptGroupReaderIdentity("CORP", userName).QualifiedAccountName);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void FallsBackToTheBareUserNameWithoutADomain(string? domain)
        {
            Assert.AreEqual("svc-dorc", new ScriptGroupReaderIdentity(domain, "svc-dorc").QualifiedAccountName);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void RefusesToNameNoAccountAtAll(string? userName)
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ScriptGroupReaderIdentity("CORP", userName!));
        }
    }
    /// <summary>
    /// The withheld keys are already excluded where configuration values become properties.
    /// This is the second gate: the script group is the artefact that crosses the process
    /// boundary into a Runner, so the invariant has to hold there rather than be assumed to
    /// have held upstream. A property reaching the set by another route - an environment
    /// property, or a request-supplied value bearing the same name - would otherwise carry
    /// DOrc's own operating credential into script scope after all.
    /// </summary>
    [TestClass]
    public class WithheldKeysAreNotDispatchedTests
    {
        private const string Withheld = "DORC_ProdDeployPassword";

        [TestMethod]
        public void TheDispatchedScriptGroupCarriesNoWithheldKey()
        {
            var pipeServer = Substitute.For<IScriptGroupPipeServer>();

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-dorc");
            configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("password");

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            var scopeValues = Substitute.For<IScriptScopeConfigValues>();
            scopeValues.IsWithheld(Withheld).Returns(true);

            var dispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                pipeServer,
                settings,
                Substitute.For<IRequestsPersistentSource>(),
                scopeValues);

            var properties = new Dictionary<string, VariableValue>
            {
                [Withheld] = new() { Value = "the-credential", Type = typeof(string) },
                ["EnvironmentName"] = new() { Value = "SOME-ENV", Type = typeof(string) }
            };

            // Fails once it reaches the security context, which a test host cannot build. The
            // script group has been published by then, which is what is under test.
            Assert.ThrowsExactly<Exception>(() => dispatcher.Dispatch(
                @"\\host\share\Scripts",
                new ScriptApiModel { Path = "Deploy.ps1", PowerShellVersionNumber = "7.4" },
                properties,
                requestId: 42,
                deploymentRequestId: 42,
                isProduction: true,
                environmentName: "SOME-ENV",
                new StringBuilder(),
                CancellationToken.None));

            pipeServer.Received(1).Start(
                Arg.Any<string>(),
                Arg.Is<ScriptGroup>(group =>
                    !group.CommonProperties.ContainsKey(Withheld)
                    && group.CommonProperties.ContainsKey("EnvironmentName")),
                Arg.Any<ScriptGroupReaderIdentity>(),
                Arg.Any<CancellationToken>());
        }
    }
}
