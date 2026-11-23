using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.PowerShellRunnerExecution
{
    [TestClass]
    [TestCategory("PowerShellRunnerExecution")]
    public class PowerShellRunnerExecutionTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void CreateDeploymentRequest_UpdatedPropertyHierarchyInheritanceAndSecretRedaction_InjectsResolvedValuesIntoRunner()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var correlationId = Guid.NewGuid().ToString("N")[..10];

            var globalProp = $"AutoReg_Global_{correlationId}";
            var parentProp = $"AutoReg_ParentInherited_{correlationId}";
            var childUpdatedProp = $"AutoReg_ChildUpdated_{correlationId}";
            var overrideProp = $"AutoReg_RequestOverride_{correlationId}";
            var secretProp = $"AutoReg_SecretPassword_{correlationId}";
            var correlationProp = "AutoReg_CorrelationId";

            var expectedGlobalVal = $"GlobalVal_{correlationId}";
            var expectedParentVal = $"ParentInheritedVal_{correlationId}";
            var initialChildVal = $"InitialChildVal_{correlationId}";
            var updatedChildVal = $"UpdatedChildVal_{correlationId}";
            var defaultOverrideVal = $"DefaultEnvVal_{correlationId}";
            var expectedOverrideVal = $"OverriddenAtRequest_{correlationId}";
            var cleartextSecret = $"Sup3rS3cr3tValu3_{correlationId}!";

            try
            {
                EnsurePropertyDefinition(caller, globalProp, secure: false);
                EnsurePropertyDefinition(caller, parentProp, secure: false);
                EnsurePropertyDefinition(caller, childUpdatedProp, secure: false);
                EnsurePropertyDefinition(caller, overrideProp, secure: false);
                EnsurePropertyDefinition(caller, secretProp, secure: true);
                EnsurePropertyDefinition(caller, correlationProp, secure: false);

                UpsertPropertyValue(caller, globalProp, secure: false, envFilter: null, value: expectedGlobalVal);
                UpsertPropertyValue(caller, parentProp, secure: false, envFilter: LiveParentEnvName, value: expectedParentVal);
                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: initialChildVal);
                UpsertPropertyValue(caller, overrideProp, secure: false, envFilter: LiveChildEnvName, value: defaultOverrideVal);
                UpsertPropertyValue(caller, secretProp, secure: true, envFilter: LiveChildEnvName, value: cleartextSecret);
                UpsertPropertyValue(caller, correlationProp, secure: false, envFilter: LiveChildEnvName, value: correlationId);

                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: updatedChildVal);

                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string> { VerifyPropsComponentName },
                    RequestProperties = new List<RequestProperty>
                    {
                        new RequestProperty
                        {
                            PropertyName = overrideProp,
                            PropertyValue = expectedOverrideVal
                        }
                    }
                };

                var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
                Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
                var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
                Assert.IsNotNull(created, $"Unexpected POST /Request body: {createResp.Content}");
                Assert.IsGreaterThan(0, created.Id, $"Expected positive RequestId, got {created.Id} ({createResp.Content})");

                var finalRequest = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                Assert.HasCount(1, results, $"Expected 1 component result for request {created.Id}.");

                var resultLogResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString(),
                    ["resultId"] = results[0].Id.ToString()
                });
                var resultLogText = !string.IsNullOrWhiteSpace(resultLogResp.Content)
                    ? resultLogResp.Content
                    : (results[0].Log ?? string.Empty);

                Assert.AreEqual(
                    "Completed",
                    finalRequest.Status,
                    $"Expected request {created.Id} to reach Completed, but got '{finalRequest.Status}'. Result status='{results[0].Status}', previewLog='{results[0].Log}', fullLog='{resultLogText}'");

                var combinedLog = $"{results[0].Log}\n{resultLogText}";
                StringAssert.Contains(
                    combinedLog,
                    "VERIFIED_ALL_PROPERTIES_IN_RUNNER",
                    $"Expected live runner to verify all resolved properties in-process. Full log: {combinedLog}");
                StringAssert.Contains(
                    combinedLog,
                    "[REDACTED]",
                    $"Expected PowerShellScriptRunner to redact secret value to '[REDACTED]' in stdout log. Full log: {combinedLog}");

                var requestRaw = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.IsFalse(
                    (requestRaw.Content ?? string.Empty).Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /RequestStatuses response.");
                Assert.IsFalse(
                    (resultsResp.Content ?? string.Empty).Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /ResultStatuses response.");
                Assert.IsFalse(
                    resultLogText.Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /ResultStatuses/Log response.");

                var runnerLogDir = @"C:\Log\DOrc\Deploy\Services\Requests";
                if (Directory.Exists(runnerLogDir))
                {
                    try
                    {
                        foreach (var logFile in Directory.GetFiles(runnerLogDir, $"*{created.Id}*"))
                        {
                            using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var sr = new StreamReader(fs);
                            var fileText = sr.ReadToEnd();
                            Assert.IsFalse(
                                fileText.Contains(cleartextSecret, StringComparison.Ordinal),
                                $"Cleartext secret leaked in runner log file '{logFile}'.");
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            finally
            {
                DeletePropertiesBestEffort(caller, globalProp, parentProp, childUpdatedProp, overrideProp, secretProp);
            }
        }

        [TestMethod]
        public void CreateDeploymentRequest_NetFrameworkRunnerPowerShell51_ExecutesDesktopEditionAndInjectsProperties()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var psVersionsResp = caller.ExecuteRaw("PowerShellVersions", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, psVersionsResp.StatusCode, psVersionsResp.Content);
            StringAssert.Contains(psVersionsResp.Content ?? string.Empty, "v5.1");

            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildNum = BuildFolderName,
                BuildUrl = buildUri,
                Components = new List<string> { NetFxRunnerComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.Id);

            var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
            var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
            var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
            Assert.HasCount(1, results);

            var resultLogResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString(),
                ["resultId"] = results[0].Id.ToString()
            });
            var combinedLog = $"{results[0].Log}\n{resultLogResp.Content}";

            Assert.AreEqual(
                "Completed",
                finalState.Status,
                $"Expected .NET Framework 4.8 (PS 5.1) request {created.Id} to reach Completed, but got '{finalState.Status}'. Log: {combinedLog}");
            StringAssert.Contains(
                combinedLog,
                "VERIFIED_NETFX_48_POWERSHELL_51_RUNNER",
                $"Expected Dorc.NetFramework.Runner to execute Windows PowerShell 5.1 Desktop edition. Log: {combinedLog}");
        }

        [TestMethod]
        public void CreateDeploymentRequest_MultiComponentWithStopOnFailure_HaltsDownstreamComponent()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var shouldFailProp = "AutoReg_ShouldFail";

            try
            {
                EnsurePropertyDefinition(caller, shouldFailProp, secure: false);
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "true");

                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string>
                    {
                        FailThenPassComponentName,
                        DownstreamSkippedComponentName
                    }
                };

                var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
                Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
                var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
                Assert.IsNotNull(created);
                Assert.IsGreaterThan(0, created.Id);

                var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                Assert.IsTrue(
                    string.Equals(finalState.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(finalState.Status, "Errored", StringComparison.OrdinalIgnoreCase),
                    $"Expected multi-component request {created.Id} to fail when upstream StopOnFailure component fails, but got '{finalState.Status}'.");

                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                var downstreamResult = results.FirstOrDefault(r =>
                    string.Equals(r.ComponentName, DownstreamSkippedComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(downstreamResult, "Expected result record for downstream component.");
                Assert.IsTrue(
                    string.Equals(downstreamResult.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(downstreamResult.Status, "Pending", StringComparison.OrdinalIgnoreCase),
                    $"Downstream component should be 'Cancelled' or 'Pending' when upstream StopOnFailure=true component fails, but got '{downstreamResult.Status}'.");
                Assert.IsFalse(
                    (downstreamResult.Log ?? string.Empty).Contains("Downstream component executed.", StringComparison.Ordinal),
                    "Downstream component script must not execute when upstream StopOnFailure=true component fails.");
            }
            finally
            {
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "false");
            }
        }
    }
}
