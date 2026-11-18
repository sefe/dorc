using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests.DatabaseAccess;

[TestClass]
public class DatabaseAccessReconcilerTests
{
    [TestMethod]
    public void ValidateTransition_EnforcesRevisionProviderProvenanceAndDbOwnerGuards()
    {
        var baseCurrent = new DatabaseAccessState
        {
            DatabaseId = 1,
            Revision = 2,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "dbo_user", Kind = "Native", LoginName = "dbo_login", Present = true, LegacyUserId = 10 },
                new() { Name = "app_user", Kind = "Native", LoginName = "app_login", Present = true }
            ],
            Roles =
            [
                new() { Name = "db_owner", Managed = false, Present = true },
                new() { Name = "custom_role", Managed = true, Present = true }
            ],
            Memberships =
            [
                new() { Principal = "dbo_user", Role = "db_owner", Present = true },
                new() { Principal = "app_user", Role = "custom_role", Present = true }
            ]
        };

        // Revision mismatch -> Conflict
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState { Revision = 1 }));

        // Provider change when Revision > 0 -> ArgumentException; when Revision == 0 -> allowed
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState { Revision = 2, Provider = "postgresql" }));
        DatabaseAccessReconciler.ValidateTransition(
            new DatabaseAccessState { Revision = 0, Provider = "sql-server" },
            new DatabaseAccessState { Revision = 0, Provider = "sql-server" });

        // Dropping or mutating existing principal identity/provenance -> ArgumentException
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState { Revision = 2, Provider = "sql-server" }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals =
                [
                    new() { Name = "dbo_user", Kind = "Native", LoginName = "dbo_login", Present = true, LegacyUserId = 99 },
                    new() { Name = "app_user", Kind = "Native", LoginName = "app_login", Present = true }
                ]
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals =
                [
                    new() { Name = "dbo_user", Kind = "Native", LoginName = "dbo_login", Present = true, LegacyUserId = 10 },
                    new() { Name = "app_user", Kind = "DirectoryUser", DirectoryId = "S-1", Present = true }
                ]
            }));

        var dirCurrent = new DatabaseAccessState
        {
            Revision = 1,
            Provider = "sql-server",
            Principals = [new() { Name = "ad_u", Kind = "DirectoryUser", DirectoryId = "S-1", Present = true }]
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(dirCurrent, new DatabaseAccessState
            {
                Revision = 1,
                Provider = "sql-server",
                Principals = [new() { Name = "ad_u", Kind = "DirectoryUser", DirectoryId = "S-2", Present = true }]
            }));

        // Changing LoginName on db_owner principal -> rejected; on non-db_owner principal -> allowed
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals =
                [
                    new() { Name = "dbo_user", Kind = "Native", LoginName = "evil_swap", Present = true, LegacyUserId = 10 },
                    new() { Name = "app_user", Kind = "Native", LoginName = "app_login", Present = true }
                ],
                Roles = baseCurrent.Roles,
                Memberships = baseCurrent.Memberships
            }));

        // Dropping role or changing Managed -> rejected
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals = baseCurrent.Principals,
                Roles = [new() { Name = "db_owner", Managed = false, Present = true }]
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals = baseCurrent.Principals,
                Roles =
                [
                    new() { Name = "db_owner", Managed = false, Present = true },
                    new() { Name = "custom_role", Managed = false, Present = true }
                ]
            }));

        // Dropping membership -> rejected
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals = baseCurrent.Principals,
                Roles = baseCurrent.Roles,
                Memberships = [new() { Principal = "dbo_user", Role = "db_owner", Present = true }]
            }));

        // Granting or re-enabling db_owner without CanApproveDbOwner -> rejected; with CanApproveDbOwner -> allowed
        var revokedDbo = new DatabaseAccessState
        {
            Revision = 3,
            Provider = "sql-server",
            Principals = [new() { Name = "dbo_user", Kind = "Native", LoginName = "dbo_login", Present = true, LegacyUserId = 10 }],
            Roles = [new() { Name = "db_owner", Managed = false, Present = true }],
            Memberships = [new() { Principal = "dbo_user", Role = "db_owner", Present = false }]
        };
        var reenabledDbo = new DatabaseAccessState
        {
            Revision = 3,
            Provider = "sql-server",
            Principals = revokedDbo.Principals,
            Roles = revokedDbo.Roles,
            Memberships = [new() { Principal = "dbo_user", Role = "db_owner", Present = true }]
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(revokedDbo, reenabledDbo));
        DatabaseAccessReconciler.ValidateTransition(revokedDbo, reenabledDbo, canApproveDbOwner: true);

        // Granting db_owner to a second principal when current has both non-db_owner and another principal's db_owner -> rejected
        var mixedMembershipsCurrent = new DatabaseAccessState
        {
            Revision = 2,
            Provider = "sql-server",
            Principals = baseCurrent.Principals,
            Roles = baseCurrent.Roles,
            Memberships =
            [
                new() { Principal = "app_user", Role = "custom_role", Present = true },
                new() { Principal = "dbo_user", Role = "db_owner", Present = true }
            ]
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(mixedMembershipsCurrent, new DatabaseAccessState
            {
                Revision = 2,
                Provider = "sql-server",
                Principals = baseCurrent.Principals,
                Roles = baseCurrent.Roles,
                Memberships =
                [
                    new() { Principal = "app_user", Role = "custom_role", Present = true },
                    new() { Principal = "dbo_user", Role = "db_owner", Present = true },
                    new() { Principal = "app_user", Role = "db_owner", Present = true }
                ]
            }));

        // New principal absent or claiming LegacyUserId -> rejected
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(new DatabaseAccessState { Provider = "sql-server" }, new DatabaseAccessState
            {
                Provider = "sql-server",
                Principals = [new() { Name = "new_absent", Kind = "Native", LoginName = "new_absent", Present = false }]
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(new DatabaseAccessState { Provider = "sql-server" }, new DatabaseAccessState
            {
                Provider = "sql-server",
                Principals = [new() { Name = "new_legacy", Kind = "Native", LoginName = "new_legacy", Present = true, LegacyUserId = 5 }]
            }));

        // Untracked absent role -> rejected
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.ValidateTransition(new DatabaseAccessState { Provider = "sql-server" }, new DatabaseAccessState
            {
                Provider = "sql-server",
                Roles = [new() { Name = "untracked_role", Managed = true, Present = false }]
            }));

        // Valid transition: remap non-db_owner login, keep existing db_owner, and revoke custom membership
        DatabaseAccessReconciler.ValidateTransition(baseCurrent, new DatabaseAccessState
        {
            Revision = 2,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "dbo_user", Kind = "Native", LoginName = "dbo_login", Present = true, LegacyUserId = 10 },
                new() { Name = "app_user", Kind = "Native", LoginName = "app_login_v2", Present = true },
                new() { Name = "fresh_user", Kind = "Native", LoginName = "fresh_login", Present = true }
            ],
            Roles = baseCurrent.Roles,
            Memberships =
            [
                new() { Principal = "dbo_user", Role = "db_owner", Present = false },
                new() { Principal = "app_user", Role = "custom_role", Present = false }
            ]
        });

        DatabaseAccessReconciler.EnsureProviderMatchesDatabaseType("sql-server", new Database { DatabaseType = DatabaseType.SqlServer });
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessReconciler.EnsureProviderMatchesDatabaseType("sql-server", new Database { DatabaseType = DatabaseType.PostgreSql }));
    }

    [TestMethod]
    public void GetSavePreviewSqlLoginsApplyAndAudit_PersistStateValidateDirectoryAndAuditOutcomes()
    {
        using var harness = LocalAccessDbHarness.Create([
            new Database { Id = 1, Name = "Db1", ServerName = "sql01", DatabaseType = DatabaseType.SqlServer },
            new Database { Id = 2, Name = null, ServerName = null, DatabaseType = DatabaseType.SqlServer }
        ]);

        var ad = Substitute.For<IActiveDirectorySearcher>();
        ad.GetUserDataById("S-grp").Returns(new UserElementApiModel { Sid = "S-grp", DisplayName = "Grp", IsGroup = true });
        ad.GetUserDataById("S-usr").Returns(new UserElementApiModel { Sid = "S-usr", DisplayName = "Usr", IsGroup = false });
        var directory = new DatabaseAccessDirectory(ad, () => false);

        var provider = Substitute.For<IDatabaseAccessProvider>();
        provider.Name.Returns("sql-server");
        var providers = new DatabaseAccessProviders([provider]);
        var logger = Substitute.For<ILogger<DatabaseAccessReconciler>>();
        var reconciler = new DatabaseAccessReconciler(harness.Store, harness.Factory, providers, directory, logger);

        // Missing database
        Assert.ThrowsExactly<ArgumentException>(() => reconciler.Get(999));

        // Route ID mismatch
        Assert.ThrowsExactly<ArgumentException>(() => reconciler.Save(1, new DatabaseAccessState { DatabaseId = 2 }, "actor"));

        // Directory kind mismatch on Save
        Assert.ThrowsExactly<ArgumentException>(() => reconciler.Save(1, new DatabaseAccessState
        {
            DatabaseId = 1,
            Provider = "sql-server",
            Principals = [new() { Name = "AD\\Usr", Kind = "DirectoryGroup", DirectoryId = "S-usr", Present = true }]
        }, "actor"));

        // Valid Save with present DirectoryGroup + absent DirectoryUser (skipped by ValidateDirectory)
        var saved = reconciler.Save(1, new DatabaseAccessState
        {
            DatabaseId = 1,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "AD\\Grp", Kind = "DirectoryGroup", DirectoryId = "S-grp", Present = true },
                new() { Name = "app_user", Kind = "Native", LoginName = "app_user", Present = true }
            ],
            Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
            Memberships = [new() { Principal = "app_user", Role = "db_datareader", Present = true }]
        }, "alice");
        Assert.AreEqual(1, saved.Revision);
        Assert.AreEqual(1, reconciler.Get(1).Revision);

        // Save with Present=false on directory principal skips ValidateDirectory lookup for removed principal
        var savedRev2 = reconciler.Save(1, new DatabaseAccessState
        {
            DatabaseId = 1,
            Revision = 1,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "AD\\Grp", Kind = "DirectoryGroup", DirectoryId = "S-grp", Present = false },
                new() { Name = "app_user", Kind = "Native", LoginName = "app_user", Present = true }
            ],
            Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
            Memberships = [new() { Principal = "app_user", Role = "db_datareader", Present = true }]
        }, "alice");
        Assert.AreEqual(2, savedRev2.Revision);

        // Preview and SqlLogins on DB 2 (with null Name/ServerName coalescing to "")
        provider.Preview(new DatabaseAccessTarget(2, "", ""), Arg.Any<DatabaseAccessState>())
            .Returns(new DatabaseAccessPreview { Token = "tok-db2" });
        provider.SqlLogins(new DatabaseAccessTarget(2, "", "")).Returns(["sa_app"]);
        Assert.AreEqual("tok-db2", reconciler.Preview(2).Token);
        CollectionAssert.AreEqual(new[] { "sa_app" }, reconciler.SqlLogins(2));

        // Apply: stale token & preview with errors -> Conflict
        provider.Preview(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>())
            .Returns(new DatabaseAccessPreview { Token = "tok-valid" });
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() => reconciler.Apply(1, "tok-stale", "alice"));

        provider.Preview(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>())
            .Returns(new DatabaseAccessPreview { Token = "tok-err", Errors = ["Missing login"] });
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() => reconciler.Apply(1, "tok-err", "alice"));

        // Apply: provider throws -> records FailedOrUncertain audit and rethrows
        provider.Preview(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>())
            .Returns(new DatabaseAccessPreview { Token = "tok-boom" });
        provider.Apply(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>(), "tok-boom")
            .Returns(_ => throw new InvalidOperationException("Target network severed"));
        Assert.ThrowsExactly<InvalidOperationException>(() => reconciler.Apply(1, "tok-boom", "alice"));

        // Apply: provider succeeds -> records Succeeded audit
        provider.Preview(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>())
            .Returns(new DatabaseAccessPreview { Token = "tok-ok" });
        provider.Apply(new DatabaseAccessTarget(1, "sql01", "Db1"), Arg.Any<DatabaseAccessState>(), "tok-ok")
            .Returns(new DatabaseAccessPreview { Token = "tok-after" });
        var applied = reconciler.Apply(1, "tok-ok", "alice");
        Assert.AreEqual("tok-after", applied.Token);

        var audits = reconciler.Audit(1);
        Assert.HasCount(4, audits);
        CollectionAssert.AreEquivalent(
            new[] { "Succeeded", "Succeeded", "FailedOrUncertain", "Succeeded" },
            audits.Select(a => a.Status).ToArray());
    }
}

