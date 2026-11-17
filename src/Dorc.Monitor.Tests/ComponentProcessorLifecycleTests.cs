using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.RunnerProcess;
using Dorc.PersistentData.Model;
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
        public void EphemeralNonProd_Provisioning_AutoApprovesAndAppliesInSamePass()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.TryConfirmTerraformPlan(Arg.Any<DeploymentResultApiModel>(), Arg.Any<TerraformPlanApproval>())
                .Returns(true);

            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(10).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "eph-pr-1",
                EnvironmentIsProd = false,
                IsEphemeral = true
            });

            var operations = new List<TerraformRunnerOperations>();
            var terraform = Substitute.For<ITerraformDispatcher>();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Do<TerraformRunnerOperations>(op => operations.Add(op)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var result = new DeploymentResultApiModel { Id = 42, RequestId = 7, Status = DeploymentResultStatus.Pending.Value };
            var component = new ComponentApiModel
            {
                ComponentId = 1, ComponentName = "Infra-Postgres", ComponentType = ComponentType.Terraform
            };

            var success = processor.DeployComponent(
                component, result, 7, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None);

            Assert.IsTrue(success);
            CollectionAssert.AreEqual(
                new[] { TerraformRunnerOperations.CreatePlan, TerraformRunnerOperations.ApplyPlan },
                operations);
            requests.Received(1).TryConfirmTerraformPlan(
                result,
                Arg.Is<TerraformPlanApproval>(a => a.ConfirmedBy == "system:ephemeral-auto-approve"));
        }

        [TestMethod]
        public void EphemeralNonProd_Destroy_AutoApprovesAndAppliesDestroyInSamePass()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.TryConfirmTerraformPlan(Arg.Any<DeploymentResultApiModel>(), Arg.Any<TerraformPlanApproval>())
                .Returns(true);
            requests.GetTerraformDeploymentReceipt(55).Returns(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 55,
                PlanSha256 = "p-sha",
                SourceSha256 = "s-sha",
                PlanContentSha256 = "c-sha"
            });

            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(10).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "eph-pr-1",
                EnvironmentIsProd = false,
                IsEphemeral = true
            });

            var operations = new List<TerraformRunnerOperations>();
            var terraform = Substitute.For<ITerraformDispatcher>();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Do<TerraformRunnerOperations>(op => operations.Add(op)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var result = new DeploymentResultApiModel { Id = 55, RequestId = 8, Status = DeploymentResultStatus.Pending.Value };
            var component = new ComponentApiModel
            {
                ComponentId = 2, ComponentName = "Infra-Kafka", ComponentType = ComponentType.Terraform
            };
            var props = new Dictionary<string, VariableValue>
            {
                [ComponentProcessor.TerraformOperationPropertyName] = new() { Value = ComponentProcessor.DestroyOperationValue }
            };

            var success = processor.DeployComponent(
                component, result, 8, false, 10, false, "eph-pr-1", "unused",
                props, CancellationToken.None);

            Assert.IsTrue(success);
            CollectionAssert.AreEqual(
                new[] { TerraformRunnerOperations.CreateDestroyPlan, TerraformRunnerOperations.ApplyDestroyPlan },
                operations);
            requests.Received(1).TryConfirmTerraformPlan(
                result,
                Arg.Is<TerraformPlanApproval>(a =>
                    a.ConfirmedBy == "system:ephemeral-auto-approve" &&
                    a.PlanSha256 == "p-sha" &&
                    a.SourceSha256 == "s-sha" &&
                    a.PlanContentSha256 == "c-sha"));
        }

        [DataTestMethod]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void EphemeralProd_SafetyCheck_RejectsDeploymentAndNeverAutoApproves(bool requestIsProd, bool envIsProd, bool isProdEnvFlag)
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(10).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "prod-eph",
                EnvironmentIsProd = envIsProd,
                IsEphemeral = true
            });

            var terraform = Substitute.For<ITerraformDispatcher>();
            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var result = new DeploymentResultApiModel { Id = 60, RequestId = 9, Status = DeploymentResultStatus.Pending.Value };
            var component = new ComponentApiModel
            {
                ComponentId = 1, ComponentName = "Infra-Postgres", ComponentType = ComponentType.Terraform
            };

            Assert.Throws<InvalidOperationException>(() => processor.DeployComponent(
                component, result, 9, requestIsProd, 10, isProdEnvFlag, "prod-eph", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            Assert.IsFalse(terraform.ReceivedCalls().Any());
            requests.DidNotReceiveWithAnyArgs().TryConfirmTerraformPlan(default!, default!);
            requests.Received(1).UpdateResultStatus(result, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Failed));
        }

        [TestMethod]
        public void EphemeralAutoApprove_WhenApplyFails_MarksResultFailed()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.TryConfirmTerraformPlan(Arg.Any<DeploymentResultApiModel>(), Arg.Any<TerraformPlanApproval>())
                .Returns(true);

            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("eph-pr-1").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10, EnvironmentName = "eph-pr-1", IsEphemeral = true
            });

            var terraform = Substitute.For<ITerraformDispatcher>();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                TerraformRunnerOperations.CreatePlan,
                Arg.Any<CancellationToken>())
                .Returns(true);
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                TerraformRunnerOperations.ApplyPlan,
                Arg.Any<CancellationToken>())
                .Returns(false);

            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var result = new DeploymentResultApiModel { Id = 70, RequestId = 9, Status = DeploymentResultStatus.Pending.Value };
            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "c", ComponentType = ComponentType.Terraform };

            Assert.IsFalse(processor.DeployComponent(
                component, result, 9, false, 0, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));
            requests.Received().UpdateResultStatus(result, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Failed));
        }

        [TestMethod]
        public void NonEphemeralDestroy_IsRejectedAndConfirmedEphemeralDestroyAppliesDestroy()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>()).Returns(true);

            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(10).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "non-eph",
                IsEphemeral = false,
                EnvironmentIsProd = false
            });
            environments.GetEnvironment(11).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 11,
                EnvironmentName = "prod-eph",
                IsEphemeral = true,
                EnvironmentIsProd = true
            });
            environments.GetEnvironment(12).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 12,
                EnvironmentName = "eph-pr-12",
                IsEphemeral = true,
                EnvironmentIsProd = false
            });

            var operations = new List<TerraformRunnerOperations>();
            var terraform = Substitute.For<ITerraformDispatcher>();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Do<TerraformRunnerOperations>(op => operations.Add(op)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var eventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            eventsPublisher.PublishResultStatusChangedAsync(Arg.Is<DeploymentResultEventData>(e => e.ResultId == 80))
                .Returns(Task.FromException(new InvalidOperationException("publish failed")));

            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), eventsPublisher,
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "c", ComponentType = ComponentType.Terraform };
            var props = new Dictionary<string, VariableValue>
            {
                [ComponentProcessor.IsDestroyPropertyName] = new() { Value = "true" },
                ["termination_protection"] = new() { Value = "true" },
                ["service_termination_protection"] = new() { Value = "true" }
            };

            // 1) Null environment with destroy -> fails immediately without dispatching
            var nullEnvResult = new DeploymentResultApiModel { Id = 80, RequestId = 11, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, nullEnvResult, 11, false, 0, false, "", "unused", props, CancellationToken.None));
            requests.Received().UpdateResultStatus(nullEnvResult, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Failed));

            // 2) Non-ephemeral environment with destroy -> fails immediately without dispatching
            var nonEphResult = new DeploymentResultApiModel { Id = 81, RequestId = 11, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, nonEphResult, 11, false, 10, false, "non-eph", "unused", props, CancellationToken.None));
            requests.Received().UpdateResultStatus(nonEphResult, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Failed));

            // 3) Prod ephemeral environment with destroy -> fails immediately without dispatching
            var prodEphResult = new DeploymentResultApiModel { Id = 82, RequestId = 11, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, prodEphResult, 11, false, 11, false, "prod-eph", "unused", props, CancellationToken.None));
            requests.Received().UpdateResultStatus(prodEphResult, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Failed));
            Assert.IsEmpty(operations);

            // 4) Non-ephemeral Pending non-destroy -> pauses at WaitingConfirmation
            var nonDestroyProps = new Dictionary<string, VariableValue>();
            var pendingNonEphResult = new DeploymentResultApiModel { Id = 83, RequestId = 11, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsTrue(processor.DeployComponent(
                component, pendingNonEphResult, 11, false, 10, false, "non-eph", "unused", nonDestroyProps, CancellationToken.None));
            requests.Received().UpdateResultStatus(pendingNonEphResult, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.WaitingConfirmation));

            // 5) Confirmed ephemeral destroy -> ApplyDestroyPlan -> Complete (and injects DOrcIsEphemeral + forces termination_protection false)
            var confirmedResult = new DeploymentResultApiModel { Id = 84, RequestId = 11, Status = DeploymentResultStatus.Confirmed.Value };
            Assert.IsTrue(processor.DeployComponent(
                component, confirmedResult, 11, false, 12, false, "eph-pr-12", "unused", props, CancellationToken.None));
            requests.Received().UpdateResultStatus(confirmedResult, Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Complete));
            Assert.AreEqual("true", props[ComponentProcessor.EphemeralPropertyName].Value);
            Assert.AreEqual("false", props["termination_protection"].Value);
            Assert.AreEqual("false", props["service_termination_protection"].Value);

            CollectionAssert.AreEqual(
                new[] { TerraformRunnerOperations.CreatePlan, TerraformRunnerOperations.ApplyDestroyPlan },
                operations);
        }

        [TestMethod]
        public void EphemeralAutoApprove_WhenCasOrRunningTransitionFailsOrCancelled_StaysWaitingConfirmation()
        {
            var requests = Substitute.For<IRequestsPersistentSource>();
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(10).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "eph-pr-1",
                IsEphemeral = true,
                EnvironmentIsProd = false
            });

            var operations = new List<TerraformRunnerOperations>();
            var terraform = Substitute.For<ITerraformDispatcher>();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Do<TerraformRunnerOperations>(op => operations.Add(op)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var processor = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(), terraform, requests,
                Substitute.For<IComponentsPersistentSource>(), Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(), Substitute.For<ILogger<ComponentProcessor>>(),
                environments);

            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "c", ComponentType = ComponentType.Terraform };

            // Case 1: TryConfirmTerraformPlan returns false -> reloads status from DB (if Cancelled or null, sets Cancelled; if custom status, preserves it) without overwriting to WaitingConfirmation
            requests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>()).Returns(true);
            requests.TryConfirmTerraformPlan(Arg.Any<DeploymentResultApiModel>(), Arg.Any<TerraformPlanApproval>())
                .Returns(false);
            requests.GetDeploymentResults(91).Returns(
                new DeploymentResultApiModel { Id = 91, Status = DeploymentResultStatus.Cancelled.Value },
                new DeploymentResultApiModel { Id = 91, Status = DeploymentResultStatus.WaitingConfirmation.Value },
                (DeploymentResultApiModel)null!);

            var result1 = new DeploymentResultApiModel { Id = 91, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result1, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));
            Assert.AreEqual(DeploymentResultStatus.Cancelled.Value, result1.Status);

            var result1Waiting = new DeploymentResultApiModel { Id = 91, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result1Waiting, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));
            Assert.AreEqual(DeploymentResultStatus.WaitingConfirmation.Value, result1Waiting.Status);

            var result1Null = new DeploymentResultApiModel { Id = 91, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result1Null, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));
            Assert.AreEqual(DeploymentResultStatus.Failed.Value, result1Null.Status);

            // Case 2: TryConfirmTerraformPlan returns true, but UpdateResultStatus(Running, Confirmed) returns false -> reloads status from DB
            requests.TryConfirmTerraformPlan(Arg.Any<DeploymentResultApiModel>(), Arg.Any<TerraformPlanApproval>())
                .Returns(true);
            requests.UpdateResultStatus(
                Arg.Is<DeploymentResultApiModel>(r => r.Id == 92),
                Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Running),
                Arg.Is<DeploymentResultStatus>(s => s == DeploymentResultStatus.Confirmed)).Returns(false);
            requests.GetDeploymentResults(92).Returns(new DeploymentResultApiModel { Id = 92, Status = DeploymentResultStatus.Cancelled.Value });

            var result2 = new DeploymentResultApiModel { Id = 92, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result2, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));
            Assert.AreEqual(DeploymentResultStatus.Cancelled.Value, result2.Status);

            // Case 3: Cancellation requested before TryConfirmTerraformPlan -> sets Cancelled
            using var cts3 = new CancellationTokenSource();
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Is<DeploymentResultApiModel>(r => r.Id == 93),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                TerraformRunnerOperations.CreatePlan,
                Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cts3.Cancel();
                    return true;
                });
            requests.GetDeploymentResults(93).Returns((DeploymentResultApiModel)null!);

            var result3 = new DeploymentResultApiModel { Id = 93, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result3, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), cts3.Token));
            Assert.AreEqual(DeploymentResultStatus.Cancelled.Value, result3.Status);

            // Only CreatePlan was ever dispatched across all 5 cases
            CollectionAssert.AreEqual(
                new[]
                {
                    TerraformRunnerOperations.CreatePlan,
                    TerraformRunnerOperations.CreatePlan,
                    TerraformRunnerOperations.CreatePlan,
                    TerraformRunnerOperations.CreatePlan,
                    TerraformRunnerOperations.CreatePlan
                },
                operations);

            // Case 5: Confirmed Destroy operation dispatches ApplyDestroyPlan directly
            var result5 = new DeploymentResultApiModel { Id = 95, RequestId = 12, Status = DeploymentResultStatus.Confirmed.Value };
            var destroyProps = new Dictionary<string, VariableValue>
            {
                [ComponentProcessor.TerraformOperationPropertyName] = new VariableValue { Value = ComponentProcessor.DestroyOperationValue }
            };
            Assert.IsTrue(processor.DeployComponent(
                component, result5, 12, false, 10, false, "eph-pr-1", "unused",
                destroyProps, CancellationToken.None));

            // Case 6: Terraform dispatch returns false -> sets result to Failed
            terraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Is<DeploymentResultApiModel>(r => r.Id == 96),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Any<TerraformRunnerOperations>(), Arg.Any<CancellationToken>())
                .Returns(false);
            var result6 = new DeploymentResultApiModel { Id = 96, RequestId = 12, Status = DeploymentResultStatus.Pending.Value };
            Assert.IsFalse(processor.DeployComponent(
                component, result6, 12, false, 10, false, "eph-pr-1", "unused",
                new Dictionary<string, VariableValue>(), CancellationToken.None));

            Assert.IsTrue(TerraformDispatcher.IsEphemeralOperation(
                null,
                new EnvironmentApiModel { IsEphemeral = true }));
        }

        [TestMethod]
        public void IsDestroyOperation_And_ShouldAutoApproveEphemeralPlan_HandleAllBranches()
        {
            ComponentProcessor.ApplyEphemeralPropertyInvariants(null);

            Assert.IsFalse(ComponentProcessor.IsDestroyOperation(null));
            Assert.IsFalse(ComponentProcessor.IsDestroyOperation(new Dictionary<string, VariableValue>()));
            Assert.IsFalse(ComponentProcessor.IsDestroyOperation(new Dictionary<string, VariableValue>
            {
                ["TerraformOperation"] = new() { Value = "Plan" },
                ["IsDestroy"] = new() { Value = "false" }
            }));
            Assert.IsTrue(ComponentProcessor.IsDestroyOperation(new Dictionary<string, VariableValue>
            {
                ["TerraformOperation"] = new() { Value = "Destroy" }
            }));
            Assert.IsTrue(ComponentProcessor.IsDestroyOperation(new Dictionary<string, VariableValue>
            {
                ["IsDestroy"] = new() { Value = "true" }
            }));
            Assert.IsFalse(ComponentProcessor.IsDestroyOperation(
                null,
                new EnvironmentApiModel { IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown }));
            Assert.IsFalse(ComponentProcessor.IsDestroyOperation(
                null,
                new EnvironmentApiModel { IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready }));

            Assert.IsFalse(ComponentProcessor.ShouldAutoApproveEphemeralPlan(null, false, false));
            Assert.IsFalse(ComponentProcessor.ShouldAutoApproveEphemeralPlan(
                new EnvironmentApiModel { IsEphemeral = false }, false, false));
            Assert.IsFalse(ComponentProcessor.ShouldAutoApproveEphemeralPlan(
                new EnvironmentApiModel { IsEphemeral = true, EnvironmentIsProd = true }, false, false));
            Assert.IsFalse(ComponentProcessor.ShouldAutoApproveEphemeralPlan(
                new EnvironmentApiModel { IsEphemeral = true, EnvironmentIsProd = false }, true, false));
            Assert.IsFalse(ComponentProcessor.ShouldAutoApproveEphemeralPlan(
                new EnvironmentApiModel { IsEphemeral = true, EnvironmentIsProd = false }, false, true));
            Assert.IsTrue(ComponentProcessor.ShouldAutoApproveEphemeralPlan(
                new EnvironmentApiModel { IsEphemeral = true, EnvironmentIsProd = false }, false, false));

            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(null, null));
            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(
                new Dictionary<string, VariableValue> { [ComponentProcessor.EphemeralPropertyName] = new() { Value = "false" } },
                new EnvironmentApiModel { IsEphemeral = false }));
            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(
                new Dictionary<string, VariableValue> { [ComponentProcessor.EphemeralPropertyName] = new() { Value = "true" } },
                null));
            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(
                new Dictionary<string, VariableValue> { [ComponentProcessor.EphemeralPropertyName] = new() { Value = "true" } },
                new EnvironmentApiModel { IsEphemeral = true, EnvironmentIsProd = true }));
            Assert.IsTrue(TerraformDispatcher.IsEphemeralOperation(
                new Dictionary<string, VariableValue>(),
                new EnvironmentApiModel { IsEphemeral = true }));
            Assert.IsTrue(TerraformDispatcher.IsEphemeralOperation(true, false));
            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(true, true));
            Assert.IsFalse(TerraformDispatcher.IsEphemeralOperation(false, false));

            var noEnvRequests = Substitute.For<IRequestsPersistentSource>();
            noEnvRequests.UpdateResultStatus(Arg.Any<DeploymentResultApiModel>(), Arg.Any<DeploymentResultStatus>(), Arg.Any<DeploymentResultStatus>())
                .Returns(true);
            noEnvRequests.GetSubmittedScript(1, 2).Returns(new ScriptApiModel { Path = "deploy.ps1", NonProdOnly = true });
            var noEnvTerraform = Substitute.For<ITerraformDispatcher>();
            noEnvTerraform.Dispatch(
                Arg.Any<ComponentApiModel>(), Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<System.Text.StringBuilder>(),
                Arg.Any<TerraformRunnerOperations>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var procWithoutEnvSource = new ComponentProcessor(
                Substitute.For<IScriptDispatcher>(),
                noEnvTerraform,
                noEnvRequests,
                Substitute.For<IComponentsPersistentSource>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<ComponentProcessor>>(),
                null);
            procWithoutEnvSource.DeployComponent(
                new ComponentApiModel { ComponentId = 1, ComponentName = "TF", ComponentType = ComponentType.Terraform },
                new DeploymentResultApiModel { Id = 99, RequestId = 1, Status = DeploymentResultStatus.Pending.Value },
                1, false, 1, false, "env", "unused", new Dictionary<string, VariableValue>(), CancellationToken.None);
            procWithoutEnvSource.DeployComponent(
                new ComponentApiModel { ComponentId = 2, ComponentName = "PS", ComponentType = ComponentType.PowerShell, ScriptPath = "deploy.ps1" },
                new DeploymentResultApiModel { Id = 100, RequestId = 1, Status = DeploymentResultStatus.Pending.Value },
                1, false, 1, true, "env", "unused", new Dictionary<string, VariableValue>(), CancellationToken.None);
        }
    }
}
