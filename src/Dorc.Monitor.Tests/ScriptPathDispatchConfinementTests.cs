using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
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
        private IConfigValuesPersistentSource _configValues = null!;
        private IPowerShellEvidenceCapture _evidenceCapture = null!;
        private RecordingLogger<ScriptDispatcher> _scriptLogger = null!;

        private readonly IDeploymentCredentialSource _credentialSource =
            CredentialSourceFor("svc-dorc", "password");

        /// <summary>
        /// A credential source that resolves, so these tests exercise what they are about rather
        /// than the missing-credential path.
        /// </summary>
        private static IDeploymentCredentialSource CredentialSourceFor(string userName, string password)
        {
            var source = Substitute.For<IDeploymentCredentialSource>();
            source.Description.Returns("test");
            // Both overloads: the dispatchers call the environment-keyed one, and NSubstitute
            // treats them as separate members.
            source.Resolve(Arg.Any<DeploymentTier>())
                .Returns(_ => DeploymentCredential.FromPlainText(userName, password));
            source.Resolve(Arg.Any<DeploymentTier>(), Arg.Any<string?>())
                .Returns(_ => DeploymentCredential.FromPlainText(userName, password));
            return source;
        }

        [TestInitialize]
        public void Setup()
        {
            _pipeServer = Substitute.For<IScriptGroupPipeServer>();
            _pipeServer.Start(
                    Arg.Any<string>(),
                    Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => throw new DispatchAcceptedException());

            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _configValues.GetConfigValue("ScriptRoot").Returns(ScriptRoot);
            _configValues.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(ScriptRoot);

            _settings = Substitute.For<IConfigurationSettings>();
            _settings.GetConfigurationDomainNameIntra().Returns(string.Empty);

            _evidenceCapture = Substitute.For<IPowerShellEvidenceCapture>();
            _scriptLogger = new RecordingLogger<ScriptDispatcher>();
        }

        private ScriptDispatcher NewDispatcher(IRequestsPersistentSource? requests = null) =>
            new(Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                _configValues,
                _scriptLogger,
                _pipeServer,
                _settings,
                requests ?? Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                _credentialSource,
                Substitute.For<IScriptsPersistentSource>(),
                _evidenceCapture);

        private bool Dispatch(
            string storedScriptPath,
            bool isPathJson = false,
            IDictionary<string, VariableValue>? properties = null)
        {
            try
            {
                return NewDispatcher().Dispatch(
                    ScriptRoot,
                    new ScriptApiModel
                    {
                        Path = storedScriptPath,
                        IsPathJSON = isPathJson,
                        PowerShellVersionNumber = "7.4",
                        Revision = "rev-1"
                    },
                    properties ?? new Dictionary<string, VariableValue>(),
                    requestId: 42,
                    deploymentRequestId: 42,
                    isProduction: false,
                    environmentName: "SOME-ENV",
                    executionIdentityReference: null,
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    new StringBuilder(),
                    CancellationToken.None);
            }
            catch (DispatchAcceptedException)
            {
                // Reaching the pipe proves the path passed confinement gates.
                return true;
            }
        }

        private sealed class DispatchAcceptedException : Exception
        {
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
        public void RefusesAnOffShareScriptPathWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsFalse(Dispatch(@"\\attacker\share\payload.ps1"));

            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
            _pipeServer.DidNotReceive().Start(
                Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
            Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Error));
        }

        [TestMethod]
        [DataRow(@"..\..\Scripts.ST\payload.ps1")]
        [DataRow(@"%2e%2e\Scripts.ST\payload.ps1")]
        [DataRow(@"%252e%252e\Scripts.ST\payload.ps1")]
        [DataRow(@"%25252e%25252e\Scripts.ST\payload.ps1")]
        [DataRow(@"%2525252e%2525252e\Scripts.ST\payload.ps1")]
        [DataRow(@"%252525252e%252525252e\Scripts.ST\payload.ps1")]
        [DataRow("file://itss.global/prod/GLO/APP/DORC/Scripts/Scripts.PR/%2e%2e/Scripts.ST/payload.ps1")]
        [DataRow(@"\\?\UNC\attacker\share\payload.ps1")]
        [DataRow(@"\\itss.global@SSL@443\prod\GLO\APP\DORC\Scripts\Scripts.PR\payload.ps1")]
        public void RefusesTraversalPercentEncodingAndExtendedOrWebDavUncWhenEnforcing(string badPath)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsFalse(Dispatch(badPath));
            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
        }

        [TestMethod]
        public void AcceptsAPathBeneathTheScriptRootWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsTrue(Dispatch(@"00 Generic\DeployMSI.ps1"),
                "A legitimate script must still dispatch with enforcement on.");
        }

        [TestMethod]
        public void AcceptsAConfinedJsonScriptPathWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsTrue(Dispatch(
                """{"ScriptPath":"00 Generic\\DeployMSI.ps1","GenericArguments":[]}""",
                isPathJson: true));
        }

        [TestMethod]
        [DataRow("""{"ScriptPath":"\\\\attacker\\share\\payload.ps1","GenericArguments":[]}""")]
        [DataRow("""{"ScriptPath":"..\\..\\Scripts.ST\\payload.ps1"}""")]
        [DataRow("""{"ScriptPath":"%2e%2e\\Scripts.ST\\payload.ps1"}""")]
        [DataRow("{not-valid-json")]
        public void RefusesUnconfinedOrMalformedJsonScriptPathBeforePreparingExecutionWhenEnforcing(string jsonPath)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            Assert.IsFalse(Dispatch(jsonPath, isPathJson: true));
            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
            _pipeServer.DidNotReceive().Start(
                Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        [DataRow(@"$DropFolder\payload.ps1", @"\\builds.corp\drops\build-99", false)]
        [DataRow(@"${DropFolder}\payload.ps1", @"\\builds.corp\drops\build-99", false)]
        [DataRow(@"$DropFolder$\payload.ps1", @"\\builds.corp\drops\build-99", false)]
        [DataRow(@"$DropFolder\..\..\..\Scripts.ST\escape.ps1", ScriptRoot + @"\Drops\build-99", false)]
        [DataRow("""{"ScriptPath":"$DropFolder\\..\\..\\..\\Scripts.ST\\escape.ps1","GenericArguments":[]}""", ScriptRoot + @"\Drops\build-99", true)]
        public void RefusesDropFolderAndVariableExpansionEscapesWhenEnforcingAndWarnsInReportMode(
            string scriptPath,
            string dropFolder,
            bool isPathJson)
        {
            var props = new Dictionary<string, VariableValue>
            {
                ["DropFolder"] = new() { Value = dropFolder, Type = typeof(string) }
            };

            _settings.GetScriptPathEnforcementEnabled().Returns(true);
            Assert.IsFalse(Dispatch(scriptPath, isPathJson, props));
            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);

            _scriptLogger.Entries.Clear();
            _settings.GetScriptPathEnforcementEnabled().Returns(false);
            Assert.IsTrue(Dispatch(scriptPath, isPathJson, props));
            Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Warning));
        }

        [TestMethod]
        public void RefusesPropertyInjectedRelativeTraversalWhenEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);
            var props = new Dictionary<string, VariableValue>
            {
                ["ModuleDir"] = new() { Value = @"..\..\Scripts.ST", Type = typeof(string) }
            };

            Assert.IsFalse(Dispatch(@"$ModuleDir\payload.ps1", isPathJson: false, props));
            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
        }

        [TestMethod]
        public void ReportsButDoesNotRefuseWhenNotEnforcing()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(false);

            Assert.IsTrue(Dispatch(@"\\attacker\share\payload.ps1"),
                "Reporting mode reports and proceeds; only enforcement stops the deployment.");
            Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Warning));
        }

        [TestMethod]
        public void EnforcementIsOffUnlessConfigured()
        {
            Assert.IsTrue(Dispatch(@"\\attacker\share\payload.ps1"));
        }

        [TestMethod]
        [DataRow(false, @"\\attacker\share\frozen.ps1", false)]
        [DataRow(true, @"\\attacker\share\frozen.ps1", false)]
        [DataRow(false, """{"ScriptPath":"\\\\attacker\\share\\frozen-json.ps1","GenericArguments":[]}""", true)]
        [DataRow(true, """{"ScriptPath":"\\\\attacker\\share\\frozen-json.ps1","GenericArguments":[]}""", true)]
        public void ComponentProcessorChecksFrozenSubmittedPowerShellSnapshotRatherThanLiveRow(
            bool enforce,
            string frozenPath,
            bool isPathJson)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(enforce);

            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(
                    Arg.Any<DeploymentResultApiModel>(),
                    Arg.Any<DeploymentResultStatus>(),
                    Arg.Any<DeploymentResultStatus>())
                .Returns(true);
            // Frozen snapshot holds an off-share path even though live component row looks remediated.
            requests.GetSubmittedScript(77, 15).Returns(new ScriptApiModel
            {
                Id = 9,
                Path = frozenPath,
                IsPathJSON = isPathJson,
                PowerShellVersionNumber = "7.4",
                Revision = "frozen-rev-1",
                NonProdOnly = false
            });

            var eventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            eventsPublisher.PublishResultStatusChangedAsync(Arg.Any<Dorc.Core.Events.DeploymentResultEventData>())
                .Returns(Task.CompletedTask);

            var dispatcher = NewDispatcher(requests);
            var processor = new ComponentProcessor(
                dispatcher,
                Substitute.For<ITerraformDispatcher>(),
                requests,
                Substitute.For<IComponentsPersistentSource>(),
                eventsPublisher,
                _configValues,
                Substitute.For<ILogger<ComponentProcessor>>());

            var liveLookingComponent = new ComponentApiModel
            {
                ComponentId = 15,
                ComponentName = "PS-Comp",
                ComponentType = ComponentType.PowerShell,
                ScriptPath = @"00 Generic\SafeLivePath.ps1"
            };
            var deploymentResult = new DeploymentResultApiModel
            {
                Id = 501,
                RequestId = 77,
                ComponentId = 15,
                Status = DeploymentResultStatus.Pending.ToString()
            };

            var dispatchAccepted = false;
            try
            {
                processor.DeployComponent(
                    liveLookingComponent,
                    deploymentResult,
                    requestId: 77,
                    isProductionRequest: false,
                    environmentId: 3,
                    isProductionEnvironment: false,
                    environmentName: "DEV",
                    executionIdentityReference: null,
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    scriptRoot: ScriptRoot,
                    commonProperties: new Dictionary<string, VariableValue>(),
                    CancellationToken.None);
            }
            catch (DispatchAcceptedException) when (!enforce)
            {
                dispatchAccepted = true;
            }

            if (enforce)
            {
                Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Error));
                _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
                _pipeServer.DidNotReceive().Start(
                    Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
                requests.Received().UpdateResultStatus(
                    deploymentResult,
                    Arg.Is<DeploymentResultStatus>(s => s.Equals(DeploymentResultStatus.Failed)));
            }
            else
            {
                Assert.IsTrue(dispatchAccepted);
                Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Warning));
                _evidenceCapture.Received(1).Prepare(
                    Arg.Any<ScriptGroup>(), "frozen-rev-1", 77, 0, Arg.Any<string>());
                _pipeServer.Received(1).Start(
                    Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
            }
        }

        [TestMethod]
        [DataRow(false, @"\\files.corp.example.com\tf\team-b\network", true)]
        [DataRow(true, @"\\files.corp.example.com\tf\team-b\network", false)]
        [DataRow(true, @"\\files.corp.example.com\tf\team-a\..\team-b\network", false)]
        [DataRow(true, "file://files.corp.example.com/tf/team-a/%2e%2e/team-b/network", false)]
        [DataRow(true, @"\\files.corp.example.com\tf\team-a\network", true)]
        public void TerraformDispatcherChecksFrozenSharedFolderSnapshotAgainstAllowedRoots(
            bool enforce,
            string frozenSharedFolderPath,
            bool expectAllowed)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(enforce);
            _settings.GetConfigurationDomainNameIntra().Returns("CORP");
            _configValues.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(string.Empty);

            var tfLogger = new RecordingLogger<TerraformDispatcher>();
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.GetRequest(88).Returns(new DeploymentRequestApiModel
            {
                Id = 88,
                Project = "Infra",
                BuildNumber = "1.0.0"
            });
            requests.GetSubmittedComponents(88).Returns(new List<ComponentApiModel>
            {
                new()
                {
                    ComponentId = 21,
                    ComponentName = "TF-Shared",
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.SharedFolder,
                    ScriptPath = frozenSharedFolderPath
                }
            });
            requests.GetSubmittedProject(88, 21).Returns(new ProjectApiModel
            {
                ProjectId = 5,
                ProjectName = "Infra",
                ArtefactsUrl = @"\\files.corp.example.com\tf",
                ArtefactsSubPaths = "team-a"
            });
            requests.GetRequestDefinitions(88).Returns(new List<Dorc.PersistentData.Model.DeploymentRequestDefinition>
            {
                new() { ComponentId = 21, DefinitionRevision = "tf-rev-1" }
            });

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "files.corp.example.com" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var propertySource = Substitute.For<IPropertiesPersistentSource>();
            propertySource.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());
            _configValues.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            var dispatcher = new TerraformDispatcher(
                tfLogger,
                requests,
                _configValues,
                _settings,
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                _pipeServer,
                Substitute.For<IAzureStorageAccountWorker>(),
                Substitute.For<IProjectsPersistentSource>(),
                Substitute.For<IGitHubHostValidator>(),
                Substitute.For<ICloudResourcesPersistentSource>(),
                Substitute.For<ICloudResourceAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDatabasesPersistentSource>(),
                propertySource,
                Substitute.For<IScriptScopeConfigValues>(),
                _credentialSource,
                allowList);

            // Caller passes a live-looking component pointing inside team-a, proving Dispatch
            // enforces against the frozen submitted snapshot rather than live state.
            var liveMutatedComponent = new ComponentApiModel
            {
                ComponentId = 21,
                ComponentName = "TF-Shared",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptPath = @"\\files.corp.example.com\tf\team-a\remediated"
            };
            var result = new DeploymentResultApiModel { Id = 900, ComponentId = 21, RequestId = 88 };

            bool reachedExecutionOrReturnedTrue;
            try
            {
                reachedExecutionOrReturnedTrue = dispatcher.Dispatch(
                    liveMutatedComponent,
                    result,
                    new Dictionary<string, VariableValue>(),
                    requestId: 88,
                    isProduction: false,
                    environmentName: "DEV",
                    executionIdentityReference: null,
                    identityAdoption: new RequestExecutionIdentityAdoption(),
                    new StringBuilder(),
                    TerraformRunnerOperations.CreatePlan,
                    CancellationToken.None);
            }
            catch (Exception ex) when (expectAllowed)
            {
                // On non-domain test hosts ProcessSecurityContextBuilder.Build() throws after
                // confinement passes; reaching past VerifySharedFolderConfinement + Running status
                // proves confinement succeeded.
                Assert.IsFalse(ex is DispatchAcceptedException && !expectAllowed);
                reachedExecutionOrReturnedTrue = true;
            }

            if (!expectAllowed)
            {
                Assert.IsFalse(reachedExecutionOrReturnedTrue);
                Assert.IsTrue(tfLogger.Entries.Any(e => e.Level == LogLevel.Error));
                requests.DidNotReceive().UpdateResultStatus(
                    result,
                    Arg.Is<DeploymentResultStatus>(s => s.Equals(DeploymentResultStatus.Running)));
                _pipeServer.DidNotReceive().Start(
                    Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                    Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
            }
            else
            {
                Assert.IsTrue(reachedExecutionOrReturnedTrue);
                requests.Received().UpdateResultStatus(
                    result,
                    Arg.Is<DeploymentResultStatus>(s => s.Equals(DeploymentResultStatus.Running)));
                if (!enforce)
                {
                    Assert.IsTrue(tfLogger.Entries.Any(e =>
                        e.Level == LogLevel.Warning && e.Message.Contains("SharedFolder")));
                }
                else
                {
                    Assert.IsFalse(tfLogger.Entries.Any(e =>
                        e.Message.Contains("SharedFolder", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        [TestMethod]
        [DataRow(TerraformSourceType.Git)]
        [DataRow(TerraformSourceType.AzureArtifact)]
        [DataRow(TerraformSourceType.GitHubArtifact)]
        [DataRow(TerraformSourceType.Catalog)]
        public void NonSharedFolderTerraformSourcesAreUnaffectedByFilesystemScriptPathConfinement(
            TerraformSourceType sourceType)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);
            _settings.GetGitHubToken().Returns("ghp_token");

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.IsArtefactSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "github.com" });
            allowList.ArtefactHosts.Returns(new[] { "dev.azure.com", "api.github.com" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());
            allowList.CheckArtefactSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var logger = new RecordingLogger<TerraformSourceConfigurator>();
            var hostValidator = new GitHubHostValidator(new ConfigurationBuilder().Build());
            var configurator = new TerraformSourceConfigurator(
                logger,
                _settings,
                hostValidator,
                allowList,
                () => "entra-token");

            var component = new ComponentApiModel
            {
                ComponentId = 31,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = sourceType,
                // Even if a legacy row carried an off-share ScriptPath, non-SharedFolder
                // Terraform sources must NOT run filesystem script-path confinement.
                ScriptPath = @"\\attacker\legacy-unused\path",
                TerraformTemplateName = "storage",
                TerraformTemplateVersion = "1.0.0"
            };
            var project = new ProjectApiModel
            {
                TerraformGitRepoUrl = "https://github.com/org/repo.git",
                ArtefactsUrl = sourceType == TerraformSourceType.GitHubArtifact
                    ? "https://api.github.com/repos/org/repo"
                    : "https://dev.azure.com/org",
                ArtefactsSubPaths = "InfraProject"
            };
            var request = new DeploymentRequestApiModel
            {
                BuildUri = sourceType == TerraformSourceType.GitHubArtifact
                    ? "123456"
                    : "vstfs:///Build/Build/99"
            };
            var props = new Dictionary<string, VariableValue>
            {
                ["Terraform_Git_PAT"] = new() { Value = "pat", Type = typeof(string) }
            };

            var scriptGroup = new ScriptGroup();
            Assert.IsTrue(configurator.VerifySharedFolderConfinement(component, project, props, configuredScriptRoot: null));
            configurator.ConfigureScriptGroup(scriptGroup, component, request, project, props);

            Assert.IsFalse(logger.Entries.Any(e =>
                e.Message.Contains("SharedFolder", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("EnforceScriptPathConfinement", StringComparison.OrdinalIgnoreCase)));
            _settings.DidNotReceive().GetScriptPathEnforcementEnabled();
        }

        [TestMethod]
        [DataRow(ScriptRoot + @"\Sub\Deploy.ps1", false)]
        [DataRow("""{"ScriptPath":"\\\\itss.global\\prod\\GLO\\APP\\DORC\\Scripts\\Scripts.PR\\Sub\\Deploy.ps1","GenericArguments":[]}""", true)]
        public void AllowsConfinedRootedPowerShellPathBeneathScriptRootAndPassesExactExecutionPath(
            string storedPath,
            bool isPathJson)
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            ScriptGroup? capturedEvidenceGroup = null;
            _evidenceCapture
                .When(c => c.Prepare(
                    Arg.Any<ScriptGroup>(),
                    Arg.Any<string?>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<string>()))
                .Do(call => capturedEvidenceGroup = call.ArgAt<ScriptGroup>(0));

            Assert.IsTrue(Dispatch(storedPath, isPathJson: isPathJson));
            Assert.IsFalse(_scriptLogger.Entries.Any(e => e.Level >= LogLevel.Warning));
            Assert.IsNotNull(capturedEvidenceGroup);
            Assert.AreEqual(
                ScriptRoot + @"\Sub\Deploy.ps1",
                capturedEvidenceGroup.ScriptProperties.Single().ScriptPath);
        }

        [TestMethod]
        public void RejectsSharedFolderOffRootAdminShareEvenWhenRequesterPropertyMatchesDollarSlashToken()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "host" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var logger = new RecordingLogger<TerraformSourceConfigurator>();
            var configurator = new TerraformSourceConfigurator(
                logger,
                _settings,
                new GitHubHostValidator(new ConfigurationBuilder().Build()),
                allowList);

            var component = new ComponentApiModel
            {
                ComponentId = 41,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptPath = @"\\host\tf$\secret"
            };
            var project = new ProjectApiModel
            {
                ArtefactsUrl = @"\\host\tf",
                ArtefactsSubPaths = "team-a"
            };
            var attackerProperties = new Dictionary<string, VariableValue>
            {
                [@"\secret"] = new() { Value = @"\team-a\ok", Type = typeof(string) }
            };

            Assert.IsFalse(configurator.VerifySharedFolderConfinement(
                component,
                project,
                attackerProperties,
                configuredScriptRoot: string.Empty));
            Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error));
        }

        [TestMethod]
        public void AllowsSharedFolderBeneathScriptRootWhenProjectHasHttpsArtefactsUrlAndRelativeSubPaths()
        {
            _settings.GetScriptPathEnforcementEnabled().Returns(true);

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "itss.global" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var logger = new RecordingLogger<TerraformSourceConfigurator>();
            var configurator = new TerraformSourceConfigurator(
                logger,
                _settings,
                new GitHubHostValidator(new ConfigurationBuilder().Build()),
                allowList);

            var component = new ComponentApiModel
            {
                ComponentId = 42,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptPath = ScriptRoot + @"\Terraform\Network"
            };
            var project = new ProjectApiModel
            {
                ArtefactsUrl = "https://dev.azure.com/org",
                ArtefactsSubPaths = "InfraPipelineA;InfraPipelineB"
            };

            Assert.IsTrue(configurator.VerifySharedFolderConfinement(
                component,
                project,
                new Dictionary<string, VariableValue>(),
                configuredScriptRoot: ScriptRoot));
            Assert.IsFalse(logger.Entries.Any(e => e.Level >= LogLevel.Warning));
        }

        [TestMethod]
        [DataRow(@"$SecretToken$\..\evil.ps1", "TopSecretValue987!", false)]
        [DataRow(@"$SecretToken$\evil.ps1", "TopSecretColon:Value987!", false)]
        [DataRow(@"$SecretToken$\evil.ps1", @"TopSecretTraversal987!\..\..", false)]
        [DataRow(@"${SecretToken}\evil.ps1", @"\\secret-host\TopSecretShare987!", false)]
        [DataRow("""{"ScriptPath":"$SecretToken$\\evil.ps1","GenericArguments":[]}""", "TopSecretJson:Value987!", true)]
        public void DoesNotLogExpandedSecretPropertyValuesInReportOrEnforceMode(
            string scriptPath,
            string secretValue,
            bool isPathJson)
        {
            const string secretMarker = "TopSecret";
            var props = new Dictionary<string, VariableValue>
            {
                ["SecretToken"] = new() { Value = secretValue, Type = typeof(string) }
            };

            _settings.GetScriptPathEnforcementEnabled().Returns(false);
            Assert.IsTrue(Dispatch(scriptPath, isPathJson, props));
            Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Warning));
            Assert.IsFalse(
                _scriptLogger.Entries.Any(e => e.Message.Contains(secretMarker, StringComparison.OrdinalIgnoreCase)),
                "Report-mode warnings must log the unexpanded path reference rather than resolved secret values.");

            _scriptLogger.Entries.Clear();
            _settings.GetScriptPathEnforcementEnabled().Returns(true);
            Assert.IsFalse(Dispatch(scriptPath, isPathJson, props));
            Assert.IsTrue(_scriptLogger.Entries.Any(e => e.Level == LogLevel.Error));
            Assert.IsFalse(
                _scriptLogger.Entries.Any(e => e.Message.Contains(secretMarker, StringComparison.OrdinalIgnoreCase)),
                "Enforce-mode errors must log the unexpanded path reference rather than resolved secret values.");
        }

        [TestMethod]
        public void WarnsOnConflictingCaseJsonScriptPathWhileAssemblingExactCasePathInReportModeAndRefusingInEnforceMode()
        {
            const string conflictingJson = """{"ScriptPath":"deploy.ps1","scriptPath":"other.ps1"}""";

            ScriptGroup? capturedGroup = null;
            _evidenceCapture
                .When(c => c.Prepare(
                    Arg.Any<ScriptGroup>(),
                    Arg.Any<string?>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<string>()))
                .Do(call => capturedGroup = call.ArgAt<ScriptGroup>(0));

            _settings.GetScriptPathEnforcementEnabled().Returns(false);
            Assert.IsTrue(Dispatch(conflictingJson, isPathJson: true));
            Assert.IsTrue(_scriptLogger.Entries.Any(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("conflicting ScriptPath properties", StringComparison.Ordinal)));
            Assert.IsNotNull(capturedGroup);
            Assert.AreEqual(
                ScriptRoot + @"\deploy.ps1",
                capturedGroup.ScriptProperties.Single().ScriptPath);

            _scriptLogger.Entries.Clear();
            _evidenceCapture.ClearReceivedCalls();
            _pipeServer.ClearReceivedCalls();

            _settings.GetScriptPathEnforcementEnabled().Returns(true);
            Assert.IsFalse(Dispatch(conflictingJson, isPathJson: true));
            Assert.IsTrue(_scriptLogger.Entries.Any(e =>
                e.Level == LogLevel.Error
                && e.Message.Contains("conflicting ScriptPath properties", StringComparison.Ordinal)));
            _evidenceCapture.DidNotReceiveWithAnyArgs().Prepare(default!, default!, default, default, default!);
            _pipeServer.DidNotReceive().Start(
                Arg.Any<string>(), Arg.Any<ScriptGroup>(),
                Arg.Any<ScriptGroupReaderIdentity>(), Arg.Any<CancellationToken>());
        }
    }
}
