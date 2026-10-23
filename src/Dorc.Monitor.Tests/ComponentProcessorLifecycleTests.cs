using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class ComponentProcessorLifecycleTests
    {
        [DataTestMethod]
        [DataRow("WaitingConfirmation")]
        [DataRow("Complete")]
        [DataRow("Confirmed")]
        public void UnapprovedCompletedOrConcurrentlyClaimedResult_IsNotDispatchedOrOverwritten(string status)
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(false);
            var terraform = Substitute.For<ITerraformDispatcher>();
            var scripts = Substitute.For<IScriptDispatcher>();
            var processor = new ComponentProcessor(scripts, terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>());
            var result = new DeploymentResultApiModel { Id = 42, RequestId = 7, Status = status };
            var component = new ComponentApiModel
            {
                ComponentId = 1, ComponentName = "catalog", ComponentType = ComponentType.Terraform
            };

            Assert.Throws<InvalidOperationException>(() => processor.DeployComponent(
                component, result, 7, false, 1, false, "test", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            Assert.IsFalse(terraform.ReceivedCalls().Any());
            Assert.IsFalse(scripts.ReceivedCalls().Any());
            requests.DidNotReceiveWithAnyArgs().UpdateResultStatus(default!, default);
            Assert.AreEqual(status, result.Status);
        }
    }
}
