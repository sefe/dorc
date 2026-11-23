using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance
{
    /// <summary>
    /// Live-environment end-to-end user-journey acceptance tests executed against the deployed DOrc stack
    /// (Dorc.Api + IdentityServer OAuth + Active Directory + SQL Server + Dorc.Monitor Windows Service + Dorc.Runner).
    /// Pure controller CRUD, RBAC, encryption, and UI validation run in regular isolated CI suites;
    /// this suite exercises full end-user workflows that require the live Windows services and runner pipeline.
    /// </summary>
    [TestClass]
    [TestCategory("LiveDvAcceptance")]
    [TestCategory("LiveEnvironment")]
    public class DeploymentRequestTests
    {
        private const string LiveProjectName = "DOrc_Live_Acceptance_Project";
        private const string LiveParentEnvName = "DOrc_Live_Acceptance_Parent_Env";
        private const string LiveChildEnvName = "DOrc_Live_Acceptance_Child_Env";
        private const string VerifyPropsComponentName = "DOrc_Live_Acceptance_VerifyProps";
        private const string FailThenPassComponentName = "DOrc_Live_Acceptance_FailThenPass";
        private const string DownstreamSkippedComponentName = "DOrc_Live_Acceptance_Z_DownstreamSkipped";
        private const string CancelWaitComponentName = "DOrc_Live_Acceptance_CancelWait";
        private const string RelativeScriptSubfolder = @"Test\DOrc_Live_Acceptance";
        private const string BuildFolderName = "DOrc Regression Testing";

        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled()
        {
            var enabled = Environment.GetEnvironmentVariable("DORC_LIVE_DV_ENABLED");
            var configured = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            var configuredDorcBaseUrl = configured.GetSection("DorcApi")["BaseUrl"];
            var baseUrl = !string.IsNullOrWhiteSpace(configuredDorcBaseUrl)
                ? configuredDorcBaseUrl
                : configured.GetSection("AppSettings")["BaseAddress"];

            var hasConfiguredLiveUrl = !string.IsNullOrWhiteSpace(baseUrl)
                && !baseUrl.Contains("DEPLOYAPI.ENDPOINT", StringComparison.OrdinalIgnoreCase);

            if (!hasConfiguredLiveUrl &&
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(
                    "Live acceptance tests require DORC_LIVE_DV_ENABLED=true (or a configured DorcApi:BaseUrl) and a reachable DOrc environment.");
            }
        }

        [TestMethod]
        public void CreateDeploymentRequest_UpdatedPropertyHierarchyInheritanceAndSecretRedaction_InjectsResolvedValuesIntoRunner()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureLiveAcceptanceFixture(caller);
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

                // End-user action: update the existing property value on the child environment via PUT /PropertyValues
                // and verify the updated value (not the initial value) is injected into the live runner.
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

                // Verify cleartext secret never leaks in API status responses, OpenSearch logs, or local runner log files
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
                        // Ignore if test runner service account lacks local log folder ACLs
                    }
                    catch (IOException)
                    {
                        // Ignore transient file lock
                    }
                }
            }
            finally
            {
                DeletePropertiesBestEffort(caller, globalProp, parentProp, childUpdatedProp, overrideProp, secretProp);
            }
        }

        [TestMethod]
        public void RestartDeploymentRequest_FailedComponentAfterPropertyUpdate_CompletesOnSecondAttempt()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureLiveAcceptanceFixture(caller);
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

            // 1. First execution should fail because AutoReg_ShouldFail == "true"
            var failedState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
            Assert.IsTrue(
                string.Equals(failedState.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(failedState.Status, "Errored", StringComparison.OrdinalIgnoreCase),
                $"Expected request {created.Id} to fail when AutoReg_ShouldFail=true, but got '{failedState.Status}'.");

            // 2. End-user updates AutoReg_ShouldFail to "false" and triggers POST /Request/restart
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
        public void CreateDeploymentRequest_MultiComponentWithStopOnFailure_HaltsDownstreamComponent()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureLiveAcceptanceFixture(caller);
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

        [TestMethod]
        public void CancelDeploymentRequest_InFlightExecution_TransitionsToCancelled()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureLiveAcceptanceFixture(caller);

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

            // Wait briefly for monitor pickup or request it to cancel immediately while Pending/Running
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
        }

        [TestMethod]
        public void GetRefDataAndAccessControl_OAuthActiveDirectoryAndEnvironmentAcl_EnforcesOwnerIntegrity()
        {
            using var caller = new DorcApiClient();
            EnsureLiveAcceptanceFixture(caller);

            var cfgResp = caller.ExecuteRaw("ApiConfig", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, cfgResp.StatusCode, cfgResp.Content);
            StringAssert.Contains(cfgResp.Content ?? string.Empty, "AuthenticationScheme");

            var metaResp = caller.ExecuteRaw("Metadata", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, metaResp.StatusCode, metaResp.Content);
            Assert.IsFalse(string.IsNullOrWhiteSpace(metaResp.Content));

            var usersResp = caller.ExecuteRaw("DirectorySearch/users", Method.Get, new Dictionary<string, string>
            {
                ["userSearchCriteria"] = "apolezhaev"
            });
            Assert.AreEqual(HttpStatusCode.OK, usersResp.StatusCode, usersResp.Content);

            var groupsResp = caller.ExecuteRaw("DirectorySearch/groups", Method.Get, new Dictionary<string, string>
            {
                ["groupSearchCriteria"] = "DOrc"
            });
            Assert.AreEqual(HttpStatusCode.OK, groupsResp.StatusCode, groupsResp.Content);

            var parentEnv = GetEnvironmentByName(caller, LiveParentEnvName);
            Assert.IsNotNull(parentEnv);

            var aclResp = caller.ExecuteRaw("AccessControl", Method.Get, new Dictionary<string, string>
            {
                ["accessControlType"] = Dorc.ApiModel.AccessControlType.Environment.ToString(),
                ["accessControlName"] = LiveParentEnvName
            });
            Assert.AreEqual(HttpStatusCode.OK, aclResp.StatusCode, aclResp.Content);
            var acl = DorcApiClient.Deserialize<AccessSecureApiModel>(aclResp.Content);
            Assert.IsNotNull(acl);

            // Verify last-owner protection on live ACL endpoint: attempting to strip all Owner privileges is rejected with 400
            var stripped = new AccessSecureApiModel
            {
                Name = acl.Name,
                ObjectId = acl.ObjectId,
                Type = acl.Type,
                UserEditable = acl.UserEditable,
                Privileges = (acl.Privileges ?? Array.Empty<AccessControlApiModel>())
                    .Select(p => new AccessControlApiModel
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Pid = p.Pid,
                        Allow = p.Allow & ~4,
                        Deny = p.Deny
                    })
                    .ToList()
            };
            var badAclResp = caller.ExecuteRaw("AccessControl", Method.Put, body: stripped);
            Assert.AreEqual(HttpStatusCode.BadRequest, badAclResp.StatusCode, "Removing the last owner must return 400 BadRequest.");
        }

        private static string EnsureLiveAcceptanceFixture(DorcApiClient caller)
        {
            var scriptRoot = GetConfiguredScriptRoot(caller);
            var scriptSubfolder = Path.Join(scriptRoot, RelativeScriptSubfolder);
            TryProvisionScriptsInScriptRoot(scriptSubfolder);

            var (ownerName, ownerId) = ResolveDefaultOwner(caller);
            var parentEnv = EnsureEnvironment(caller, LiveParentEnvName, ownerName, ownerId);
            var childEnv = EnsureEnvironment(caller, LiveChildEnvName, ownerName, ownerId);

            if (childEnv.ParentId != parentEnv.EnvironmentId)
            {
                var setParentResp = caller.ExecuteRaw(
                    "RefDataEnvironmentsDetails/SetParentForEnvironment",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["parentEnvId"] = parentEnv.EnvironmentId.ToString(),
                        ["childEnvId"] = childEnv.EnvironmentId.ToString()
                    });
                Assert.AreEqual(HttpStatusCode.OK, setParentResp.StatusCode, $"SetParentForEnvironment failed: {setParentResp.Content}");
            }

            var (dropRootUri, buildUri) = ResolveConfiguredArtefactDrop(caller);
            var project = EnsureProject(caller, LiveProjectName, dropRootUri);
            EnsureEnvironmentMappedToProject(caller, LiveProjectName, LiveParentEnvName);
            EnsureEnvironmentMappedToProject(caller, LiveProjectName, LiveChildEnvName);
            EnsureProjectComponents(caller, project.ProjectId);

            return buildUri;
        }

        private static (string DropRootUri, string BuildUri) ResolveConfiguredArtefactDrop(DorcApiClient caller)
        {
            var devOpsResp = caller.ExecuteRaw("RefDataProjects/DevOps", Method.Get);
            if (devOpsResp.StatusCode == HttpStatusCode.OK)
            {
                var devOpsProject = DorcApiClient.Deserialize<ProjectApiModel>(devOpsResp.Content);
                if (devOpsProject != null && !string.IsNullOrWhiteSpace(devOpsProject.ArtefactsUrl))
                {
                    var rootUri = devOpsProject.ArtefactsUrl.Split(';')[0].TrimEnd('/');
                    return (rootUri, $"{rootUri}/{BuildFolderName}");
                }
            }

            var fallbackRoot = @"C:\ProgramData\SEFE\DOrc\LiveAcceptanceDrop";
            var fallbackBuildDir = Path.Join(fallbackRoot, BuildFolderName);
            Directory.CreateDirectory(fallbackBuildDir);
            return (new Uri(fallbackRoot).AbsoluteUri, new Uri(fallbackBuildDir).AbsoluteUri);
        }

        private static void TryProvisionScriptsInScriptRoot(string scriptSubfolder)
        {
            try
            {
                if (!Directory.Exists(scriptSubfolder))
                {
                    Directory.CreateDirectory(scriptSubfolder);
                }

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "verify-properties.ps1"), @"
$ErrorActionPreference = 'Stop'
$cid = $AutoReg_CorrelationId
$globalName = ""AutoReg_Global_$cid""
$parentName = ""AutoReg_ParentInherited_$cid""
$childName  = ""AutoReg_ChildUpdated_$cid""
$overrideName = ""AutoReg_RequestOverride_$cid""
$secretName = ""AutoReg_SecretPassword_$cid""

$globalVal = [string](Get-Variable -Name $globalName -ValueOnly -ErrorAction SilentlyContinue)
$parentVal = [string](Get-Variable -Name $parentName -ValueOnly -ErrorAction SilentlyContinue)
$childVal  = [string](Get-Variable -Name $childName -ValueOnly -ErrorAction SilentlyContinue)
$overrideVal = [string](Get-Variable -Name $overrideName -ValueOnly -ErrorAction SilentlyContinue)
$secretVal = [string](Get-Variable -Name $secretName -ValueOnly -ErrorAction SilentlyContinue)

if ($globalVal -ne ""GlobalVal_$cid"") { throw ""GlobalVal mismatch: '$globalVal'"" }
if ($parentVal -ne ""ParentInheritedVal_$cid"") { throw ""ParentInheritedVal mismatch: '$parentVal'"" }
if ($childVal -ne ""UpdatedChildVal_$cid"") { throw ""ChildUpdatedVal mismatch: '$childVal'"" }
if ($overrideVal -ne ""OverriddenAtRequest_$cid"") { throw ""RequestOverrideVal mismatch: '$overrideVal'"" }
if ($secretVal -ne ""Sup3rS3cr3tValu3_$cid!"") { throw ""SecretVal decryption mismatch (length=$($secretVal.Length))"" }

# Intentionally write a line containing the secret to stdout to verify PowerShellScriptRunner redacts it to [REDACTED]
Write-Output ""Executing live acceptance verification for secret=$secretVal""
Write-Output ""VERIFIED_ALL_PROPERTIES_IN_RUNNER""
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "fail-then-pass.ps1"), @"
$ErrorActionPreference = 'Stop'
Write-Output ""AutoReg_ShouldFail is '$AutoReg_ShouldFail'""
if ($AutoReg_ShouldFail -eq 'true') {
    Write-Error 'Simulated deployment failure because AutoReg_ShouldFail=true'
    exit 1
}
Write-Output 'Deployment succeeded after property update and restart.'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "cancel-wait.ps1"), @"
Write-Output 'Starting cancellable wait script...'
Start-Sleep -Seconds 25
Write-Output 'Finished wait.'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "downstream-skipped.ps1"), @"
Write-Output 'Downstream component executed.'
");
            }
            catch (UnauthorizedAccessException)
            {
                // In ADO TestDOrcXX stages, the test runner service account (e.g. SVC-GMT-DV-DORAUTST)
                // may have read-only or restricted access to \\Itss.global\...\Scripts.*, while the scripts
                // are already provisioned on the share and executed by Dorc.Monitor (svc-live-deploy-nonp).
            }
            catch (IOException)
            {
                // Ignore if the UNC share is not directly writable from the test runner identity.
            }
        }

        private static void WriteScriptIfChanged(string path, string content)
        {
            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path);
                    if (string.Equals(existing, content, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
            }

            File.WriteAllText(path, content);
        }

        private static string GetConfiguredScriptRoot(DorcApiClient caller)
        {
            var configResp = caller.ExecuteRaw("RefDataConfig", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, configResp.StatusCode, $"GET /RefDataConfig failed: {configResp.Content}");
            var configs = DorcApiClient.Deserialize<List<ConfigValueApiModel>>(configResp.Content);
            var scriptRootCfg = configs?.FirstOrDefault(c => string.Equals(c.Key, "ScriptRoot", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(scriptRootCfg, "Expected ScriptRoot entry in GET /RefDataConfig.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(scriptRootCfg.Value), "Expected non-empty ScriptRoot value in RefDataConfig.");
            return scriptRootCfg.Value;
        }

        private static (string OwnerName, string OwnerId) ResolveDefaultOwner(DorcApiClient caller)
        {
            var envsResp = caller.ExecuteRaw("RefDataEnvironments", Method.Get);
            if (envsResp.StatusCode == HttpStatusCode.OK)
            {
                var envs = DorcApiClient.Deserialize<List<EnvironmentApiModel>>(envsResp.Content);
                var existingWithOwner = envs?.FirstOrDefault(e =>
                    e.Details != null &&
                    !string.IsNullOrWhiteSpace(e.Details.EnvironmentOwner) &&
                    !string.IsNullOrWhiteSpace(e.Details.EnvironmentOwnerId));
                if (existingWithOwner != null)
                {
                    return (existingWithOwner.Details.EnvironmentOwner, existingWithOwner.Details.EnvironmentOwnerId);
                }
            }

            var usersResp = caller.ExecuteRaw("DirectorySearch/users", Method.Get, new Dictionary<string, string>
            {
                ["userSearchCriteria"] = "svc"
            });
            if (usersResp.StatusCode == HttpStatusCode.OK)
            {
                var users = DorcApiClient.Deserialize<List<UserElementApiModel>>(usersResp.Content);
                var first = users?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Pid));
                if (first != null)
                {
                    return (first.DisplayName ?? first.Username ?? "DirectoryIdentity", first.Pid);
                }
            }

            return ("DOrcAutomatedAcceptance", "S-1-5-18");
        }

        private static EnvironmentApiModel? GetEnvironmentByName(DorcApiClient caller, string envName)
        {
            var getResp = caller.ExecuteRaw("RefDataEnvironments", Method.Get, new Dictionary<string, string>
            {
                ["env"] = envName
            });
            if (getResp.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }
            var list = DorcApiClient.Deserialize<List<EnvironmentApiModel>>(getResp.Content);
            return list?.FirstOrDefault(e => e != null && string.Equals(e.EnvironmentName, envName, StringComparison.OrdinalIgnoreCase));
        }

        private static EnvironmentApiModel EnsureEnvironment(DorcApiClient caller, string envName, string ownerName, string ownerId)
        {
            var existing = GetEnvironmentByName(caller, envName);
            if (existing != null && existing.EnvironmentId > 0)
            {
                return existing;
            }

            var createModel = new EnvironmentApiModel
            {
                EnvironmentName = envName,
                EnvironmentSecure = false,
                EnvironmentIsProd = false,
                Details = new EnvironmentDetailsApiModel
                {
                    EnvironmentOwner = ownerName,
                    EnvironmentOwnerId = ownerId,
                    Description = "Automated live acceptance environment",
                    FileShare = string.Empty,
                    Notes = "Managed by Tests.Acceptance",
                    RestoredFromSourceDb = string.Empty,
                    ThinClient = string.Empty
                }
            };

            var postResp = caller.ExecuteRaw("RefDataEnvironments", Method.Post, body: createModel);
            Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode, $"Failed creating environment '{envName}': {postResp.Content}");
            var created = DorcApiClient.Deserialize<EnvironmentApiModel>(postResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.EnvironmentId, $"Expected EnvironmentId > 0 for '{envName}', got {postResp.Content}");
            return created;
        }

        private static ProjectApiModel EnsureProject(DorcApiClient caller, string projectName, string artefactsUrl)
        {
            var getResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(projectName)}", Method.Get);
            if (getResp.StatusCode == HttpStatusCode.OK)
            {
                var existing = DorcApiClient.Deserialize<ProjectApiModel>(getResp.Content);
                if (existing != null && existing.ProjectId > 0)
                {
                    if (!string.Equals(existing.ArtefactsUrl, artefactsUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.ArtefactsUrl = artefactsUrl;
                        existing.ArtefactsSubPaths = ".";
                        existing.ArtefactsBuildRegex = string.Empty;
                        var putResp = caller.ExecuteRaw("RefDataProjects", Method.Put, body: existing);
                        Assert.AreEqual(HttpStatusCode.OK, putResp.StatusCode, $"Failed updating project '{projectName}': {putResp.Content}");
                    }
                    return existing;
                }
            }

            var createProject = new ProjectApiModel
            {
                ProjectName = projectName,
                ProjectDescription = "Automated live acceptance project",
                ArtefactsUrl = artefactsUrl,
                ArtefactsSubPaths = ".",
                ArtefactsBuildRegex = string.Empty
            };
            var postResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: createProject);
            Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode, $"Failed creating project '{projectName}': {postResp.Content}");
            var created = DorcApiClient.Deserialize<ProjectApiModel>(postResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.ProjectId);
            return created;
        }

        private static void EnsureEnvironmentMappedToProject(DorcApiClient caller, string projectName, string envName)
        {
            var getResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Get, new Dictionary<string, string>
            {
                ["project"] = projectName,
                ["includeRead"] = "true"
            });
            if (getResp.StatusCode == HttpStatusCode.OK)
            {
                var mapped = DorcApiClient.Deserialize<TemplateApiModel<EnvironmentApiModel>>(getResp.Content);
                if (mapped?.Items != null && mapped.Items.Any(e => string.Equals(e.EnvironmentName, envName, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }

            var postResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
            {
                ["project"] = projectName,
                ["environment"] = envName
            });
            Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode, $"Failed mapping '{envName}' to '{projectName}': {postResp.Content}");
        }

        private static void EnsureProjectComponents(DorcApiClient caller, int projectId)
        {
            var getResp = caller.ExecuteRaw($"RefData/{projectId}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode, $"GET /RefData/{projectId} failed: {getResp.Content}");
            var refData = DorcApiClient.Deserialize<RefDataApiModel>(getResp.Content);
            Assert.IsNotNull(refData);
            refData.Components ??= new List<ComponentApiModel>();

            var required = new (string Name, string ScriptName)[]
            {
                (VerifyPropsComponentName, Path.Join(RelativeScriptSubfolder, "verify-properties.ps1")),
                (FailThenPassComponentName, Path.Join(RelativeScriptSubfolder, "fail-then-pass.ps1")),
                (DownstreamSkippedComponentName, Path.Join(RelativeScriptSubfolder, "downstream-skipped.ps1")),
                (CancelWaitComponentName, Path.Join(RelativeScriptSubfolder, "cancel-wait.ps1"))
            };

            var psV7 = PowerShellVersion.V7.ToVersionString();
            var modified = false;
            foreach (var (name, scriptName) in required)
            {
                var existing = refData.Components.FirstOrDefault(c =>
                    string.Equals(c.ComponentName, name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    refData.Components.Add(new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = name,
                        ScriptPath = scriptName,
                        IsEnabled = true,
                        NonProdOnly = true,
                        StopOnFailure = true,
                        ComponentType = ComponentType.PowerShell,
                        PSVersion = psV7,
                        Children = new List<ComponentApiModel>()
                    });
                    modified = true;
                }
                else if (!string.Equals(existing.ScriptPath, scriptName, StringComparison.OrdinalIgnoreCase) ||
                         existing.PSVersion != psV7 ||
                         !existing.IsEnabled)
                {
                    existing.ScriptPath = scriptName;
                    existing.PSVersion = psV7;
                    existing.IsEnabled = true;
                    modified = true;
                }
            }

            if (modified)
            {
                var putResp = caller.ExecuteRaw("RefData", Method.Put, body: refData);
                Assert.AreEqual(HttpStatusCode.OK, putResp.StatusCode, $"PUT /RefData failed: {putResp.Content}");
            }
        }

        private static void EnsurePropertyDefinition(DorcApiClient caller, string propName, bool secure)
        {
            var getResp = caller.ExecuteRaw($"Properties/id={Uri.EscapeDataString(propName)}", Method.Get);
            if (getResp.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            var postResp = caller.ExecuteRaw("Properties", Method.Post, body: new[]
            {
                new PropertyApiModel
                {
                    Name = propName,
                    Secure = secure,
                    IsArray = false
                }
            });
            Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode, $"POST /Properties for '{propName}' failed: {postResp.Content}");
        }

        private static void UpsertPropertyValue(DorcApiClient caller, string propName, bool secure, string? envFilter, string value)
        {
            var query = new Dictionary<string, string> { ["propertyName"] = propName };
            if (!string.IsNullOrWhiteSpace(envFilter))
            {
                query["environmentName"] = envFilter;
            }

            var getResp = caller.ExecuteRaw("PropertyValues", Method.Get, query);
            if (getResp.StatusCode == HttpStatusCode.OK)
            {
                var existingList = DorcApiClient.Deserialize<List<PropertyValueDto>>(getResp.Content);
                var match = existingList?.FirstOrDefault(pv =>
                    string.Equals(pv.PropertyValueFilter ?? string.Empty, envFilter ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.Value = value;
                    var putResp = caller.ExecuteRaw("PropertyValues", Method.Put, body: new[] { match });
                    Assert.AreEqual(HttpStatusCode.OK, putResp.StatusCode, $"PUT /PropertyValues for '{propName}' failed: {putResp.Content}");
                    return;
                }
            }

            var postResp = caller.ExecuteRaw("PropertyValues", Method.Post, body: new[]
            {
                new PropertyValueDto
                {
                    Property = new PropertyApiModel
                    {
                        Name = propName,
                        Secure = secure,
                        IsArray = false
                    },
                    PropertyValueFilter = envFilter,
                    Value = value
                }
            });
            Assert.AreEqual(HttpStatusCode.OK, postResp.StatusCode, $"POST /PropertyValues for '{propName}' failed: {postResp.Content}");
        }

        private static void DeletePropertiesBestEffort(DorcApiClient caller, params string[] propNames)
        {
            try
            {
                caller.ExecuteRaw("Properties", Method.Delete, body: propNames);
            }
            catch (HttpRequestException)
            {
                // Best-effort cleanup
            }
            catch (InvalidOperationException)
            {
                // Best-effort cleanup
            }
        }

        private static DeploymentRequestApiModel WaitForTerminalRequestStatus(DorcApiClient caller, int requestId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            DeploymentRequestApiModel? latest = null;
            while (DateTime.UtcNow < deadline)
            {
                var resp = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = requestId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);
                latest = DorcApiClient.Deserialize<DeploymentRequestApiModel>(resp.Content);
                var status = latest?.Status ?? string.Empty;
                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Errored", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "WaitingConfirmation", StringComparison.OrdinalIgnoreCase))
                {
                    return latest!;
                }

                Thread.Sleep(1500);
            }

            Assert.Fail($"Timed out after {timeout.TotalSeconds}s waiting for request {requestId} to reach a terminal state. Last status: '{latest?.Status}'.");
            return latest!;
        }

        private static DeploymentRequestApiModel WaitForRequestStatusAfterRestart(DorcApiClient caller, int requestId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            DeploymentRequestApiModel? latest = null;
            var sawNonFailed = false;
            while (DateTime.UtcNow < deadline)
            {
                var resp = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = requestId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);
                latest = DorcApiClient.Deserialize<DeploymentRequestApiModel>(resp.Content);
                var status = latest?.Status ?? string.Empty;
                if (!string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "Errored", StringComparison.OrdinalIgnoreCase))
                {
                    sawNonFailed = true;
                }

                if (sawNonFailed &&
                    (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "Errored", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)))
                {
                    return latest!;
                }

                Thread.Sleep(1500);
            }

            Assert.Fail($"Timed out after {timeout.TotalSeconds}s waiting for restarted request {requestId} to complete. Last status: '{latest?.Status}'.");
            return latest!;
        }
    }
}
