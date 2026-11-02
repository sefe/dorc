using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Dorc.Core.Interfaces;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace Dorc.Core.Tests;

[TestClass]
public class DatabaseAccessSqlTests
{
    [TestMethod]
    [TestCategory("LocalSql")]
    public void IsolatedSqlServerReconciliationAndSchemaConstraints()
    {
        var server = Environment.GetEnvironmentVariable("DORC_ACCESS_TEST_SERVER");
        if (server != @"(localdb)\DOrcAccessTests")
            Assert.Inconclusive("Set DORC_ACCESS_TEST_SERVER=(localdb)\\DOrcAccessTests to use an isolated test instance.");
        var database = "DorcAccess_" + Guid.NewGuid().ToString("N");
        var login = "DorcAccessLogin_" + Guid.NewGuid().ToString("N");
        using var master = new SqlConnection(new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = "master", IntegratedSecurity = true, Encrypt = false, Pooling = false
        }.ConnectionString);
        master.Open();
        Execute(master, $"CREATE DATABASE [{database}];");
        try
        {
            Execute(master, $"CREATE LOGIN [{login}] WITH PASSWORD = 'Aa1!{Guid.NewGuid():N}';");
            Execute(master, $"CREATE LOGIN [{login}_off] WITH PASSWORD = 'Aa1!{Guid.NewGuid():N}'; ALTER LOGIN [{login}_off] DISABLE;");
            using var connection = new SqlConnection(new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = database }.ConnectionString);
            connection.Open();
            Execute(connection, "CREATE SCHEMA [deploy];");
            Execute(connection, "CREATE TABLE [dbo].[DATABASE] ([DB_ID] INT PRIMARY KEY); INSERT [dbo].[DATABASE] VALUES (1);");
            foreach (var name in SchemaTables)
                foreach (var batch in Batches(File.ReadAllText(SchemaFile(name))))
                    Execute(connection, batch);
            Execute(connection, "INSERT deploy.DatabaseAccessConfiguration VALUES (1,'sql-server',0);");
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessConfiguration VALUES (1,'sql-server',0);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessConfiguration VALUES (999,'sql-server',0);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessDatabaseMembership VALUES (1,1,1,1);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessSharedPrincipal VALUES ('bad','DirectoryUser','cached-login',NULL);"));
            Execute(connection, "INSERT [dbo].[DATABASE] VALUES (2); INSERT deploy.DatabaseAccessConfiguration VALUES (2,'sql-server',0);");
            Execute(connection, """
                INSERT deploy.DatabaseAccessSharedPrincipal VALUES ('one','Native','login',NULL);
                INSERT deploy.DatabaseAccessSharedRole VALUES ('two');
                INSERT deploy.DatabaseAccessDatabasePrincipal SELECT 1, Id, 1, NULL FROM deploy.DatabaseAccessSharedPrincipal WHERE Name='one';
                INSERT deploy.DatabaseAccessDatabaseRole SELECT 2, Id, 0, 1 FROM deploy.DatabaseAccessSharedRole WHERE Name='two';
                """);
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, """
                INSERT deploy.DatabaseAccessDatabaseMembership
                SELECT 1, p.Id, r.Id, 1 FROM deploy.DatabaseAccessSharedPrincipal p, deploy.DatabaseAccessSharedRole r WHERE p.Name='one' AND r.Name='two';
                """));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessSharedPrincipal VALUES ('ONE','Native','LOGIN',NULL);"),
                "A shared identity must be stored once.");
            Execute(connection, "DELETE deploy.DatabaseAccessDatabasePrincipal; DELETE deploy.DatabaseAccessDatabaseRole; DELETE deploy.DatabaseAccessSharedPrincipal; DELETE deploy.DatabaseAccessSharedRole;");

            var provider = new SqlServerDatabaseAccessProvider();
            var target = new DatabaseAccessTarget(1, server!, database);
            var logins = provider.SqlLogins(target);
            CollectionAssert.Contains(logins, login);
            CollectionAssert.DoesNotContain(logins, login + "_off", "Disabled logins cannot be granted access.");
            CollectionAssert.DoesNotContain(logins, "sa", "Protected logins are not offered.");
            var state = new DatabaseAccessState
            {
                DatabaseId = 1,
                Principals = [new() { Name = "app]; --", LoginName = login }],
                Roles = [new() { Name = "reader]; --", Managed = true }],
                Memberships = [new() { Principal = "app]; --", Role = "reader]; --" }]
            };
            using (var access = new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>().UseSqlServer(connection.ConnectionString).Options))
            {
                var saved = DatabaseAccessStore.Save(access, state, "test", "Save");
                Assert.AreEqual(1L, saved.Revision);
                Assert.AreEqual(state.Principals.Single().Name, saved.Principals.Single().Name);
                Assert.ThrowsExactly<DbUpdateConcurrencyException>(() => DatabaseAccessStore.Save(access, state, "test", "Save"));
                var factory = Substitute.For<IDeploymentContextFactory>();
                var legacy = Substitute.For<IDeploymentContext>();
                legacy.Database.Returns(access.Database);
                factory.GetContext().Returns(legacy);
                var store = new DatabaseAccessStore(factory);
                using (var firstLock = store.Open(1))
                    Assert.ThrowsExactly<SqlException>(() => store.Open(1));
                using var nextLock = store.Open(1);
                Assert.AreEqual(1L, DatabaseAccessStore.Read(nextLock, 1).Revision);

                var databases = Set(new[] { new Dorc.PersistentData.Model.Database { Id = 2, Name = database, ServerName = server } });
                var maps = Set(new[] {
                    new Dorc.PersistentData.Model.EnvironmentUser { DbId = 2, UserId = 42, PermissionId = 7 },
                    new Dorc.PersistentData.Model.EnvironmentUser { DbId = 2, UserId = 42, PermissionId = 8 },
                    new Dorc.PersistentData.Model.EnvironmentUser { DbId = 2, UserId = 43, PermissionId = 7 } });
                var users = Set(new[] {
                    new Dorc.PersistentData.Model.User { Id = 42, LoginId = login, LoginType = "Endur" },
                    new Dorc.PersistentData.Model.User { Id = 43, LoginId = "AD\\missing", LoginType = "Windows" } });
                var permissions = Set(new[] {
                    new Dorc.PersistentData.Model.Permission { Id = 7, Name = "Read" },
                    new Dorc.PersistentData.Model.Permission { Id = 8, Name = "db_owner" } });
                legacy.Databases.Returns(databases);
                legacy.EnvironmentUsers.Returns(maps);
                legacy.Users.Returns(users);
                legacy.Permissions.Returns(permissions);
                var unavailableProvider = Substitute.For<IDatabaseAccessProvider>();
                unavailableProvider.Name.Returns(provider.Name);
                var targetUnavailable = true;
                unavailableProvider.Preview(Arg.Any<DatabaseAccessTarget>(), Arg.Any<DatabaseAccessState>())
                    .Returns(_ => targetUnavailable
                        ? throw new InvalidOperationException("Target is unavailable.")
                        : new DatabaseAccessPreview { Token = "blocked", Errors = ["Login is not visible."] });
                var service = new DatabaseAccessService(store, factory, new DatabaseAccessProviders([unavailableProvider]),
                    new DatabaseAccessDirectory(Substitute.For<IActiveDirectorySearcher>()), NullLogger<DatabaseAccessService>.Instance);
                var import = new DatabaseAccessImportRequest
                {
                    RoleMappings = new() { ["Read"] = "db_datareader", ["db_owner"] = "db_owner" }, ExcludedLegacyUserIds = [43]
                };
                Assert.IsTrue(service.Import(2, import, false, "test").Errors.Any(e => e.Contains("explicit db_owner")));
                import.ApprovedDbOwnerLegacyUserIds.Add(42);
                var importPreview = service.Import(2, import, false, "test");
                Assert.HasCount(0, importPreview.Errors, string.Join("; ", importPreview.Errors));
                Assert.AreEqual(0L, service.Get(2).Revision);
                import.Token = importPreview.Token;
                import.ApprovedDbOwnerLegacyUserIds.Clear();
                Assert.ThrowsExactly<DatabaseAccessConflictException>(() => service.Import(2, import, true, "test"));
                import.ApprovedDbOwnerLegacyUserIds.Add(42);
                service.Import(2, import, true, "test");
                Assert.AreEqual(1L, service.Get(2).Revision);
                Execute(connection, "INSERT [dbo].[DATABASE] VALUES (3);");
                var imported = service.Get(2).Principals.Single();
                DatabaseAccessStore.Save(access, new DatabaseAccessState
                {
                    DatabaseId = 3,
                    Principals = [new() { Name = imported.Name, Kind = imported.Kind, LoginName = imported.LoginName }],
                    Roles = [new() { Name = "db_datareader" }],
                    Memberships = [new() { Principal = imported.Name, Role = "db_datareader" }]
                }, "test", "Save");
                Assert.AreEqual(1, Scalar(connection, $"SELECT COUNT(*) FROM deploy.DatabaseAccessSharedPrincipal WHERE Name = N'{login}'"),
                    "The same database user on two databases must share one principal row.");
                Assert.AreEqual(1, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessSharedRole WHERE Name = N'db_datareader'"));
                var remapped = DatabaseAccessStore.Read(access, 3);
                remapped.Principals[0].LoginName = "other-login";
                remapped = DatabaseAccessStore.Save(access, remapped, "test", "Save");
                Assert.AreEqual("other-login", remapped.Principals.Single().LoginName);
                Assert.AreEqual(imported.Name, remapped.Memberships.Single().Principal, "A remapped principal keeps its role memberships.");
                Assert.AreEqual(imported.LoginName, service.Get(2).Principals.Single().LoginName, "Remapping one database must not affect another.");
                Assert.AreEqual("Native", service.Get(2).Principals.Single().Kind);
                CollectionAssert.AreEqual(new[] { 43 }, service.Get(2).ExcludedLegacyUserIds);
                Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT deploy.DatabaseAccessExclusion VALUES (2,0);"));
                var audit = service.Audit(2).Single(a => a.Action == "Import");
                Assert.AreEqual("Succeeded", audit.Status);
                var detail = System.Text.Json.JsonSerializer.Deserialize<DatabaseAccessImportPreview>(audit.Detail)!;
                Assert.AreEqual(43, detail.ExcludedAccounts.Single().LegacyUserId);
                Assert.AreEqual(3, detail.SourceAssignments);
                Assert.AreEqual(42, detail.ApprovedDbOwnerLegacyUserIds.Single());
                import.Token = service.Import(2, import, false, "test").Token;
                service.Import(2, import, true, "test");
                Assert.AreEqual(1L, service.Get(2).Revision, "An identical rerun must not revise desired state.");
                audit = service.Audit(2).First(a => a.Action == "Import");
                Assert.AreEqual("NoChanges", audit.Status);
                detail = System.Text.Json.JsonSerializer.Deserialize<DatabaseAccessImportPreview>(audit.Detail)!;
                Assert.AreEqual("AD\\missing", detail.ExcludedAccounts.Single().LoginName);
                import.ExcludedLegacyUserIds.Clear();
                Assert.ThrowsExactly<DatabaseAccessConflictException>(() => service.Import(2, import, true, "test"));
                import.ExcludedLegacyUserIds.Add(43);
                import.Token = "stale";
                Assert.ThrowsExactly<DatabaseAccessConflictException>(() => service.Import(2, import, true, "test"));
                Assert.IsFalse(provider.Preview(target, state).Observed.Principals.Any(p => p.Name == login),
                    "Import must not create a target database user.");
                unavailableProvider.DidNotReceiveWithAnyArgs().Preview(default!, default!);
                unavailableProvider.DidNotReceiveWithAnyArgs().Apply(default!, default!, default!);
                Assert.ThrowsExactly<InvalidOperationException>(() => service.Preview(2));
                Assert.ThrowsExactly<InvalidOperationException>(() => service.Apply(2, "token", "test"));
                targetUnavailable = false;
                Assert.HasCount(1, service.Preview(2).Errors);
                Assert.ThrowsExactly<DatabaseAccessConflictException>(() => service.Apply(2, "blocked", "test"));
                unavailableProvider.DidNotReceiveWithAnyArgs().Apply(default!, default!, default!);
                Assert.AreEqual(1L, service.Get(2).Revision);
                service = new DatabaseAccessService(store, factory, new DatabaseAccessProviders([provider]),
                    new DatabaseAccessDirectory(Substitute.For<IActiveDirectorySearcher>()), NullLogger<DatabaseAccessService>.Instance);
                var approvedPreview = service.Preview(2);
                var applied = service.Apply(2, approvedPreview.Token, "test");
                Assert.IsTrue(applied.Observed.Memberships.Any(m => m.Principal == login && m.Role == "db_owner"));
                Assert.HasCount(0, service.Preview(2).Operations);
                Execute(connection, $"ALTER ROLE [db_owner] DROP MEMBER [{login}];");
                var drift = service.Preview(2);
                Assert.AreEqual("GrantRole", drift.Operations.Single().Action);
                Assert.AreEqual("db_owner", drift.Operations.Single().Role);
                Assert.HasCount(0, service.Apply(2, drift.Token, "test").Operations);
                var revoked = service.Get(2);
                foreach (var membership in revoked.Memberships) membership.Present = false;
                revoked.Principals[0].Present = false;
                service.Save(2, revoked, "test");
                var revokePreview = service.Preview(2);
                Assert.HasCount(0, service.Apply(2, revokePreview.Token, "test").Operations);
            }
            var preview = provider.Preview(target, state);
            Assert.HasCount(0, preview.Errors);
            Assert.HasCount(3, preview.Operations);
            Assert.ThrowsExactly<DatabaseAccessConflictException>(() => provider.Apply(target, state, "stale"));
            var after = provider.Apply(target, state, preview.Token);
            Assert.HasCount(0, after.Operations);
            Assert.HasCount(0, provider.Apply(target, state, after.Token).Operations);

            // A dependent schema causes DROP USER to fail; all preceding role revocations must roll back.
            Execute(connection, "CREATE SCHEMA [OwnedByApp] AUTHORIZATION [app]]; --];");
            state.Principals[0].Present = false;
            state.Roles[0].Present = false;
            state.Memberships[0].Present = false;
            preview = provider.Preview(target, state);
            Assert.ThrowsExactly<SqlException>(() => provider.Apply(target, state, preview.Token));
            Assert.IsTrue(provider.Preview(target, state).Observed.Memberships.Any(m => m.Principal == "app]; --"));
            Execute(connection, "DROP SCHEMA [OwnedByApp];");
            preview = provider.Preview(target, state);
            Assert.HasCount(0, provider.Apply(target, state, preview.Token).Operations);
        }
        finally
        {
            Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
            Execute(master, $"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name='{login}') DROP LOGIN [{login}];");
            Execute(master, $"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name='{login}_off') DROP LOGIN [{login}_off];");
        }
    }

    private static readonly string[] SchemaTables =
    [
        "DatabaseAccessConfiguration", "DatabaseAccessSharedPrincipal", "DatabaseAccessSharedRole", "DatabaseAccessDatabasePrincipal",
        "DatabaseAccessDatabaseRole", "DatabaseAccessDatabaseMembership", "DatabaseAccessExclusion", "DatabaseAccessAudit"
    ];

    private static IEnumerable<string> Batches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(sql, @"(?m)^GO\s*$").Where(batch => !string.IsNullOrWhiteSpace(batch));

    private static int Scalar(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return (int)command.ExecuteScalar();
    }

    [TestMethod]
    [TestCategory("LocalSql")]
    public void DeploymentScriptsMovePerDatabaseRowsIntoSharedCatalogues()
    {
        var server = Environment.GetEnvironmentVariable("DORC_ACCESS_TEST_SERVER");
        if (server != @"(localdb)\DOrcAccessTests")
            Assert.Inconclusive("Set DORC_ACCESS_TEST_SERVER=(localdb)\\DOrcAccessTests to use an isolated test instance.");
        var database = "DorcAccess_" + Guid.NewGuid().ToString("N");
        using var master = new SqlConnection(new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = "master", IntegratedSecurity = true, Encrypt = false, Pooling = false
        }.ConnectionString);
        master.Open();
        Execute(master, $"CREATE DATABASE [{database}];");
        try
        {
            using var connection = new SqlConnection(new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = database }.ConnectionString);
            connection.Open();
            Execute(connection, "CREATE SCHEMA [deploy];");
            Execute(connection, "CREATE TABLE [dbo].[DATABASE] ([DB_ID] INT PRIMARY KEY); INSERT [dbo].[DATABASE] VALUES (1), (2);");
            foreach (var batch in Batches(File.ReadAllText(SchemaFile("DatabaseAccessConfiguration")))) Execute(connection, batch);
            // The per-database shape that shipped before normalisation.
            Execute(connection, """
                CREATE TABLE deploy.DatabaseAccessPrincipal (DatabaseId INT NOT NULL, Name NVARCHAR(128) NOT NULL, Kind NVARCHAR(32) NOT NULL,
                    LoginName NVARCHAR(128) NULL, DirectoryId NVARCHAR(256) NULL, Present BIT NOT NULL, LegacyUserId INT NULL,
                    PRIMARY KEY (DatabaseId, Name), FOREIGN KEY (DatabaseId) REFERENCES deploy.DatabaseAccessConfiguration (DatabaseId));
                CREATE TABLE deploy.DatabaseAccessRole (DatabaseId INT NOT NULL, Name NVARCHAR(128) NOT NULL, Managed BIT NOT NULL, Present BIT NOT NULL,
                    PRIMARY KEY (DatabaseId, Name), FOREIGN KEY (DatabaseId) REFERENCES deploy.DatabaseAccessConfiguration (DatabaseId));
                CREATE TABLE deploy.DatabaseAccessMembership (DatabaseId INT NOT NULL, Principal NVARCHAR(128) NOT NULL, Role NVARCHAR(128) NOT NULL, Present BIT NOT NULL,
                    PRIMARY KEY (DatabaseId, Principal, Role),
                    FOREIGN KEY (DatabaseId, Principal) REFERENCES deploy.DatabaseAccessPrincipal (DatabaseId, Name),
                    FOREIGN KEY (DatabaseId, Role) REFERENCES deploy.DatabaseAccessRole (DatabaseId, Name));
                INSERT deploy.DatabaseAccessConfiguration VALUES (1,'sql-server',3), (2,'sql-server',1);
                INSERT deploy.DatabaseAccessPrincipal VALUES
                    (1,'appserv01','Native','appserv01',NULL,1,11), (2,'appserv01','Native','appserv01',NULL,1,11),
                    (1,'AD\GRP-ENDENVACC-DV','DirectoryGroup',NULL,'S-1-5-21-1',1,NULL), (2,'AD\GRP-ENDENVACC-DV','DirectoryGroup',NULL,'S-1-5-21-1',0,NULL);
                INSERT deploy.DatabaseAccessRole VALUES (1,'olf_user',0,1), (2,'olf_user',0,1), (2,'custom',1,1);
                INSERT deploy.DatabaseAccessMembership VALUES
                    (1,'appserv01','olf_user',1), (2,'appserv01','olf_user',1), (2,'appserv01','custom',0), (1,'AD\GRP-ENDENVACC-DV','olf_user',1);
                """);
            var stage = File.ReadAllText(ScriptFile("Pre-Deployment", "StageDatabaseAccessForNormalisation.sql"));
            var migrate = File.ReadAllText(ScriptFile("Post-Deployment", "MigrateStagedDatabaseAccess.sql"));
            foreach (var batch in Batches(stage)) Execute(connection, batch);
            Assert.AreEqual(0, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessPrincipal"), "Pre-deploy empties the old tables.");
            foreach (var name in SchemaTables.Where(n => n is not "DatabaseAccessConfiguration"))
                foreach (var batch in Batches(File.ReadAllText(SchemaFile(name)))) Execute(connection, batch);
            foreach (var batch in Batches(migrate)) Execute(connection, batch);
            foreach (var batch in Batches(stage).Concat(Batches(migrate))) Execute(connection, batch);

            Assert.AreEqual(2, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessSharedPrincipal"));
            Assert.AreEqual(2, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessSharedRole"));
            Assert.AreEqual(4, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessDatabasePrincipal"));
            Assert.AreEqual(3, Scalar(connection, "SELECT COUNT(*) FROM deploy.DatabaseAccessDatabaseRole"));
            Assert.AreEqual(0, Scalar(connection, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('DatabaseAccessPrincipal','DatabaseAccessRole','DatabaseAccessMembership') OR name LIKE '%NORMALISATION_STAGING'"));
            using var access = new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>().UseSqlServer(connection.ConnectionString).Options);
            var second = DatabaseAccessStore.Read(access, 2);
            Assert.AreEqual(1L, second.Revision);
            Assert.AreEqual(11, second.Principals.Single(p => p.Name == "appserv01").LegacyUserId);
            Assert.IsFalse(second.Principals.Single(p => p.Kind == "DirectoryGroup").Present);
            Assert.IsTrue(second.Roles.Single(r => r.Name == "custom").Managed);
            CollectionAssert.AreEquivalent(new[] { "olf_user:True", "custom:False" },
                second.Memberships.Select(m => $"{m.Role}:{m.Present}").ToArray());
            Assert.HasCount(2, DatabaseAccessStore.Read(access, 1).Memberships);
        }
        finally
        {
            Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    private static string ScriptFile(string folder, string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "..", "Dorc.Database", "Scripts", folder, name));

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static DbSet<T> Set<T>(IEnumerable<T> values) where T : class
    {
        var query = values.AsQueryable();
        var set = Substitute.For<DbSet<T>, IQueryable<T>>();
        ((IQueryable<T>)set).Provider.Returns(query.Provider);
        ((IQueryable<T>)set).Expression.Returns(query.Expression);
        ((IQueryable<T>)set).ElementType.Returns(query.ElementType);
        ((IQueryable<T>)set).GetEnumerator().Returns(_ => query.GetEnumerator());
        return set;
    }

    [TestMethod]
    public void SchemaFileRequiresAnUnqualifiedTableName()
    {
        var path = SchemaFile("DatabaseAccessSharedRole");
        StringAssert.EndsWith(path, Path.Join("Dorc.Database", "deploy", "Tables", "DatabaseAccessSharedRole.sql"));
        Assert.IsTrue(File.Exists(path));
        Assert.ThrowsExactly<ArgumentException>(() => SchemaFile(Path.GetFullPath("DatabaseAccessRole")));
        Assert.ThrowsExactly<ArgumentException>(() => SchemaFile(Path.Join("..", "DatabaseAccessRole")));
        Assert.ThrowsExactly<ArgumentException>(() => SchemaFile(Path.Join("nested", "DatabaseAccessRole")));
        Assert.ThrowsExactly<ArgumentException>(() => SchemaFile(""));
    }

    private static string SchemaFile(string name, [CallerFilePath] string source = "")
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(fileName) || fileName != name)
            throw new ArgumentException("A schema table name must not contain a directory or rooted path.", nameof(name));
        return Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "..", "Dorc.Database", "deploy", "Tables", fileName + ".sql"));
    }
}
