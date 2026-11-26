using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.RequestSnapshotsAndLifecycle
{
    [Binding]
    public class RequestSnapshotAndLifecycleTests
    {
        private bool _restartAfterPropertyUpdateExecuted;
        private bool _cancelInFlightExecuted;
        private bool _snapshotReplayExecuted;

        [Given("the gate property is set to fail so that the initial deployment request fails")]
        public void GivenTheGatePropertyIsSetToFail()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user submits the deployment request, waits for Failed status, updates the gate property to pass, and restarts the request")]
        public void WhenTheUserSubmitsFailingRequestUpdatesPropertyAndRestarts()
        {
            RestartDeploymentRequest_FailedComponentAfterPropertyUpdate_CompletesOnSecondAttempt();
            _restartAfterPropertyUpdateExecuted = true;
        }

        [Then("the restarted deployment request executes a second attempt and transitions to Completed")]
        [Then("the prior failed attempt history is preserved alongside the completed retry")]
        public void ThenRestartedRequestCompletesAndPreservesAttemptHistory()
        {
            Assert.IsTrue(_restartAfterPropertyUpdateExecuted);
        }

        [Given("a long-running deployment request is submitted on the live child environment")]
        public void GivenALongRunningDeploymentRequestIsSubmitted()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user cancels the deployment request via PUT Request cancel while it is Pending or Running")]
        public void WhenTheUserCancelsTheInFlightDeploymentRequest()
        {
            CancelDeploymentRequest_InFlightExecution_TransitionsToCancelled();
            _cancelInFlightExecuted = true;
        }

        [Then("the deployment request transitions to Cancelled and stops executing")]
        public void ThenTheDeploymentRequestTransitionsToCancelled()
        {
            Assert.IsTrue(_cancelInFlightExecuted);
        }

        [Given("a deployment request is submitted and completed using version 1 of a project component script")]
        public void GivenADeploymentRequestIsSubmittedAndCompletedUsingVersion1()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the project owner edits the component script definition to version 2 and restarts the original deployment request while also submitting a new request")]
        public void WhenTheProjectOwnerEditsTheScriptDefinitionAndRestartsOriginalWhileSubmittingNew()
        {
            RestartDeploymentRequest_AfterProjectScriptDefinitionEdit_ReplaysOriginalSnapshotWhileNewRequestRunsUpdatedScript();
            _snapshotReplayExecuted = true;
        }

        [Then("the restarted original request replays the immutable version 1 snapshot from when it was submitted")]
        [Then("the newly submitted deployment request executes the updated version 2 script definition")]
        public void ThenRestartedRequestReplaysSnapshotWhileNewRequestRunsUpdatedScript()
        {
            Assert.IsTrue(_snapshotReplayExecuted);
        }

        public void RestartDeploymentRequest_FailedComponentAfterPropertyUpdate_CompletesOnSecondAttempt()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var shouldFailProp = "AutoReg_ShouldFail";

            EnsurePropertyDefinition(caller, shouldFailProp, secure: false);
            UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "true");

            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildNum = BuildFolderName,
                BuildUrl = buildUri,
                Components = new List<string> { FailThenPassComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.Id);

            var failedState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
            Assert.IsTrue(
                string.Equals(failedState.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(failedState.Status, "Errored", StringComparison.OrdinalIgnoreCase),
                $"Expected request {created.Id} to fail when AutoReg_ShouldFail=true, but got '{failedState.Status}'.");

            UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "false");

            var restartResp = caller.ExecuteRaw("Request/restart", Method.Post, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, restartResp.StatusCode, $"POST /Request/restart failed: {restartResp.Content}");

            var completedState = WaitForRequestStatusAfterRestart(caller, created.Id, TimeSpan.FromMinutes(3));
            Assert.AreEqual(
                "Completed",
                completedState.Status,
                $"Expected restarted request {created.Id} to reach Completed after property fix, but got '{completedState.Status}'.");

            var attemptsResp = caller.ExecuteRaw($"Request/{created.Id}/attempts", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, attemptsResp.StatusCode, attemptsResp.Content);
            StringAssert.Contains(attemptsResp.Content ?? string.Empty, FailThenPassComponentName);
        }

        public void CancelDeploymentRequest_InFlightExecution_TransitionsToCancelled()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildNum = BuildFolderName,
                BuildUrl = buildUri,
                Components = new List<string> { CancelWaitComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.Id);

            var runningDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < runningDeadline)
            {
                var statusResp = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                if (statusResp.StatusCode == HttpStatusCode.OK)
                {
                    var statusModel = DorcApiClient.Deserialize<DeploymentRequestApiModel>(statusResp.Content);
                    if (string.Equals(statusModel?.Status, "Running", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                Thread.Sleep(500);
            }

            var cancelResp = caller.ExecuteRaw("Request/cancel", Method.Put, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, cancelResp.StatusCode, $"PUT /Request/cancel failed: {cancelResp.Content}");

            var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(2));
            Assert.AreEqual(
                "Cancelled",
                finalState.Status,
                $"Expected cancelled request {created.Id} to reach Cancelled, but got '{finalState.Status}'.");

            var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
            var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
            foreach (var result in results)
            {
                Assert.AreNotEqual(
                    "Completed",
                    result.Status,
                    $"Cancelled request {created.Id} must never promote component '{result.ComponentName}' to Completed.");
            }

            var cancelLog = GetCombinedRequestLog(caller, created.Id);
            Assert.IsFalse(
                cancelLog.Contains("Finished wait.", StringComparison.Ordinal),
                $"Cancelled request {created.Id} must terminate before the wait script finishes. Log: {cancelLog}");
        }

        public void RestartDeploymentRequest_AfterProjectScriptDefinitionEdit_ReplaysOriginalSnapshotWhileNewRequestRunsUpdatedScript()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);

            var v1ScriptPath = Path.Join(RelativeScriptSubfolder, "snapshot-v1.ps1");
            var v2ScriptPath = Path.Join(RelativeScriptSubfolder, "snapshot-v2.ps1");

            try
            {
                UpdateComponentScriptPath(caller, project.ProjectId, SnapshotComponentName, v1ScriptPath);

                var request1Payload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string> { SnapshotComponentName }
                };
                var create1Resp = caller.ExecuteRaw("Request", Method.Post, body: request1Payload);
                Assert.AreEqual(HttpStatusCode.OK, create1Resp.StatusCode, create1Resp.Content);
                var created1 = DorcApiClient.Deserialize<RequestStatusDto>(create1Resp.Content);
                Assert.IsNotNull(created1);
                Assert.IsGreaterThan(0, created1.Id);
                Assert.IsTrue(
                    new[] { "Pending", "Running", "Completed" }.Contains(created1.Status, StringComparer.OrdinalIgnoreCase),
                    $"Expected POST /Request initial status to be Pending/Running/Completed, got '{created1.Status}'.");

                var statusLookupResp = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created1.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, statusLookupResp.StatusCode, statusLookupResp.Content);
                var statusLookup = DorcApiClient.Deserialize<DeploymentRequestApiModel>(statusLookupResp.Content);
                Assert.IsNotNull(statusLookup);
                Assert.AreEqual(created1.Id, statusLookup.Id);
                Assert.AreEqual(LiveProjectName, statusLookup.Project);
                Assert.AreEqual(LiveChildEnvName, statusLookup.EnvironmentName);

                var requestLookupResp = caller.ExecuteRaw("Request", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = created1.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, requestLookupResp.StatusCode, requestLookupResp.Content);
                var requestLookup = DorcApiClient.Deserialize<RequestStatusDto>(requestLookupResp.Content);
                Assert.IsNotNull(requestLookup);
                Assert.AreEqual(created1.Id, requestLookup.Id);
                Assert.IsTrue(
                    new[] { "Pending", "Running", "Completed" }.Contains(requestLookup.Status, StringComparer.OrdinalIgnoreCase),
                    $"Expected GET /Request?id={created1.Id} status to be Pending/Running/Completed, got '{requestLookup.Status}'.");

                var final1 = WaitForTerminalRequestStatus(caller, created1.Id, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", final1.Status, $"Expected Request #1 ({created1.Id}) to complete.");
                var log1Initial = GetCombinedRequestLog(caller, created1.Id);
                StringAssert.Contains(log1Initial, "IMMUTABLE_DEF_VERSION_V1_EXECUTED");
                StringAssert.Contains(log1Initial, "snapshot-v1.ps1");

                UpdateComponentScriptPath(caller, project.ProjectId, SnapshotComponentName, v2ScriptPath);

                var restartResp = caller.ExecuteRaw("Request/restart", Method.Post, new Dictionary<string, string>
                {
                    ["requestId"] = created1.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, restartResp.StatusCode, restartResp.Content);
                var restarted1 = WaitForRequestStatusAfterRestart(caller, created1.Id, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", restarted1.Status);
                var log1Restarted = GetCombinedRequestLog(caller, created1.Id);
                StringAssert.Contains(log1Restarted, "IMMUTABLE_DEF_VERSION_V1_EXECUTED");
                StringAssert.Contains(log1Restarted, "snapshot-v1.ps1");
                Assert.IsFalse(
                    log1Restarted.Contains("IMMUTABLE_DEF_VERSION_V2_EXECUTED", StringComparison.Ordinal),
                    $"Restarted request {created1.Id} must execute its snapshotted v1 definition, not the mutated v2 script. Log: {log1Restarted}");

                var create2Resp = caller.ExecuteRaw("Request", Method.Post, body: request1Payload);
                Assert.AreEqual(HttpStatusCode.OK, create2Resp.StatusCode, create2Resp.Content);
                var created2 = DorcApiClient.Deserialize<RequestStatusDto>(create2Resp.Content);
                Assert.IsNotNull(created2);

                var final2 = WaitForTerminalRequestStatus(caller, created2.Id, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", final2.Status);
                var log2 = GetCombinedRequestLog(caller, created2.Id);
                StringAssert.Contains(log2, "IMMUTABLE_DEF_VERSION_V2_EXECUTED");
                StringAssert.Contains(log2, "snapshot-v2.ps1");

                // Verify GET /Request/Components?projectId={projectId}
                var reqCompsResp = caller.ExecuteRaw("Request/Components", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, reqCompsResp.StatusCode, $"GET /Request/Components failed: {reqCompsResp.Content}");
                var reqComps = DorcApiClient.Deserialize<List<DeployComponentDto>>(reqCompsResp.Content);
                Assert.IsNotNull(reqComps);
                var snapshotComp = reqComps.FirstOrDefault(c => string.Equals(c.Name, SnapshotComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(snapshotComp, $"Expected GET /Request/Components to include '{SnapshotComponentName}'.");

                // Verify GET /DeploymentDefinitions and GET /DeploymentDefinitions/history
                var defsResp = caller.ExecuteRaw("DeploymentDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["componentType"] = "PowerShell",
                    ["search"] = SnapshotComponentName,
                    ["page"] = "1",
                    ["limit"] = "50"
                });
                Assert.AreEqual(HttpStatusCode.OK, defsResp.StatusCode, $"GET /DeploymentDefinitions failed: {defsResp.Content}");
                var defsPage = DorcApiClient.Deserialize<GetDeploymentDefinitionsResponse>(defsResp.Content);
                Assert.IsNotNull(defsPage);
                Assert.IsNotNull(defsPage.Items);
                var snapshotDef = defsPage.Items.FirstOrDefault(d =>
                    d.Component != null && string.Equals(d.Component.ComponentName, SnapshotComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(snapshotDef);
                Assert.IsNotNull(snapshotDef.Component);
                var snapshotDefComponentId = snapshotDef.Component.ComponentId;
                Assert.IsTrue(snapshotDefComponentId.HasValue, $"Expected GET /DeploymentDefinitions to include '{SnapshotComponentName}'.");

                var defHistoryResp = caller.ExecuteRaw("DeploymentDefinitions/history", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["componentId"] = snapshotDefComponentId.Value.ToString(),
                    ["page"] = "1",
                    ["limit"] = "50"
                });
                Assert.AreEqual(HttpStatusCode.OK, defHistoryResp.StatusCode, $"GET /DeploymentDefinitions/history failed: {defHistoryResp.Content}");
                var defHistoryPage = DorcApiClient.Deserialize<GetDeploymentDefinitionHistoryResponse>(defHistoryResp.Content);
                Assert.IsNotNull(defHistoryPage);
                Assert.IsNotNull(defHistoryPage.Items);
                Assert.IsGreaterThanOrEqualTo(2, defHistoryPage.Items.Count, "Expected at least 2 definition history versions (V1 and V2) for SnapshotComponent.");

                // Verify GET /Request/{requestId}/attempts/log?deploymentResultId={id}
                var results1Resp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created1.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, results1Resp.StatusCode, results1Resp.Content);
                var results1 = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(results1Resp.Content) ?? new List<DeploymentResultApiModel>();
                Assert.IsNotEmpty(results1);
                var attemptLogResp = caller.ExecuteRaw($"Request/{created1.Id}/attempts/log", Method.Get, new Dictionary<string, string>
                {
                    ["deploymentResultId"] = results1[0].Id.ToString()
                });
                Assert.IsTrue(
                    attemptLogResp.StatusCode == HttpStatusCode.OK || attemptLogResp.StatusCode == HttpStatusCode.NotFound,
                    $"Unexpected status from GET /Request/{created1.Id}/attempts/log: {attemptLogResp.StatusCode} {attemptLogResp.Content}");

                // Verify PUT /Request/pause and PUT /Request/resume contract
                var pauseResp = caller.ExecuteRaw("Request/pause", Method.Put, new Dictionary<string, string>
                {
                    ["requestId"] = created2.Id.ToString()
                });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    pauseResp.StatusCode,
                    $"Pausing a terminal Completed request (or when PauseDeploymentEnabled is false) must return 400 BadRequest: {pauseResp.Content}");

                var resumeResp = caller.ExecuteRaw("Request/resume", Method.Put, new Dictionary<string, string>
                {
                    ["requestId"] = created2.Id.ToString()
                });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    resumeResp.StatusCode,
                    $"Resuming a non-Paused request (or when PauseDeploymentEnabled is false) must return 400 BadRequest: {resumeResp.Content}");
            }
            finally
            {
                UpdateComponentScriptPath(caller, project.ProjectId, SnapshotComponentName, v1ScriptPath);
            }
        }
    }
}
