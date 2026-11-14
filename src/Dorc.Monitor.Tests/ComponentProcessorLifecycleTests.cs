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
                component, result, 7, false, 1, false, "test", null, new RequestExecutionIdentityAdoption(), "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            Assert.IsFalse(terraform.ReceivedCalls().Any());
            Assert.IsFalse(scripts.ReceivedCalls().Any());
            requests.DidNotReceiveWithAnyArgs().UpdateResultStatus(default!, default);
            Assert.AreEqual(status, result.Status);
        }

        [TestMethod]
        public void DispatcherRefusalLog_IsPersistedViaUpdateResultLog()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.GetSubmittedScript(7, 1).Returns(new ScriptApiModel { Path = "deploy.ps1" });

            var events = Substitute.For<IDeploymentEventsPublisher>();
            events.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>())
                .Returns(Task.CompletedTask);

            var scripts = Substitute.For<IScriptDispatcher>();
            scripts.Dispatch(
                    Arg.Any<string>(),
                    Arg.Any<ScriptApiModel>(),
                    Arg.Any<IDictionary<string, VariableValue>>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<RequestExecutionIdentityAdoption>(),
                    Arg.Any<System.Text.StringBuilder>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    call.Arg<System.Text.StringBuilder>().AppendLine(
                        "Deployment refused: execution identity 'payments-prod' for environment 'PAYMENTS-PROD' could not be resolved from vault.");
                    return false;
                });

            var processor = new ComponentProcessor(scripts, Substitute.For<ITerraformDispatcher>(), requests,
                Substitute.For<IComponentsPersistentSource>(), events,
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>());
            var result = new DeploymentResultApiModel { Id = 42, RequestId = 7, Status = "Pending" };
            var component = new ComponentApiModel
            {
                ComponentId = 1, ComponentName = "ps-comp", ComponentType = ComponentType.PowerShell
            };

            var succeeded = processor.DeployComponent(
                component, result, 7, true, 1, true, "PAYMENTS-PROD", "payments-prod",
                new RequestExecutionIdentityAdoption(), "C:\\Scripts",
                new Dictionary<string, VariableValue>(), CancellationToken.None);

            Assert.IsFalse(succeeded);
            StringAssert.Contains(result.Log, "Deployment refused");
            StringAssert.Contains(result.Log, "payments-prod");
            requests.Received(1).UpdateResultLog(
                result,
                Arg.Is<string>(log => log.Contains("Deployment refused") && log.Contains("payments-prod")));
            requests.Received(1).UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.ToString() == DeploymentResultStatus.Failed.ToString()));
        }
    }
}
