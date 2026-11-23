using System.Net;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.BundledRequestsMakeLikeProd
{
    [TestClass]
    [TestCategory("BundledRequestsMakeLikeProd")]
    public class BundledRequestMakeLikeProdTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
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
                createdBundleId = bundleItems[0].Id;

                var mlpResp = caller.ExecuteRaw("MakeLikeProd", Method.Put, body: new
                {
                    TargetEnv = LiveChildEnvName,
                    DataBackup = "Live Snap",
                    BundleName = bundleName,
                    BundleProperties = new List<RequestProperty>()
                });
                Assert.AreEqual(HttpStatusCode.OK, mlpResp.StatusCode, $"PUT /MakeLikeProd failed: {mlpResp.Content}");

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
    }
}
