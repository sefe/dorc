using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text;
using System.Text.Json;

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
                _scriptScopeConfigValues,
                Substitute.For<IPowerShellEvidenceCapture>());

        /// <summary>
        /// Dispatch is driven to the point where the Runner's security context is built and no
        /// further. Building it is Windows interop against a real account, which a test host
        /// cannot satisfy - so the credential is deliberately incomplete and the resulting
        /// failure is the vehicle for observing what happened before it.
        ///
        /// The exception TYPE is not asserted on purpose. Which one surfaces depends on how
        /// far the host gets before refusing, and on what ProcessSecurityContextBuilder
        /// happens to throw today; pinning it would make these tests fail for reasons that
        /// have nothing to do with the bundle lifetime they exist to cover. Assert.Throws
        /// accepts any derived type, so the indifference is expressed without a catch-all
        /// clause that would swallow an assertion failure with it.
        /// </summary>
        private static Exception DispatchExpectingFailure(ScriptDispatcher dispatcher, bool isProduction)
        {
            return Assert.Throws<Exception>(
                () => dispatcher.Dispatch(
                    @"\\host\share\Scripts",
                    new ScriptApiModel { Path = "Deploy.ps1", PowerShellVersionNumber = "7.4" },
                    new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: isProduction,
                    environmentName: "SOME-ENV",
                    new StringBuilder(),
                    CancellationToken.None),
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
        private const string BuiltInWithheld = "DORC_ProdDeployPassword";
        private const string OperatorConfiguredWithheld = "OperatorOnlySigningPassword";

        private static ScriptScopeConfigValues CreateWithholdingPolicy(params string[] configuredKeys)
        {
            var values = new Dictionary<string, string?>();
            for (var i = 0; i < configuredKeys.Length; i++)
            {
                values[$"{ScriptScopeConfigValues.WithheldKeysSetting}:{i}"] = configuredKeys[i];
            }

            return new ScriptScopeConfigValues(
                new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }

        [TestMethod]
        public void TheDispatchedScriptGroupCarriesNoWithheldKey()
        {
            var pipeServer = Substitute.For<IScriptGroupPipeServer>();

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-dorc");
            configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("password");

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            var scopeValues = CreateWithholdingPolicy(BuiltInWithheld, OperatorConfiguredWithheld);

            var dispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                pipeServer,
                settings,
                Substitute.For<IRequestsPersistentSource>(),
                scopeValues,
                Substitute.For<IPowerShellEvidenceCapture>());

            var properties = new Dictionary<string, VariableValue>
            {
                [BuiltInWithheld] = new() { Value = "the-credential", Type = typeof(string) },
                [OperatorConfiguredWithheld] = new() { Value = "operator-credential", Type = typeof(string) },
                ["EnvironmentName"] = new() { Value = "SOME-ENV", Type = typeof(string) }
            };

            // Fails once it reaches the security context, which a test host cannot build. The
            // script group has been published by then, which is what is under test.
            Assert.Throws<Exception>(() => dispatcher.Dispatch(
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
                    !group.CommonProperties.ContainsKey(BuiltInWithheld)
                    && !group.CommonProperties.ContainsKey(OperatorConfiguredWithheld)
                    && group.CommonProperties.ContainsKey("EnvironmentName")),
                Arg.Any<ScriptGroupReaderIdentity>(),
                Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public void JsonScriptReferencingWithheldKeyIsRefusedBeforeDispatch()
        {
            var pipeServer = Substitute.For<IScriptGroupPipeServer>();
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-dorc");
            configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("password");

            var dispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                pipeServer,
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IRequestsPersistentSource>(),
                CreateWithholdingPolicy(BuiltInWithheld),
                Substitute.For<IPowerShellEvidenceCapture>());

            var jsonPath = JsonSerializer.Serialize(new JSONPath
            {
                ScriptPath = "Deploy.ps1",
                GenericArguments = new List<IDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["SecretParam"] = $"${{{BuiltInWithheld}}}" }
                }
            });
            var properties = new Dictionary<string, VariableValue>
            {
                [BuiltInWithheld] = new() { Value = "the-credential", Type = typeof(string) }
            };

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => dispatcher.Dispatch(
                @"\\host\share\Scripts",
                new ScriptApiModel { Path = jsonPath, IsPathJSON = true, PowerShellVersionNumber = "7.4" },
                properties,
                requestId: 42,
                deploymentRequestId: 42,
                isProduction: true,
                environmentName: "SOME-ENV",
                new StringBuilder(),
                CancellationToken.None));

            StringAssert.Contains(ex.Message, "unavailable");
            pipeServer.DidNotReceiveWithAnyArgs().Start(default!, default!, default!, default);
        }

        [TestMethod]
        public void JsonScriptGenericArgumentsWithWithheldKeysAreStrippedFromScriptProperties()
        {
            const string literalProdSecret = "literal-prod-secret-value";
            const string literalOperatorSecret = "literal-operator-secret-value";

            var pipeServer = Substitute.For<IScriptGroupPipeServer>();
            ScriptGroup? capturedGroup = null;
            pipeServer
                .When(server => server.Start(
                    Arg.Any<string>(),
                    Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(),
                    Arg.Any<CancellationToken>()))
                .Do(call => capturedGroup = call.ArgAt<ScriptGroup>(1));

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-dorc");
            configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("password");

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            var logger = new RecordingLogger<ScriptDispatcher>();

            var dispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                configValues,
                logger,
                pipeServer,
                settings,
                Substitute.For<IRequestsPersistentSource>(),
                CreateWithholdingPolicy(BuiltInWithheld, OperatorConfiguredWithheld),
                Substitute.For<IPowerShellEvidenceCapture>());

            var jsonPath = JsonSerializer.Serialize(new JSONPath
            {
                ScriptPath = "Deploy.ps1",
                GenericArguments = new List<IDictionary<string, string>>
                {
                    new Dictionary<string, string> { [BuiltInWithheld] = literalProdSecret },
                    new Dictionary<string, string> { [OperatorConfiguredWithheld] = literalOperatorSecret },
                    new Dictionary<string, string> { ["DORC_WebDeployPassword"] = "${EnvironmentName}" },
                    new Dictionary<string, string> { ["LegitScriptArg"] = "allowed-arg-value" }
                }
            });
            var properties = new Dictionary<string, VariableValue>
            {
                ["EnvironmentName"] = new() { Value = "SOME-ENV", Type = typeof(string) }
            };

            var thrown = Assert.Throws<Exception>(() => dispatcher.Dispatch(
                @"\\host\share\Scripts",
                new ScriptApiModel { Path = jsonPath, IsPathJSON = true, PowerShellVersionNumber = "7.4" },
                properties,
                requestId: 42,
                deploymentRequestId: 42,
                isProduction: true,
                environmentName: "SOME-ENV",
                new StringBuilder(),
                CancellationToken.None));

            Assert.IsNotNull(capturedGroup, $"Expected ScriptGroup to be published before failure, but Dispatch threw early: {thrown}");
            Assert.AreEqual(1, capturedGroup.ScriptProperties.Count);
            var scriptProperties = capturedGroup.ScriptProperties.Single().Properties;
            Assert.IsFalse(scriptProperties.ContainsKey(BuiltInWithheld));
            Assert.IsFalse(scriptProperties.ContainsKey(OperatorConfiguredWithheld));
            Assert.IsFalse(scriptProperties.ContainsKey("DORC_WebDeployPassword"));
            Assert.IsTrue(scriptProperties.TryGetValue("LegitScriptArg", out var legitProp));
            Assert.AreEqual("allowed-arg-value", legitProp.Value?.ToString());

            var serializedGroup = JsonSerializer.Serialize(capturedGroup);
            Assert.IsFalse(serializedGroup.Contains(BuiltInWithheld, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serializedGroup.Contains(OperatorConfiguredWithheld, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serializedGroup.Contains("DORC_WebDeployPassword", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serializedGroup.Contains(literalProdSecret, StringComparison.Ordinal));
            Assert.IsFalse(serializedGroup.Contains(literalOperatorSecret, StringComparison.Ordinal));

            Assert.IsTrue(logger.Entries.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(BuiltInWithheld, StringComparison.Ordinal)
                && entry.Message.Contains(OperatorConfiguredWithheld, StringComparison.Ordinal)
                && entry.Message.Contains("DORC_WebDeployPassword", StringComparison.Ordinal)));
            Assert.IsFalse(logger.Entries.Any(entry =>
                entry.Message.Contains(literalProdSecret, StringComparison.Ordinal)
                || entry.Message.Contains(literalOperatorSecret, StringComparison.Ordinal)));
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }

        [TestMethod]
        public void TerraformScriptGroupStripsWithheldKeysFromPropertiesAndCatalogInputs()
        {
            const int requestId = 77;
            const int componentId = 19;

            var requests = Substitute.For<IRequestsPersistentSource>();
            var requestDetails = new DeploymentRequestDetailSerializer().Serialize(new DeploymentRequestDetail
            {
                EnvironmentName = "SOME-ENV",
                Components = new List<string> { "CatalogComp" },
                Properties = new List<PropertyPair>()
            });
            var request = new DeploymentRequestApiModel
            {
                Id = requestId,
                Project = "Infra",
                EnvironmentName = "SOME-ENV",
                RequestDetails = requestDetails
            };
            requests.GetCatalogRequestInputs(requestId, componentId).Returns(new CatalogRequestInputs
            {
                DeploymentRequestId = requestId,
                ComponentId = componentId,
                TemplateName = "storage",
                TemplateVersion = "1.0.0",
                InputsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [BuiltInWithheld] = "smuggled-via-catalog-builtin",
                    [OperatorConfiguredWithheld] = "smuggled-via-catalog-custom",
                    ["account_name"] = "legitstorage"
                })
            });

            var propertiesSource = Substitute.For<IPropertiesPersistentSource>();
            propertiesSource.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            var hostAllowList = new SourceHostAllowList(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{SourceHostAllowList.TerraformHostsSetting}:0"] = "github.corp.example.com"
                    })
                    .Build());

            var scopeValues = new ScriptScopeConfigValues(new ConfigurationBuilder().Build());
            var configuredScopeValues = CreateWithholdingPolicy(
                BuiltInWithheld,
                "DORC_WebDeployPassword",
                OperatorConfiguredWithheld);

            Assert.IsTrue(scopeValues.IsWithheld(BuiltInWithheld));

            var dispatcher = new TerraformDispatcher(
                NullLogger<TerraformDispatcher>.Instance,
                requests,
                configValues,
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IAzureStorageAccountWorker>(),
                Substitute.For<IProjectsPersistentSource>(),
                Substitute.For<IGitHubHostValidator>(),
                Substitute.For<ICloudResourcesPersistentSource>(),
                Substitute.For<ICloudResourceAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDatabasesPersistentSource>(),
                propertiesSource,
                configuredScopeValues,
                hostAllowList);

            var component = new ComponentApiModel
            {
                ComponentId = componentId,
                ComponentName = "CatalogComp",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog
            };
            var inheritedProperties = new Dictionary<string, VariableValue>
            {
                ["DORC_WebDeployPassword"] = new() { Value = "smuggled-via-properties", Type = typeof(string) },
                ["EnvironmentName"] = new() { Value = "SOME-ENV", Type = typeof(string) }
            };

            var assembledProperties = dispatcher.ApplyCatalogInputs(component, inheritedProperties, requestId, request);
            Assert.IsTrue(assembledProperties.ContainsKey(BuiltInWithheld),
                "Precondition: Catalog snapshot merged the withheld key before GetScriptGroup runs.");
            Assert.IsTrue(assembledProperties.ContainsKey(OperatorConfiguredWithheld),
                "Precondition: Catalog snapshot merged the operator-configured withheld key before GetScriptGroup runs.");

            var group = dispatcher.GetScriptGroup(
                string.Empty,
                assembledProperties,
                deploymentResultId: 901,
                component,
                request,
                new ProjectApiModel { ProjectId = 5, ProjectName = "Infra" });

            Assert.IsFalse(group.CommonProperties.ContainsKey(BuiltInWithheld));
            Assert.IsFalse(group.CommonProperties.ContainsKey("DORC_WebDeployPassword"));
            Assert.IsFalse(group.CommonProperties.ContainsKey(OperatorConfiguredWithheld));
            Assert.AreEqual("legitstorage", group.CommonProperties["account_name"].Value);
            Assert.AreEqual("SOME-ENV", group.CommonProperties["EnvironmentName"].Value);
        }
    }
}
