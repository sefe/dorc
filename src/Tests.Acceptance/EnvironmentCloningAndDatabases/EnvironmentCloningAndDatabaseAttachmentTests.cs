using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.EnvironmentCloningAndDatabases
{
    [TestClass]
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
                db2Id = createdDb2?.Id;

                var attachDb2Resp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = db2Id!.Value.ToString(),
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
                    ["componentId"] = db2Id.Value.ToString(),
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
    }
}
