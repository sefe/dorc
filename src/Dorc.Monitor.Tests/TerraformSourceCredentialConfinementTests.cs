using System.Reflection;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class TerraformSourceCredentialConfinementTests
    {
        [TestMethod]
        public void UnconfiguredAllowListRejectsGitSource()
        {
            var scriptGroup = new ScriptGroup();

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                Configure(scriptGroup, BuildAllowList()));

            StringAssert.Contains(refusal.Message, "no source host allow-list is configured");
            AssertNoSourceOrCredentialConfigured(scriptGroup);
        }

        [TestMethod]
        public void DisallowedHostRejectsGitSource()
        {
            var scriptGroup = new ScriptGroup();

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                Configure(
                    scriptGroup,
                    BuildAllowList(terraformHosts: ["github.corp.example.com"])));

            StringAssert.Contains(refusal.Message, "Withholding Git credentials");
            AssertNoSourceOrCredentialConfigured(scriptGroup);
        }

        [TestMethod]
        public void FrozenRequestSnapshotWithDisallowedGitHostIsRejectedEvenWhenLiveProjectChanged()
        {
            var frozenSnapshotProject = new ProjectApiModel
            {
                ProjectId = 7,
                ProjectName = "Infra",
                TerraformGitRepoUrl = "https://attacker.example/frozen.git"
            };
            var liveProject = new ProjectApiModel
            {
                ProjectId = 7,
                ProjectName = "Infra",
                TerraformGitRepoUrl = "https://github.corp.example.com/org/live.git"
            };

            var requestsPds = Substitute.For<IRequestsPersistentSource>();
            requestsPds.GetSubmittedProject(42, 11).Returns(frozenSnapshotProject);
            var projectsPds = Substitute.For<IProjectsPersistentSource>();
            projectsPds.GetProject("Infra").Returns(liveProject);
            var propertySource = Substitute.For<IPropertiesPersistentSource>();
            propertySource.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());
            var configSource = Substitute.For<IConfigValuesPersistentSource>();
            configSource.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            var allowList = BuildAllowList(terraformHosts: ["github.corp.example.com"]);
            var dispatcher = new TerraformDispatcher(
                NullLogger<TerraformDispatcher>.Instance,
                requestsPds,
                configSource,
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IAzureStorageAccountWorker>(),
                projectsPds,
                Substitute.For<IGitHubHostValidator>(),
                Substitute.For<ICloudResourcesPersistentSource>(),
                Substitute.For<ICloudResourceAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDatabasesPersistentSource>(),
                propertySource,
                Substitute.For<IScriptScopeConfigValues>(),
                Substitute.For<Dorc.Core.Security.IDeploymentCredentialSource>(),
                allowList);

            var getScriptGroup = typeof(TerraformDispatcher).GetMethod(
                "GetScriptGroup",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var resolvedFrozenProject = requestsPds.GetSubmittedProject(42, 11);

            var invocation = Assert.ThrowsExactly<TargetInvocationException>(() =>
                getScriptGroup.Invoke(
                    dispatcher,
                    [
                        string.Empty,
                        WithPat("secret-pat"),
                        99,
                        new ComponentApiModel { ComponentId = 11, TerraformSourceType = TerraformSourceType.Git },
                        new DeploymentRequestApiModel { Id = 42, Project = "Infra" },
                        resolvedFrozenProject
                    ]));

            Assert.IsInstanceOfType<InvalidOperationException>(invocation.InnerException);
            StringAssert.Contains(invocation.InnerException!.Message, "Withholding Git credentials");
        }

        [TestMethod]
        public void CatalogSourceFailsClosedWhenTerraformAllowListIsUnconfigured()
        {
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(BuildAllowList());

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                configurator.ConfigureScriptGroup(
                    scriptGroup,
                    new ComponentApiModel
                    {
                        TerraformSourceType = TerraformSourceType.Catalog,
                        TerraformTemplateName = "storage",
                        TerraformTemplateVersion = "1.0.0"
                    },
                    new DeploymentRequestApiModel(),
                    new ProjectApiModel(),
                    WithPat("catalog-pat")));

            StringAssert.Contains(refusal.Message, SourceHostAllowList.TerraformHostsSetting);
            AssertNoSourceOrCredentialConfigured(scriptGroup);
        }

        [TestMethod]
        public void CatalogSourceWithNonAzureAllowListwithholdsEntraTokenAndForwardsAllowedHosts()
        {
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(BuildAllowList(terraformHosts: ["github.com"]));

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel
                {
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "storage",
                    TerraformTemplateVersion = "1.0.0"
                },
                new DeploymentRequestApiModel(),
                new ProjectApiModel(),
                WithPat("catalog-pat"));

            Assert.AreEqual("catalog-pat", scriptGroup.TerraformGitPat);
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
            CollectionAssert.AreEquivalent(new[] { "github.com" }, scriptGroup.AllowedTerraformSourceHosts.ToArray());
        }

        [TestMethod]
        public void CatalogSourceWithAzureDevOpsAllowListAttachesEntraTokenAndAllowedHosts()
        {
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(BuildAllowList(terraformHosts: ["dev.azure.com"]));

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel
                {
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "storage",
                    TerraformTemplateVersion = "1.0.0"
                },
                new DeploymentRequestApiModel(),
                new ProjectApiModel(),
                WithPat("catalog-pat"));

            Assert.AreEqual("catalog-pat", scriptGroup.TerraformGitPat);
            Assert.AreEqual("entra-token", scriptGroup.AzureBearerToken);
            CollectionAssert.AreEquivalent(new[] { "dev.azure.com" }, scriptGroup.AllowedTerraformSourceHosts.ToArray());
        }

        [TestMethod]
        [DataRow(true, "https://dev.azure.com/myorg")]
        [DataRow(false, "https://dev.azure.com.attacker.net/myorg")]
        [DataRow(false, "https://api.github.com/repos/myorg/myrepo")]
        public void AzureArtifactSourceWithholdsEntraTokenUnlessHostIsPermittedAzureDevOpsAuthority(
            bool unconfigured,
            string artefactsUrl)
        {
            var allowList = unconfigured
                ? BuildAllowList()
                : BuildAllowList(artefactHosts: ["dev.azure.com", "api.github.com"]);
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(allowList);

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                configurator.ConfigureScriptGroup(
                    scriptGroup,
                    new ComponentApiModel { TerraformSourceType = TerraformSourceType.AzureArtifact },
                    new DeploymentRequestApiModel { BuildUri = "vstfs:///Build/Build/99" },
                    new ProjectApiModel { ArtefactsUrl = artefactsUrl, ArtefactsSubPaths = "ProjA" },
                    new Dictionary<string, VariableValue>()));

            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureOrganization));
        }

        [TestMethod]
        public void AzureArtifactSourceAttachesEntraTokenForPermittedAzureDevOpsHost()
        {
            var allowList = BuildAllowList(artefactHosts: ["dev.azure.com"]);
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(allowList);

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.AzureArtifact },
                new DeploymentRequestApiModel { BuildUri = "vstfs:///Build/Build/99" },
                new ProjectApiModel { ArtefactsUrl = "https://dev.azure.com/myorg", ArtefactsSubPaths = "ProjA" },
                new Dictionary<string, VariableValue>());

            Assert.AreEqual("myorg", scriptGroup.AzureOrganization);
            Assert.AreEqual("99", scriptGroup.AzureBuildId);
            Assert.AreEqual("entra-token", scriptGroup.AzureBearerToken);
        }

        [TestMethod]
        [DataRow(true, "https://api.github.com/repos/myorg/myrepo")]
        [DataRow(false, "https://api.github.com.attacker.net/repos/myorg/myrepo")]
        [DataRow(false, "https://api.github.com/invalid-single-segment")]
        public void GitHubArtifactSourceWithholdsTokenWhenAllowListOrUrlValidationFails(
            bool unconfigured,
            string artefactsUrl)
        {
            var allowList = unconfigured
                ? BuildAllowList()
                : BuildAllowList(artefactHosts: ["api.github.com"]);
            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetGitHubToken().Returns("ghp_estate_secret");
            var hostValidator = new GitHubHostValidator(new ConfigurationBuilder().Build());
            var scriptGroup = new ScriptGroup();
            var configurator = new TerraformSourceConfigurator(
                Substitute.For<ILogger>(),
                settings,
                hostValidator,
                allowList,
                () => "entra-token");

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                configurator.ConfigureScriptGroup(
                    scriptGroup,
                    new ComponentApiModel { TerraformSourceType = TerraformSourceType.GitHubArtifact },
                    new DeploymentRequestApiModel { BuildUri = "12345" },
                    new ProjectApiModel { ArtefactsUrl = artefactsUrl },
                    new Dictionary<string, VariableValue>()));

            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.GitHubToken));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.GitHubApiBaseUrl));
        }

        [TestMethod]
        [DataRow(@"\\attacker.example\tf\module")]
        [DataRow(@"\\?\UNC\attacker.example\tf\module")]
        [DataRow(@"\\files.corp.example.com@SSL@443\tf\module")]
        [DataRow(@"\\sub.files.corp.example.com\tf\module")]
        public void RemoteSharedFolderRequiresPermittedTerraformHost(string scriptPath)
        {
            var scriptGroup = new ScriptGroup();
            var configurator = CreateConfigurator(BuildAllowList(terraformHosts: ["files.corp.example.com"]));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                configurator.ConfigureScriptGroup(
                    scriptGroup,
                    new ComponentApiModel
                    {
                        TerraformSourceType = TerraformSourceType.SharedFolder,
                        ScriptPath = scriptPath
                    },
                    new DeploymentRequestApiModel(),
                    new ProjectApiModel(),
                    new Dictionary<string, VariableValue>()));
        }

        [TestMethod]
        [DataRow(@"\\attacker.corp\drops\1.0")]
        [DataRow(@"\\?\UNC\attacker.corp\drops\1.0")]
        [DataRow(@"\\attacker.corp@SSL@443\drops\1.0")]
        public void PendingRequestProcessorRefusesUnpermittedRemoteDropLocationBeforeDeployingComponents(string dropLocation)
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            var componentProcessor = Substitute.For<Dorc.Monitor.IComponentProcessor>();
            var requestsPds = Substitute.For<IRequestsPersistentSource>();
            requestsPds.SetRequestStartStatus(
                Arg.Any<DeploymentRequestApiModel>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DateTimeOffset>()).Returns(true);
            requestsPds.GetSubmittedComponents(77).Returns(new List<ComponentApiModel>
            {
                new() { ComponentId = 1, ComponentName = "Comp1", ScriptPath = "deploy.ps1", IsEnabled = true }
            });
            requestsPds.GetDeploymentResultsForRequest(77).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 1, ComponentId = 1, RequestId = 77, Status = DeploymentResultStatus.Pending.ToString() }
            });

            var envsPds = Substitute.For<IEnvironmentsPersistentSource>();
            envsPds.GetEnvironment("Dev").Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "Dev" });
            var manageProjectsPds = Substitute.For<IManageProjectsPersistentSource>();
            var configValuesPds = Substitute.For<IConfigValuesPersistentSource>();
            configValuesPds.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns("C:\\Scripts");
            configValuesPds.GetConfigValue("ScriptRoot").Returns("C:\\Scripts");
            configValuesPds.GetConfigValue("DeploymentLogDir", Arg.Any<string>()).Returns("C:\\Logs");
            configValuesPds.GetAllConfigValues(true).Returns(Array.Empty<ConfigValueApiModel>());

            var propValuesPds = Substitute.For<IPropertyValuesPersistentSource>();
            propValuesPds.LoadAllPropertiesIntoCache().Returns(new Dictionary<string, PropertyValueDto>());

            var eventsPublisher = Substitute.For<Dorc.Core.Interfaces.IDeploymentEventsPublisher>();
            eventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<Dorc.Core.Events.DeploymentRequestEventData>())
                .Returns(Task.CompletedTask);
            eventsPublisher.PublishResultStatusChangedAsync(Arg.Any<Dorc.Core.Events.DeploymentResultEventData>())
                .Returns(Task.CompletedTask);

            var gitHubDownloader = Substitute.For<Dorc.Core.Interfaces.IGitHubArtifactDownloader>();
            gitHubDownloader.IsGitHubArtifactUrl(Arg.Any<string>()).Returns(false);

            var processor = new Dorc.Monitor.RequestProcessors.PendingRequestProcessor(
                loggerFactory,
                componentProcessor,
                Substitute.For<Dorc.Core.VariableResolution.IVariableScopeOptionsResolver>(),
                requestsPds,
                propValuesPds,
                envsPds,
                manageProjectsPds,
                configValuesPds,
                Substitute.For<Dorc.Core.VariableResolution.IPropertyEvaluator>(),
                eventsPublisher,
                Substitute.For<Dorc.Monitor.Notifications.IDeploymentNotificationSink>(),
                gitHubDownloader,
                BuildAllowList(artefactHosts: ["trusted.corp"]),
                Substitute.For<IScriptScopeConfigValues>());

            var request = new RequestToProcessDto(
                new DeploymentRequestApiModel
                {
                    Id = 77,
                    Project = "Proj",
                    EnvironmentName = "Dev",
                    BuildNumber = "1.0",
                    RequestDetails = "<details>"
                },
                new Dorc.Core.DeploymentRequestDetail
                {
                    EnvironmentName = "Dev",
                    Components = new List<string> { "Comp1" },
                    BuildDetail = new Dorc.Core.BuildDetail { DropLocation = dropLocation }
                });

            processor.Execute(request, CancellationToken.None, loggerFactory);

            componentProcessor.DidNotReceiveWithAnyArgs().DeployComponent(
                default!, default!, default, default, default, default, default!, default!, default!, default);
            requestsPds.Received().SetRequestCompletionStatus(
                77,
                DeploymentRequestStatus.Errored,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>());
        }

        private static void AssertNoSourceOrCredentialConfigured(ScriptGroup scriptGroup)
        {
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitPat));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.GitHubToken));
        }

        private static void Configure(ScriptGroup scriptGroup, ISourceHostAllowList sourceHosts)
        {
            var configurator = CreateConfigurator(sourceHosts);

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.Git },
                new DeploymentRequestApiModel(),
                new ProjectApiModel
                {
                    TerraformGitRepoUrl = "https://attacker.example/repository.git"
                },
                new Dictionary<string, VariableValue>());
        }

        private static TerraformSourceConfigurator CreateConfigurator(ISourceHostAllowList sourceHosts)
        {
            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetGitHubToken().Returns("ghp_estate_secret");
            var hostValidator = Substitute.For<IGitHubHostValidator>();
            hostValidator.GetApiBase(Arg.Any<string>()).Returns("https://api.github.com");

            return new TerraformSourceConfigurator(
                Substitute.For<ILogger>(),
                settings,
                hostValidator,
                sourceHosts,
                () => "entra-token");
        }

        private static IDictionary<string, VariableValue> WithPat(string pat) =>
            new Dictionary<string, VariableValue>
            {
                ["Terraform_Git_PAT"] = new() { Value = pat, Type = typeof(string) }
            };

        private static SourceHostAllowList BuildAllowList(
            string[]? artefactHosts = null,
            string[]? terraformHosts = null)
        {
            var entries = new Dictionary<string, string?>();
            if (artefactHosts != null)
            {
                for (var i = 0; i < artefactHosts.Length; i++)
                {
                    entries[$"{SourceHostAllowList.ArtefactHostsSetting}:{i}"] = artefactHosts[i];
                }
            }

            if (terraformHosts != null)
            {
                for (var i = 0; i < terraformHosts.Length; i++)
                {
                    entries[$"{SourceHostAllowList.TerraformHostsSetting}:{i}"] = terraformHosts[i];
                }
            }

            return new SourceHostAllowList(new ConfigurationBuilder().AddInMemoryCollection(entries).Build());
        }
    }
}
