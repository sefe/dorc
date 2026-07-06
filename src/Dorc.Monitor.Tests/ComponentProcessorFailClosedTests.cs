using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.RunnerProcess;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class ComponentProcessorFailClosedTests
    {
        [TestMethod]
        public void DeployComponent_UnknownComponentType_MarksFailed_AndReturnsFalse()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(),
                Substitute.For<ITerraformDispatcher>(),
                requests,
                Substitute.For<IComponentsPersistentSource>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ComponentProcessor>>());

            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "MysteryComponent",
                ComponentType = (ComponentType)999 // not PowerShell or Terraform
            };
            var deploymentResult = new DeploymentResultApiModel { Id = 5, Status = DeploymentResultStatus.Pending.Value };

            var result = processor.DeployComponent(
                component, deploymentResult,
                requestId: 7, isProductionRequest: false, environmentId: 3,
                isProductionEnvironment: false, environmentName: "ENV",
                scriptRoot: "root",
                commonProperties: new Dictionary<string, VariableValue>(),
                cancellationToken: CancellationToken.None);

            // Must NOT report success for an unrecognised component type.
            Assert.IsFalse(result);
            // And the persisted result status must be Failed, not StatusNotSet.
            // (DeploymentResultStatus compares by Value; each static getter returns a
            // fresh instance, so match on Value rather than reference.)
            requests.Received().UpdateResultStatus(deploymentResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == "Failed"));
            requests.DidNotReceive().UpdateResultStatus(deploymentResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == "StatusNotSet"));
        }
    }
}
