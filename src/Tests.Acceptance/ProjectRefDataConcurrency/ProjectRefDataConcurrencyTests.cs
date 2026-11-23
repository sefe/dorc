using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.ProjectRefDataConcurrency
{
    [TestClass]
    [TestCategory("ProjectRefDataConcurrency")]
    public class ProjectRefDataConcurrencyTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void PutRefData_MissingOrStaleRevisionAndLocalScriptEdit_EnforcesOptimisticConcurrencyAndPreservesSharedScripts()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);

            var getRef1 = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, getRef1.StatusCode, getRef1.Content);
            var refDataTabA = DorcApiClient.Deserialize<RefDataApiModel>(getRef1.Content);
            var refDataTabB = DorcApiClient.Deserialize<RefDataApiModel>(getRef1.Content);
            Assert.IsNotNull(refDataTabA);
            Assert.IsNotNull(refDataTabB);
            Assert.IsFalse(string.IsNullOrWhiteSpace(refDataTabA.Revision), "Expected SHA-256 Revision token on GET /RefData/{id}.");

            var missingRevisionPayload = DorcApiClient.Deserialize<RefDataApiModel>(getRef1.Content)!;
            missingRevisionPayload.Revision = string.Empty;
            var missingRevResp = caller.ExecuteRaw("RefData", Method.Put, body: missingRevisionPayload);
            Assert.AreEqual(
                HttpStatusCode.PreconditionRequired,
                missingRevResp.StatusCode,
                $"Expected 428 PreconditionRequired when Revision is missing, got {missingRevResp.StatusCode}: {missingRevResp.Content}");

            var originalDescription = refDataTabA.Project.ProjectDescription;
            refDataTabA.Project.ProjectDescription = $"Automated live acceptance project ({Guid.NewGuid():N})";
            var tabASaveResp = caller.ExecuteRaw("RefData", Method.Put, body: refDataTabA);
            Assert.AreEqual(HttpStatusCode.OK, tabASaveResp.StatusCode, $"Valid PUT /RefData failed: {tabASaveResp.Content}");

            try
            {
                var staleDescription = "Stale concurrent edit that must be rejected";
                refDataTabB.Project.ProjectDescription = staleDescription;
                var tabBSaveResp = caller.ExecuteRaw("RefData", Method.Put, body: refDataTabB);
                Assert.AreEqual(
                    HttpStatusCode.Conflict,
                    tabBSaveResp.StatusCode,
                    $"Expected 409 Conflict when saving with a stale Revision token, got {tabBSaveResp.StatusCode}: {tabBSaveResp.Content}");

                var verifyResp = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, verifyResp.StatusCode, verifyResp.Content);
                var verifiedRefData = DorcApiClient.Deserialize<RefDataApiModel>(verifyResp.Content);
                Assert.IsNotNull(verifiedRefData);
                Assert.AreEqual(
                    refDataTabA.Project.ProjectDescription,
                    verifiedRefData.Project.ProjectDescription,
                    "Rejected 409 Conflict save must not partially overwrite project state.");

                var scriptsResp = caller.ExecuteRaw(
                    "RefDataScripts",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "50" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "Path", FilterValue = "DOrc_Live_Acceptance" }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, scriptsResp.StatusCode, scriptsResp.Content);
                StringAssert.Contains(scriptsResp.Content ?? string.Empty, "verify-properties.ps1");
            }
            finally
            {
                var latestResp = caller.ExecuteRaw($"RefData/{project.ProjectId}", Method.Get);
                if (latestResp.StatusCode == HttpStatusCode.OK)
                {
                    var latest = DorcApiClient.Deserialize<RefDataApiModel>(latestResp.Content);
                    if (latest != null)
                    {
                        latest.Project.ProjectDescription = originalDescription ?? "Automated live acceptance project";
                        caller.ExecuteRaw("RefData", Method.Put, body: latest);
                    }
                }
            }
        }
    }
}
