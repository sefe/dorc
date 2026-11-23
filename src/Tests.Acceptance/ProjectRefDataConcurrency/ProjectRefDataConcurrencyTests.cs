using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

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

        [TestMethod]
        public void MapAndUnmapEnvironmentToProject_RefDataProjectsHierarchyAndEnvironmentMappings_VerifiesProjectLookupParentChainAndMappingLifecycle()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var allProjectsResp = caller.ExecuteRaw("RefDataProjects", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, allProjectsResp.StatusCode, allProjectsResp.Content);
            var allProjects = DorcApiClient.Deserialize<List<ProjectApiModel>>(allProjectsResp.Content) ?? new List<ProjectApiModel>();
            Assert.IsTrue(allProjects.Any(p => string.Equals(p.ProjectName, LiveProjectName, StringComparison.OrdinalIgnoreCase)));

            var projectResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, projectResp.StatusCode, projectResp.Content);
            var project = DorcApiClient.Deserialize<ProjectApiModel>(projectResp.Content);
            Assert.IsNotNull(project);
            Assert.IsGreaterThan(0, project.ProjectId);
            Assert.AreEqual(LiveProjectName, project.ProjectName);
            Assert.IsNotNull(project.ProjectDescription);
            Assert.IsNotNull(project.ArtefactsUrl);
            Assert.IsNotNull(project.ArtefactsSubPaths);
            Assert.IsNotNull(project.ArtefactsBuildRegex);

            var projectAuditResp = caller.ExecuteRaw(
                "RefDataProjectAudit",
                Method.Put,
                new Dictionary<string, string>
                {
                    ["projectId"] = project.ProjectId.ToString(),
                    ["page"] = "1",
                    ["limit"] = "20"
                },
                body: new PagedDataOperators());
            Assert.AreEqual(HttpStatusCode.OK, projectAuditResp.StatusCode, projectAuditResp.Content);

            var scriptsAuditResp = caller.ExecuteRaw(
                "ScriptsAudit",
                Method.Put,
                new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                body: new PagedDataOperators());
            Assert.AreEqual(HttpStatusCode.OK, scriptsAuditResp.StatusCode, scriptsAuditResp.Content);

            var parentEnv = GetEnvironmentByName(caller, LiveParentEnvName);
            Assert.IsNotNull(parentEnv);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var tempEnvName = $"DOrc_Live_MapEnv_{suffix}";
            int? tempEnvId = null;

            try
            {
                var createEnvResp = caller.ExecuteRaw("RefDataEnvironments", Method.Post, body: new EnvironmentApiModel
                {
                    EnvironmentName = tempEnvName,
                    EnvironmentSecure = false,
                    EnvironmentIsProd = false,
                    Details = new EnvironmentDetailsApiModel
                    {
                        EnvironmentOwner = parentEnv.Details?.EnvironmentOwner ?? Environment.UserName,
                        Description = "Temporary environment for project mapping and hierarchy test",
                        FileShare = string.Empty,
                        ThinClient = string.Empty,
                        RestoredFromSourceDb = string.Empty,
                        Notes = "Automated live test"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, createEnvResp.StatusCode, $"POST /RefDataEnvironments failed: {createEnvResp.Content}");
                var createdEnv = DorcApiClient.Deserialize<EnvironmentApiModel>(createEnvResp.Content);
                Assert.IsNotNull(createdEnv);
                Assert.IsGreaterThan(0, createdEnv.EnvironmentId);
                tempEnvId = createdEnv.EnvironmentId;

                var possibleBeforeMapResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/GetPossibleEnvironmentChildren", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = parentEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, possibleBeforeMapResp.StatusCode, possibleBeforeMapResp.Content);
                Assert.IsFalse(
                    (possibleBeforeMapResp.Content ?? string.Empty).Contains(tempEnvName, StringComparison.OrdinalIgnoreCase),
                    "Unmapped environment must not appear in GetPossibleEnvironmentChildren before sharing a project with the parent.");

                var mapResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
                {
                    ["project"] = LiveProjectName,
                    ["environment"] = tempEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, mapResp.StatusCode, $"POST /RefDataProjectEnvironmentMappings failed: {mapResp.Content}");

                var mappingsAfterAddResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Get, new Dictionary<string, string>
                {
                    ["project"] = LiveProjectName,
                    ["includeRead"] = "true"
                });
                Assert.AreEqual(HttpStatusCode.OK, mappingsAfterAddResp.StatusCode, mappingsAfterAddResp.Content);
                var mappingsAfterAdd = DorcApiClient.Deserialize<TemplateApiModel<EnvironmentApiModel>>(mappingsAfterAddResp.Content);
                Assert.IsNotNull(mappingsAfterAdd?.Items);
                Assert.IsTrue(
                    mappingsAfterAdd.Items.Any(e => e.EnvironmentId == tempEnvId.Value && string.Equals(e.EnvironmentName, tempEnvName, StringComparison.OrdinalIgnoreCase)),
                    $"Expected GET /RefDataProjectEnvironmentMappings to include newly mapped environment '{tempEnvName}'.");

                var possibleChildrenResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/GetPossibleEnvironmentChildren", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = parentEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, possibleChildrenResp.StatusCode, possibleChildrenResp.Content);
                StringAssert.Contains(possibleChildrenResp.Content ?? string.Empty, tempEnvName);

                var setParentResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                {
                    ["parentEnvId"] = parentEnv.EnvironmentId.ToString(),
                    ["childEnvId"] = tempEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, setParentResp.StatusCode, setParentResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(setParentResp.Content)?.Result ?? false, setParentResp.Content);

                var childAfterAttach = GetEnvironmentByName(caller, tempEnvName);
                Assert.IsNotNull(childAfterAttach);
                Assert.AreEqual(parentEnv.EnvironmentId, childAfterAttach.ParentId);

                var possibleWhileChildResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/GetPossibleEnvironmentChildren", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = parentEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, possibleWhileChildResp.StatusCode, possibleWhileChildResp.Content);
                Assert.IsFalse(
                    (possibleWhileChildResp.Content ?? string.Empty).Contains(tempEnvName, StringComparison.OrdinalIgnoreCase),
                    "Already-attached child environment must not appear in GetPossibleEnvironmentChildren.");

                var clearParentResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                {
                    ["childEnvId"] = tempEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, clearParentResp.StatusCode, clearParentResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(clearParentResp.Content)?.Result ?? false, clearParentResp.Content);

                var childAfterDetach = GetEnvironmentByName(caller, tempEnvName);
                Assert.IsNotNull(childAfterDetach);
                Assert.IsNull(childAfterDetach.ParentId);

                var unmapResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Delete, new Dictionary<string, string>
                {
                    ["project"] = LiveProjectName,
                    ["environment"] = tempEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, unmapResp.StatusCode, $"DELETE /RefDataProjectEnvironmentMappings failed: {unmapResp.Content}");

                var mappingsAfterDeleteResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Get, new Dictionary<string, string>
                {
                    ["project"] = LiveProjectName,
                    ["includeRead"] = "true"
                });
                Assert.AreEqual(HttpStatusCode.OK, mappingsAfterDeleteResp.StatusCode, mappingsAfterDeleteResp.Content);
                var mappingsAfterDelete = DorcApiClient.Deserialize<TemplateApiModel<EnvironmentApiModel>>(mappingsAfterDeleteResp.Content);
                Assert.IsNotNull(mappingsAfterDelete?.Items);
                Assert.IsFalse(
                    mappingsAfterDelete.Items.Any(e => e.EnvironmentId == tempEnvId.Value || string.Equals(e.EnvironmentName, tempEnvName, StringComparison.OrdinalIgnoreCase)),
                    $"Expected unmapped environment '{tempEnvName}' to be absent from GET /RefDataProjectEnvironmentMappings.");

                // RefDataComponents and RefDataScripts/edit Postman & global shared script edit endpoints
                var refDataComponentsResp = caller.ExecuteRaw("RefDataComponents", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = LiveProjectName
                });
                Assert.AreEqual(HttpStatusCode.OK, refDataComponentsResp.StatusCode, refDataComponentsResp.Content);
                var refDataComponents = DorcApiClient.Deserialize<TemplateApiModel<ComponentApiModel>>(refDataComponentsResp.Content);
                Assert.IsNotNull(refDataComponents?.Items);
                var verifyPropsComp = refDataComponents.Items.FirstOrDefault(c => string.Equals(c.ComponentName, VerifyPropsComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(verifyPropsComp, $"Expected GET /RefDataComponents?id={LiveProjectName} to include '{VerifyPropsComponentName}'.");

                var scriptsFilteredResp = caller.ExecuteRaw(
                    "RefDataScripts",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "50" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "Path", FilterValue = "verify-properties.ps1" }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, scriptsFilteredResp.StatusCode, scriptsFilteredResp.Content);
                var scriptsFilteredPage = DorcApiClient.Deserialize<GetScriptsListResponseDto>(scriptsFilteredResp.Content);
                var scriptToEdit = scriptsFilteredPage?.Items?.FirstOrDefault(s =>
                    s.Path != null && s.Path.EndsWith("verify-properties.ps1", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(scriptToEdit, "Expected to find verify-properties.ps1 in PUT /RefDataScripts.");

                var missingRevEditScriptResp = caller.ExecuteRaw("RefDataScripts/edit", Method.Put, body: new ScriptApiModel
                {
                    Id = scriptToEdit.Id,
                    Name = scriptToEdit.Name,
                    Path = scriptToEdit.Path,
                    IsPathJSON = scriptToEdit.IsPathJSON,
                    NonProdOnly = scriptToEdit.NonProdOnly
                });
                Assert.AreEqual(
                    HttpStatusCode.PreconditionRequired,
                    missingRevEditScriptResp.StatusCode,
                    $"Expected 428 PreconditionRequired when ExpectedRevision is omitted from PUT /RefDataScripts/edit, got {missingRevEditScriptResp.StatusCode}: {missingRevEditScriptResp.Content}");

                var editScriptResp = caller.ExecuteRaw("RefDataScripts/edit", Method.Put, body: new ScriptApiModel
                {
                    Id = scriptToEdit.Id,
                    Name = scriptToEdit.Name,
                    Path = scriptToEdit.Path,
                    IsPathJSON = scriptToEdit.IsPathJSON,
                    NonProdOnly = scriptToEdit.NonProdOnly,
                    Revision = scriptToEdit.Revision,
                    ConfirmImpact = true,
                    AffectedProjectIds = scriptToEdit.AffectedProjectIds,
                    IsEnabled = scriptToEdit.IsEnabled,
                    PowerShellVersionNumber = scriptToEdit.PowerShellVersionNumber
                });
                Assert.AreEqual(HttpStatusCode.OK, editScriptResp.StatusCode, $"PUT /RefDataScripts/edit failed: {editScriptResp.Content}");
            }
            finally
            {
                caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Delete, new Dictionary<string, string>
                {
                    ["project"] = LiveProjectName,
                    ["environment"] = tempEnvName
                });
                if (tempEnvId.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                    {
                        ["childEnvId"] = tempEnvId.Value.ToString()
                    });
                    caller.ExecuteRaw("RefDataEnvironments", Method.Delete, body: new EnvironmentApiModel
                    {
                        EnvironmentId = tempEnvId.Value,
                        EnvironmentName = tempEnvName
                    });
                }
            }
        }
    }
}
