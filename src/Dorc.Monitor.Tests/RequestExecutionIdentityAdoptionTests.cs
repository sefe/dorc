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
                .Returns(new DeploymentCredential("shared", "password"));
            source.Resolve(DeploymentTier.NonProduction, "payments")
                .Returns(new DeploymentCredential("payments", "password"));
            var adoption = new RequestExecutionIdentityAdoption();

            adoption.Resolve(source, DeploymentTier.NonProduction, null);
            adoption.Resolve(source, DeploymentTier.NonProduction, "payments");

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
        public void PowerShellAndTerraformDispatchersAccumulateIntoTheSameRequestTracker()
        {
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
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
                credentialSource);

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
                Substitute.For<IScriptScopeConfigValues>(),
                credentialSource);

            Assert.IsFalse(scriptDispatcher.Dispatch(
                "C:\\Scripts",
                new ScriptApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                1,
                false,
                "Test",
                null,
                adoption,
                new StringBuilder(),
                CancellationToken.None));

            Assert.IsFalse(terraformDispatcher.Dispatch(
                new ComponentApiModel { ComponentName = "Terraform" },
                new DeploymentResultApiModel(),
                new Dictionary<string, VariableValue>(),
                1,
                false,
                "Test",
                null,
                adoption,
                new StringBuilder(),
                TerraformRunnerOperations.CreatePlan,
                CancellationToken.None));

            Assert.AreEqual((0, 2), adoption.Counts);
        }
    }
}
