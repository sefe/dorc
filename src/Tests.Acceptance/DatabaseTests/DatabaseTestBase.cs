using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Dac;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Tests.Acceptance.DatabaseTests
{
    /// <summary>
    /// Shared test harness for DB-level integration tests. Each test creates its own ephemeral
    /// database (dorc_test_{guid8}), publishes the SSDT dacpac against it, runs assertions, and
    /// drops the database in cleanup. Tests are tagged [TestCategory("Database")] so CI can run
    /// them as a named group and local developers can skip them.
    /// </summary>
    public abstract class DatabaseTestBase
    {
        public TestContext TestContext { get; set; } = null!;

        protected string MasterConnectionString { get; private set; } = string.Empty;
        protected string EphemeralDatabaseName { get; private set; } = string.Empty;
        protected string EphemeralConnectionString { get; private set; } = string.Empty;

        private bool _skipped;

        [TestInitialize]
        public void BaseInitialize()
        {
            var configurationRoot = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json")
                .Build();

            var configuredConnection = configurationRoot.GetConnectionString("DOrcConnectionString");

            if (string.IsNullOrWhiteSpace(configuredConnection))
            {
                _skipped = true;
                Assert.Inconclusive(
                    "DOrcConnectionString is not configured in appsettings.test.json. " +
                    "Database integration tests require a reachable SQL Server. " +
                    "Provide a connection string via CI or local override to enable these tests.");
                return;
            }

            var builder = new SqlConnectionStringBuilder(configuredConnection);
            var originalDb = builder.InitialCatalog;

            // Route CREATE/DROP DATABASE through master
            builder.InitialCatalog = "master";
            MasterConnectionString = builder.ConnectionString;

            EphemeralDatabaseName = $"dorc_test_{Guid.NewGuid():N}".Substring(0, 20);
            ExecuteNonQuery(MasterConnectionString, $"CREATE DATABASE [{EphemeralDatabaseName}]");

            builder.InitialCatalog = EphemeralDatabaseName;
            EphemeralConnectionString = builder.ConnectionString;

            // Suppress "unused" warning on originalDb — kept as a breadcrumb for future diagnostics.
            _ = originalDb;
        }

        [TestCleanup]
        public void BaseCleanup()
        {
            if (_skipped || string.IsNullOrEmpty(EphemeralDatabaseName))
                return;

            try
            {
                ExecuteNonQuery(MasterConnectionString,
                    $"IF DB_ID('{EphemeralDatabaseName}') IS NOT NULL " +
                    $"BEGIN " +
                    $"  ALTER DATABASE [{EphemeralDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"  DROP DATABASE [{EphemeralDatabaseName}]; " +
                    $"END");
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
            {
                TestContext.WriteLine($"Warning: failed to drop ephemeral DB {EphemeralDatabaseName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Publish the Dorc.Database dacpac against the ephemeral DB. BlockOnPossibleDataLoss is
        /// left at default (true) so the staging-table pattern in S-002 is genuinely exercised —
        /// if pre-deploy doesn't empty the legacy tables, the publish fails and so does the test.
        /// </summary>
        protected void PublishDacpac()
        {
            var dacpacPath = LocateDacpac();
            var dacServices = new DacServices(EphemeralConnectionString);
            using var package = DacPackage.Load(dacpacPath);

            var options = new DacDeployOptions
            {
                CreateNewDatabase = false,
                BlockOnPossibleDataLoss = true,
                IncludeTransactionalScripts = true
            };

            dacServices.Deploy(package, EphemeralDatabaseName, upgradeExisting: true, options: options);
        }

        protected int ExecuteScalarInt(string sql)
        {
            using var conn = new SqlConnection(EphemeralConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            var result = cmd.ExecuteScalar();
            return result is null || result is DBNull ? 0 : Convert.ToInt32(result);
        }

        protected long ExecuteScalarLong(string sql)
        {
            using var conn = new SqlConnection(EphemeralConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            var result = cmd.ExecuteScalar();
            return result is null || result is DBNull ? 0L : Convert.ToInt64(result);
        }

        protected void ExecuteEphemeral(string sql)
        {
            ExecuteNonQuery(EphemeralConnectionString, sql);
        }

        protected static string ReadFixture(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Fixture path must be non-empty", nameof(relativePath));
            if (Path.IsPathRooted(relativePath))
                throw new ArgumentException("Fixture path must be relative, not rooted", nameof(relativePath));

            // Path.Join does not drop the base on a rooted second arg (unlike Path.Combine); the
            // rooted-path guard above keeps intent explicit so a future caller can't accidentally
            // escape AppContext.BaseDirectory via ".." either (File.Exists check below enforces
            // existence; callers stay inside the test output folder).
            var fixtureFile = Path.Join(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(fixtureFile))
                throw new FileNotFoundException($"Test fixture not found: {fixtureFile}");
            return File.ReadAllText(fixtureFile);
        }

        private static void ExecuteNonQuery(string connectionString, string sql)
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Walk up from the test-output directory to the repo root, then into the SSDT build folder.
        /// Probes Debug first then Release.
        /// </summary>
        private static string LocateDacpac()
        {
            var baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);

            // Walk up until we find the "src" folder
            while (dir != null && dir.Name != "src")
                dir = dir.Parent;

            if (dir == null)
                throw new DirectoryNotFoundException($"Could not locate 'src' directory from {baseDir}");

            foreach (var config in new[] { "debug", "release" })
            {
                // Path.Join preserves intent when concatenating path segments; all segments here
                // are known-relative, so it's equivalent to Path.Combine for this input but
                // avoids the "drop earlier arguments" behaviour if a segment ever becomes rooted.
                var candidate = Path.Join(dir.FullName, "Dorc.Database", "sql", config, "Dorc.Database.dacpac");
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException(
                $"Dorc.Database.dacpac not found under {dir.FullName}/Dorc.Database/sql/{{debug,release}}. " +
                "Build the Dorc.Database project with MSBuild before running database tests.");
        }
    }
}
