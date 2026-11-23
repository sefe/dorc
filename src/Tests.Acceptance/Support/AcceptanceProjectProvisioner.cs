using System.Net;
using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;

namespace Tests.Acceptance.Support
{
    internal static class AcceptanceProjectProvisioner
    {
        internal const string LiveProjectName = "DOrc_Live_Acceptance_Project";
        internal const string LiveParentEnvName = "DOrc_Live_Acceptance_Parent_Env";
        internal const string LiveChildEnvName = "DOrc_Live_Acceptance_Child_Env";
        internal const string VerifyPropsComponentName = "DOrc_Live_Acceptance_VerifyProps";
        internal const string NetFxRunnerComponentName = "DOrc_Live_Acceptance_NetFxRunner";
        internal const string FailThenPassComponentName = "DOrc_Live_Acceptance_FailThenPass";
        internal const string DownstreamSkippedComponentName = "DOrc_Live_Acceptance_Z_DownstreamSkipped";
        internal const string CancelWaitComponentName = "DOrc_Live_Acceptance_CancelWait";
        internal const string SnapshotComponentName = "DOrc_Live_Acceptance_SnapshotComponent";
        internal const string BundleJobComponentName = "DOrc_Live_Acceptance_BundleJob";
        internal const string RelativeScriptSubfolder = @"Test\DOrc_Live_Acceptance";
        internal const string BuildFolderName = "DOrc Regression Testing";

        internal static void AssertLiveEnvironmentEnabled()
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

        internal static string EnsureProvisioned(DorcApiClient caller)
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

        internal static EnvironmentApiModel? GetEnvironmentByName(DorcApiClient caller, string envName)
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

