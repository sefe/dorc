using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.EnvironmentCloningAndDatabases
{
    [TestClass]
    [TestCategory("LiveEnvironment")]
    [TestCategory("EnvironmentCloningAndDatabases")]
    public class EnvironmentCloningAndDatabaseAttachmentTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void CloneEnvironmentAndAttachDatabase_CopyFlagsTagLookupAndScopeCollision_IsolatesClonedResourcesAndDetectsTagCollision()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var db1Name = $"DOrc_Live_DB_{suffix}";
            var db2Name = $"DOrc_Live_DB_Collide_{suffix}";
            var dbServer = $"SQLDV_{suffix}";
            var tagNormalized = $"LiveTag_{suffix}";
            var tagColliding = $"LiveTag {suffix}";
            var clonedEnvName = $"DOrc_Live_Clone_{suffix}";

            int? db1Id = null;
            int? db2Id = null;
            int? clonedEnvId = null;

            try
            {
                var createDb1Resp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = db1Name,
                    ServerName = dbServer,
                    Tags = new[] { tagNormalized, "Endur Reporting" },
                    DatabaseType = DatabaseType.SqlServer,
                    AdGroup = "everyone"
                });
                Assert.AreEqual(HttpStatusCode.OK, createDb1Resp.StatusCode, $"POST /RefDataDatabases failed: {createDb1Resp.Content}");
                var createdDb1 = DorcApiClient.Deserialize<DatabaseApiModel>(createDb1Resp.Content);
                Assert.IsNotNull(createdDb1);
                db1Id = createdDb1.Id;
                Assert.AreEqual(DatabaseType.SqlServer, createdDb1.DatabaseType);

                var attachDb1Resp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = db1Id.Value.ToString(),
                    ["action"] = "attach",
                    ["component"] = "database"
                });
                Assert.AreEqual(HttpStatusCode.OK, attachDb1Resp.StatusCode, attachDb1Resp.Content);

                var byTagResp = caller.ExecuteRaw("RefDataDatabases/ByTag", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["tag"] = tagNormalized
                });
                Assert.AreEqual(HttpStatusCode.OK, byTagResp.StatusCode, byTagResp.Content);
                StringAssert.Contains(byTagResp.Content ?? string.Empty, db1Name);

                var relatedResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = db1Id.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, relatedResp.StatusCode, relatedResp.Content);
                var related = DorcApiClient.Deserialize<DatabaseRelatedResourcesApiModel>(relatedResp.Content);
                Assert.IsNotNull(related);
                Assert.IsTrue(
                    related.Environments.Any(e => string.Equals(e.EnvironmentName, LiveChildEnvName, StringComparison.OrdinalIgnoreCase)),
                    "Expected RelatedResources to list LiveChildEnvName as attached to the database.");

                var scopeOkResp = caller.ExecuteRaw("PropertyValues/ScopeOptions", Method.Get, new Dictionary<string, string>
                {
                    ["propertyValueScope"] = LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, scopeOkResp.StatusCode, scopeOkResp.Content);
                StringAssert.Contains(scopeOkResp.Content ?? string.Empty, db1Name);

                var createDb2Resp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = db2Name,
                    ServerName = dbServer,
                    Tags = new[] { tagColliding, "Endur Reporting" },
                    DatabaseType = DatabaseType.SqlServer,
                    AdGroup = "everyone"
                });
                Assert.AreEqual(HttpStatusCode.OK, createDb2Resp.StatusCode, createDb2Resp.Content);
                var createdDb2 = DorcApiClient.Deserialize<DatabaseApiModel>(createDb2Resp.Content);
                Assert.IsNotNull(createdDb2);
                var createdDb2Id = createdDb2.Id;
                db2Id = createdDb2Id;

                var attachDb2Resp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = createdDb2Id.ToString(),
                    ["action"] = "attach",
                    ["component"] = "database"
                });
                Assert.AreEqual(HttpStatusCode.OK, attachDb2Resp.StatusCode, attachDb2Resp.Content);

                var scopeCollisionResp = caller.ExecuteRaw("PropertyValues/ScopeOptions", Method.Get, new Dictionary<string, string>
                {
                    ["propertyValueScope"] = LiveChildEnvName
                });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    scopeCollisionResp.StatusCode,
                    $"Expected 400 BadRequest when two attached databases carry singleton tag 'Endur Reporting', got {scopeCollisionResp.StatusCode}: {scopeCollisionResp.Content}");

                caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = createdDb2Id.ToString(),
                    ["action"] = "detach",
                    ["component"] = "database"
                });

                var cloneResp = caller.ExecuteRaw("RefDataEnvironments/Clone", Method.Post, body: new CloneEnvironmentRequest
                {
                    SourceEnvironmentId = childEnv.EnvironmentId,
                    NewEnvironmentName = clonedEnvName,
                    CopyPropertyValues = true,
                    CopyServerMappings = false,
                    CopyDatabaseMappings = true,
                    CopyProjectMappings = true,
                    CopyAccessControls = true
                });
                Assert.AreEqual(HttpStatusCode.OK, cloneResp.StatusCode, $"POST /RefDataEnvironments/Clone failed: {cloneResp.Content}");
                var clonedEnv = DorcApiClient.Deserialize<EnvironmentApiModel>(cloneResp.Content);
                Assert.IsNotNull(clonedEnv);
                clonedEnvId = clonedEnv.EnvironmentId;
                Assert.IsGreaterThan(0, clonedEnvId.Value);

                var clonedDbsResp = caller.ExecuteRaw($"RefDataDatabases/ByEnvId/{clonedEnvId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, clonedDbsResp.StatusCode, clonedDbsResp.Content);
                StringAssert.Contains(clonedDbsResp.Content ?? string.Empty, db1Name);

                // Verify 2-level environment hierarchy invariant (legacy RefDataEnvironmentsDetails.feature:
                // a child environment cannot become the parent of a third environment, while a root parent can).
                var parentEnv = GetEnvironmentByName(caller, LiveParentEnvName);
                Assert.IsNotNull(parentEnv);

                var invalidGrandchildResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                {
                    ["parentEnvId"] = childEnv.EnvironmentId.ToString(),
                    ["childEnvId"] = clonedEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, invalidGrandchildResp.StatusCode, invalidGrandchildResp.Content);
                var invalidGrandchildResult = DorcApiClient.Deserialize<ApiBoolResult>(invalidGrandchildResp.Content);
                Assert.IsFalse(invalidGrandchildResult?.Result ?? true, "Setting a child environment as the parent of another environment must fail (Result=false).");

                var validSetParentResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                {
                    ["parentEnvId"] = parentEnv.EnvironmentId.ToString(),
                    ["childEnvId"] = clonedEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, validSetParentResp.StatusCode, validSetParentResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(validSetParentResp.Content)?.Result ?? false, "Setting a valid root parent environment must succeed.");

                var detachParentResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/SetParentForEnvironment", Method.Put, new Dictionary<string, string>
                {
                    ["childEnvId"] = clonedEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, detachParentResp.StatusCode, detachParentResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(detachParentResp.Content)?.Result ?? false, "Detaching parent environment (parentEnvId=null) must succeed.");
            }
            finally
            {
                if (clonedEnvId.HasValue)
                {
                    if (db1Id.HasValue)
                    {
                        caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                        {
                            ["envId"] = clonedEnvId.Value.ToString(),
                            ["componentId"] = db1Id.Value.ToString(),
                            ["action"] = "detach",
                            ["component"] = "database"
                        });
                    }
                    caller.ExecuteRaw("RefDataEnvironments", Method.Delete, body: new EnvironmentApiModel
                    {
                        EnvironmentId = clonedEnvId.Value,
                        EnvironmentName = clonedEnvName
                    });
                }

                if (db2Id.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = db2Id.Value.ToString(),
                        ["action"] = "detach",
                        ["component"] = "database"
                    });
                    caller.ExecuteRaw($"RefDataDatabases/{db2Id.Value}", Method.Delete);
                }

                if (db1Id.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = db1Id.Value.ToString(),
                        ["action"] = "detach",
                        ["component"] = "database"
                    });
                    caller.ExecuteRaw($"RefDataDatabases/{db1Id.Value}", Method.Delete);
                }
            }
        }

        [TestMethod]
        public void CreateAttachDetachAndDeleteServerAndDatabase_RefDataServersAndDatabasesLifecycle_VerifiesTagsAttachmentUsersAuditAndDeletionNoContent()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var serverName = $"DORCSRV_{suffix}";
            var dbName = $"DORCDB_{suffix}";
            var dbServerName = $"DORCSQL_{suffix}\\INST01";

            int? serverId = null;
            int? dbId = null;
            int? secondDbId = null;

            try
            {
                var createSrvResp = caller.ExecuteRaw("RefDataServers", Method.Post, body: new ServerApiModel
                {
                    Name = serverName,
                    OsName = "Windows Server 2022 Standard",
                    UserEditable = true
                });
                Assert.AreEqual(HttpStatusCode.OK, createSrvResp.StatusCode, $"POST /RefDataServers failed: {createSrvResp.Content}");
                var createdSrv = DorcApiClient.Deserialize<ServerApiModel>(createSrvResp.Content);
                Assert.IsNotNull(createdSrv);
                Assert.IsGreaterThan(0, createdSrv.ServerId);
                Assert.AreEqual(serverName, createdSrv.Name);
                Assert.AreEqual("Windows Server 2022 Standard", createdSrv.OsName);
                serverId = createdSrv.ServerId;

                var dupSrvResp = caller.ExecuteRaw("RefDataServers", Method.Post, body: new ServerApiModel
                {
                    Name = serverName,
                    OsName = "Windows Server 2022 Standard",
                    UserEditable = true
                });
                if (dupSrvResp.StatusCode == HttpStatusCode.OK)
                {
                    var dupSrv = DorcApiClient.Deserialize<ServerApiModel>(dupSrvResp.Content);
                    Assert.AreEqual(0, dupSrv?.ServerId ?? 0, "Duplicate server creation must not assign a new positive ServerId.");
                }

                var getSrvByNameResp = caller.ExecuteRaw($"RefDataServers/{Uri.EscapeDataString(serverName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getSrvByNameResp.StatusCode, getSrvByNameResp.Content);
                var srvByName = DorcApiClient.Deserialize<ServerApiModel>(getSrvByNameResp.Content);
                Assert.IsNotNull(srvByName);
                Assert.AreEqual(serverId.Value, srvByName.ServerId);
                Assert.AreEqual(serverName, srvByName.Name);

                var getSrvByIdResp = caller.ExecuteRaw($"RefDataServers/ById/{serverId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getSrvByIdResp.StatusCode, getSrvByIdResp.Content);
                var srvById = DorcApiClient.Deserialize<ServerApiModel>(getSrvByIdResp.Content);
                Assert.IsNotNull(srvById);
                Assert.AreEqual(serverId.Value, srvById.ServerId);
                Assert.AreEqual(serverName, srvById.Name);

                var updateSrvResp = caller.ExecuteRaw(
                    "RefDataServers",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = serverId.Value.ToString() },
                    body: new ServerApiModel
                    {
                        ServerId = serverId.Value,
                        Name = serverName,
                        OsName = "Windows Server 2022 Standard",
                        UserEditable = true,
                        Tags = new[] { "DevOpsTest", "AppTier" }
                    });
                Assert.AreEqual(HttpStatusCode.OK, updateSrvResp.StatusCode, $"PUT /RefDataServers failed: {updateSrvResp.Content}");
                var updatedSrv = DorcApiClient.Deserialize<ServerApiModel>(updateSrvResp.Content);
                Assert.IsNotNull(updatedSrv);
                CollectionAssert.Contains(updatedSrv.Tags.ToList(), "DevOpsTest");
                CollectionAssert.Contains(updatedSrv.Tags.ToList(), "AppTier");

                var attachSrvResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = serverId.Value.ToString(),
                    ["action"] = "attach",
                    ["component"] = "server"
                });
                Assert.AreEqual(HttpStatusCode.OK, attachSrvResp.StatusCode, attachSrvResp.Content);
                var attachSrvResult = DorcApiClient.Deserialize<ApiBoolResult>(attachSrvResp.Content);
                Assert.IsTrue(attachSrvResult?.Result ?? false, $"Expected server attach Result=true, got: {attachSrvResp.Content}");

                var dupAttachSrvResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = serverId.Value.ToString(),
                    ["action"] = "attach",
                    ["component"] = "server"
                });
                Assert.AreEqual(HttpStatusCode.OK, dupAttachSrvResp.StatusCode, dupAttachSrvResp.Content);

                Assert.IsTrue(serverId.HasValue, "Expected serverId to have a value before server environment detail checks.");
                var serverIdValue = serverId.Value;

                var badActionSrvResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = serverIdValue.ToString(),
                    ["action"] = "invalid_action",
                    ["component"] = "server"
                });
                var badActionSrvResult = DorcApiClient.Deserialize<ApiBoolResult>(badActionSrvResp.Content);
                Assert.IsFalse(badActionSrvResult?.Result ?? true, "Invalid component action must return Result=false.");

                var envSrvsResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = childEnv.EnvironmentId.ToString(),
                    ["type"] = "1"
                });
                Assert.AreEqual(HttpStatusCode.OK, envSrvsResp.StatusCode, envSrvsResp.Content);
                var envSrvsDto = DorcApiClient.Deserialize<EnvironmentComponentsDto<ServerApiModel>>(envSrvsResp.Content);
                Assert.IsNotNull(envSrvsDto);
                Assert.IsNotNull(envSrvsDto.Result);
                var envSrvsResult = envSrvsDto.Result;
                Assert.AreEqual(
                    1,
                    envSrvsResult.Count(s => s.ServerId == serverIdValue),
                    "Re-attaching an already-attached server must be idempotent and never create duplicate environment-server rows.");
                var attachedSrv = envSrvsResult.Single(s => s.ServerId == serverIdValue);
                Assert.AreEqual(serverName, attachedSrv.Name);
                CollectionAssert.Contains(attachedSrv.Tags.ToList(), "DevOpsTest");

                var srvAuditResp = caller.ExecuteRaw(
                    "ServerAudit",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, srvAuditResp.StatusCode, srvAuditResp.Content);
                StringAssert.Contains(srvAuditResp.Content ?? string.Empty, serverName);

                var detachSrvResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = serverId.Value.ToString(),
                    ["action"] = "detach",
                    ["component"] = "server"
                });
                Assert.AreEqual(HttpStatusCode.OK, detachSrvResp.StatusCode, detachSrvResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(detachSrvResp.Content)?.Result ?? false);

                var deleteSrvResp = caller.ExecuteRaw("RefDataServers", Method.Delete, new Dictionary<string, string>
                {
                    ["serverId"] = serverId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, deleteSrvResp.StatusCode, deleteSrvResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(deleteSrvResp.Content)?.Result ?? false);

                var getDeletedSrvByName = caller.ExecuteRaw($"RefDataServers/{Uri.EscapeDataString(serverName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.NoContent, getDeletedSrvByName.StatusCode, "GET /RefDataServers/{name} after deletion must return 204 NoContent.");
                var getDeletedSrvById = caller.ExecuteRaw($"RefDataServers/ById/{serverId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.NotFound, getDeletedSrvById.StatusCode, "GET /RefDataServers/ById/{id} after deletion must return 404 NotFound.");
                StringAssert.Contains(getDeletedSrvById.Content ?? string.Empty, "Server not found");
                serverId = null;

                var createDbResp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = dbName,
                    ServerName = dbServerName,
                    DatabaseType = DatabaseType.SqlServer,
                    AdGroup = "OTHER",
                    Tags = new[] { "DevOpsDbTag" }
                });
                Assert.AreEqual(HttpStatusCode.OK, createDbResp.StatusCode, $"POST /RefDataDatabases failed: {createDbResp.Content}");
                var createdDb = DorcApiClient.Deserialize<DatabaseApiModel>(createDbResp.Content);
                Assert.IsNotNull(createdDb);
                Assert.IsGreaterThan(0, createdDb.Id);
                Assert.AreEqual(dbName, createdDb.Name);
                Assert.AreEqual(dbServerName, createdDb.ServerName);
                Assert.AreEqual("OTHER", createdDb.AdGroup);
                dbId = createdDb.Id;

                var getDbListResp = caller.ExecuteRaw("RefDataDatabases", Method.Get, new Dictionary<string, string>
                {
                    ["name"] = dbName,
                    ["server"] = dbServerName
                });
                Assert.AreEqual(HttpStatusCode.OK, getDbListResp.StatusCode, getDbListResp.Content);
                var dbList = DorcApiClient.Deserialize<List<DatabaseApiModel>>(getDbListResp.Content) ?? new List<DatabaseApiModel>();
                Assert.HasCount(1, dbList);
                Assert.AreEqual(dbId.Value, dbList[0].Id);
                Assert.AreEqual(dbName, dbList[0].Name);

                var getDbByIdResp = caller.ExecuteRaw($"RefDataDatabases/{dbId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getDbByIdResp.StatusCode, getDbByIdResp.Content);
                var dbById = DorcApiClient.Deserialize<DatabaseApiModel>(getDbByIdResp.Content);
                Assert.IsNotNull(dbById);
                Assert.AreEqual(dbId.Value, dbById.Id);
                Assert.AreEqual(dbName, dbById.Name);

                var unattachedDbUsersResp = caller.ExecuteRaw("RefDataDatabaseUsers", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = dbId.Value.ToString(),
                    ["envId"] = childEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(
                    HttpStatusCode.BadRequest,
                    unattachedDbUsersResp.StatusCode,
                    "GET /RefDataDatabaseUsers for a database not attached to the environment must return 400 BadRequest.");

                // RelatedResources before environment attachment (#967 / commit ba22d7a8)
                var relatedUnattachedResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = dbId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, relatedUnattachedResp.StatusCode, relatedUnattachedResp.Content);
                var relatedUnattached = DorcApiClient.Deserialize<DatabaseRelatedResourcesApiModel>(relatedUnattachedResp.Content);
                Assert.IsNotNull(relatedUnattached);
                Assert.IsEmpty(relatedUnattached.Environments ?? Array.Empty<EnvironmentShortApiModel>(), "Unattached database must report zero related environments.");

                var relatedBadParamsResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get);
                Assert.AreEqual(HttpStatusCode.BadRequest, relatedBadParamsResp.StatusCode, "GET /RefDataDatabases/RelatedResources without id or name must return 400 BadRequest.");

                var relatedNotFoundResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = "99999999"
                });
                Assert.AreEqual(HttpStatusCode.NotFound, relatedNotFoundResp.StatusCode, "GET /RefDataDatabases/RelatedResources for unknown id must return 404 NotFound.");

                // Put ByPage and GetDatabasServerNameslist
                var dbByPageResp = caller.ExecuteRaw(
                    "RefDataDatabases/ByPage",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "Name", FilterValue = dbName }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, dbByPageResp.StatusCode, dbByPageResp.Content);
                var dbByPage = DorcApiClient.Deserialize<GetDatabaseApiModelListResponseDto>(dbByPageResp.Content);
                Assert.IsNotNull(dbByPage);
                Assert.IsNotNull(dbByPage.Items);
                var dbByPageItems = dbByPage.Items;
                Assert.IsTrue(dbByPageItems.Any(d => d.Id == dbId.Value), "PUT /RefDataDatabases/ByPage must return the newly created database.");

                var dbServerListResp = caller.ExecuteRaw("RefDataDatabases/GetDatabasServerNameslist", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, dbServerListResp.StatusCode, dbServerListResp.Content);
                var dbServerNames = DorcApiClient.Deserialize<List<string>>(dbServerListResp.Content) ?? new List<string>();
                Assert.IsTrue(dbServerNames.Any(s => string.Equals(s, dbServerName, StringComparison.OrdinalIgnoreCase)),
                    "GET /RefDataDatabases/GetDatabasServerNameslist must include the created database server name.");

                var attachDbResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = dbId.Value.ToString(),
                    ["action"] = "attach",
                    ["component"] = "database"
                });
                Assert.AreEqual(HttpStatusCode.OK, attachDbResp.StatusCode, attachDbResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(attachDbResp.Content)?.Result ?? false);

                // RelatedResources after environment attachment (by id and by name+server)
                var relatedAttachedByIdResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = dbId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, relatedAttachedByIdResp.StatusCode, relatedAttachedByIdResp.Content);
                var relatedAttachedById = DorcApiClient.Deserialize<DatabaseRelatedResourcesApiModel>(relatedAttachedByIdResp.Content);
                Assert.IsNotNull(relatedAttachedById);
                Assert.IsNotNull(relatedAttachedById.Environments);
                Assert.IsTrue(relatedAttachedById.Environments.Any(e => string.Equals(e.EnvironmentName, LiveChildEnvName, StringComparison.OrdinalIgnoreCase)));

                var relatedAttachedByNameResp = caller.ExecuteRaw("RefDataDatabases/RelatedResources", Method.Get, new Dictionary<string, string>
                {
                    ["name"] = dbName,
                    ["server"] = dbServerName
                });
                Assert.AreEqual(HttpStatusCode.OK, relatedAttachedByNameResp.StatusCode, relatedAttachedByNameResp.Content);
                var relatedAttachedByName = DorcApiClient.Deserialize<DatabaseRelatedResourcesApiModel>(relatedAttachedByNameResp.Content);
                Assert.IsNotNull(relatedAttachedByName);
                Assert.IsNotNull(relatedAttachedByName.Environments);
                Assert.IsTrue(relatedAttachedByName.Environments.Any(e => string.Equals(e.EnvironmentName, LiveChildEnvName, StringComparison.OrdinalIgnoreCase)));

                // RefDataDatabases/ByTag: valid tag, missing tag, invalid semicolon tag, and duplicate/ambiguous tag collision (commits 4ff050e2 & 0f432a71)
                var dbByTagOkResp = caller.ExecuteRaw("RefDataDatabases/ByTag", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["tag"] = "DevOpsDbTag"
                });
                Assert.AreEqual(HttpStatusCode.OK, dbByTagOkResp.StatusCode, dbByTagOkResp.Content);
                var dbByTagModel = DorcApiClient.Deserialize<DatabaseApiModel>(dbByTagOkResp.Content);
                Assert.IsNotNull(dbByTagModel);
                Assert.AreEqual(dbId.Value, dbByTagModel.Id);

                var dbByTagNotFoundResp = caller.ExecuteRaw("RefDataDatabases/ByTag", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["tag"] = "NonExistentTag_XYZ"
                });
                Assert.AreEqual(HttpStatusCode.NotFound, dbByTagNotFoundResp.StatusCode);

                var dbByTagInvalidResp = caller.ExecuteRaw("RefDataDatabases/ByTag", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["tag"] = "Invalid;MultiTag"
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, dbByTagInvalidResp.StatusCode);

                // Attach a second DB with the same tag "DevOpsDbTag" to verify deterministic 400 BadRequest on ambiguous tag match
                var createSecondDbResp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = $"{dbName}_2",
                    ServerName = dbServerName,
                    DatabaseType = DatabaseType.SqlServer,
                    AdGroup = "OTHER",
                    Tags = new[] { "DevOpsDbTag" }
                });
                Assert.AreEqual(HttpStatusCode.OK, createSecondDbResp.StatusCode, createSecondDbResp.Content);
                var createdSecondDb = DorcApiClient.Deserialize<DatabaseApiModel>(createSecondDbResp.Content);
                Assert.IsNotNull(createdSecondDb);
                secondDbId = createdSecondDb.Id;

                var attachSecondDbResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = secondDbId.Value.ToString(),
                    ["action"] = "attach",
                    ["component"] = "database"
                });
                Assert.AreEqual(HttpStatusCode.OK, attachSecondDbResp.StatusCode, attachSecondDbResp.Content);

                var dbByTagAmbiguousResp = caller.ExecuteRaw("RefDataDatabases/ByTag", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["tag"] = "DevOpsDbTag"
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, dbByTagAmbiguousResp.StatusCode,
                    $"Expected 400 BadRequest when multiple databases in '{LiveChildEnvName}' share tag 'DevOpsDbTag', got {(int)dbByTagAmbiguousResp.StatusCode}: {dbByTagAmbiguousResp.Content}");

                // Detach and delete the second DB immediately after verifying ambiguous tag rejection
                caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = secondDbId.Value.ToString(),
                    ["action"] = "detach",
                    ["component"] = "database"
                });
                caller.ExecuteRaw("RefDataDatabases", Method.Delete, new Dictionary<string, string>
                {
                    ["databaseId"] = secondDbId.Value.ToString()
                });
                secondDbId = null;

                // RefDataEnvironmentsHistory, RefDataSqlPorts, and ResetAppPassword/ForUser (Postman endpoints)
                var envHistResp = caller.ExecuteRaw("RefDataEnvironmentsHistory", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = childEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, envHistResp.StatusCode, envHistResp.Content);
                var envHistory = DorcApiClient.Deserialize<List<EnvironmentHistoryApiModel>>(envHistResp.Content) ?? new List<EnvironmentHistoryApiModel>();
                Assert.IsNotEmpty(envHistory, "Expected environment history entries after server/database attach operations.");
                var latestHistory = envHistory[0];
                var putHistResp = caller.ExecuteRaw("RefDataEnvironmentsHistory", Method.Put, body: new EnvironmentHistoryApiModel
                {
                    Id = latestHistory.Id,
                    EnvName = LiveChildEnvName,
                    Comment = $"Live regression comment {suffix}"
                });
                Assert.AreEqual(HttpStatusCode.OK, putHistResp.StatusCode, putHistResp.Content);

                var sqlPortsResp = caller.ExecuteRaw("RefDataSqlPorts", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, sqlPortsResp.StatusCode, sqlPortsResp.Content);

                var sqlPortNotFoundResp = caller.ExecuteRaw("RefDataSqlPorts/ByInstance", Method.Get, new Dictionary<string, string>
                {
                    ["instanceName"] = "NotExistingSqlInstance_XYZ"
                });
                Assert.AreEqual(HttpStatusCode.NotFound, sqlPortNotFoundResp.StatusCode);

                var resetPwdResp = caller.ExecuteRaw("ResetAppPassword/ForUser", Method.Put, new Dictionary<string, string>
                {
                    ["envFilter"] = "Endur",
                    ["envName"] = LiveChildEnvName,
                    ["username"] = "SVC-LIVE-TFSTEST"
                });
                Assert.AreEqual(HttpStatusCode.OK, resetPwdResp.StatusCode, resetPwdResp.Content);
                var resetPwdResult = DorcApiClient.Deserialize<ApiBoolResult>(resetPwdResp.Content);
                Assert.IsNotNull(resetPwdResult);
                Assert.IsFalse(resetPwdResult.Result, "Expected ResetAppPassword/ForUser to return Result=false when no matching tag database is attached.");
                StringAssert.Contains(resetPwdResult.Message ?? string.Empty, "No application database found");

                var envDbsResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = childEnv.EnvironmentId.ToString(),
                    ["type"] = "0"
                });
                Assert.AreEqual(HttpStatusCode.OK, envDbsResp.StatusCode, envDbsResp.Content);
                var envDbsDto = DorcApiClient.Deserialize<EnvironmentComponentsDto<DatabaseApiModel>>(envDbsResp.Content);
                Assert.IsNotNull(envDbsDto);
                Assert.IsNotNull(envDbsDto.Result);
                Assert.IsTrue(envDbsDto.Result.Any(d => d.Id == dbId.Value && string.Equals(d.Name, dbName, StringComparison.OrdinalIgnoreCase)));

                var envsForDbResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/GetEnvironmentsForDatabase", Method.Get, new Dictionary<string, string>
                {
                    ["serverName"] = dbServerName,
                    ["databaseName"] = dbName
                });
                Assert.AreEqual(HttpStatusCode.OK, envsForDbResp.StatusCode, envsForDbResp.Content);
                StringAssert.Contains(envsForDbResp.Content ?? string.Empty, LiveChildEnvName);

                var attachedDbUsersResp = caller.ExecuteRaw("RefDataDatabaseUsers", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = dbId.Value.ToString(),
                    ["envId"] = childEnv.EnvironmentId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, attachedDbUsersResp.StatusCode, attachedDbUsersResp.Content);

                var dbPermsOkResp = caller.ExecuteRaw("RefDataDatabaseUsers/GetDbUsersPermissions", Method.Get, new Dictionary<string, string>
                {
                    ["serverName"] = dbServerName,
                    ["databaseName"] = dbName
                });
                Assert.AreEqual(HttpStatusCode.OK, dbPermsOkResp.StatusCode, dbPermsOkResp.Content);

                var dbPermsBadTagResp = caller.ExecuteRaw("RefDataDatabaseUsers/GetDbUsersPermissions", Method.Get, new Dictionary<string, string>
                {
                    ["serverName"] = dbServerName,
                    ["databaseName"] = dbName,
                    ["tag"] = "Invalid;MultiTag"
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, dbPermsBadTagResp.StatusCode);

                var dbAuditResp = caller.ExecuteRaw(
                    "DatabaseAudit",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, dbAuditResp.StatusCode, dbAuditResp.Content);
                StringAssert.Contains(dbAuditResp.Content ?? string.Empty, dbName);

                var detachDbResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = dbId.Value.ToString(),
                    ["action"] = "detach",
                    ["component"] = "database"
                });
                Assert.AreEqual(HttpStatusCode.OK, detachDbResp.StatusCode, detachDbResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(detachDbResp.Content)?.Result ?? false);

                var deleteDbResp = caller.ExecuteRaw("RefDataDatabases", Method.Delete, new Dictionary<string, string>
                {
                    ["databaseId"] = dbId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, deleteDbResp.StatusCode, deleteDbResp.Content);
                Assert.IsTrue(DorcApiClient.Deserialize<ApiBoolResult>(deleteDbResp.Content)?.Result ?? false);

                var getDeletedDbById = caller.ExecuteRaw($"RefDataDatabases/{dbId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.NoContent, getDeletedDbById.StatusCode, "GET /RefDataDatabases/{id} after deletion must return 204 NoContent.");
                dbId = null;
            }
            finally
            {
                if (serverId.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = serverId.Value.ToString(),
                        ["action"] = "detach",
                        ["component"] = "server"
                    });
                    caller.ExecuteRaw("RefDataServers", Method.Delete, new Dictionary<string, string>
                    {
                        ["serverId"] = serverId.Value.ToString()
                    });
                }

                if (secondDbId.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = secondDbId.Value.ToString(),
                        ["action"] = "detach",
                        ["component"] = "database"
                    });
                    caller.ExecuteRaw("RefDataDatabases", Method.Delete, new Dictionary<string, string>
                    {
                        ["databaseId"] = secondDbId.Value.ToString()
                    });
                }

                if (dbId.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = dbId.Value.ToString(),
                        ["action"] = "detach",
                        ["component"] = "database"
                    });
                    caller.ExecuteRaw("RefDataDatabases", Method.Delete, new Dictionary<string, string>
                    {
                        ["databaseId"] = dbId.Value.ToString()
                    });
                }
            }
        }

        [TestMethod]
        public void ManageEnvironmentResourcesAndSqlPorts_ApiRegistrationsCloudResourcesContainersAuditsAppServersAndSqlPorts_PersistsAndCleansUp()
        {
            AssertLiveEnvironmentEnabled();

            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var apiRegName = $"DOrc_Live_ApiReg_{suffix}";
            var cloudResName = $"DOrc_Live_CloudRes_{suffix}";
            var containerName = $"DOrc_Live_Container_{suffix}";
            var sqlInstanceName = $"DORCSQLPORT_{suffix}\\INST01";
            var sqlPortVal = "14433";

            int? apiRegId = null;
            int? cloudResId = null;
            int? containerId = null;
            bool createdSqlPort = false;

            try
            {
                // 1. RefDataApiRegistrations + ApiRegistrationAudit full lifecycle
                var postApiRegResp = caller.ExecuteRaw("RefDataApiRegistrations", Method.Post, body: new ApiRegistrationApiModel
                {
                    Name = apiRegName,
                    BaseUrl = $"https://api-{suffix}.internal.local",
                    Version = "1.0.0",
                    HealthCheckUrl = $"https://api-{suffix}.internal.local/health",
                    Tags = "Regression;ApiTier"
                });
                Assert.AreEqual(HttpStatusCode.OK, postApiRegResp.StatusCode, $"POST /RefDataApiRegistrations failed: {postApiRegResp.Content}");
                var createdApiReg = DorcApiClient.Deserialize<ApiRegistrationApiModel>(postApiRegResp.Content);
                Assert.IsNotNull(createdApiReg);
                Assert.IsGreaterThan(0, createdApiReg.Id);
                apiRegId = createdApiReg.Id;

                var getApiRegsResp = caller.ExecuteRaw("RefDataApiRegistrations", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getApiRegsResp.StatusCode, getApiRegsResp.Content);

                var getApiRegByIdResp = caller.ExecuteRaw($"RefDataApiRegistrations/ById/{apiRegId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getApiRegByIdResp.StatusCode, getApiRegByIdResp.Content);
                var fetchedApiReg = DorcApiClient.Deserialize<ApiRegistrationApiModel>(getApiRegByIdResp.Content);
                Assert.IsNotNull(fetchedApiReg);

                fetchedApiReg.Version = "1.1.0";
                var putApiRegResp = caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}", Method.Put, body: fetchedApiReg);
                Assert.AreEqual(HttpStatusCode.OK, putApiRegResp.StatusCode, $"PUT /RefDataApiRegistrations/{apiRegId.Value} failed: {putApiRegResp.Content}");
                var updatedApiReg = DorcApiClient.Deserialize<ApiRegistrationApiModel>(putApiRegResp.Content);
                Assert.IsNotNull(updatedApiReg);
                Assert.AreEqual("1.1.0", updatedApiReg.Version);

                var putApiRegTagsResp = caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}/tags", Method.Put, body: new
                {
                    ExpectedTags = updatedApiReg.Tags,
                    Tags = "Regression;ApiTier;UpdatedTag"
                });
                Assert.AreEqual(HttpStatusCode.OK, putApiRegTagsResp.StatusCode, $"PUT /RefDataApiRegistrations/{apiRegId.Value}/tags failed: {putApiRegTagsResp.Content}");

                var attachApiRegResp = caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}/environments/{childEnv.EnvironmentId}", Method.Put);
                Assert.AreEqual(HttpStatusCode.OK, attachApiRegResp.StatusCode, $"Attach ApiRegistration failed: {attachApiRegResp.Content}");

                var getApiRegByEnvResp = caller.ExecuteRaw($"RefDataApiRegistrations/ByEnvId/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getApiRegByEnvResp.StatusCode, getApiRegByEnvResp.Content);
                StringAssert.Contains(getApiRegByEnvResp.Content ?? string.Empty, apiRegName);

                var getApiRegCapResp = caller.ExecuteRaw($"RefDataApiRegistrations/Capabilities/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getApiRegCapResp.StatusCode, getApiRegCapResp.Content);

                var apiRegAuditResp = caller.ExecuteRaw(
                    "ApiRegistrationAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["apiRegistrationId"] = apiRegId.Value.ToString(),
                        ["page"] = "1",
                        ["limit"] = "20"
                    },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, apiRegAuditResp.StatusCode, $"PUT /ApiRegistrationAudit failed: {apiRegAuditResp.Content}");
                StringAssert.Contains(apiRegAuditResp.Content ?? string.Empty, apiRegName);

                var detachApiRegResp = caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, detachApiRegResp.StatusCode, $"Detach ApiRegistration failed: {detachApiRegResp.Content}");

                var delApiRegResp = caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, delApiRegResp.StatusCode, $"DELETE /RefDataApiRegistrations/{apiRegId.Value} failed: {delApiRegResp.Content}");
                apiRegId = null;

                // 2. RefDataCloudResources + CloudResourceAudit full lifecycle
                var postCloudResResp = caller.ExecuteRaw("RefDataCloudResources", Method.Post, body: new CloudResourceApiModel
                {
                    Name = cloudResName,
                    Provider = "Azure",
                    ResourceType = "Microsoft.ServiceBus/namespaces",
                    ResourceIdentifier = $"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-dorc/providers/Microsoft.ServiceBus/namespaces/{cloudResName}",
                    Subscription = "sub-dorc-nonprod",
                    Tags = "Regression;CloudTier"
                });
                Assert.AreEqual(HttpStatusCode.OK, postCloudResResp.StatusCode, $"POST /RefDataCloudResources failed: {postCloudResResp.Content}");
                var createdCloudRes = DorcApiClient.Deserialize<CloudResourceApiModel>(postCloudResResp.Content);
                Assert.IsNotNull(createdCloudRes);
                Assert.IsGreaterThan(0, createdCloudRes.Id);
                cloudResId = createdCloudRes.Id;

                var getCloudResByIdResp = caller.ExecuteRaw($"RefDataCloudResources/ById/{cloudResId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getCloudResByIdResp.StatusCode, getCloudResByIdResp.Content);
                var fetchedCloudRes = DorcApiClient.Deserialize<CloudResourceApiModel>(getCloudResByIdResp.Content);
                Assert.IsNotNull(fetchedCloudRes);

                fetchedCloudRes.Subscription = "sub-dorc-updated";
                var putCloudResResp = caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}", Method.Put, body: fetchedCloudRes);
                Assert.AreEqual(HttpStatusCode.OK, putCloudResResp.StatusCode, $"PUT /RefDataCloudResources/{cloudResId.Value} failed: {putCloudResResp.Content}");

                var attachCloudResResp = caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}/environments/{childEnv.EnvironmentId}", Method.Put);
                Assert.AreEqual(HttpStatusCode.OK, attachCloudResResp.StatusCode, $"Attach CloudResource failed: {attachCloudResResp.Content}");

                var getCloudResByEnvResp = caller.ExecuteRaw($"RefDataCloudResources/ByEnvId/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getCloudResByEnvResp.StatusCode, getCloudResByEnvResp.Content);
                StringAssert.Contains(getCloudResByEnvResp.Content ?? string.Empty, cloudResName);

                var cloudResAuditResp = caller.ExecuteRaw(
                    "CloudResourceAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["cloudResourceId"] = cloudResId.Value.ToString(),
                        ["page"] = "1",
                        ["limit"] = "20"
                    },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, cloudResAuditResp.StatusCode, $"PUT /CloudResourceAudit failed: {cloudResAuditResp.Content}");
                StringAssert.Contains(cloudResAuditResp.Content ?? string.Empty, cloudResName);

                var detachCloudResResp = caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, detachCloudResResp.StatusCode, $"Detach CloudResource failed: {detachCloudResResp.Content}");

                var delCloudResResp = caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, delCloudResResp.StatusCode, $"DELETE /RefDataCloudResources/{cloudResId.Value} failed: {delCloudResResp.Content}");
                cloudResId = null;

                // 3. RefDataContainers + ContainerAudit full lifecycle
                var postContainerResp = caller.ExecuteRaw("RefDataContainers", Method.Post, body: new ContainerApiModel
                {
                    Name = containerName,
                    Image = $"proget.internal.local/dorc/{containerName}:1.0.0",
                    Registry = "proget.internal.local",
                    HostServerName = "DEPAPP01DV",
                    Tags = "Regression;ContainerTier"
                });
                Assert.AreEqual(HttpStatusCode.OK, postContainerResp.StatusCode, $"POST /RefDataContainers failed: {postContainerResp.Content}");
                var createdContainer = DorcApiClient.Deserialize<ContainerApiModel>(postContainerResp.Content);
                Assert.IsNotNull(createdContainer);
                Assert.IsGreaterThan(0, createdContainer.Id);
                containerId = createdContainer.Id;

                var getContainerByIdResp = caller.ExecuteRaw($"RefDataContainers/ById/{containerId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getContainerByIdResp.StatusCode, getContainerByIdResp.Content);
                var fetchedContainer = DorcApiClient.Deserialize<ContainerApiModel>(getContainerByIdResp.Content);
                Assert.IsNotNull(fetchedContainer);

                fetchedContainer.Image = $"proget.internal.local/dorc/{containerName}:1.1.0";
                var putContainerResp = caller.ExecuteRaw($"RefDataContainers/{containerId.Value}", Method.Put, body: fetchedContainer);
                Assert.AreEqual(HttpStatusCode.OK, putContainerResp.StatusCode, $"PUT /RefDataContainers/{containerId.Value} failed: {putContainerResp.Content}");

                var attachContainerResp = caller.ExecuteRaw($"RefDataContainers/{containerId.Value}/environments/{childEnv.EnvironmentId}", Method.Put);
                Assert.AreEqual(HttpStatusCode.OK, attachContainerResp.StatusCode, $"Attach Container failed: {attachContainerResp.Content}");

                var getContainerByEnvResp = caller.ExecuteRaw($"RefDataContainers/ByEnvId/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getContainerByEnvResp.StatusCode, getContainerByEnvResp.Content);
                StringAssert.Contains(getContainerByEnvResp.Content ?? string.Empty, containerName);

                var containerAuditResp = caller.ExecuteRaw(
                    "ContainerAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["containerId"] = containerId.Value.ToString(),
                        ["page"] = "1",
                        ["limit"] = "20"
                    },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, containerAuditResp.StatusCode, $"PUT /ContainerAudit failed: {containerAuditResp.Content}");
                StringAssert.Contains(containerAuditResp.Content ?? string.Empty, containerName);

                var detachContainerResp = caller.ExecuteRaw($"RefDataContainers/{containerId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, detachContainerResp.StatusCode, $"Detach Container failed: {detachContainerResp.Content}");

                var delContainerResp = caller.ExecuteRaw($"RefDataContainers/{containerId.Value}", Method.Delete);
                Assert.AreEqual(HttpStatusCode.OK, delContainerResp.StatusCode, $"DELETE /RefDataContainers/{containerId.Value} failed: {delContainerResp.Content}");
                containerId = null;

                // 4. RefDataAppServers (GET /RefDataAppServers/{envId} & PUT /RefDataAppServers with safe empty server no-op)
                var getAppSrvsResp = caller.ExecuteRaw($"RefDataAppServers/{childEnv.EnvironmentId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getAppSrvsResp.StatusCode, $"GET /RefDataAppServers/{childEnv.EnvironmentId} failed: {getAppSrvsResp.Content}");

                var putAppSrvNoOpResp = caller.ExecuteRaw("RefDataAppServers", Method.Put, new Dictionary<string, string>
                {
                    ["server"] = string.Empty
                });
                Assert.IsTrue(
                    putAppSrvNoOpResp.StatusCode == HttpStatusCode.OK || putAppSrvNoOpResp.StatusCode == HttpStatusCode.NoContent,
                    $"PUT /RefDataAppServers (empty no-op) failed with {putAppSrvNoOpResp.StatusCode}: {putAppSrvNoOpResp.Content}");

                // 5. RefDataSqlPorts full CRUD lifecycle (POST, GET /ByInstance, DELETE)
                var postSqlPortResp = caller.ExecuteRaw("RefDataSqlPorts", Method.Post, body: new SqlPortApiModel
                {
                    InstanceName = sqlInstanceName,
                    SqlPort = sqlPortVal
                });
                Assert.AreEqual(HttpStatusCode.OK, postSqlPortResp.StatusCode, $"POST /RefDataSqlPorts failed: {postSqlPortResp.Content}");
                createdSqlPort = true;

                var getSqlPortByInstResp = caller.ExecuteRaw("RefDataSqlPorts/ByInstance", Method.Get, new Dictionary<string, string>
                {
                    ["instanceName"] = sqlInstanceName
                });
                Assert.AreEqual(HttpStatusCode.OK, getSqlPortByInstResp.StatusCode, $"GET /RefDataSqlPorts/ByInstance failed: {getSqlPortByInstResp.Content}");
                Assert.AreEqual(sqlPortVal, (getSqlPortByInstResp.Content ?? string.Empty).Trim('"'));

                var delSqlPortResp = caller.ExecuteRaw("RefDataSqlPorts", Method.Delete, body: new SqlPortApiModel
                {
                    InstanceName = sqlInstanceName,
                    SqlPort = sqlPortVal
                });
                Assert.AreEqual(HttpStatusCode.OK, delSqlPortResp.StatusCode, $"DELETE /RefDataSqlPorts failed: {delSqlPortResp.Content}");
                createdSqlPort = false;

                var getDeletedSqlPortResp = caller.ExecuteRaw("RefDataSqlPorts/ByInstance", Method.Get, new Dictionary<string, string>
                {
                    ["instanceName"] = sqlInstanceName
                });
                Assert.AreEqual(HttpStatusCode.NotFound, getDeletedSqlPortResp.StatusCode, "GET /RefDataSqlPorts/ByInstance after DELETE must return 404 NotFound.");
            }
            finally
            {
                if (apiRegId.HasValue)
                {
                    caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                    caller.ExecuteRaw($"RefDataApiRegistrations/{apiRegId.Value}", Method.Delete);
                }
                if (cloudResId.HasValue)
                {
                    caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                    caller.ExecuteRaw($"RefDataCloudResources/{cloudResId.Value}", Method.Delete);
                }
                if (containerId.HasValue)
                {
                    caller.ExecuteRaw($"RefDataContainers/{containerId.Value}/environments/{childEnv.EnvironmentId}", Method.Delete);
                    caller.ExecuteRaw($"RefDataContainers/{containerId.Value}", Method.Delete);
                }
                if (createdSqlPort)
                {
                    caller.ExecuteRaw("RefDataSqlPorts", Method.Delete, body: new SqlPortApiModel
                    {
                        InstanceName = sqlInstanceName,
                        SqlPort = sqlPortVal
                    });
                }
            }
        }
    }
}
