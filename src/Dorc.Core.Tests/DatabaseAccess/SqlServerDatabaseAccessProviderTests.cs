using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Microsoft.Data.SqlClient;

namespace Dorc.Core.Tests.DatabaseAccess;

[TestClass]
public class SqlServerDatabaseAccessProviderTests
{
    [TestMethod]
    public void CommandTextAndInternalInvariants_GenerateQuotedSqlAndRejectViolations()
    {
        var provider = new SqlServerDatabaseAccessProvider();
        Assert.AreEqual("sql-server", provider.Name);

        Assert.AreEqual("CREATE ROLE [app]]role] AUTHORIZATION [dbo];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "CreateRole", Role = "app]role" }));
        Assert.AreEqual("CREATE USER [app_u] FOR LOGIN [app_l];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "CreateUser", Principal = "app_u", Login = "app_l" }));
        Assert.AreEqual("ALTER USER [app_u] WITH LOGIN = [app_l];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "MapUser", Principal = "app_u", Login = "app_l" }));
        Assert.AreEqual("ALTER ROLE [db_datareader] ADD MEMBER [app_u];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "GrantRole", Role = "db_datareader", Principal = "app_u" }));
        Assert.AreEqual("ALTER ROLE [db_datareader] DROP MEMBER [app_u];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "RevokeRole", Role = "db_datareader", Principal = "app_u" }));
        Assert.AreEqual("DROP USER [app_u];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "DropUser", Principal = "app_u" }));
        Assert.AreEqual("DROP ROLE [app_role];",
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "DropRole", Role = "app_role" }));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            SqlServerDatabaseAccessProvider.CommandText(new() { Action = "TruncateEverything" }));

        SqlServerDatabaseAccessProvider.VerifyConverged(new DatabaseAccessPreview());
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() =>
            SqlServerDatabaseAccessProvider.VerifyConverged(new DatabaseAccessPreview { Errors = ["err"] }));
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() =>
            SqlServerDatabaseAccessProvider.VerifyConverged(new DatabaseAccessPreview { Operations = [new() { Action = "DropRole" }] }));

        Assert.AreEqual("Native", SqlServerDatabaseAccessProvider.Kind("S"));
        Assert.AreEqual("DirectoryUser", SqlServerDatabaseAccessProvider.Kind("U"));
        Assert.AreEqual("DirectoryGroup", SqlServerDatabaseAccessProvider.Kind("G"));
        Assert.AreEqual("Unsupported", SqlServerDatabaseAccessProvider.Kind("E"));

        if (OperatingSystem.IsWindows())
        {
            var sid = new SecurityIdentifier("S-1-5-18");
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            Assert.AreEqual("S-1-5-18", SqlServerDatabaseAccessProvider.FormatDirectorySid(bytes, "U"));
            Assert.AreEqual("S-1-5-18", SqlServerDatabaseAccessProvider.FormatDirectorySid(bytes, "G"));
            Assert.IsNull(SqlServerDatabaseAccessProvider.FormatDirectorySid(bytes, "S"));
            Assert.IsNull(SqlServerDatabaseAccessProvider.FormatDirectorySid(DBNull.Value, "U"));
        }

        SqlServerDatabaseAccessProvider.RejectCaseCollisions(new DatabaseAccessObservation());
        Assert.ThrowsExactly<NotSupportedException>(() => SqlServerDatabaseAccessProvider.RejectCaseCollisions(new()
        {
            Principals = [new() { Name = "u" }, new() { Name = "U" }]
        }));
        Assert.ThrowsExactly<NotSupportedException>(() => SqlServerDatabaseAccessProvider.RejectCaseCollisions(new()
        {
            Roles = [new() { Name = "r" }, new() { Name = "R" }]
        }));
        Assert.ThrowsExactly<NotSupportedException>(() => SqlServerDatabaseAccessProvider.RejectCaseCollisions(new()
        {
            Logins = [new() { Name = "l" }, new() { Name = "L" }]
        }));

        // Connect target validation & connection dispose on failure
        Assert.ThrowsExactly<ArgumentException>(() => provider.SqlLogins(new DatabaseAccessTarget(1, "", "AppDb")));
        Assert.ThrowsExactly<ArgumentException>(() => provider.SqlLogins(new DatabaseAccessTarget(1, "sql01", "")));
        Assert.ThrowsExactly<ArgumentException>(() => provider.SqlLogins(new DatabaseAccessTarget(1, "sql01", "master")));
        Assert.ThrowsExactly<SqlException>(() => provider.SqlLogins(new DatabaseAccessTarget(1, "np:\\\\.\\pipe\\missing_pipe_954", "AppDb")));
    }

