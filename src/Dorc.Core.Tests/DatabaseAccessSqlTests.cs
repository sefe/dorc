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
            using var connection = new SqlConnection(new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = database }.ConnectionString);
            connection.Open();
            Execute(connection, "CREATE TABLE [dbo].[DATABASE] ([DB_ID] INT PRIMARY KEY); INSERT [dbo].[DATABASE] VALUES (1);");
            foreach (var name in new[] { "DatabaseAccessConfiguration", "DatabaseAccessPrincipal", "DatabaseAccessRole", "DatabaseAccessMembership", "DatabaseAccessAudit" })
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(File.ReadAllText(SchemaFile(name)), @"(?m)^GO\s*$"))
                    if (!string.IsNullOrWhiteSpace(batch)) Execute(connection, batch);
            Execute(connection, "INSERT dbo.DatabaseAccessConfiguration VALUES (1,'sql-server',0);");
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT dbo.DatabaseAccessConfiguration VALUES (1,'sql-server',0);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT dbo.DatabaseAccessConfiguration VALUES (999,'sql-server',0);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT dbo.DatabaseAccessMembership VALUES (1,'missing','missing',1);"));
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT dbo.DatabaseAccessPrincipal VALUES (1,'bad','DirectoryUser','cached-login',NULL,1,NULL);"));
            Execute(connection, "INSERT [dbo].[DATABASE] VALUES (2); INSERT dbo.DatabaseAccessConfiguration VALUES (2,'sql-server',0);");
            Execute(connection, "INSERT dbo.DatabaseAccessPrincipal VALUES (1,'one','Native','login',NULL,1,NULL); INSERT dbo.DatabaseAccessRole VALUES (2,'two',0,1);");
            Assert.ThrowsExactly<SqlException>(() => Execute(connection, "INSERT dbo.DatabaseAccessMembership VALUES (1,'one','two',1);"));
            Execute(connection, "DELETE dbo.DatabaseAccessPrincipal WHERE Name='one'; DELETE dbo.DatabaseAccessRole WHERE Name='two';");

            var provider = new SqlServerDatabaseAccessProvider();
            var target = new DatabaseAccessTarget(1, server!, database);
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
                var maps = Set(new[] { new Dorc.PersistentData.Model.EnvironmentUser { DbId = 2, UserId = 42, PermissionId = 7 } });
                var users = Set(new[] { new Dorc.PersistentData.Model.User { Id = 42, LoginId = login, LoginType = "Endur" } });
                var permissions = Set(new[] { new Dorc.PersistentData.Model.Permission { Id = 7, Name = "Read" } });
                legacy.Databases.Returns(databases);
                legacy.EnvironmentUsers.Returns(maps);
                legacy.Users.Returns(users);
                legacy.Permissions.Returns(permissions);
                var service = new DatabaseAccessService(store, factory, new DatabaseAccessProviders([provider]),
                    new DatabaseAccessDirectory(Substitute.For<IActiveDirectorySearcher>()), NullLogger<DatabaseAccessService>.Instance);
                var import = new DatabaseAccessImportRequest { RoleMappings = new() { ["Read"] = "db_datareader" } };
                var importPreview = service.Import(2, import, false, "test");
                Assert.HasCount(0, importPreview.Errors, string.Join("; ", importPreview.Errors));
                Assert.AreEqual(0L, service.Get(2).Revision);
                import.Token = importPreview.Token;
                service.Import(2, import, true, "test");
                Assert.AreEqual(1L, service.Get(2).Revision);
                Assert.AreEqual("Native", service.Get(2).Principals.Single().Kind);
                import.Token = service.Import(2, import, false, "test").Token;
                service.Import(2, import, true, "test");
                Assert.AreEqual(1L, service.Get(2).Revision, "An identical rerun must not revise desired state.");
                import.Token = "stale";
                Assert.ThrowsExactly<DatabaseAccessConflictException>(() => service.Import(2, import, true, "test"));
                Assert.IsFalse(provider.Preview(target, state).Observed.Principals.Any(p => p.Name == login),
                    "Import must not create a target database user.");
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
        }
    }

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

    private static string SchemaFile(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "Dorc.Database", "dbo", "Tables", name + ".sql"));
}
