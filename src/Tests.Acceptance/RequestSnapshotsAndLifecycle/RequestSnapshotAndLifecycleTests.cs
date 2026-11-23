using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.RequestSnapshotsAndLifecycle
{
    [TestClass]
    [TestCategory("RequestSnapshotsAndLifecycle")]
    public class RequestSnapshotAndLifecycleTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
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

        [TestMethod]
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
        }

        [TestMethod]
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
            }
            finally
            {
                UpdateComponentScriptPath(caller, project.ProjectId, SnapshotComponentName, v1ScriptPath);
            }
        }
    }
}