    [TestMethod]
    [TestCategory("LocalSql")]
    public void PreviewApplyAndSqlLogins_ReconcileRealSqlServerDatabaseEndToEnd()
    {
        var server = Environment.GetEnvironmentVariable("DORC_ACCESS_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(server))
            Assert.Inconclusive("Set DORC_ACCESS_TEST_SERVER to run LocalSql target reconciliation tests.");

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var dbName = $"dorc_target_{suffix}";
        var loginA = $"dorc_login_a_{suffix}";
        var loginB = $"dorc_login_b_{suffix}";
        var disabledLogin = $"dorc_disabled_{suffix}";
        var protectedPrefixLogin = $"##dorc_prot_{suffix}##";

        var masterCs = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true
        }.ConnectionString;

        using var masterConn = new SqlConnection(masterCs);
        masterConn.Open();
        using (var setupCmd = new SqlCommand($"""
            CREATE DATABASE [{dbName}];
            CREATE LOGIN [{loginA}] WITH PASSWORD = N'Str0ng!Pass_{suffix}_A', CHECK_POLICY = OFF;
            CREATE LOGIN [{loginB}] WITH PASSWORD = N'Str0ng!Pass_{suffix}_B', CHECK_POLICY = OFF;
            CREATE LOGIN [{disabledLogin}] WITH PASSWORD = N'Str0ng!Pass_{suffix}_D', CHECK_POLICY = OFF;
            ALTER LOGIN [{disabledLogin}] DISABLE;
            CREATE LOGIN [{protectedPrefixLogin}] WITH PASSWORD = N'Str0ng!Pass_{suffix}_P', CHECK_POLICY = OFF;
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'BUILTIN\Users')
                CREATE LOGIN [BUILTIN\Users] FROM WINDOWS;
            """, masterConn))
        {
            setupCmd.ExecuteNonQuery();
        }

        try
        {
            var dbCs = new SqlConnectionStringBuilder(masterCs) { InitialCatalog = dbName }.ConnectionString;
            using (var dbConn = new SqlConnection(dbCs))
            {
                dbConn.Open();
                // Create a ## protected-named user in target DB to cover ProtectedPrincipal(name) when principal_id > 4
                using var seedCmd = new SqlCommand("CREATE USER [##dorc_marker] WITHOUT LOGIN;", dbConn);
                seedCmd.ExecuteNonQuery();
            }

            var provider = new SqlServerDatabaseAccessProvider();
            var target = new DatabaseAccessTarget(1, server, dbName);

            var visibleLogins = provider.SqlLogins(target);
            CollectionAssert.Contains(visibleLogins, loginA);
            CollectionAssert.Contains(visibleLogins, loginB);
            CollectionAssert.DoesNotContain(visibleLogins, disabledLogin);
            CollectionAssert.DoesNotContain(visibleLogins, protectedPrefixLogin);

            // Apply when target preview contains errors -> Conflict
            var brokenState = new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 1,
                Provider = "sql-server",
                Principals = [new() { Name = "missing_login_user", Kind = "Native", LoginName = "nonexistent_login_954", Present = true }]
            };
            Assert.ThrowsExactly<DatabaseAccessConflictException>(() => provider.Apply(target, brokenState, "any-token"));

            // Step 1: Create custom role, create user, grant role
            var step1 = new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 1,
                Provider = "sql-server",
                Principals = [new() { Name = "app_user", Kind = "Native", LoginName = loginA, Present = true }],
                Roles = [new() { Name = "app_readers", Managed = true, Present = true }],
                Memberships = [new() { Principal = "app_user", Role = "app_readers", Present = true }]
            };
            var preview1 = provider.Preview(target, step1);
            Assert.HasCount(0, preview1.Errors);
            Assert.ThrowsExactly<DatabaseAccessConflictException>(() => provider.Apply(target, step1, "stale-token"));

            // Also verify exclusive sp_getapplock contention when another transaction holds DOrc.DatabaseAccess
            using (var lockConn = new SqlConnection(dbCs))
            {
                lockConn.Open();
                using var lockTx = lockConn.BeginTransaction();
                using var holdCmd = new SqlCommand("""
                    EXEC sys.sp_getapplock @Resource=N'DOrc.DatabaseAccess', @LockMode='Exclusive',
                        @LockOwner='Transaction', @LockTimeout=0;
                    """, lockConn, lockTx);
                holdCmd.ExecuteNonQuery();
                Assert.ThrowsExactly<SqlException>(() => provider.Apply(target, step1, preview1.Token));
            }

            var after1 = provider.Apply(target, step1, preview1.Token);
            Assert.HasCount(0, after1.Operations);

            // Step 2: Remap user to loginB, revoke role, then drop user & role in Step 3
            var step2 = new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 2,
                Provider = "sql-server",
                Principals = [new() { Name = "app_user", Kind = "Native", LoginName = loginB, Present = true }],
                Roles = [new() { Name = "app_readers", Managed = true, Present = true }],
                Memberships = [new() { Principal = "app_user", Role = "app_readers", Present = false }]
            };
            var preview2 = provider.Preview(target, step2);
            var after2 = provider.Apply(target, step2, preview2.Token);
            Assert.HasCount(0, after2.Operations);

            var step3 = new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = 3,
                Provider = "sql-server",
                Principals = [new() { Name = "app_user", Kind = "Native", LoginName = loginB, Present = false }],
                Roles = [new() { Name = "app_readers", Managed = true, Present = false }],
                Memberships = [new() { Principal = "app_user", Role = "app_readers", Present = false }]
            };
            var preview3 = provider.Preview(target, step3);
            var after3 = provider.Apply(target, step3, preview3.Token);
            Assert.HasCount(0, after3.Operations);
        }
        finally
        {
            using var cleanupCmd = new SqlCommand($"""
                IF DB_ID(N'{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END
                IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginA}') DROP LOGIN [{loginA}];
                IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginB}') DROP LOGIN [{loginB}];
                IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{disabledLogin}') DROP LOGIN [{disabledLogin}];
                IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{protectedPrefixLogin}') DROP LOGIN [{protectedPrefixLogin}];
                """, masterConn);
            cleanupCmd.ExecuteNonQuery();
        }
    }
}
