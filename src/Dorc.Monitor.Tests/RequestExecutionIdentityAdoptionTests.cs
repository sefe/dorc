using Dorc.Core.Security;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class RequestExecutionIdentityAdoptionTests
    {
        [TestMethod]
        public void SharedRequestTrackerCombinesBoundAndFallbackResolutions()
        {
            var source = Substitute.For<IDeploymentCredentialSource>();
            source.Resolve(DeploymentTier.NonProduction, null)
                .Returns(_ => DeploymentCredential.FromPlainText("shared", "password"));
            source.Resolve(DeploymentTier.NonProduction, "payments")
                .Returns(_ => DeploymentCredential.FromPlainText("payments", "password"));
            var adoption = new RequestExecutionIdentityAdoption();

            using var first = adoption.Resolve(source, DeploymentTier.NonProduction, null);
            using var second = adoption.Resolve(source, DeploymentTier.NonProduction, "payments");

            Assert.AreEqual((1, 1), adoption.Counts);
        }

        [TestMethod]
        public void RefusedNamedIdentityIsNotReportedAsBoundOrFallback()
        {
            var source = Substitute.For<IDeploymentCredentialSource>();
            source.Resolve(DeploymentTier.Production, "missing").Returns((DeploymentCredential?)null);
            var adoption = new RequestExecutionIdentityAdoption();

            adoption.Resolve(source, DeploymentTier.Production, "missing");

            Assert.AreEqual((0, 0), adoption.Counts);
        }

        [TestMethod]
        public void RefusedTierFallbackIsNotReportedAsFallbackInDispatchersAndRecordsRefusalLog()
        {
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("DOrc configuration values");
            credentialSource.Resolve(Arg.Any<DeploymentTier>(), null)
                .Returns((DeploymentCredential?)null);
            var adoption = new RequestExecutionIdentityAdoption();

            var scriptDispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ScriptDispatcher>>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                Substitute.For<IScriptsPersistentSource>());

            var terraformDispatcher = new TerraformDispatcher(
                Substitute.For<ILogger<TerraformDispatcher>>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IConfigValuesPersistentSource>(),
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
                Substitute.For<IPropertiesPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                Substitute.For<ISourceHostAllowList>());

            var scriptLog = new StringBuilder();
            Assert.IsFalse(scriptDispatcher.Dispatch(
                "C:\\Scripts",
                new ScriptApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                1,
                false,
                "Test-NonProd",
                null,
                adoption,
                scriptLog,
                CancellationToken.None));

            var terraformLog = new StringBuilder();
            Assert.IsFalse(terraformDispatcher.Dispatch(
                new ComponentApiModel { ComponentName = "Terraform" },
                new DeploymentResultApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                false,
                "Test-NonProd",
                null,
                adoption,
                terraformLog,
                TerraformRunnerOperations.CreatePlan,
                CancellationToken.None));

            Assert.AreEqual((0, 0), adoption.Counts);
            StringAssert.Contains(scriptLog.ToString(), "Deployment refused");
            StringAssert.Contains(scriptLog.ToString(), "Test-NonProd");
            StringAssert.Contains(scriptLog.ToString(), "DOrc configuration values");
            StringAssert.Contains(terraformLog.ToString(), "Deployment refused");
            StringAssert.Contains(terraformLog.ToString(), "Test-NonProd");
            StringAssert.Contains(terraformLog.ToString(), "DOrc configuration values");
        }

        [TestMethod]
        public void RefusedBoundIdentityInBothDispatchersAppendsSafeExplanationToComponentLog()
        {
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("vault");
            credentialSource.Resolve(DeploymentTier.Production, "payments-prod")
                .Returns((DeploymentCredential?)null);
            var adoption = new RequestExecutionIdentityAdoption();

            var scriptDispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ScriptDispatcher>>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                Substitute.For<IScriptsPersistentSource>());

            var terraformDispatcher = new TerraformDispatcher(
                Substitute.For<ILogger<TerraformDispatcher>>(),
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IConfigValuesPersistentSource>(),
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
                Substitute.For<IPropertiesPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                Substitute.For<ISourceHostAllowList>());

            var scriptLog = new StringBuilder();
            Assert.IsFalse(scriptDispatcher.Dispatch(
                "C:\\Scripts",
                new ScriptApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                1,
                true,
                "PAYMENTS-PROD",
                "payments-prod",
                adoption,
                scriptLog,
                CancellationToken.None));

            var terraformLog = new StringBuilder();
            Assert.IsFalse(terraformDispatcher.Dispatch(
                new ComponentApiModel { ComponentName = "Terraform" },
                new DeploymentResultApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                true,
                "PAYMENTS-PROD",
                "payments-prod",
                adoption,
                terraformLog,
                TerraformRunnerOperations.CreatePlan,
                CancellationToken.None));

            Assert.AreEqual((0, 0), adoption.Counts);
            StringAssert.Contains(scriptLog.ToString(), "payments-prod");
            StringAssert.Contains(scriptLog.ToString(), "PAYMENTS-PROD");
            StringAssert.Contains(scriptLog.ToString(), "vault");
            StringAssert.Contains(terraformLog.ToString(), "payments-prod");
            StringAssert.Contains(terraformLog.ToString(), "PAYMENTS-PROD");
            StringAssert.Contains(terraformLog.ToString(), "vault");
        }

        [TestMethod]
        public void PowerShellAndTerraformDispatchersAccumulateSuccessfulResolutionsIntoTheSameRequestTracker()
        {
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("vault");
            credentialSource.Resolve(DeploymentTier.NonProduction, null)
                .Returns(_ => DeploymentCredential.FromPlainText("svc-shared", "shared-password"));
            credentialSource.Resolve(DeploymentTier.NonProduction, "payments-dev")
                .Returns(_ => DeploymentCredential.FromPlainText("svc-payments-dev", "dev-password"));

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns(string.Empty);
            settings.GetScriptPathEnforcementEnabled().Returns(false);

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("ScriptRoot").Returns(@"C:\Scripts");
            configValues.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(@"C:\Scripts");
            configValues.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            var scriptDispatcher = new ScriptDispatcher(
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                configValues,
                Substitute.For<ILogger<ScriptDispatcher>>(),
                Substitute.For<IScriptGroupPipeServer>(),
                settings,
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                Substitute.For<IScriptsPersistentSource>(),
                Substitute.For<IPowerShellEvidenceCapture>());

            var requests = Substitute.For<IRequestsPersistentSource>();
            var tfComponent = new ComponentApiModel
            {
                ComponentId = 21,
                ComponentName = "TF-Shared",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptPath = @"\\files.corp.example.com\tf\team-a\network"
            };
            requests.GetRequest(88).Returns(new DeploymentRequestApiModel { Id = 88, Project = "Infra", BuildNumber = "1.0.0" });
            requests.GetSubmittedComponents(88).Returns(new List<ComponentApiModel> { tfComponent });
            requests.GetSubmittedProject(88, 21).Returns(new ProjectApiModel
            {
                ProjectId = 5,
                ProjectName = "Infra",
                ArtefactsUrl = @"\\files.corp.example.com\tf",
                ArtefactsSubPaths = "team-a"
            });
            requests.GetRequestDefinitions(88).Returns(new List<Dorc.PersistentData.Model.DeploymentRequestDefinition>
            {
                new() { ComponentId = 21, DefinitionRevision = "rev-1" }
            });

            var propertySource = Substitute.For<IPropertiesPersistentSource>();
            propertySource.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "files.corp.example.com" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var terraformDispatcher = new TerraformDispatcher(
                Substitute.For<ILogger<TerraformDispatcher>>(),
                requests,
                configValues,
                settings,
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IAzureStorageAccountWorker>(),
                Substitute.For<IProjectsPersistentSource>(),
                Substitute.For<IGitHubHostValidator>(),
                Substitute.For<ICloudResourcesPersistentSource>(),
                Substitute.For<ICloudResourceAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDatabasesPersistentSource>(),
                propertySource,
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                allowList);

            var adoption = new RequestExecutionIdentityAdoption();

            var scriptEx = Assert.Throws<Exception>(() => scriptDispatcher.Dispatch(
                "C:\\Scripts",
                new ScriptApiModel { Path = "deploy.ps1", PowerShellVersionNumber = "7.4", Revision = "rev-1" },
                new Dictionary<string, VariableValue>(),
                88,
                901,
                false,
                "DEV",
                null,
                adoption,
                new StringBuilder(),
                CancellationToken.None));
            StringAssert.Contains(scriptEx.Message, "'Domain' should be specified");

            var tfEx = Assert.Throws<Exception>(() => terraformDispatcher.Dispatch(
                tfComponent,
                new DeploymentResultApiModel { Id = 902, ComponentId = 21, RequestId = 88 },
                new Dictionary<string, VariableValue>(),
                88,
                false,
                "PAYMENTS-DEV",
                "payments-dev",
                adoption,
                new StringBuilder(),
                TerraformRunnerOperations.CreatePlan,
                CancellationToken.None));
            StringAssert.Contains(tfEx.Message, "'Domain' should be specified");

            Assert.AreEqual((1, 1), adoption.Counts);
        }

        [TestMethod]
        [DataRow(TerraformRunnerOperations.CreatePlan)]
        [DataRow(TerraformRunnerOperations.ApplyPlan)]
        public void TerraformDispatcherResolvesBoundExecutionIdentityForPlanAndApply(
            TerraformRunnerOperations operation)
        {
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("vault");
            credentialSource.Resolve(DeploymentTier.Production, "payments-prod")
                .Returns(_ => DeploymentCredential.FromPlainText("svc-payments", "named-password"));

            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.GetRequest(88).Returns(new DeploymentRequestApiModel
            {
                Id = 88,
                Project = "Infra",
                BuildNumber = "1.0.0"
            });
            var component = new ComponentApiModel
            {
                ComponentId = 21,
                ComponentName = "TF-Shared",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.SharedFolder,
                ScriptPath = @"\\files.corp.example.com\tf\team-a\network"
            };
            requests.GetSubmittedComponents(88).Returns(new List<ComponentApiModel> { component });
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

            var settings = Substitute.For<IConfigurationSettings>();
            // Empty domain stops cleanly inside ProcessSecurityContextBuilder.Build after
            // verifying UserName ("svc-payments") and Password ("named-password") are non-empty,
            // avoiding any Windows network LogonUser interop on the test host.
            settings.GetConfigurationDomainNameIntra().Returns(string.Empty);
            settings.GetScriptPathEnforcementEnabled().Returns(true);

            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(string.Empty);
            configValues.GetAllConfigValues(false).Returns(Array.Empty<ConfigValueApiModel>());

            var propertySource = Substitute.For<IPropertiesPersistentSource>();
            propertySource.GetAllProperties().Returns(Array.Empty<PropertyApiModel>());

            var allowList = Substitute.For<ISourceHostAllowList>();
            allowList.IsTerraformSourceUnconfigured.Returns(false);
            allowList.TerraformSourceHosts.Returns(new[] { "files.corp.example.com" });
            allowList.CheckTerraformSource(Arg.Any<string>()).Returns(PolicyDecision.Allow());

            var dispatcher = new TerraformDispatcher(
                Substitute.For<ILogger<TerraformDispatcher>>(),
                requests,
                configValues,
                settings,
                Substitute.For<IDeploymentRequestProcessesPersistentSource>(),
                Substitute.For<IScriptGroupPipeServer>(),
                Substitute.For<IAzureStorageAccountWorker>(),
                Substitute.For<IProjectsPersistentSource>(),
                Substitute.For<IGitHubHostValidator>(),
                Substitute.For<ICloudResourcesPersistentSource>(),
                Substitute.For<ICloudResourceAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDatabasesPersistentSource>(),
                propertySource,
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource,
                allowList);

            var adoption = new RequestExecutionIdentityAdoption();
            var ex = Assert.Throws<Exception>(() => dispatcher.Dispatch(
                component,
                new DeploymentResultApiModel { Id = 900, ComponentId = 21, RequestId = 88 },
                new Dictionary<string, VariableValue>(),
                requestId: 88,
                isProduction: true,
                environmentName: "PAYMENTS-PROD",
                executionIdentityReference: "payments-prod",
                identityAdoption: adoption,
                new StringBuilder(),
                operation,
                CancellationToken.None));

            StringAssert.Contains(ex.Message, "'Domain' should be specified");
            Assert.AreEqual((1, 0), adoption.Counts);
            credentialSource.Received(1).Resolve(DeploymentTier.Production, "payments-prod");
            credentialSource.DidNotReceive().Resolve(DeploymentTier.Production, null);
        }
    }
}