internal sealed class LocalAccessDbHarness : IDisposable
{
    private readonly string _masterConnectionString;
    private readonly string _dbName;
    public string ConnectionString { get; }
    public DbContextOptions<DatabaseAccessContext> Options { get; }
    public IDeploymentContextFactory Factory { get; }
    public DatabaseAccessStore Store { get; }

    private LocalAccessDbHarness(
        string masterConnectionString,
        string dbName,
        string connectionString,
        DbContextOptions<DatabaseAccessContext> options,
        IDeploymentContextFactory factory,
        DatabaseAccessStore store)
    {
        _masterConnectionString = masterConnectionString;
        _dbName = dbName;
        ConnectionString = connectionString;
        Options = options;
        Factory = factory;
        Store = store;
    }

    public static LocalAccessDbHarness Create(List<Database> databases)
    {
        var server = System.Environment.GetEnvironmentVariable("DORC_ACCESS_TEST_SERVER") ?? @"(localdb)\MSSQLLocalDB";
        var dbName = "dorc_core_access_" + Guid.NewGuid().ToString("N");
        var masterCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60
        }.ConnectionString;

        using (var masterSql = new Microsoft.Data.SqlClient.SqlConnection(masterCs))
        {
            masterSql.Open();
            using var createCmd = new Microsoft.Data.SqlClient.SqlCommand($"CREATE DATABASE [{dbName}];", masterSql);
            createCmd.ExecuteNonQuery();
        }

