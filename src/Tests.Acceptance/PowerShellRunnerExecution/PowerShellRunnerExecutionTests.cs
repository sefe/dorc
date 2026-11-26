using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.PowerShellRunnerExecution
{
    [Binding]
    public class PowerShellRunnerExecutionTests
    {
        private bool _ps7HierarchyAndRedactionExecuted;
        private bool _netFx51RunnerExecuted;
        private bool _stopOnFailureExecuted;
        private bool _buildServersAndCliToolsExecuted;
        private bool _isArrayRunnersExecuted;

        [Given("hierarchical properties, a request override property, and an encrypted secret property are configured for the synthetic project")]
        public void GivenHierarchicalPropertiesAndSecretAreConfigured()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user submits a PowerShell 7 deployment request on the live child environment with a request property override")]
        public void WhenTheUserSubmitsAPowerShell7DeploymentRequestWithOverride()
        {
            CreateDeploymentRequest_UpdatedPropertyHierarchyInheritanceAndSecretRedaction_InjectsResolvedValuesIntoRunner();
            _ps7HierarchyAndRedactionExecuted = true;
        }

        [Then("the runner executes on PowerShell Core, resolves global, parent-inherited, child-updated, and request-overridden values, and completes")]
        [Then("the secret value is verified inside the script while being redacted as REDACTED in all deployment logs")]
        public void ThenPowerShell7HierarchyResolutionAndSecretRedactionSucceed()
        {
            Assert.IsTrue(_ps7HierarchyAndRedactionExecuted);
        }

        [Given("the synthetic project includes a component configured for Windows PowerShell 5.1 on the .NET Framework 4.8 runner")]
        public void GivenTheSyntheticProjectIncludesANetFramework51Component()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user submits a deployment request for the .NET Framework 4.8 runner component on the live child environment")]
        public void WhenTheUserSubmitsADeploymentRequestForTheNetFrameworkRunnerComponent()
        {
            CreateDeploymentRequest_NetFrameworkRunnerPowerShell51_ExecutesDesktopEditionAndInjectsProperties();
            _netFx51RunnerExecuted = true;
        }

        [Then("the deployment executes under Windows PowerShell Desktop edition and completes with all resolved properties injected")]
        public void ThenTheDeploymentExecutesUnderWindowsPowerShellDesktopEdition()
        {
            Assert.IsTrue(_netFx51RunnerExecuted);
        }

        [Given("a two-component deployment sequence where the first component fails and has StopOnFailure enabled")]
        public void GivenATwoComponentDeploymentSequenceWithStopOnFailure()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user submits a deployment request for both components on the live child environment")]
        public void WhenTheUserSubmitsADeploymentRequestForBothComponents()
        {
            CreateDeploymentRequest_MultiComponentWithStopOnFailure_HaltsDownstreamComponent();
            _stopOnFailureExecuted = true;
        }

        [Then("the overall deployment request transitions to Failed")]
        [Then("the first component is marked Failed while the downstream component is never executed")]
        public void ThenTheFirstComponentFailsAndHaltsDownstreamComponent()
        {
            Assert.IsTrue(_stopOnFailureExecuted);
        }

        [Given("the synthetic project is configured to query build servers and installed DOrc CLI binaries on the deployment host")]
        public void GivenTheSyntheticProjectIsConfiguredToQueryBuildServersAndCliTools()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user queries BuildServers and ProjectBuilds endpoints and executes the installed CLI tools with OAuth client credentials")]
        public void WhenTheUserQueriesBuildServersAndExecutesInstalledCliTools()
        {
            BuildServersAndInstalledCliTools_AdoAndGitHubBuildResolutionAndInstalledCliExecution_ResolvesArtifactsAndExecutesInstalledTools();
            _buildServersAndCliToolsExecuted = true;
        }

        [Then("the build server and artifact resolution endpoints respond and the installed CLI tools authenticate and execute without missing Scope errors")]
        public void ThenBuildServerResolutionAndInstalledCliToolsSucceed()
        {
            Assert.IsTrue(_buildServersAndCliToolsExecuted);
        }

        [Given("IsArray properties for plain, interpolated, nested, encrypted secure, and request-overridden string arrays are configured via the API")]
        public void GivenIsArrayPropertiesAreConfiguredViaTheApi()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user updates the array properties, verifies RefDataScopedPropertyValues and Export, and runs PowerShell 7 and Windows PowerShell 5.1 deployments")]
        public void WhenTheUserUpdatesArrayPropertiesAndRunsBothRunnerDeployments()
        {
            CreateDeploymentRequest_IsArrayPropertiesPlainInterpolatedNestedSecureAndRequestOverride_InjectsStringArraysIntoRunners();
            _isArrayRunnersExecuted = true;
        }

        [Then("both runners receive genuine PowerShell string arrays with exact element counts, interpolated elements, and request overrides")]
        [Then("secure array element values are verified in-script and redacted as REDACTED in all runner and deployment logs")]
        public void ThenBothRunnersReceiveStringArraysAndRedactSecureElements()
        {
            Assert.IsTrue(_isArrayRunnersExecuted);
        }

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
                    catch (UnauthorizedAccessException ex)
                    {
                        Console.WriteLine($"Skipping inaccessible runner log file under '{runnerLogDir}': {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Skipping transient IO error reading runner log file under '{runnerLogDir}': {ex.Message}");
                    }
                }
            }
            finally
            {
                DeletePropertiesBestEffort(caller, globalProp, parentProp, childUpdatedProp, overrideProp, secretProp);
            }
        }

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

        public void CreateDeploymentRequest_IsArrayPropertiesPlainInterpolatedNestedSecureAndRequestOverride_InjectsStringArraysIntoRunners()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var cid = Guid.NewGuid().ToString("N")[..10];

            var arrayCorrelationProp = "AutoReg_ArrayCorrelationId";
            var tagProp = $"AutoReg_ArrayTag_{cid}";
            var baseArrayProp = $"AutoReg_ArrayBase_{cid}";
            var expandedArrayProp = $"AutoReg_ArrayExpanded_{cid}";
            var joinedScalarProp = $"AutoReg_ArrayJoined_{cid}";
            var overrideArrayProp = $"AutoReg_ArrayOverride_{cid}";
            var secretArrayProp = $"AutoReg_ArraySecret_{cid}";
            var secretElemA = $"SecArrElemA_{cid}!";
            var secretElemB = $"SecArrElemB_{cid}!";

            try
            {
                EnsurePropertyDefinition(caller, arrayCorrelationProp, secure: false, isArray: false);
                EnsurePropertyDefinition(caller, tagProp, secure: false, isArray: false);

                // Create baseArrayProp as scalar first, then toggle IsArray=true via PUT /Properties
                EnsurePropertyDefinition(caller, baseArrayProp, secure: false, isArray: false);
                var toggleResp = caller.ExecuteRaw("Properties", Method.Put, body: new Dictionary<string, PropertyApiModel>
                {
                    [baseArrayProp] = new PropertyApiModel
                    {
                        Name = baseArrayProp,
                        Secure = false,
                        IsArray = true
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, toggleResp.StatusCode, $"PUT /Properties IsArray toggle failed: {toggleResp.Content}");

                var verifyPropResp = caller.ExecuteRaw($"Properties/id={Uri.EscapeDataString(baseArrayProp)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, verifyPropResp.StatusCode, verifyPropResp.Content);
                var verifiedProp = DorcApiClient.Deserialize<PropertyApiModel>(verifyPropResp.Content);
                Assert.IsNotNull(verifiedProp);
                Assert.IsTrue(verifiedProp.IsArray, "Expected PUT /Properties to persist IsArray = true.");

                EnsurePropertyDefinition(caller, expandedArrayProp, secure: false, isArray: true);
                EnsurePropertyDefinition(caller, joinedScalarProp, secure: false, isArray: false);
                EnsurePropertyDefinition(caller, overrideArrayProp, secure: false, isArray: true);
                EnsurePropertyDefinition(caller, secretArrayProp, secure: true, isArray: true);

                UpsertPropertyValue(caller, arrayCorrelationProp, secure: false, envFilter: LiveChildEnvName, value: cid, isArray: false);
                UpsertPropertyValue(caller, tagProp, secure: false, envFilter: null, value: $"Tag_{cid}", isArray: false);
                UpsertPropertyValue(
                    caller,
                    baseArrayProp,
                    secure: false,
                    envFilter: LiveParentEnvName,
                    value: $"[\"node1-${tagProp}$\",\"node2-${tagProp}$\"]",
                    isArray: true);
                UpsertPropertyValue(
                    caller,
                    expandedArrayProp,
                    secure: false,
                    envFilter: LiveChildEnvName,
                    value: $"[\"${baseArrayProp}$\",\"node3-${tagProp}$\"]",
                    isArray: true);
                UpsertPropertyValue(
                    caller,
                    joinedScalarProp,
                    secure: false,
                    envFilter: LiveChildEnvName,
                    value: $"cluster=${baseArrayProp}$",
                    isArray: false);
                UpsertPropertyValue(
                    caller,
                    overrideArrayProp,
                    secure: false,
                    envFilter: LiveChildEnvName,
                    value: "[\"default-1\",\"default-2\"]",
                    isArray: true);
                UpsertPropertyValue(
                    caller,
                    secretArrayProp,
                    secure: true,
                    envFilter: LiveChildEnvName,
                    value: $"[\"{secretElemA}\",\"{secretElemB}\"]",
                    isArray: true);

                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string>
                    {
                        DownstreamSkippedComponentName,
                        NetFxRunnerComponentName
                    },
                    RequestProperties = new List<RequestProperty>
                    {
                        new RequestProperty
                        {
                            PropertyName = overrideArrayProp,
                            PropertyValue = $"[\"override-1-{cid}\",\"override-2-{cid}\"]"
                        }
                    }
                };

                var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
                Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
                var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
                Assert.IsNotNull(created);
                Assert.IsGreaterThan(0, created.Id);

                var finalRequest = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                Assert.HasCount(2, results, $"Expected 2 component results (.NET 8 PS7 + .NET Framework 4.8 PS5.1) for request {created.Id}.");

                var combinedLogs = new List<string>();
                foreach (var res in results)
                {
                    var logResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
                    {
                        ["requestId"] = created.Id.ToString(),
                        ["resultId"] = res.Id.ToString()
                    });
                    combinedLogs.Add($"{res.Log}\n{logResp.Content}");
                }

                var fullLog = string.Join("\n---\n", combinedLogs);
                Assert.AreEqual(
                    "Completed",
                    finalRequest.Status,
                    $"Expected IsArray multi-runner request {created.Id} to reach Completed, got '{finalRequest.Status}'. Logs:\n{fullLog}");

                StringAssert.Contains(
                    fullLog,
                    $"VERIFIED_ISARRAY_NET8_PS7_RUNNER_{cid}",
                    $"Expected .NET 8 (PS 7) runner to receive and verify string[] variables. Log:\n{fullLog}");
                StringAssert.Contains(
                    fullLog,
                    $"VERIFIED_ISARRAY_PROPERTIES_IN_RUNNER_{cid}",
                    $"Expected .NET Framework 4.8 (PS 5.1) runner to receive and verify string[] variables. Log:\n{fullLog}");
                StringAssert.Contains(
                    fullLog,
                    "ARRAY_SECRET_PROBE=[REDACTED]|[REDACTED]",
                    $"Expected both runners to redact every element of a Secure IsArray variable. Log:\n{fullLog}");

                Assert.IsFalse(
                    fullLog.Contains(secretElemA, StringComparison.Ordinal) || fullLog.Contains(secretElemB, StringComparison.Ordinal),
                    "Cleartext secure array element leaked in runner log.");
            }
            finally
            {
                try
                {
                    UpsertPropertyValue(caller, arrayCorrelationProp, secure: false, envFilter: LiveChildEnvName, value: string.Empty, isArray: false);
                }
                catch
                {
                    // Best-effort cleanup of shared correlation marker
                }

                DeletePropertiesBestEffort(
                    caller,
                    tagProp,
                    baseArrayProp,
                    expandedArrayProp,
                    joinedScalarProp,
                    overrideArrayProp,
                    secretArrayProp);
            }
        }
    }
}
