using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

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

        [TestMethod]
        public void BuildServersAndInstalledCliTools_AdoAndGitHubBuildResolutionAndInstalledCliExecution_ResolvesArtifactsAndExecutesInstalledTools()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var runId = Guid.NewGuid().ToString("N")[..8];
            var adoProjectName = $"DOrc_Live_AdoBuild_{runId}";
            var ghProjectName = $"DOrc_Live_GitHubBuild_{runId}";
            var cliPropName = $"DOrc_Live_CliCreated_{runId}";
            int? adoProjectId = null;
            int? ghProjectId = null;

            try
            {
                // 1. FileShare BuildDefinitions & Builds discovery on LiveProjectName
                var liveProjResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, liveProjResp.StatusCode, liveProjResp.Content);
                var liveProj = DorcApiClient.Deserialize<ProjectApiModel>(liveProjResp.Content);
                Assert.IsNotNull(liveProj);

                var fsBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = liveProj.ProjectId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, fsBuildDefsResp.StatusCode, fsBuildDefsResp.Content);
                var fsBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(fsBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(fsBuildDefs, $"Expected FileShare project to return build definitions. Actual: {fsBuildDefsResp.Content}");
                var fsDefName = fsBuildDefs[0].Name;

                var fsBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = liveProj.ProjectId.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = fsDefName
                });
                Assert.AreEqual(HttpStatusCode.OK, fsBuildsResp.StatusCode, fsBuildsResp.Content);
                var fsBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(fsBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsTrue(fsBuilds.Any(b => b.Name != null && b.Name.Contains(BuildFolderName, StringComparison.OrdinalIgnoreCase)),
                    $"Expected FileShare project builds to include '{BuildFolderName}'. Actual: {fsBuildsResp.Content}");

                // 2. Azure DevOps BuildDefinitions, Builds, and live deployment + installed CLI tools execution on DEPAPP01DV
                EnsurePropertyDefinition(caller, "AutoReg_CliPropName", secure: false);
                UpsertPropertyValue(caller, "AutoReg_CliPropName", secure: false, envFilter: LiveParentEnvName, value: cliPropName);
                EnsurePropertyDefinition(caller, "AutoReg_FileShareBuildUri", secure: false);
                UpsertPropertyValue(caller, "AutoReg_FileShareBuildUri", secure: false, envFilter: LiveParentEnvName, value: buildUri);

                var createAdoProjResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: new ProjectApiModel
                {
                    ProjectName = adoProjectName,
                    ProjectDescription = $"Live ADO build & CLI verification {runId}",
                    SourceControlType = SourceControlType.AzureDevOps,
                    ArtefactsUrl = "https://dev.azure.com/sefe/",
                    ArtefactsSubPaths = "Deployment Orchestrator",
                    ArtefactsBuildRegex = "dorc-active-batch"
                });
                Assert.AreEqual(HttpStatusCode.OK, createAdoProjResp.StatusCode, createAdoProjResp.Content);
                var adoCreatedProj = DorcApiClient.Deserialize<ProjectApiModel>(createAdoProjResp.Content);
                Assert.IsNotNull(adoCreatedProj);
                adoProjectId = adoCreatedProj.ProjectId;

                var mapAdoEnvResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
                {
                    ["project"] = adoProjectName,
                    ["environment"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, mapAdoEnvResp.StatusCode, mapAdoEnvResp.Content);

                var adoCompName = $"Live_AdoBuild_Comp_{runId}";
                var getAdoRefResp = caller.ExecuteRaw($"RefData/{adoProjectId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getAdoRefResp.StatusCode, getAdoRefResp.Content);
                var adoTemplate = DorcApiClient.Deserialize<RefDataApiModel>(getAdoRefResp.Content);
                Assert.IsNotNull(adoTemplate);
                adoTemplate.Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = adoCompName,
                        ScriptPath = Path.Join(RelativeScriptSubfolder, "verify-ado-build-and-cli.ps1"),
                        NonProdOnly = true,
                        StopOnFailure = true,
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        PSVersion = PowerShellVersion.V7.ToVersionString(),
                        Children = new List<ComponentApiModel>()
                    }
                };
                var putAdoRefResp = caller.ExecuteRaw("RefData", Method.Put, body: adoTemplate);
                Assert.AreEqual(HttpStatusCode.OK, putAdoRefResp.StatusCode, putAdoRefResp.Content);

                var adoBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = adoProjectId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, adoBuildDefsResp.StatusCode, adoBuildDefsResp.Content);
                var adoBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(adoBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                var adoDef = adoBuildDefs.FirstOrDefault(d => d.Name != null && d.Name.Contains("dorc-active-batch", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(adoDef, $"Expected ADO BuildDefinitions to include 'dorc-active-batch'. Actual: {adoBuildDefsResp.Content}");

                var adoBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = adoProjectId.Value.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = adoDef.Name
                });
                Assert.AreEqual(HttpStatusCode.OK, adoBuildsResp.StatusCode, adoBuildsResp.Content);
                var adoBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(adoBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(adoBuilds, "Expected non-empty builds list from Azure DevOps build server.");

                var adoRequestResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = adoProjectName,
                    Environment = LiveParentEnvName,
                    BuildText = adoDef.Name,
                    BuildNum = "latest",
                    Components = new List<string> { adoCompName }
                });
                Assert.AreEqual(HttpStatusCode.OK, adoRequestResp.StatusCode, $"ADO Request submission failed: {adoRequestResp.Content}");
                var adoCreatedReq = DorcApiClient.Deserialize<RequestStatusDto>(adoRequestResp.Content);
                Assert.IsNotNull(adoCreatedReq);

                var adoFinalStatus = WaitForTerminalRequestStatus(caller, adoCreatedReq.Id, TimeSpan.FromMinutes(3));
                var adoJobLog = GetCombinedRequestLog(caller, adoCreatedReq.Id);
                Assert.AreEqual("Completed", adoFinalStatus.Status, $"Expected ADO build deployment #{adoCreatedReq.Id} to complete. Log:\n{adoJobLog}");

                StringAssert.Contains(adoJobLog, $"ADO_DEPLOY_VERIFIED|ENV={LiveParentEnvName}|BUILD=dorc-activebatch.");
                StringAssert.Contains(adoJobLog, "DROP=");
                StringAssert.Contains(adoJobLog, "PROP_CLI_OUT=");
                StringAssert.Contains(adoJobLog, "REQ_CLI_OUT=");

                // Wait for the CLI-triggered request to finish so it doesn't overlap subsequent checks
                var cliReqMatch = System.Text.RegularExpressions.Regex.Match(adoJobLog, @"Request (\d+) created");
                Assert.IsTrue(cliReqMatch.Success, $"Expected RequestCLI output with request ID in log: {adoJobLog}");
                var cliTriggeredRequestId = int.Parse(cliReqMatch.Groups[1].Value);
                var cliFinalStatus = WaitForTerminalRequestStatus(caller, cliTriggeredRequestId, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", cliFinalStatus.Status, $"Expected CLI-triggered request #{cliTriggeredRequestId} to complete.");

                // Verify PropertyValueCreationCLI created the property and value in DOrc DV 01
                var cliPropValsResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = cliPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, cliPropValsResp.StatusCode, cliPropValsResp.Content);
                var cliPropVals = DorcApiClient.Deserialize<List<PropertyValueDto>>(cliPropValsResp.Content) ?? new List<PropertyValueDto>();
                Assert.IsTrue(cliPropVals.Any(pv =>
                    string.Equals(pv.PropertyValueFilter, LiveParentEnvName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pv.Value, $"CliCreatedValue_{cliPropName}", StringComparison.Ordinal)),
                    $"Expected Tools.PropertyValueCreationCLI.exe to create '{cliPropName}' on '{LiveParentEnvName}'.");

                // Verify RefDataProjectBuilds records the deployed ADO build on LiveParentEnvName
                var projBuildsResp = caller.ExecuteRaw("RefDataProjectBuilds", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, projBuildsResp.StatusCode, projBuildsResp.Content);
                StringAssert.Contains(projBuildsResp.Content ?? string.Empty, "dorc-activebatch.");

                // 3. GitHub Actions BuildDefinitions, Builds, and live GitHubArtifactDownloader zip download/extraction deployment
                var createGhProjResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: new ProjectApiModel
                {
                    ProjectName = ghProjectName,
                    ProjectDescription = $"Live GitHub Actions artifact verification {runId}",
                    SourceControlType = SourceControlType.GitHub,
                    ArtefactsUrl = "https://api.github.com/repos/sefe/dorc-ps-deploy-module",
                    ArtefactsSubPaths = "build.yml",
                    ArtefactsBuildRegex = ".*"
                });
                Assert.AreEqual(HttpStatusCode.OK, createGhProjResp.StatusCode, createGhProjResp.Content);
                var ghCreatedProj = DorcApiClient.Deserialize<ProjectApiModel>(createGhProjResp.Content);
                Assert.IsNotNull(ghCreatedProj);
                ghProjectId = ghCreatedProj.ProjectId;

                var mapGhEnvResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
                {
                    ["project"] = ghProjectName,
                    ["environment"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, mapGhEnvResp.StatusCode, mapGhEnvResp.Content);

                var ghCompName = $"Live_GitHubBuild_Comp_{runId}";
                var getGhRefResp = caller.ExecuteRaw($"RefData/{ghProjectId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getGhRefResp.StatusCode, getGhRefResp.Content);
                var ghTemplate = DorcApiClient.Deserialize<RefDataApiModel>(getGhRefResp.Content);
                Assert.IsNotNull(ghTemplate);
                ghTemplate.Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = ghCompName,
                        ScriptPath = Path.Join(RelativeScriptSubfolder, "verify-github-build.ps1"),
                        NonProdOnly = true,
                        StopOnFailure = true,
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        PSVersion = PowerShellVersion.V7.ToVersionString(),
                        Children = new List<ComponentApiModel>()
                    }
                };
                var putGhRefResp = caller.ExecuteRaw("RefData", Method.Put, body: ghTemplate);
                Assert.AreEqual(HttpStatusCode.OK, putGhRefResp.StatusCode, putGhRefResp.Content);

                var ghBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = ghProjectId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, ghBuildDefsResp.StatusCode, ghBuildDefsResp.Content);
                var ghBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(ghBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                var ghDef = ghBuildDefs.FirstOrDefault(d => string.Equals(d.Name, "Build DOrcDeployModule", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(ghDef, $"Expected GitHub BuildDefinitions to include 'Build DOrcDeployModule'. Actual: {ghBuildDefsResp.Content}");

                var ghBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = ghProjectId.Value.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = ghDef.Name
                });
                Assert.AreEqual(HttpStatusCode.OK, ghBuildsResp.StatusCode, ghBuildsResp.Content);
                var ghBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(ghBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(ghBuilds, "Expected non-empty workflow runs from GitHub Actions build server.");

                var ghRequestResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = ghProjectName,
                    Environment = LiveParentEnvName,
                    BuildText = ghDef.Name,
                    BuildNum = "latest",
                    Components = new List<string> { ghCompName }
                });
                Assert.AreEqual(HttpStatusCode.OK, ghRequestResp.StatusCode, $"GitHub Request submission failed: {ghRequestResp.Content}");
                var ghCreatedReq = DorcApiClient.Deserialize<RequestStatusDto>(ghRequestResp.Content);
                Assert.IsNotNull(ghCreatedReq);

                var ghFinalStatus = WaitForTerminalRequestStatus(caller, ghCreatedReq.Id, TimeSpan.FromMinutes(3));
                var ghJobLog = GetCombinedRequestLog(caller, ghCreatedReq.Id);
                Assert.AreEqual("Completed", ghFinalStatus.Status, $"Expected GitHub artifact deployment #{ghCreatedReq.Id} to complete. Log:\n{ghJobLog}");

                StringAssert.Contains(ghJobLog, $"GITHUB_DEPLOY_VERIFIED|ENV={LiveParentEnvName}|BUILD=");
                StringAssert.Contains(ghJobLog, "FILE_COUNT=");
            }
            finally
            {
                DeletePropertiesBestEffort(caller, cliPropName, "AutoReg_FileShareBuildUri");
                if (adoProjectId.HasValue)
                {
                    caller.ExecuteRaw("RefDataProjects", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = adoProjectId.Value.ToString()
                    });
                }
                if (ghProjectId.HasValue)
                {
                    caller.ExecuteRaw("RefDataProjects", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = ghProjectId.Value.ToString()
                    });
                }
            }
        }
    }
}