        internal static void UpdateComponentScriptPath(DorcApiClient caller, int projectId, string componentName, string scriptPath)
        {
            var getResp = caller.ExecuteRaw($"RefData/{projectId}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode, getResp.Content);
            var refData = DorcApiClient.Deserialize<RefDataApiModel>(getResp.Content);
            Assert.IsNotNull(refData?.Components);
            var comp = refData!.Components.FirstOrDefault(c =>
                string.Equals(c.ComponentName, componentName, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(comp, $"Component '{componentName}' not found in project {projectId}.");
            comp.ScriptPath = scriptPath;
            var putResp = caller.ExecuteRaw("RefData", Method.Put, body: refData);
            Assert.AreEqual(HttpStatusCode.OK, putResp.StatusCode, $"PUT /RefData scriptPath update failed: {putResp.Content}");
        }

        internal static string GetCombinedRequestLog(DorcApiClient caller, int requestId)
        {
            var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = requestId.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
            var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
            if (results.Count == 0)
            {
                return string.Empty;
            }

            var logResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = requestId.ToString(),
                ["resultId"] = results[0].Id.ToString()
            });
            return $"{results[0].Log}\n{logResp.Content}";
        }

        internal static void EnsurePropertyDefinition(DorcApiClient caller, string propName, bool secure)
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

        internal static void UpsertPropertyValue(DorcApiClient caller, string propName, bool secure, string? envFilter, string value)
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

        internal static void DeletePropertiesBestEffort(DorcApiClient caller, params string[] propNames)
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

        internal static DeploymentRequestApiModel WaitForTerminalRequestStatus(DorcApiClient caller, int requestId, TimeSpan timeout)
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

        internal static DeploymentRequestApiModel WaitForRequestStatusAfterRestart(DorcApiClient caller, int requestId, TimeSpan timeout)
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

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "verify-netfx-runner.ps1"), @"
$ErrorActionPreference = 'Stop'
$edition = $PSVersionTable.PSEdition
$major = $PSVersionTable.PSVersion.Major
Write-Output ""PSEdition=$edition PSVersion=$($PSVersionTable.PSVersion)""
if ($edition -ne 'Desktop' -or $major -ne 5) {
    throw ""Expected Windows PowerShell 5.1 Desktop edition, got PSEdition='$edition' Major='$major'""
}
if ($EnvironmentName -ne 'DOrc_Live_Acceptance_Child_Env') {
    throw ""Expected EnvironmentName='DOrc_Live_Acceptance_Child_Env', got '$EnvironmentName'""
}
Write-Output 'VERIFIED_NETFX_48_POWERSHELL_51_RUNNER'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "snapshot-v1.ps1"), @"
$ErrorActionPreference = 'Stop'
Write-Output 'IMMUTABLE_DEF_VERSION_V1_EXECUTED'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "snapshot-v2.ps1"), @"
$ErrorActionPreference = 'Stop'
Write-Output 'IMMUTABLE_DEF_VERSION_V2_EXECUTED'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "bundle-job.ps1"), @"
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AllRequestIds) -or ($AllRequestIds -notmatch '^\d+')) {
    throw ""Expected AllRequestIds to be populated with numeric request ID(s), got '$AllRequestIds'""
}
Write-Output ""BundleJob executed. StartingRequestId='$StartingRequestId' AllRequestIds='$AllRequestIds' DataBackup='$DataBackup'""
Write-Output 'VERIFIED_MAKE_LIKE_PROD_BUNDLE_EXECUTION'
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "verify-ado-build-and-cli.ps1"), @"
$ErrorActionPreference = 'Stop'
Write-Output ""ADO_DEPLOY_VERIFIED|ENV=$EnvironmentName|BUILD=$BuildNumber|DROP=$DropFolder""
if ([string]::IsNullOrWhiteSpace($DropFolder) -or ($DropFolder -notlike 'https://dev.azure.com/sefe/*' -and $DropFolder -notlike '\\*' -and $DropFolder -notlike 'file:*')) {
    throw ""Expected Azure DevOps drop path or URL in DropFolder, got: $DropFolder""
}
$propCliDir = 'C:\Program Files\DOrc\Deploy\Tools\PropertyValueCreation'
$propCliExe = Join-Path $propCliDir 'Tools.PropertyValueCreationCLI.exe'
$reqCliDir = 'C:\Program Files\DOrc\Deploy\Tools\Request'
$reqCliExe = Join-Path $reqCliDir 'Tools.RequestCLI.exe'
if (-not (Test-Path $propCliExe)) {
    throw ""Missing installed Tools.PropertyValueCreationCLI.exe at $propCliExe""
}
if (-not (Test-Path $reqCliExe)) {
    throw ""Missing installed Tools.RequestCLI.exe at $reqCliExe""
}
$csvTemp = Join-Path $env:TEMP ""dorc_cli_props_$AutoReg_CliPropName.csv""
Set-Content -Path $csvTemp -Value ""$AutoReg_CliPropName,0,CliCreatedValue_$AutoReg_CliPropName,$EnvironmentName"" -Encoding UTF8
try {
    Push-Location $env:TEMP
    try {
        $propOut = & $propCliExe $csvTemp 2>&1 | Out-String
        Write-Output ""PROP_CLI_OUT=$($propOut.Trim())""
        if ($LASTEXITCODE -ne 0) {
            throw ""Tools.PropertyValueCreationCLI.exe failed with exit code $LASTEXITCODE : $propOut""
        }
        $reqOut = & $reqCliExe '/project:DOrc_Live_Acceptance_Project' ""/targetenv:$EnvironmentName"" ""/builduri:$AutoReg_FileShareBuildUri"" '/buildnum:DOrc Regression Testing' '/components:DOrc_Live_Acceptance_SnapshotComponent' '/wait:false' 2>&1 | Out-String
        Write-Output ""REQ_CLI_OUT=$($reqOut.Trim())""
        if ($LASTEXITCODE -ne 0) {
            throw ""Tools.RequestCLI.exe failed with exit code $LASTEXITCODE : $reqOut""
        }
    } finally {
        Pop-Location
    }
} finally {
    Remove-Item -Path $csvTemp -Force -ErrorAction SilentlyContinue
}
");

                WriteScriptIfChanged(Path.Join(scriptSubfolder, "verify-github-build.ps1"), @"
$ErrorActionPreference = 'Stop'
$extractedDrop = Join-Path $DropFolder 'drop'
if (-not (Test-Path $extractedDrop)) {
    throw ""Expected extracted GitHub artifact folder at $extractedDrop""
}
$files = @(Get-ChildItem -Path $extractedDrop -Recurse -File)
Write-Output ""GITHUB_DEPLOY_VERIFIED|ENV=$EnvironmentName|BUILD=$BuildNumber|FILE_COUNT=$($files.Count)""
if ($files.Count -le 0) {
    throw ""Expected extracted GitHub artifact files in $extractedDrop""
}
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

        private static EnvironmentApiModel EnsureEnvironment(DorcApiClient caller, string envName, string ownerName, string ownerId)
        {
            var existing = GetEnvironmentByName(caller, envName);
            if (existing != null && existing.EnvironmentId > 0)
            {
                if (existing.Details != null && string.IsNullOrWhiteSpace(existing.Details.ThinClient))
                {
                    existing.Details.ThinClient = "Endur";
                    var putResp = caller.ExecuteRaw("RefDataEnvironments", Method.Put, body: existing);
                    Assert.AreEqual(HttpStatusCode.OK, putResp.StatusCode, $"Failed updating ThinClient on '{envName}': {putResp.Content}");
                }
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
                    ThinClient = "Endur"
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

            var psV7 = PowerShellVersion.V7.ToVersionString();
            var psV51 = PowerShellVersion.V5_1.ToVersionString();
            var required = new (string Name, string ScriptName, string PsVersion)[]
            {
                (VerifyPropsComponentName, Path.Join(RelativeScriptSubfolder, "verify-properties.ps1"), psV7),
                (NetFxRunnerComponentName, Path.Join(RelativeScriptSubfolder, "verify-netfx-runner.ps1"), psV51),
                (FailThenPassComponentName, Path.Join(RelativeScriptSubfolder, "fail-then-pass.ps1"), psV7),
                (DownstreamSkippedComponentName, Path.Join(RelativeScriptSubfolder, "downstream-skipped.ps1"), psV7),
                (CancelWaitComponentName, Path.Join(RelativeScriptSubfolder, "cancel-wait.ps1"), psV7),
                (SnapshotComponentName, Path.Join(RelativeScriptSubfolder, "snapshot-v1.ps1"), psV7),
                (BundleJobComponentName, Path.Join(RelativeScriptSubfolder, "bundle-job.ps1"), psV7)
            };

            var modified = false;
            foreach (var (name, scriptName, psVersion) in required)
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
                        PSVersion = psVersion,
                        Children = new List<ComponentApiModel>()
                    });
                    modified = true;
                }
                else if (!string.Equals(existing.ScriptPath, scriptName, StringComparison.OrdinalIgnoreCase) ||
                         existing.PSVersion != psVersion ||
                         !existing.IsEnabled)
                {
                    existing.ScriptPath = scriptName;
                    existing.PSVersion = psVersion;
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
    }
}
