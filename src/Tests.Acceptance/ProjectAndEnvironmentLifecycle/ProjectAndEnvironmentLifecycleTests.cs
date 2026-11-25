using System.Net;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.ProjectAndEnvironmentLifecycle
{
    /// <summary>
    /// End-to-end live acceptance test covering the full gamut of a DOrc project and environment
    /// lifecycle from zero to full teardown:
    /// 1. Provision parent and child environments and wire hierarchy inheritance.
    /// 2. Create, tag-update, and attach a server and a database to the child environment.
    /// 3. Create a brand-new project, map both environments, and configure a PowerShell 7
    ///    component tree via RefData with optimistic concurrency (409 Conflict) verification.
    /// 4. Provision global, parent-inherited, child-updated, request-overridden, and encrypted
    ///    secret variables, and verify Read Secrets masking via RefDataScopedPropertyValues.
    /// 5. Trigger a live deployment on the child environment, monitor RequestStatuses and
    ///    ResultStatuses to completion, verify the physical JSON output artifact produced by the
    ///    PowerShell script on disk, and verify secret redaction ([REDACTED]) across all logs.
    /// 6. Verify all 4 audit trails (RefDataProjectAudit, PropertyValuesAudit, ServerAudit,
    ///    DatabaseAudit) captured the exact Create/Insert and Update transitions.
    /// 7. Deterministically tear down all created resources in a finally block.
    /// </summary>
    [TestClass]
    [TestCategory("LiveEnvironment")]
    [TestCategory("ProjectAndEnvironmentLifecycle")]
    public class ProjectAndEnvironmentLifecycleTests
    {
        [TestMethod]
        public void ExecuteFullProjectAndEnvironmentLifecycle_FromScratchToDeploymentMonitoringLogsAndAudits()
        {
            AcceptanceProjectProvisioner.AssertLiveEnvironmentEnabled();
            var caller = new DorcApiClient();

            // Ensure baseline acceptance scripts and daemon status are initialized
            var buildUri = AcceptanceProjectProvisioner.EnsureProvisioned(caller);
            var (dropRootUri, _) = AcceptanceProjectProvisioner.ResolveConfiguredArtefactDrop(caller);
            var (ownerName, ownerId) = AcceptanceProjectProvisioner.ResolveDefaultOwner(caller);

            var cid = Guid.NewGuid().ToString("N")[..8];
            var parentEnvName = $"DOrc_Life_ParentEnv_{cid}";
            var childEnvName = $"DOrc_Life_ChildEnv_{cid}";
            var serverName = $"dorc-life-srv-{cid}";
            var databaseName = $"dorc_life_db_{cid}";
            var projectName = $"DOrc_Life_Proj_{cid}";
            var componentName = $"DOrc_Life_VerifyComp_{cid}";

            var globalPropName = $"Lifecycle_Global_{cid}";
            var parentPropName = $"Lifecycle_ParentInherited_{cid}";
            var childPropName = $"Lifecycle_ChildScoped_{cid}";
            var overridePropName = $"Lifecycle_RequestOverride_{cid}";
            var secretPropName = $"Lifecycle_Secret_{cid}";
            const string correlationPropName = "Lifecycle_CorrelationId";
            var secretCleartext = $"Sup3rS3cr3t_{cid}!";

            var createdProps = new List<string>
            {
                globalPropName,
                parentPropName,
                childPropName,
                overridePropName,
                secretPropName
            };

            EnvironmentApiModel? parentEnv = null;
            EnvironmentApiModel? childEnv = null;
            ServerApiModel? createdServer = null;
            DatabaseApiModel? createdDb = null;
            ProjectApiModel? createdProject = null;
            var serverAttached = false;
            var dbAttached = false;
            var parentMapped = false;
            var childMapped = false;
            var parentLinked = false;
            string? outputJsonPath = null;

            try
            {
                // =========================================================================
                // PHASE 1: Create Parent & Child Environments and Wire Hierarchy
                // =========================================================================
                var createParentResp = caller.ExecuteRaw("RefDataEnvironments", Method.Post, body: new EnvironmentApiModel
                {
                    EnvironmentName = parentEnvName,
                    EnvironmentSecure = false,
                    EnvironmentIsProd = false,
                    Details = new EnvironmentDetailsApiModel
                    {
                        EnvironmentOwner = ownerName,
                        EnvironmentOwnerId = ownerId,
                        Description = $"Lifecycle parent environment ({cid})",
                        FileShare = string.Empty,
                        Notes = "Managed by Tests.Acceptance",
                        RestoredFromSourceDb = string.Empty,
                        ThinClient = "Endur"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, createParentResp.StatusCode,
                    $"POST /RefDataEnvironments (parent) failed: {createParentResp.Content}");
                parentEnv = DorcApiClient.Deserialize<EnvironmentApiModel>(createParentResp.Content);
                Assert.IsNotNull(parentEnv);
                Assert.IsGreaterThan(0, parentEnv.EnvironmentId);

                var createChildResp = caller.ExecuteRaw("RefDataEnvironments", Method.Post, body: new EnvironmentApiModel
                {
                    EnvironmentName = childEnvName,
                    EnvironmentSecure = false,
                    EnvironmentIsProd = false,
                    Details = new EnvironmentDetailsApiModel
                    {
                        EnvironmentOwner = ownerName,
                        EnvironmentOwnerId = ownerId,
                        Description = $"Lifecycle child environment ({cid})",
                        FileShare = string.Empty,
                        Notes = "Managed by Tests.Acceptance",
                        RestoredFromSourceDb = string.Empty,
                        ThinClient = "Endur"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, createChildResp.StatusCode,
                    $"POST /RefDataEnvironments (child) failed: {createChildResp.Content}");
                childEnv = DorcApiClient.Deserialize<EnvironmentApiModel>(createChildResp.Content);
                Assert.IsNotNull(childEnv);
                Assert.IsGreaterThan(0, childEnv.EnvironmentId);

                var setParentResp = caller.ExecuteRaw(
                    "RefDataEnvironmentsDetails/SetParentForEnvironment",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["parentEnvId"] = parentEnv.EnvironmentId.ToString(),
                        ["childEnvId"] = childEnv.EnvironmentId.ToString()
                    });
                Assert.AreEqual(HttpStatusCode.OK, setParentResp.StatusCode,
                    $"SetParentForEnvironment failed: {setParentResp.Content}");
                parentLinked = true;

                var verifiedChildEnv = AcceptanceProjectProvisioner.GetEnvironmentByName(caller, childEnvName);
                Assert.IsNotNull(verifiedChildEnv);
                Assert.AreEqual(parentEnv.EnvironmentId, verifiedChildEnv.ParentId,
                    "Child environment must report ParentId pointing to the parent environment.");

                // =========================================================================
                // PHASE 2: Provision, Tag-Update, and Attach Server & Database
                // =========================================================================
                var createSrvResp = caller.ExecuteRaw("RefDataServers", Method.Post, body: new ServerApiModel
                {
                    Name = serverName,
                    OsName = "Windows Server 2022",
                    Tags = new[] { "appserv", $"lifecycle-{cid}" }
                });
                Assert.AreEqual(HttpStatusCode.OK, createSrvResp.StatusCode,
                    $"POST /RefDataServers failed: {createSrvResp.Content}");
                createdServer = DorcApiClient.Deserialize<ServerApiModel>(createSrvResp.Content);
                Assert.IsNotNull(createdServer);
                Assert.IsGreaterThan(0, createdServer.ServerId);

                var updateSrvResp = caller.ExecuteRaw(
                    "RefDataServers",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = createdServer.ServerId.ToString() },
                    new ServerApiModel
                    {
                        ServerId = createdServer.ServerId,
                        Name = serverName,
                        OsName = "Windows Server 2022",
                        Tags = new[] { "appserv", $"lifecycle-{cid}", "patched" }
                    });
                Assert.AreEqual(HttpStatusCode.OK, updateSrvResp.StatusCode,
                    $"PUT /RefDataServers failed: {updateSrvResp.Content}");

                var attachSrvResp = caller.ExecuteRaw(
                    "RefDataEnvironmentsDetails",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = createdServer.ServerId.ToString(),
                        ["action"] = "attach",
                        ["component"] = "server"
                    });
                Assert.AreEqual(HttpStatusCode.OK, attachSrvResp.StatusCode,
                    $"Attach server to child environment failed: {attachSrvResp.Content}");
                serverAttached = true;

                var createDbResp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = databaseName,
                    ServerName = @"localhost\SQLEXPRESS",
                    Tags = new[] { "Endur", "Report" },
                    AdGroup = "DEL-GG-GLO-DOrc-PowerUsers-NP"
                });
                Assert.AreEqual(HttpStatusCode.OK, createDbResp.StatusCode,
                    $"POST /RefDataDatabases failed: {createDbResp.Content}");
                createdDb = DorcApiClient.Deserialize<DatabaseApiModel>(createDbResp.Content);
                Assert.IsNotNull(createdDb);
                Assert.IsGreaterThan(0, createdDb.Id);

                var updateDbResp = caller.ExecuteRaw(
                    "RefDataDatabases",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = createdDb.Id.ToString() },
                    new DatabaseApiModel
                    {
                        Id = createdDb.Id,
                        Name = databaseName,
                        ServerName = @"localhost\SQLEXPRESS",
                        Tags = new[] { "Endur", "Report", "Analytics" },
                        AdGroup = "DEL-GG-GLO-DOrc-PowerUsers-NP"
                    });
                Assert.AreEqual(HttpStatusCode.OK, updateDbResp.StatusCode,
                    $"PUT /RefDataDatabases failed: {updateDbResp.Content}");

                var attachDbResp = caller.ExecuteRaw(
                    "RefDataEnvironmentsDetails",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = createdDb.Id.ToString(),
                        ["action"] = "attach",
                        ["component"] = "database"
                    });
                Assert.AreEqual(HttpStatusCode.OK, attachDbResp.StatusCode,
                    $"Attach database to child environment failed: {attachDbResp.Content}");
                dbAttached = true;

                var envContentResp = caller.ExecuteRaw($"RefDataEnvironmentsDetails/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, envContentResp.StatusCode, envContentResp.Content);
                var envContent = DorcApiClient.Deserialize<EnvironmentContentApiModel>(envContentResp.Content);
                Assert.IsNotNull(envContent);
                Assert.IsTrue(
                    envContent.AppServers?.Any(s => s.ServerId == createdServer.ServerId && (s.Tags?.Contains("patched") ?? false)) ?? false,
                    "Attached server with updated 'patched' tag must appear in child environment content.");
                Assert.IsTrue(
                    envContent.DbServers?.Any(d => d.Id == createdDb.Id && (d.Tags?.Contains("Analytics") ?? false)) ?? false,
                    "Attached database with updated 'Analytics' tag must appear in child environment content.");

                // =========================================================================
                // PHASE 3: Create Project, Map Environments, and Configure Component Tree
                // =========================================================================
                var createProjResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: new ProjectApiModel
                {
                    ProjectName = projectName,
                    ProjectDescription = $"Full lifecycle live E2E project ({cid})",
                    ArtefactsUrl = dropRootUri,
                    ArtefactsSubPaths = ".",
                    ArtefactsBuildRegex = string.Empty
                });
                Assert.AreEqual(HttpStatusCode.OK, createProjResp.StatusCode,
                    $"POST /RefDataProjects failed: {createProjResp.Content}");
                createdProject = DorcApiClient.Deserialize<ProjectApiModel>(createProjResp.Content);
                Assert.IsNotNull(createdProject);
                Assert.IsGreaterThan(0, createdProject.ProjectId);

                var mapParentResp = caller.ExecuteRaw(
                    "RefDataProjectEnvironmentMappings",
                    Method.Post,
                    new Dictionary<string, string>
                    {
                        ["project"] = projectName,
                        ["environment"] = parentEnvName
                    });
                Assert.AreEqual(HttpStatusCode.OK, mapParentResp.StatusCode,
                    $"Map parent env to project failed: {mapParentResp.Content}");
                parentMapped = true;

                var mapChildResp = caller.ExecuteRaw(
                    "RefDataProjectEnvironmentMappings",
                    Method.Post,
                    new Dictionary<string, string>
                    {
                        ["project"] = projectName,
                        ["environment"] = childEnvName
                    });
                Assert.AreEqual(HttpStatusCode.OK, mapChildResp.StatusCode,
                    $"Map child env to project failed: {mapChildResp.Content}");
                childMapped = true;

                var getRefDataResp = caller.ExecuteRaw($"RefData/{createdProject.ProjectId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getRefDataResp.StatusCode, getRefDataResp.Content);
                var refData = DorcApiClient.Deserialize<RefDataApiModel>(getRefDataResp.Content);
                Assert.IsNotNull(refData);
                var initialRevision = refData.Revision;
                Assert.IsFalse(string.IsNullOrWhiteSpace(initialRevision),
                    "GET /RefData/{id} must return a non-empty Revision concurrency token.");

                var scriptRelativePath = Path.Join(
                    AcceptanceProjectProvisioner.RelativeScriptSubfolder,
                    "verify-project-environment-lifecycle.ps1");

                refData.Components = new List<ComponentApiModel>
                {
                    new()
                    {
                        ComponentId = 0,
                        ComponentName = componentName,
                        ScriptPath = scriptRelativePath,
                        NonProdOnly = true,
                        StopOnFailure = true,
                        IsEnabled = true,
                        PSVersion = PowerShellVersion.V7.ToVersionString(),
                        ComponentType = ComponentType.PowerShell,
                        Children = new List<ComponentApiModel>()
                    }
                };

                var putRefDataResp = caller.ExecuteRaw("RefData", Method.Put, body: refData);
                Assert.AreEqual(HttpStatusCode.OK, putRefDataResp.StatusCode,
                    $"PUT /RefData component configuration failed: {putRefDataResp.Content}");
                var updatedRefData = DorcApiClient.Deserialize<RefDataApiModel>(putRefDataResp.Content);
                Assert.IsNotNull(updatedRefData);
                Assert.AreNotEqual(initialRevision, updatedRefData.Revision,
                    "PUT /RefData must advance the project Revision concurrency token.");

                // Verify optimistic concurrency: re-submitting stale revision returns 409 Conflict
                refData.Revision = initialRevision;
                var stalePutResp = caller.ExecuteRaw("RefData", Method.Put, body: refData);
                Assert.AreEqual(HttpStatusCode.Conflict, stalePutResp.StatusCode,
                    $"Replaying stale Revision token on PUT /RefData must return 409 Conflict, got {stalePutResp.StatusCode}: {stalePutResp.Content}");

                // =========================================================================
                // PHASE 4: Provision Variables Across Global, Parent, Child, and Secret Scopes
                // =========================================================================
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, correlationPropName, secure: false);
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, globalPropName, secure: false);
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, parentPropName, secure: false);
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, childPropName, secure: false);
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, overridePropName, secure: false);
                AcceptanceProjectProvisioner.EnsurePropertyDefinition(caller, secretPropName, secure: true);

                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, globalPropName, secure: false, envFilter: null, $"GlobalVal_{cid}");
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, parentPropName, secure: false, envFilter: parentEnvName, $"ParentVal_{cid}");
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, childPropName, secure: false, envFilter: childEnvName, $"ChildVal_V1_{cid}");
                // Update child-scoped property from V1 -> V2 to verify audit trail & runner value resolution
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, childPropName, secure: false, envFilter: childEnvName, $"ChildVal_V2_{cid}");
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, overridePropName, secure: false, envFilter: childEnvName, $"DefaultOverride_{cid}");
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, secretPropName, secure: true, envFilter: childEnvName, secretCleartext);
                AcceptanceProjectProvisioner.UpsertPropertyValue(caller, correlationPropName, secure: false, envFilter: childEnvName, cid);

                // Verify RefDataScopedPropertyValues returns child variables and masks the secret value
                var scopedValsResp = caller.ExecuteRaw(
                    "RefDataScopedPropertyValues",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["scope"] = childEnvName,
                        ["page"] = "1",
                        ["limit"] = "100"
                    },
                    new PagedDataOperators { Filters = new List<PagedDataFilter>() });
                Assert.AreEqual(HttpStatusCode.OK, scopedValsResp.StatusCode,
                    $"PUT /RefDataScopedPropertyValues failed: {scopedValsResp.Content}");
                Assert.IsFalse((scopedValsResp.Content ?? string.Empty).Contains(secretCleartext, StringComparison.Ordinal),
                    "RefDataScopedPropertyValues must never return cleartext secret values.");

                // =========================================================================
                // PHASE 5: Trigger Deployment, Monitor to Completion, Verify Output & Logs
                // =========================================================================
                var requestPostResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = projectName,
                    Environment = childEnvName,
                    BuildNum = AcceptanceProjectProvisioner.BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string> { componentName },
                    RequestProperties = new List<RequestProperty>
                    {
                        new()
                        {
                            PropertyName = overridePropName,
                            PropertyValue = $"OverriddenAtRequest_{cid}"
                        }
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, requestPostResp.StatusCode,
                    $"POST /Request failed ({requestPostResp.StatusCode}): {requestPostResp.Content}");
                var createdRequest = DorcApiClient.Deserialize<RequestStatusDto>(requestPostResp.Content);
                Assert.IsNotNull(createdRequest);
                Assert.IsGreaterThan(0, createdRequest.Id, $"Expected positive request ID, got {createdRequest.Id}");

                var finalStatus = PollUntilTerminalStatus(caller, createdRequest.Id, TimeSpan.FromMinutes(3));
                var combinedLog = $"{finalStatus.Log}\n{AcceptanceProjectProvisioner.GetCombinedRequestLog(caller, createdRequest.Id)}";

                Assert.AreEqual("Completed", finalStatus.Status,
                    $"Expected lifecycle deployment {createdRequest.Id} to reach 'Completed', got '{finalStatus.Status}'. Log:\n{combinedLog}");

                // Verify component result status
                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = createdRequest.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content);
                Assert.IsNotNull(results);
                Assert.HasCount(1, results);
                Assert.IsTrue(
                    string.Equals(results[0].Status, "Complete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(results[0].Status, "Completed", StringComparison.OrdinalIgnoreCase),
                    $"Expected component result status Complete/Completed, got '{results[0].Status}'.");

                // Verify stdout markers and secret redaction ([REDACTED])
                StringAssert.Contains(combinedLog, $"LIFECYCLE_SCRIPT_EXECUTION_VERIFIED_{cid}",
                    $"Runner log must contain verification marker. Log:\n{combinedLog}");
                StringAssert.Contains(combinedLog, "[REDACTED]",
                    $"Runner log must contain [REDACTED] placeholder where secret was echoed. Log:\n{combinedLog}");
                Assert.IsFalse(combinedLog.Contains(secretCleartext, StringComparison.Ordinal),
                    "Runner log must never contain cleartext secret value.");
                Assert.IsFalse((resultsResp.Content ?? string.Empty).Contains(secretCleartext, StringComparison.Ordinal),
                    "ResultStatuses response must never contain cleartext secret value.");

                // Verify physical JSON output file created by the PowerShell script on disk
                var publicFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
                var publicRoot = Directory.GetParent(publicFolder)?.FullName ?? @"C:\Users\Public";
                outputJsonPath = Path.Join(publicRoot, "DOrcLifecycleOutputs", $"lifecycle-output-{cid}.json");

                string? outputJsonContent = null;
                if (File.Exists(outputJsonPath))
                {
                    outputJsonContent = File.ReadAllText(outputJsonPath);
                }
                else
                {
                    // Fallback when running acceptance tests remotely against a target DV server
                    var markerPrefix = "LIFECYCLE_OUTPUT_JSON=";
                    var markerLine = combinedLog
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(l => l.Contains(markerPrefix, StringComparison.Ordinal));
                    if (markerLine != null)
                    {
                        var idx = markerLine.IndexOf(markerPrefix, StringComparison.Ordinal);
                        outputJsonContent = markerLine[(idx + markerPrefix.Length)..].Trim();
                    }
                }

                Assert.IsFalse(string.IsNullOrWhiteSpace(outputJsonContent),
                    $"Expected structured JSON output from lifecycle script at '{outputJsonPath}' or in runner stdout.");
                Assert.IsFalse(outputJsonContent!.Contains(secretCleartext, StringComparison.Ordinal),
                    "Structured JSON output artifact must never contain cleartext secret.");

                using (var doc = JsonDocument.Parse(outputJsonContent!))
                {
                    var root = doc.RootElement;
                    Assert.AreEqual(cid, root.GetProperty("CorrelationId").GetString());
                    Assert.AreEqual(childEnvName, root.GetProperty("EnvironmentName").GetString());
                    Assert.AreEqual("Core", root.GetProperty("PSEdition").GetString());
                    Assert.AreEqual($"GlobalVal_{cid}", root.GetProperty("GlobalValue").GetString());
                    Assert.AreEqual($"ParentVal_{cid}", root.GetProperty("ParentInheritedValue").GetString());
                    Assert.AreEqual($"ChildVal_V2_{cid}", root.GetProperty("ChildUpdatedValue").GetString());
                    Assert.AreEqual($"OverriddenAtRequest_{cid}", root.GetProperty("RequestOverrideValue").GetString());
                    Assert.IsTrue(root.GetProperty("SecretVerified").GetBoolean());
                }

                // Also verify raw log files on disk (if running on the Monitor host) do not contain cleartext secret
                const string rawLogDir = @"C:\Log\DOrc\Deploy\Services\Requests";
                if (Directory.Exists(rawLogDir))
                {
                    foreach (var logFile in Directory.GetFiles(rawLogDir, $"*{createdRequest.Id}*"))
                    {
                        var diskLog = File.ReadAllText(logFile);
                        Assert.IsFalse(diskLog.Contains(secretCleartext, StringComparison.Ordinal),
                            $"Raw runner log file '{logFile}' must never contain cleartext secret.");
                    }
                }

                // =========================================================================
                // PHASE 6: Verify All 4 Audit Trails (Project, Variables, Server, Database)
                // =========================================================================

                // 6.1 Project RefData Audit (PUT /RefDataProjectAudit)
                var projAuditResp = caller.ExecuteRaw(
                    "RefDataProjectAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["projectId"] = createdProject.ProjectId.ToString(),
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    new PagedDataOperators { Filters = new List<PagedDataFilter>() });
                Assert.AreEqual(HttpStatusCode.OK, projAuditResp.StatusCode,
                    $"PUT /RefDataProjectAudit failed: {projAuditResp.Content}");
                var projAuditList = DorcApiClient.Deserialize<GetRefDataAuditListResponseDto>(projAuditResp.Content);
                Assert.IsNotNull(projAuditList, "PUT /RefDataProjectAudit returned a null payload.");
                Assert.IsNotNull(projAuditList.Items, "PUT /RefDataProjectAudit returned null Items.");
                var projAuditItems = projAuditList.Items;
                Assert.IsNotEmpty(projAuditItems,
                    "Expected at least one RefDataProjectAudit entry for the newly configured project.");
                Assert.IsTrue(
                    projAuditItems.Any(a =>
                        a.ProjectId == createdProject.ProjectId &&
                        (a.Json?.Contains(componentName, StringComparison.OrdinalIgnoreCase) ?? false) &&
                        !string.IsNullOrWhiteSpace(a.Username)),
                    "RefDataProjectAudit must contain the component tree configuration with a non-empty Username.");

                // 6.2 Property Values Audit (PUT /PropertyValuesAudit)
                var propAuditResp = caller.ExecuteRaw(
                    "PropertyValuesAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new() { Path = "PropertyName", FilterValue = childPropName }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, propAuditResp.StatusCode,
                    $"PUT /PropertyValuesAudit failed: {propAuditResp.Content}");
                var propAuditList = DorcApiClient.Deserialize<GetPropertyValuesAuditListResponseDto>(propAuditResp.Content);
                Assert.IsNotNull(propAuditList, "PUT /PropertyValuesAudit returned a null payload.");
                Assert.IsNotNull(propAuditList.Items, "PUT /PropertyValuesAudit returned null Items.");
                var propAuditItems = propAuditList.Items;
                Assert.IsTrue(
                    propAuditItems.Any(a =>
                        string.Equals(a.Type, "Insert", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.ToValue, $"ChildVal_V1_{cid}", StringComparison.Ordinal)),
                    "PropertyValuesAudit must record the initial Insert of ChildVal_V1.");
                Assert.IsTrue(
                    propAuditItems.Any(a =>
                        string.Equals(a.Type, "Update", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.FromValue, $"ChildVal_V1_{cid}", StringComparison.Ordinal) &&
                        string.Equals(a.ToValue, $"ChildVal_V2_{cid}", StringComparison.Ordinal)),
                    "PropertyValuesAudit must record the Update transition from ChildVal_V1 to ChildVal_V2.");

                // 6.3 Server Audit (PUT /ServerAudit)
                var srvAuditResp = caller.ExecuteRaw(
                    "ServerAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new() { Path = "ToValue", FilterValue = serverName }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, srvAuditResp.StatusCode,
                    $"PUT /ServerAudit failed: {srvAuditResp.Content}");
                var srvAuditList = DorcApiClient.Deserialize<GetServerAuditListResponseDto>(srvAuditResp.Content);
                Assert.IsNotNull(srvAuditList, "PUT /ServerAudit returned a null payload.");
                Assert.IsNotNull(srvAuditList.Items, "PUT /ServerAudit returned null Items.");
                var srvAuditItems = srvAuditList.Items;
                Assert.IsTrue(
                    srvAuditItems.Any(a =>
                        string.Equals(a.ServerName, serverName, StringComparison.OrdinalIgnoreCase) &&
                        (a.ToValue?.Contains("patched", StringComparison.OrdinalIgnoreCase) ?? false)),
                    "ServerAudit must record the tag update ('patched') for the lifecycle server.");

                // 6.4 Database Audit (PUT /DatabaseAudit)
                var dbAuditResp = caller.ExecuteRaw(
                    "DatabaseAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new() { Path = "ToValue", FilterValue = databaseName }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, dbAuditResp.StatusCode,
                    $"PUT /DatabaseAudit failed: {dbAuditResp.Content}");
                var dbAuditList = DorcApiClient.Deserialize<GetDatabaseAuditListResponseDto>(dbAuditResp.Content);
                Assert.IsNotNull(dbAuditList, "PUT /DatabaseAudit returned a null payload.");
                Assert.IsNotNull(dbAuditList.Items, "PUT /DatabaseAudit returned null Items.");
                var dbAuditItems = dbAuditList.Items;
                Assert.IsTrue(
                    dbAuditItems.Any(a =>
                        string.Equals(a.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase) &&
                        (a.ToValue?.Contains("Analytics", StringComparison.OrdinalIgnoreCase) ?? false)),
                    "DatabaseAudit must record the tag update ('Analytics') for the lifecycle database.");
            }
            finally
            {
                // =========================================================================
                // PHASE 7: Deterministic Full Teardown
                // =========================================================================
                if (!string.IsNullOrWhiteSpace(outputJsonPath) && File.Exists(outputJsonPath))
                {
                    try
                    {
                        File.Delete(outputJsonPath);
                    }
                    catch (IOException)
                    {
                        // Best-effort cleanup of local verification output
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best-effort cleanup of local verification output
                    }
                }

                AcceptanceProjectProvisioner.DeletePropertiesBestEffort(caller, createdProps.ToArray());

                if (childEnv != null && createdServer != null && serverAttached)
                {
                    caller.ExecuteRaw(
                        "RefDataEnvironmentsDetails",
                        Method.Put,
                        new Dictionary<string, string>
                        {
                            ["envId"] = childEnv.EnvironmentId.ToString(),
                            ["componentId"] = createdServer.ServerId.ToString(),
                            ["action"] = "detach",
                            ["component"] = "server"
                        });
                }

                if (childEnv != null && createdDb != null && dbAttached)
                {
                    caller.ExecuteRaw(
                        "RefDataEnvironmentsDetails",
                        Method.Put,
                        new Dictionary<string, string>
                        {
                            ["envId"] = childEnv.EnvironmentId.ToString(),
                            ["componentId"] = createdDb.Id.ToString(),
                            ["action"] = "detach",
                            ["component"] = "database"
                        });
                }

                if (createdServer != null)
                {
                    caller.ExecuteRaw(
                        "RefDataServers",
                        Method.Delete,
                        new Dictionary<string, string> { ["serverId"] = createdServer.ServerId.ToString() });
                }

                if (createdDb != null)
                {
                    caller.ExecuteRaw(
                        "RefDataDatabases",
                        Method.Delete,
                        new Dictionary<string, string> { ["databaseId"] = createdDb.Id.ToString() });
                }

                if (createdProject != null && childMapped)
                {
                    caller.ExecuteRaw(
                        "RefDataProjectEnvironmentMappings",
                        Method.Delete,
                        new Dictionary<string, string>
                        {
                            ["project"] = projectName,
                            ["environment"] = childEnvName
                        });
                }

                if (createdProject != null && parentMapped)
                {
                    caller.ExecuteRaw(
                        "RefDataProjectEnvironmentMappings",
                        Method.Delete,
                        new Dictionary<string, string>
                        {
                            ["project"] = projectName,
                            ["environment"] = parentEnvName
                        });
                }

                if (childEnv != null && parentLinked)
                {
                    caller.ExecuteRaw(
                        "RefDataEnvironmentsDetails/SetParentForEnvironment",
                        Method.Put,
                        new Dictionary<string, string>
                        {
                            ["childEnvId"] = childEnv.EnvironmentId.ToString()
                        });
                }

                if (childEnv != null)
                {
                    caller.ExecuteRaw(
                        "RefDataEnvironments",
                        Method.Delete,
                        body: new EnvironmentApiModel
                        {
                            EnvironmentId = childEnv.EnvironmentId,
                            EnvironmentName = childEnvName
                        });
                }

                if (parentEnv != null)
                {
                    caller.ExecuteRaw(
                        "RefDataEnvironments",
                        Method.Delete,
                        body: new EnvironmentApiModel
                        {
                            EnvironmentId = parentEnv.EnvironmentId,
                            EnvironmentName = parentEnvName
                        });
                }

                if (createdProject != null)
                {
                    caller.ExecuteRaw($"RefDataProjects/{createdProject.ProjectId}", Method.Delete);
                }
            }
        }

        private static DeploymentRequestApiModel PollUntilTerminalStatus(DorcApiClient caller, int requestId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            DeploymentRequestApiModel? last = null;

            while (DateTime.UtcNow < deadline)
            {
                var resp = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = requestId.ToString()
                });

                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    last = DorcApiClient.Deserialize<DeploymentRequestApiModel>(resp.Content);
                    if (last != null &&
                        (string.Equals(last.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(last.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(last.Status, "Errored", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(last.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)))
                    {
                        return last;
                    }
                }

                Thread.Sleep(TimeSpan.FromSeconds(2));
            }

            Assert.Fail($"Deployment request {requestId} did not reach a terminal status within {timeout}. Last status: '{last?.Status}'.");
            return null!;
        }
    }
}
