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
    public class DeploymentRequestAcceptanceTests
    {
        private const string LiveProjectName = "DOrc_Live_Acceptance_Project";
        private const string LiveParentEnvName = "DOrc_Live_Acceptance_Parent_Env";
        private const string LiveChildEnvName = "DOrc_Live_Acceptance_Child_Env";
        private const string VerifyPropsComponentName = "DOrc_Live_Acceptance_VerifyProps";
        private const string FailThenPassComponentName = "DOrc_Live_Acceptance_FailThenPass";
        private const string DownstreamSkippedComponentName = "DOrc_Live_Acceptance_DownstreamSkipped";
        private const string CancelWaitComponentName = "DOrc_Live_Acceptance_CancelWait";
        private const string DropRootPath = @"C:\ProgramData\SEFE\DOrc\LiveAcceptanceDrop";
        private const string BuildFolderName = "Build_1_0_0";

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
        public void EndToEndUserJourney_UpdatePropertyAndInheritanceAndSecretRedaction_InjectsResolvedValuesIntoLiveRunner()
        {
            using var caller = new DorcApiClient();
            var buildDir = EnsureLiveAcceptanceFixture(caller);
            var runToken = Guid.NewGuid().ToString("N")[..10];
            var outputJsonPath = Path.Combine(buildDir, $"resolved-props-{runToken}.json");

            var globalProp = $"AutoReg_Global_{runToken}";
            var parentProp = $"AutoReg_ParentInherited_{runToken}";
            var childUpdatedProp = $"AutoReg_ChildUpdated_{runToken}";
            var overrideProp = $"AutoReg_RequestOverride_{runToken}";
            var secretProp = $"AutoReg_SecretPassword_{runToken}";
            var runTokenProp = "AutoReg_RunToken";

            var expectedGlobalVal = $"GlobalVal_{runToken}";
            var expectedParentVal = $"ParentInheritedVal_{runToken}";
            var initialChildVal = $"InitialChildVal_{runToken}";
            var updatedChildVal = $"UpdatedChildVal_{runToken}";
            var defaultOverrideVal = $"DefaultEnvVal_{runToken}";
            var expectedOverrideVal = $"OverriddenAtRequest_{runToken}";
            var cleartextSecret = $"Sup3rS3cr3tValu3_{runToken}!";

            try
            {
                EnsurePropertyDefinition(caller, globalProp, secure: false);
                EnsurePropertyDefinition(caller, parentProp, secure: false);
                EnsurePropertyDefinition(caller, childUpdatedProp, secure: false);
                EnsurePropertyDefinition(caller, overrideProp, secure: false);
                EnsurePropertyDefinition(caller, secretProp, secure: true);
                EnsurePropertyDefinition(caller, runTokenProp, secure: false);

                UpsertPropertyValue(caller, globalProp, secure: false, envFilter: null, value: expectedGlobalVal);
                UpsertPropertyValue(caller, parentProp, secure: false, envFilter: LiveParentEnvName, value: expectedParentVal);
                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: initialChildVal);
                UpsertPropertyValue(caller, overrideProp, secure: false, envFilter: LiveChildEnvName, value: defaultOverrideVal);
                UpsertPropertyValue(caller, secretProp, secure: true, envFilter: LiveChildEnvName, value: cleartextSecret);
                UpsertPropertyValue(caller, runTokenProp, secure: false, envFilter: LiveChildEnvName, value: runToken);

                // End-user action: update the existing property value on the child environment via PUT /PropertyValues
                // and verify the updated value (not the initial value) is injected into the live runner.
                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: updatedChildVal);

                var buildUri = new Uri(buildDir).AbsoluteUri;
                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
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
                Assert.IsTrue(created.Id > 0, $"Expected positive RequestId, got {created.Id} ({createResp.Content})");

                var finalRequest = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                Assert.AreEqual(1, results.Count, $"Expected 1 component result for request {created.Id}.");

                var resultLogResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString(),
                    ["resultId"] = results[0].Id.ToString()
                });
                var resultLogText = resultLogResp.Content ?? string.Empty;

                Assert.AreEqual(
                    "Completed",
                    finalRequest.Status,
                    $"Expected request {created.Id} to reach Completed, but got '{finalRequest.Status}'. Result status='{results[0].Status}', previewLog='{results[0].Log}', fullLog='{resultLogText}'");

                Assert.IsTrue(
                    File.Exists(outputJsonPath),
                    $"Expected live runner script to write resolved properties artifact '{outputJsonPath}'. Full log: {resultLogText}");

                var resolvedJson = File.ReadAllText(outputJsonPath);
                using var doc = JsonDocument.Parse(resolvedJson);
                var root = doc.RootElement;

                Assert.AreEqual(expectedGlobalVal, root.GetProperty("GlobalVal").GetString(), "Global property value was not injected into live runner.");
                Assert.AreEqual(expectedParentVal, root.GetProperty("ParentInheritedVal").GetString(), "Parent-environment inherited property value was not injected into live runner.");
                Assert.AreEqual(updatedChildVal, root.GetProperty("ChildUpdatedVal").GetString(), "Updated child-environment property value was not injected into live runner.");
                Assert.AreEqual(expectedOverrideVal, root.GetProperty("RequestOverrideVal").GetString(), "Request-time override property value was not injected into live runner.");
                Assert.AreEqual(cleartextSecret.Length, root.GetProperty("SecretLength").GetInt32(), "Encrypted secret property was not decrypted before injection into live runner.");
                Assert.IsTrue(root.GetProperty("SecretVerified").GetBoolean(), "Decrypted secret prefix/suffix verification failed inside live runner.");

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
                    foreach (var logFile in Directory.GetFiles(runnerLogDir, $"*{created.Id}*"))
                    {
                        var fileText = File.ReadAllText(logFile);
                        Assert.IsFalse(
                            fileText.Contains(cleartextSecret, StringComparison.Ordinal),
                            $"Cleartext secret leaked in runner log file '{logFile}'.");
                    }
                }
            }
            finally
            {
                if (File.Exists(outputJsonPath))
                {
                    File.Delete(outputJsonPath);
                }
                DeletePropertiesBestEffort(caller, globalProp, parentProp, childUpdatedProp, overrideProp, secretProp);
            }
        }

        [TestMethod]
        public void EndToEndUserJourney_DeployFailsThenUserUpdatesPropertyAndRestarts_CompletesOnSecondAttempt()
        {
            using var caller = new DorcApiClient();
            var buildDir = EnsureLiveAcceptanceFixture(caller);
            var shouldFailProp = "AutoReg_ShouldFail";

            EnsurePropertyDefinition(caller, shouldFailProp, secure: false);
            UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "true");

            var buildUri = new Uri(buildDir).AbsoluteUri;
            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildUrl = buildUri,
                Components = new List<string> { FailThenPassComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsTrue(created.Id > 0);

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
        public void EndToEndUserJourney_MultiComponentStopOnFailure_HaltsDownstreamComponentInLiveMonitor()
        {
            using var caller = new DorcApiClient();
            var buildDir = EnsureLiveAcceptanceFixture(caller);
            var shouldFailProp = "AutoReg_ShouldFail";
            var runTokenProp = "AutoReg_RunToken";
            var runToken = Guid.NewGuid().ToString("N")[..10];
            var downstreamMarkerPath = Path.Combine(buildDir, $"downstream-ran-{runToken}.txt");

            try
            {
                EnsurePropertyDefinition(caller, shouldFailProp, secure: false);
                EnsurePropertyDefinition(caller, runTokenProp, secure: false);
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "true");
                UpsertPropertyValue(caller, runTokenProp, secure: false, envFilter: LiveChildEnvName, value: runToken);

                var buildUri = new Uri(buildDir).AbsoluteUri;
                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
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
                Assert.IsTrue(created.Id > 0);

                var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                Assert.IsTrue(
                    string.Equals(finalState.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(finalState.Status, "Errored", StringComparison.OrdinalIgnoreCase),
                    $"Expected multi-component request {created.Id} to fail when upstream StopOnFailure component fails, but got '{finalState.Status}'.");

                Assert.IsFalse(
                    File.Exists(downstreamMarkerPath),
                    "Downstream component should NOT execute when upstream component with StopOnFailure=true fails.");
            }
            finally
            {
                if (File.Exists(downstreamMarkerPath))
                {
                    File.Delete(downstreamMarkerPath);
                }
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "false");
            }
        }

        [TestMethod]
        public void EndToEndUserJourney_CancelDeploymentRequest_TransitionsToCancelledInLiveMonitor()
        {
            using var caller = new DorcApiClient();
            var buildDir = EnsureLiveAcceptanceFixture(caller);
            var buildUri = new Uri(buildDir).AbsoluteUri;

            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildUrl = buildUri,
                Components = new List<string> { CancelWaitComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsTrue(created.Id > 0);

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
        public void LiveEnvironmentIntegration_OAuthApiConfigActiveDirectoryAndEnvironmentAcl_ValidatesLiveSeams()
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
                ["userSearchCriteria"] = "svc"
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
                ["accessSecureId"] = parentEnv.ObjectId.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, aclResp.StatusCode, aclResp.Content);
            var acl = DorcApiClient.Deserialize<AccessSecureObjectsApiModel>(aclResp.Content);
            Assert.IsNotNull(acl);

            // Verify last-owner protection on live ACL endpoint: attempting to strip all Owner privileges is rejected with 400
            var stripped = new AccessSecureObjectsApiModel
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
                        Sid = p.Sid,
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
            var buildDir = Path.Combine(DropRootPath, BuildFolderName);
            Directory.CreateDirectory(buildDir);
            GrantEveryoneReadExecuteAndWrite(DropRootPath);
            GrantEveryoneReadExecuteAndWrite(buildDir);

            var verifyScriptPath = Path.Combine(buildDir, "verify-properties.ps1");
            File.WriteAllText(verifyScriptPath, @"
$ErrorActionPreference = 'Stop'
$token = $AutoReg_RunToken
$globalName = ""AutoReg_Global_$token""
$parentName = ""AutoReg_ParentInherited_$token""
$childName  = ""AutoReg_ChildUpdated_$token""
$overrideName = ""AutoReg_RequestOverride_$token""
$secretName = ""AutoReg_SecretPassword_$token""

$globalVal = (Get-Variable -Name $globalName -ValueOnly -ErrorAction SilentlyContinue)
$parentVal = (Get-Variable -Name $parentName -ValueOnly -ErrorAction SilentlyContinue)
$childVal  = (Get-Variable -Name $childName -ValueOnly -ErrorAction SilentlyContinue)
$overrideVal = (Get-Variable -Name $overrideName -ValueOnly -ErrorAction SilentlyContinue)
$secretVal = [string](Get-Variable -Name $secretName -ValueOnly -ErrorAction SilentlyContinue)

$expectedPrefix = ""Sup3rS3cr3tValu3_""
$secretVerified = ($secretVal.StartsWith($expectedPrefix) -and $secretVal.EndsWith('!'))

# Intentionally write a line containing the secret to stdout to verify PowerShellScriptRunner redacts it to [REDACTED]
Write-Output ""Executing live acceptance verification for token=$token secret=$secretVal""
Write-Output ""RESOLVED: Global=$globalVal | Parent=$parentVal | ChildUpdated=$childVal | Override=$overrideVal""

$payload = @{
    GlobalVal = [string]$globalVal
    ParentInheritedVal = [string]$parentVal
    ChildUpdatedVal = [string]$childVal
    RequestOverrideVal = [string]$overrideVal
    SecretLength = $secretVal.Length
    SecretVerified = [bool]$secretVerified
} | ConvertTo-Json -Compress

$outFile = Join-Path $PSScriptRoot ""resolved-props-$token.json""
Set-Content -Path $outFile -Value $payload -Encoding UTF8
");

            var failThenPassScriptPath = Path.Combine(buildDir, "fail-then-pass.ps1");
            File.WriteAllText(failThenPassScriptPath, @"
$ErrorActionPreference = 'Stop'
Write-Output ""AutoReg_ShouldFail is '$AutoReg_ShouldFail'""
if ($AutoReg_ShouldFail -eq 'true') {
    Write-Error 'Simulated deployment failure because AutoReg_ShouldFail=true'
    exit 1
}
Write-Output 'Deployment succeeded after property update and restart.'
");

            var cancelWaitScriptPath = Path.Combine(buildDir, "cancel-wait.ps1");
            File.WriteAllText(cancelWaitScriptPath, @"
Write-Output 'Starting cancellable wait script...'
Start-Sleep -Seconds 25
Write-Output 'Finished wait.'
");

            var downstreamScriptPath = Path.Combine(buildDir, "downstream-skipped.ps1");
            File.WriteAllText(downstreamScriptPath, @"
$outFile = Join-Path $PSScriptRoot ""downstream-ran-$AutoReg_RunToken.txt""
Set-Content -Path $outFile -Value 'RAN' -Encoding UTF8
Write-Output 'Downstream component executed.'
");

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

            var dropRootUri = new Uri(DropRootPath).AbsoluteUri;
            var project = EnsureProject(caller, LiveProjectName, dropRootUri);
            EnsureEnvironmentMappedToProject(caller, LiveProjectName, LiveParentEnvName);
            EnsureEnvironmentMappedToProject(caller, LiveProjectName, LiveChildEnvName);
            EnsureProjectComponents(caller, project.ProjectId);

            return buildDir;
        }

        private static void GrantEveryoneReadExecuteAndWrite(string directoryPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                var security = dirInfo.GetAccessControl();
                var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                security.AddAccessRule(new FileSystemAccessRule(
                    everyoneSid,
                    FileSystemRights.Modify | FileSystemRights.Synchronize,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                dirInfo.SetAccessControl(security);
            }
            catch
            {
                // Best-effort ACL adjustment for local runner service account
            }
        }

        private static (string OwnerName, string OwnerId) ResolveDefaultOwner(DorcApiClient caller)
        {
            var envsResp = caller.ExecuteRaw("RefDataEnvironments", Method.Get);
            if (envsResp.StatusCode == HttpStatusCode.OK)
            {
                var envs = DorcApiClient.Deserialize<List<EnvironmentApiModel>>(envsResp.Content);
                var existingWithOwner = envs?.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e?.Details?.EnvironmentOwner) &&
                    !string.IsNullOrWhiteSpace(e?.Details?.EnvironmentOwnerId));
                if (existingWithOwner?.Details != null)
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
                var first = users?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Sid) || !string.IsNullOrWhiteSpace(u.Pid));
                if (first != null)
                {
                    return (first.DisplayName ?? first.Username ?? "ServiceAccount",
                        !string.IsNullOrWhiteSpace(first.Sid) ? first.Sid : first.Pid);
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
            Assert.IsTrue(created.EnvironmentId > 0, $"Expected EnvironmentId > 0 for '{envName}', got {postResp.Content}");
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
            Assert.IsTrue(created.ProjectId > 0);
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
                var mapped = DorcApiClient.Deserialize<EnvironmentApiModelTemplate>(getResp.Content);
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
                (VerifyPropsComponentName, "verify-properties.ps1"),
                (FailThenPassComponentName, "fail-then-pass.ps1"),
                (DownstreamSkippedComponentName, "downstream-skipped.ps1"),
                (CancelWaitComponentName, "cancel-wait.ps1")
            };

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
                        PSVersion = PowerShellVersion.V7,
                        Children = new List<ComponentApiModel>()
                    });
                    modified = true;
                }
                else if (!string.Equals(existing.ScriptPath, scriptName, StringComparison.OrdinalIgnoreCase) ||
                         existing.PSVersion != PowerShellVersion.V7 ||
                         !existing.IsEnabled)
                {
                    existing.ScriptPath = scriptName;
                    existing.PSVersion = PowerShellVersion.V7;
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
            catch
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