        var dbCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(masterCs) { InitialCatalog = dbName }.ConnectionString;
        var options = new DbContextOptionsBuilder<DatabaseAccessContext>().UseSqlServer(dbCs).Options;

        using (var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(dbCs))
        {
            sqlConn.Open();
            using (var schemaCmd = new Microsoft.Data.SqlClient.SqlCommand("CREATE SCHEMA [deploy];", sqlConn))
                schemaCmd.ExecuteNonQuery();
            using (var tableCmd = new Microsoft.Data.SqlClient.SqlCommand("CREATE TABLE [deploy].[Database] ([Id] INT PRIMARY KEY);", sqlConn))
                tableCmd.ExecuteNonQuery();
            foreach (var db in databases)
            {
                using var insertCmd = new Microsoft.Data.SqlClient.SqlCommand($"INSERT [deploy].[Database] VALUES ({db.Id});", sqlConn);
                insertCmd.ExecuteNonQuery();
            }
        }

        using (var accessCtx = new DatabaseAccessContext(options))
        {
            var creator = (Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator)
                ((Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure<IServiceProvider>)accessCtx.Database)
                .Instance.GetRequiredService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
            creator.CreateTables();
        }

        var factory = new FuncFactory(() =>
        {
            var dep = Substitute.For<IDeploymentContext>();
            var inner = new DatabaseAccessContext(options);
            dep.Database.Returns(inner.Database);
            dep.When(x => x.Dispose()).Do(_ => inner.Dispose());
            var dbSet = CreateMockDbSet(databases);
            dep.Databases.Returns(dbSet);
            return dep;
        });

        return new LocalAccessDbHarness(masterCs, dbName, dbCs, options, factory, new DatabaseAccessStore(factory));
    }

    private static DbSet<T> CreateMockDbSet<T>(List<T> items) where T : class
    {
        var queryable = items.AsQueryable();
        var set = Substitute.For<DbSet<T>, IQueryable<T>>();
        ((IQueryable<T>)set).Provider.Returns(queryable.Provider);
        ((IQueryable<T>)set).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)set).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)set).GetEnumerator().Returns(_ => queryable.GetEnumerator());
        return set;
    }

    public void Dispose()
    {
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        using var masterSql = new Microsoft.Data.SqlClient.SqlConnection(_masterConnectionString);
        masterSql.Open();
        using var dropCmd = new Microsoft.Data.SqlClient.SqlCommand(
            $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}];", masterSql);
        dropCmd.ExecuteNonQuery();
    }

    private sealed class FuncFactory(Func<IDeploymentContext> create) : IDeploymentContextFactory
    {
        public IDeploymentContext GetContext() => create();
    }
}
