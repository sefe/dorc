using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Core.Tests.DatabaseAccess;

[TestClass]
public class DatabaseAccessStorePersistenceTests
{
    [TestMethod]
    public void Store_PersistsSharedCatalogAcrossDatabasesEnforcesLocksAndConcurrencyAndAnswersReaderQueries()
    {
        using var harness = LocalAccessDbHarness.Create([
            new Database { Id = 1, Name = "Db1", ServerName = "sql01", DatabaseType = DatabaseType.SqlServer },
            new Database { Id = 2, Name = "Db2", ServerName = "sql01", DatabaseType = DatabaseType.SqlServer }
        ]);

        // Empty collection guards on reader methods
        Assert.HasCount(0, harness.Store.GetNativePrincipalsForDatabases([]));
        Assert.IsNull(harness.Store.FindDatabaseWithNativePrincipal([], "app"));
        Assert.IsNull(harness.Store.FindDatabaseWithNativePrincipal([1], "  "));

        // Exclusive session sp_getapplock contention in Open(databaseId)
        using (var held = harness.Store.Open(1))
        {
            Assert.ThrowsExactly<SqlException>(() => harness.Store.Open(1));

            // Save initial state on DB 1 (Native + DirectoryUser)
            var saved1 = DatabaseAccessStore.Save(held, new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 0,
                Provider = "sql-server",
                Principals =
                [
                    new() { Name = "app_alias", Kind = "Native", LoginName = "svc_app", Present = true, LegacyUserId = 5 },
                    new() { Name = "DOMAIN\\ops", Kind = "DirectoryGroup", DirectoryId = "S-1-5-21-9", Present = true },
                    new() { Name = "temp_login", Kind = "Native", LoginName = "temp_login", Present = true }
                ],
                Roles =
                [
                    new() { Name = "db_datareader", Managed = false, Present = true },
                    new() { Name = "temp_role", Managed = true, Present = true }
                ],
                Memberships =
                [
                    new() { Principal = "app_alias", Role = "db_datareader", Present = true },
                    new() { Principal = "DOMAIN\\ops", Role = "db_datareader", Present = true }
                ]
            }, "alice", "Save");
            Assert.AreEqual(1, saved1.Revision);

            // Stale revision throws DbUpdateConcurrencyException
            Assert.ThrowsExactly<DbUpdateConcurrencyException>(() => DatabaseAccessStore.Save(held, new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 0,
                Provider = "sql-server"
            }, "alice", "Save"));

            // Second save on DB 1 updates existing bindings and removes dropped bindings (temp_login, temp_role) with explicit auditDetail
            var saved1V2 = DatabaseAccessStore.Save(held, new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 1,
                Provider = "sql-server",
                Principals =
                [
                    new() { Name = "app_alias", Kind = "Native", LoginName = "svc_app", Present = true, LegacyUserId = 5 },
                    new() { Name = "DOMAIN\\ops", Kind = "DirectoryGroup", DirectoryId = "S-1-5-21-9", Present = true }
                ],
                Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
                Memberships =
                [
                    new() { Principal = "app_alias", Role = "db_datareader", Present = true },
                    new() { Principal = "DOMAIN\\ops", Role = "db_datareader", Present = true }
                ]
            }, "alice", "Save", "custom-detail");
            Assert.AreEqual(2, saved1V2.Revision);
            Assert.HasCount(2, saved1V2.Principals);
            Assert.HasCount(1, saved1V2.Roles);
        }

        // Save overlapping principal/role on DB 2 -> reuses PrincipalCatalog & RoleCatalog rows!
        using (var held2 = harness.Store.Open(2))
        {
            DatabaseAccessStore.Save(held2, new DatabaseAccessState
            {
                DatabaseId = 2,
                Revision = 0,
                Provider = "sql-server",
                Principals = [new() { Name = "app_alias", Kind = "Native", LoginName = "svc_app", Present = true }],
                Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
                Memberships = [new() { Principal = "app_alias", Role = "db_datareader", Present = true }]
            }, "bob", "Save");
        }

        using (var writableCatalog = harness.Store.OpenWritable())
        {
            var directPrincipal = new DatabaseAccessPrincipalCatalog
            {
                Name = "direct_principal",
                Kind = "Native",
                LoginName = "direct_login",
                DirectoryId = null
            };
            var directRole = new DatabaseAccessRoleCatalog
            {
                Name = "direct_role"
            };
            writableCatalog.AddRange(directPrincipal, directRole);
            writableCatalog.SaveChanges();
            Assert.IsGreaterThan(0, directPrincipal.Id);
            Assert.IsGreaterThan(0, directRole.Id);
        }

        using (var verify = harness.Store.OpenReadOnly())
        {
            var principalRow = verify.Set<DatabaseAccessPrincipalCatalog>().Single(p => p.Name == "app_alias");
            Assert.IsGreaterThan(0, principalRow.Id);
            Assert.AreEqual("Native", principalRow.Kind);
            Assert.AreEqual("svc_app", principalRow.LoginName);
            Assert.IsNull(principalRow.DirectoryId);

            var roleRow = verify.Set<DatabaseAccessRoleCatalog>().Single(r => r.Name == "db_datareader");
            Assert.IsGreaterThan(0, roleRow.Id);
            Assert.AreEqual("db_datareader", roleRow.Name);
        }

        // Verify IDatabaseAccessReader queries
        var assignments = harness.Store.GetRoleAssignments(1);
        Assert.HasCount(2, assignments);
        Assert.IsTrue(assignments.Any(a => a.Principal == "svc_app" && a.Role == "db_datareader"));
        Assert.IsTrue(assignments.Any(a => a.Principal == "DOMAIN\\ops" && a.Role == "db_datareader"));

        var nativeUsers = harness.Store.GetNativePrincipalsForDatabases([1, 2]);
        Assert.HasCount(1, nativeUsers);
        Assert.AreEqual("svc_app", nativeUsers[0].LoginId);
        Assert.AreEqual("app_alias", nativeUsers[0].DisplayName);

        Assert.AreEqual(1, harness.Store.FindDatabaseWithNativePrincipal([1, 2], "SVC_APP"));
        Assert.AreEqual(1, harness.Store.FindDatabaseWithNativePrincipal([1, 2], "APP_ALIAS"));
        Assert.IsNull(harness.Store.FindDatabaseWithNativePrincipal([1, 2], "nobody"));
    }
}
