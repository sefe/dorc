using System.Net;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.BundledRequestsMakeLikeProd
{
    [Binding]
    public class BundledRequestMakeLikeProdTests
    {
        private bool _makeLikeProdBundleExecuted;
        private bool _bundleManifestAndCopyEnvExecuted;

        [Given("a Make-Like-Prod bundled job request is configured for the synthetic project with AllRequestIds interpolation")]
        public void GivenAMakeLikeProdBundledJobRequestIsConfigured()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user triggers Make-Like-Prod against the live child environment using that bundle")]
        public void WhenTheUserTriggersMakeLikeProdAgainstTheLiveChildEnvironment()
        {
            PutMakeLikeProd_ConfiguredBundledJobRequest_DispatchesTargetEnvironmentRequestWithResolvedAllRequestIds();
            _makeLikeProdBundleExecuted = true;
        }

        [Then("DOrc dispatches the bundled deployment request to the child environment and executes it to Completed")]
        [Then("the runner execution log confirms that AllRequestIds was populated and the temporary bundle definition is cleaned up")]
        public void ThenDorcDispatchesTheBundledDeploymentRequestAndCleansUp()
        {
            Assert.IsTrue(_makeLikeProdBundleExecuted);
        }

        [Given("valid and invalid bundle.json manifests are staged in the local bundle artefact drop folder")]
        public void GivenValidAndInvalidBundleJsonManifestsAreStaged()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user queries BundleComponents, submits a multi-component bundle deployment, requests ReleaseInformation, and triggers CopyEnvBuild")]
        public void WhenTheUserQueriesBundleComponentsAndSubmitsBundleDeployment()
        {
            Request_BundleManifestDiscoveryAndValidation_ResolvesValidComponentsRejectsUnknownComponentsAndExpandsBundleDeployments();
            _bundleManifestAndCopyEnvExecuted = true;
        }

        [Then("BundleComponents returns distinct components for a valid manifest and rejects unknown components with 400 BadRequest")]
        [Then("the bundle deployment expands per component build number, completes, returns release information, and copies the build to the child environment")]
        public void ThenBundleComponentsAndMultiComponentBundleDeploymentSucceed()
        {
            Assert.IsTrue(_bundleManifestAndCopyEnvExecuted);
        }

        public void PutMakeLikeProd_ConfiguredBundledJobRequest_DispatchesTargetEnvironmentRequestWithResolvedAllRequestIds()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);

            var refResp = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, refResp.StatusCode, refResp.Content);
            var refData = DorcApiClient.Deserialize<RefDataApiModel>(refResp.Content);
            var bundleComponent = refData?.Components?.FirstOrDefault(c =>
                string.Equals(c.ComponentName, BundleJobComponentName, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(bundleComponent, $"Expected component '{BundleJobComponentName}' in project '{LiveProjectName}'.");

            var bundleName = $"DOrc_Live_Bundle_{Guid.NewGuid():N}"[..26];
            int? createdBundleId = null;

            try
            {
                var requestPayloadJson = JsonSerializer.Serialize(new RequestDto
                {
                    Project = LiveProjectName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    BuildText = BuildFolderName,
                    Components = new List<string> { BundleJobComponentName },
                    RequestProperties = new List<RequestProperty>
                    {
                        new RequestProperty
                        {
                            PropertyName = "BundleRequestIdsList",
                            PropertyValue = "$AllRequestIds$"
                        }
                    }
                });

                var postBundleResp = caller.ExecuteRaw("BundledRequests", Method.Post, body: new BundledRequestsApiModel
                {
                    BundleName = bundleName,
                    ProjectId = project.ProjectId,
                    Type = BundledRequestType.JobRequest,
                    RequestName = "LiveBundleJobStep1",
                    Sequence = 1,
                    Request = requestPayloadJson
                });
                Assert.AreEqual(HttpStatusCode.OK, postBundleResp.StatusCode, $"POST /BundledRequests failed: {postBundleResp.Content}");

                var listBundleResp = caller.ExecuteRaw("BundledRequests/RequestsForBundle", Method.Get, new Dictionary<string, string>
                {
                    ["bundleName"] = bundleName
                });
                Assert.AreEqual(HttpStatusCode.OK, listBundleResp.StatusCode, listBundleResp.Content);
                var bundleItems = DorcApiClient.Deserialize<List<BundledRequestsApiModel>>(listBundleResp.Content) ?? new List<BundledRequestsApiModel>();
                Assert.HasCount(1, bundleItems);
                var bundleId = bundleItems[0].Id;
                createdBundleId = bundleId;

                var byProjectResp = caller.ExecuteRaw($"BundledRequests?projectNames={Uri.EscapeDataString(LiveProjectName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, byProjectResp.StatusCode, byProjectResp.Content);
                var projectBundles = DorcApiClient.Deserialize<List<BundledRequestsApiModel>>(byProjectResp.Content) ?? new List<BundledRequestsApiModel>();
                var matchingBundle = projectBundles.FirstOrDefault(b => b.Id == bundleId);
                Assert.IsNotNull(matchingBundle, "Expected GET /BundledRequests?projectNames=... to include the created bundle.");
                Assert.AreEqual(bundleName, matchingBundle.BundleName);
                Assert.AreEqual(project.ProjectId, matchingBundle.ProjectId);
                Assert.AreEqual(BundledRequestType.JobRequest, matchingBundle.Type);
                Assert.AreEqual("LiveBundleJobStep1", matchingBundle.RequestName);
                Assert.AreEqual(1, matchingBundle.Sequence);
                Assert.IsFalse(string.IsNullOrWhiteSpace(matchingBundle.Request));
                var parsedInnerRequest = DorcApiClient.Deserialize<RequestDto>(matchingBundle.Request);
                Assert.IsNotNull(parsedInnerRequest, "Expected BundledRequestsApiModel.Request to contain valid serialized RequestDto JSON.");
                Assert.AreEqual(LiveProjectName, parsedInnerRequest.Project);

                var preStatusesResp = caller.ExecuteRaw(
                    "RequestStatuses",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "EnvironmentName", FilterValue = LiveChildEnvName },
                            new PagedDataFilter { Path = "Project", FilterValue = LiveProjectName }
                        },
                        SortOrders = new List<PagedDataSorting>
                        {
                            new PagedDataSorting { Path = "Id", Direction = "desc" }
                        }
                    });
                var preStatusPage = DorcApiClient.Deserialize<GetRequestStatusesListResponseDto>(preStatusesResp.Content);
                var preCallMaxRequestId = preStatusPage?.Items?.Any() == true ? preStatusPage.Items.Max(i => i.Id) : 0;

                var mlpResp = caller.ExecuteRaw("MakeLikeProd", Method.Put, body: new
                {
                    TargetEnv = LiveChildEnvName,
                    DataBackup = "Live Snap",
                    BundleName = bundleName,
                    BundleProperties = new List<RequestProperty>()
                });
                Assert.AreEqual(HttpStatusCode.OK, mlpResp.StatusCode, $"PUT /MakeLikeProd failed: {mlpResp.Content}");
                StringAssert.Contains(mlpResp.Content ?? string.Empty, "The requests have been passed to DOrc");

                var statusesResp = caller.ExecuteRaw(
                    "RequestStatuses",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "EnvironmentName", FilterValue = LiveChildEnvName },
                            new PagedDataFilter { Path = "Project", FilterValue = LiveProjectName }
                        },
                        SortOrders = new List<PagedDataSorting>
                        {
                            new PagedDataSorting { Path = "Id", Direction = "desc" }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, statusesResp.StatusCode, statusesResp.Content);
                var statusPage = DorcApiClient.Deserialize<GetRequestStatusesListResponseDto>(statusesResp.Content);
                var dispatched = statusPage?.Items?
                    .Where(i => i.Id > preCallMaxRequestId)
                    .OrderByDescending(i => i.Id)
                    .FirstOrDefault(i => (i.Components ?? string.Empty).Contains(BundleJobComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(dispatched, "Expected PUT /MakeLikeProd to queue a deployment request for BundleJobComponentName.");

                var finalBundleReq = WaitForTerminalRequestStatus(caller, dispatched.Id, TimeSpan.FromMinutes(3));
                var bundleLog = GetCombinedRequestLog(caller, dispatched.Id);
                Assert.AreEqual(
                    "Completed",
                    finalBundleReq.Status,
                    $"Expected MakeLikeProd bundle request {dispatched.Id} to reach Completed, got '{finalBundleReq.Status}'. Log: {bundleLog}");
                StringAssert.Contains(bundleLog, "VERIFIED_MAKE_LIKE_PROD_BUNDLE_EXECUTION");
                Assert.IsFalse(
                    bundleLog.Contains("AllRequestIds=''", StringComparison.OrdinalIgnoreCase),
                    $"Expected AllRequestIds to be populated in bundle execution log, got: {bundleLog}");
            }
            finally
            {
                if (createdBundleId.HasValue)
                {
                    caller.ExecuteRaw("BundledRequests", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = createdBundleId.Value.ToString()
                    });
                }
            }
        }

        public void Request_BundleManifestDiscoveryAndValidation_ResolvesValidComponentsRejectsUnknownComponentsAndExpandsBundleDeployments()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);

            var originalArtefactsUrl = project.ArtefactsUrl ?? string.Empty;
            var writableBundleRoot = ResolveWritableBundleDropDirectory(caller);
            var runId = Guid.NewGuid().ToString("N")[..8];
            var validBundleFolderName = $"DOrc_Bundle_Valid_{runId}_bundle.json";
            var invalidBundleFolderName = $"DOrc_Bundle_Invalid_{runId}_bundle.json";
            var validBundleDir = Path.Join(writableBundleRoot, validBundleFolderName);
            var invalidBundleDir = Path.Join(writableBundleRoot, invalidBundleFolderName);

            try
            {
                if (!originalArtefactsUrl.Contains(LocalBundleDropRootUri, StringComparison.OrdinalIgnoreCase))
                {
                    project.ArtefactsUrl = $"{originalArtefactsUrl};{LocalBundleDropRootUri}";
                    var putProjResp = caller.ExecuteRaw("RefDataProjects", Method.Put, body: project);
                    Assert.AreEqual(HttpStatusCode.OK, putProjResp.StatusCode, $"Failed adding bundle root to project: {putProjResp.Content}");
                }

                Directory.CreateDirectory(validBundleDir);
                Directory.CreateDirectory(invalidBundleDir);

                var validManifestJson = JsonSerializer.Serialize(new
                {
                    Items = new[]
                    {
                        new { Component = SnapshotComponentName, BuildDefinition = "BundleDefA", Build = $"BundleBuild_A_{runId}" },
                        new { Component = NetFxRunnerComponentName, BuildDefinition = "BundleDefB", Build = $"BundleBuild_B_{runId}" },
                        new { Component = SnapshotComponentName, BuildDefinition = "BundleDefA", Build = $"BundleBuild_A_{runId}" }
                    }
                });
                File.WriteAllText(Path.Join(validBundleDir, "bundle.json"), validManifestJson);

                var invalidManifestJson = JsonSerializer.Serialize(new
                {
                    Items = new[]
                    {
                        new { Component = SnapshotComponentName, BuildDefinition = "BundleDefA", Build = $"BundleBuild_A_{runId}" },
                        new { Component = $"Unknown_Bundle_Component_{runId}", BuildDefinition = "BundleDefX", Build = $"BundleBuild_X_{runId}" }
                    }
                });
                File.WriteAllText(Path.Join(invalidBundleDir, "bundle.json"), invalidManifestJson);

                var validBundleUri = new Uri(Path.Join(LocalBundleDropDirectory, validBundleFolderName)).AbsoluteUri;
                var invalidBundleUri = new Uri(Path.Join(LocalBundleDropDirectory, invalidBundleFolderName)).AbsoluteUri;

                // 1. GET /Request/BundleComponents with empty buildUrl returns 200 OK with empty list
                var emptyBundleResp = caller.ExecuteRaw("Request/BundleComponents", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["buildDefinitionName"] = "BundleRelease",
                    ["buildUrl"] = string.Empty
                });
                Assert.AreEqual(HttpStatusCode.OK, emptyBundleResp.StatusCode, emptyBundleResp.Content);
                var emptyComponents = DorcApiClient.Deserialize<List<string>>(emptyBundleResp.Content) ?? new List<string>();
                Assert.IsEmpty(emptyComponents, "Expected empty buildUrl to return an empty component list.");

                // 2. GET /Request/BundleComponents with valid bundle.json returns distinct project component names
                var validBundleResp = caller.ExecuteRaw("Request/BundleComponents", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["buildDefinitionName"] = "BundleRelease",
                    ["buildUrl"] = validBundleUri
                });
                Assert.AreEqual(HttpStatusCode.OK, validBundleResp.StatusCode, $"GET /Request/BundleComponents (valid) failed: {validBundleResp.Content}");
                var resolvedComponents = DorcApiClient.Deserialize<List<string>>(validBundleResp.Content) ?? new List<string>();
                Assert.HasCount(2, resolvedComponents, $"Expected 2 distinct components from valid bundle.json, got: {string.Join(", ", resolvedComponents)}");
                CollectionAssert.Contains(resolvedComponents, SnapshotComponentName);
                CollectionAssert.Contains(resolvedComponents, NetFxRunnerComponentName);

                // 3. GET /Request/BundleComponents with unknown component in bundle.json returns 400 BadRequest (commit 58147d1b)
                var invalidBundleResp = caller.ExecuteRaw("Request/BundleComponents", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["buildDefinitionName"] = "BundleRelease",
                    ["buildUrl"] = invalidBundleUri
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, invalidBundleResp.StatusCode,
                    $"Expected 400 BadRequest when bundle.json references non-existent component, got {(int)invalidBundleResp.StatusCode}: {invalidBundleResp.Content}");
                StringAssert.Contains(invalidBundleResp.Content ?? string.Empty, "Bundle validation failed");
                StringAssert.Contains(invalidBundleResp.Content ?? string.Empty, $"Unknown_Bundle_Component_{runId}");

                // 4. POST /Request with valid bundle.json expands into separate requests per Build group in bundle.json (commit 625b048d)
                var submitBundleResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveParentEnvName,
                    BuildUrl = validBundleUri,
                    BuildText = "BundleRelease",
                    BuildNum = $"Bundle_{runId}",
                    Components = new List<string> { SnapshotComponentName, NetFxRunnerComponentName }
                });
                Assert.AreEqual(HttpStatusCode.OK, submitBundleResp.StatusCode, $"POST /Request with bundle.json failed: {submitBundleResp.Content}");
                var createdFirst = DorcApiClient.Deserialize<RequestStatusDto>(submitBundleResp.Content);
                Assert.IsNotNull(createdFirst);
                Assert.IsGreaterThan(0, createdFirst.Id, $"Expected positive request ID for expanded bundle deployment: {submitBundleResp.Content}");

                var finalFirst = WaitForTerminalRequestStatus(caller, createdFirst.Id, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", finalFirst.Status,
                    $"Expected first expanded bundle request {createdFirst.Id} to complete, got '{finalFirst.Status}'. Log: {GetCombinedRequestLog(caller, createdFirst.Id)}");
                Assert.AreEqual($"BundleBuild_A_{runId}", finalFirst.BuildNumber,
                    "Expected expanded bundle request to carry the component's per-item Build number from bundle.json.");

                // 5. POST /ReleaseInformation for the completed request
                var releaseInfoResp = caller.ExecuteRaw("ReleaseInformation", Method.Post, body: new
                {
                    RequestIds = new[] { createdFirst.Id }
                });
                Assert.AreEqual(HttpStatusCode.OK, releaseInfoResp.StatusCode, $"POST /ReleaseInformation failed: {releaseInfoResp.Content}");
                StringAssert.Contains(releaseInfoResp.Content ?? string.Empty, LiveProjectName);

                // 6. POST /CopyEnvBuild from LiveParentEnvName to LiveChildEnvName for SnapshotComponentName
                var refDataResp = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, refDataResp.StatusCode, refDataResp.Content);
                var refData = DorcApiClient.Deserialize<RefDataApiModel>(refDataResp.Content);
                Assert.IsNotNull(refData);
                Assert.IsNotNull(refData.Components);
                var snapshotComp = refData.Components.FirstOrDefault(c =>
                    string.Equals(c.ComponentName, SnapshotComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(snapshotComp, "Expected SnapshotComponent in RefData.");
                var snapshotComponentId = snapshotComp.ComponentId;
                Assert.IsTrue(snapshotComponentId.HasValue, "Expected SnapshotComponentId in RefData.");

                var copyEnvBuildResp = caller.ExecuteRaw("CopyEnvBuild", Method.Post, body: new CopyEnvBuildDto
                {
                    SourceEnv = LiveParentEnvName,
                    TargetEnv = LiveChildEnvName,
                    Project = LiveProjectName,
                    Components = snapshotComponentId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, copyEnvBuildResp.StatusCode, $"POST /CopyEnvBuild failed: {copyEnvBuildResp.Content}");
                var copyEnvBuildResult = DorcApiClient.Deserialize<CopyEnvBuildResponseDto>(copyEnvBuildResp.Content);
                Assert.IsNotNull(copyEnvBuildResult);
                Assert.IsTrue(copyEnvBuildResult.Success, $"Expected CopyEnvBuildResponseDto.Success=true, got: {copyEnvBuildResp.Content}");
                Assert.IsNotEmpty(copyEnvBuildResult.RequestIds, "Expected CopyEnvBuildResponseDto.RequestIds to contain the created request ID.");
            }
            finally
            {
                if (!string.Equals(project.ArtefactsUrl, originalArtefactsUrl, StringComparison.OrdinalIgnoreCase))
                {
                    project.ArtefactsUrl = originalArtefactsUrl;
                    caller.ExecuteRaw("RefDataProjects", Method.Put, body: project);
                }

                foreach (var dir in new[] { validBundleDir, invalidBundleDir })
                {
                    try
                    {
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                    }
                    catch (IOException ex)
                    {
                        Assert.Inconclusive($"Best-effort cleanup could not remove '{dir}': {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Assert.Inconclusive($"Best-effort cleanup could not remove '{dir}': {ex.Message}");
                    }
                }
            }
        }
    }
}
