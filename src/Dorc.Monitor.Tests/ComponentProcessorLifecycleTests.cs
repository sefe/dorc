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

        [TestMethod]
        public void DeployComponent_InterruptedWhileRequestStillRunning_KeepsResultStatusRunning()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel
            {
                Id = 7,
                Status = DeploymentRequestStatus.Running.ToString()
            });
            requests.GetSubmittedScript(7, 1).Returns(new ScriptApiModel { Path = "restore.ps1", NonProdOnly = false });

            var scripts = Substitute.For<IScriptDispatcher>();
            scripts.Dispatch(
                Arg.Any<string>(),
                Arg.Any<ScriptApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<System.Text.StringBuilder>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => throw new OperationCanceledException());

            var events = Substitute.For<IDeploymentEventsPublisher>();
            events.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>()).Returns(Task.CompletedTask);

            var processor = new ComponentProcessor(
                scripts,
                Substitute.For<ITerraformDispatcher>(),
                requests,
                Substitute.For<IComponentsPersistentSource>(),
                events,
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ComponentProcessor>>());

            var result = new DeploymentResultApiModel
            {
                Id = 42,
                RequestId = 7,
                ComponentId = 1,
                Status = DeploymentResultStatus.Pending.ToString(),
                Log = "Persisted runner log output"
            };
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "040 - RVC Restore Database",
                ComponentType = ComponentType.PowerShell
            };

            Assert.Throws<OperationCanceledException>(() => processor.DeployComponent(
                component, result, 7, false, 1, false, "RVC DEV", "C:\\Scripts",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            // Initial dispatch sets Running once; finally block must NOT overwrite a non-empty persisted DB log with an empty local builder
            requests.DidNotReceive().UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));
            requests.Received(1).UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Running.Value));
            requests.DidNotReceiveWithAnyArgs().UpdateResultLog(default!, default!);
            Assert.AreEqual("Persisted runner log output", result.Log);
        }

        [TestMethod]
        public void DeployComponent_InterruptedWhenGetRequestThrows_PreservesRunningStatus()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.GetRequest(7).Returns(_ => throw new TimeoutException("SQL connection timeout"));
            requests.GetSubmittedScript(7, 1).Returns(new ScriptApiModel { Path = "restore.ps1", NonProdOnly = false });

            var scripts = Substitute.For<IScriptDispatcher>();
            scripts.Dispatch(
                Arg.Any<string>(),
                Arg.Any<ScriptApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<System.Text.StringBuilder>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => throw new OperationCanceledException());

            var events = Substitute.For<IDeploymentEventsPublisher>();
            events.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>()).Returns(Task.CompletedTask);

            var processor = new ComponentProcessor(
                scripts,
                Substitute.For<ITerraformDispatcher>(),
                requests,
                Substitute.For<IComponentsPersistentSource>(),
                events,
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ComponentProcessor>>());

            var result = new DeploymentResultApiModel
            {
                Id = 42,
                RequestId = 7,
                ComponentId = 1,
                Status = DeploymentResultStatus.Pending.ToString()
            };
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "040 - RVC Restore Database",
                ComponentType = ComponentType.PowerShell
            };

            Assert.Throws<OperationCanceledException>(() => processor.DeployComponent(
                component, result, 7, false, 1, false, "RVC DEV", "C:\\Scripts",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            // Lookup failure must conservatively preserve Running status rather than writing StatusNotSet or Cancelled
            requests.Received(1).UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Running.Value));
            requests.DidNotReceive().UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value != DeploymentResultStatus.Running.Value));
        }

        [TestMethod]
        public void DeployComponent_InterruptedWhenUserCancelling_MarksResultStatusCancelled()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.GetRequest(7).Returns(new DeploymentRequestApiModel
            {
                Id = 7,
                Status = DeploymentRequestStatus.Cancelling.ToString()
            });
            requests.GetSubmittedScript(7, 1).Returns(new ScriptApiModel { Path = "restore.ps1", NonProdOnly = false });

            var scripts = Substitute.For<IScriptDispatcher>();
            scripts.Dispatch(
                Arg.Any<string>(),
                Arg.Any<ScriptApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<System.Text.StringBuilder>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => throw new OperationCanceledException());

            var events = Substitute.For<IDeploymentEventsPublisher>();
            events.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>()).Returns(Task.CompletedTask);

            var processor = new ComponentProcessor(
                scripts,
                Substitute.For<ITerraformDispatcher>(),
                requests,
                Substitute.For<IComponentsPersistentSource>(),
                events,
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ComponentProcessor>>());

            var result = new DeploymentResultApiModel
            {
                Id = 42,
                RequestId = 7,
                ComponentId = 1,
                Status = DeploymentResultStatus.Pending.ToString()
            };
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "040 - RVC Restore Database",
                ComponentType = ComponentType.PowerShell
            };

            Assert.Throws<OperationCanceledException>(() => processor.DeployComponent(
                component, result, 7, false, 1, false, "RVC DEV", "C:\\Scripts",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            requests.Received().UpdateResultStatus(
                result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));
        }
    }
}
