using System.Security.Cryptography;
using System.Text.Json;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Tools.EncryptionMigrationCLI;
using Environment = System.Environment;

namespace Dorc.Api.Tests.Sources;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class EncryptionMigrationSqlTests
{
    private static string _masterConnectionString = null!;
    private static string _databaseName = null!;
    private static DeploymentContextFactory _factory = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "master",
            Encrypt = SqlConnectionEncryptOption.True,
            TrustServerCertificate = true
        };
        try
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            probe.Open();
        }
        catch (SqlException ex)
        {
            if (Environment.GetEnvironmentVariable("DORC_DB_TESTS_REQUIRED") == "1")
                Assert.Fail($"SQL Server is required for Database category tests: {ex.Message}");
            Assert.Inconclusive($"SQL Server is not reachable ({ex.Message}).");
        }

        _masterConnectionString = builder.ConnectionString;
        _databaseName = "dorc_prop_enc_" + Guid.NewGuid().ToString("N");

        using (var sql = new SqlConnection(_masterConnectionString))
        {
            sql.Open();
            using var cmd = new SqlCommand($"CREATE DATABASE [{_databaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS", sql);
            cmd.ExecuteNonQuery();
        }

        builder.InitialCatalog = _databaseName;
        _factory = new DeploymentContextFactory(builder.ConnectionString);

        using var context = _factory.GetContext();
        context.Database.EnsureCreated();
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (string.IsNullOrEmpty(_databaseName) || string.IsNullOrEmpty(_masterConnectionString))
            return;

        using var sql = new SqlConnection(_masterConnectionString);
        sql.Open();
        using var cmd = new SqlCommand($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]", sql);
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    public void EncryptionMigration_MigratesScalarArrayAndConfigValuesToAesGcm()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var ivBytes = new byte[16];
        RandomNumberGenerator.Fill(ivBytes);

        var encryptor = new AesGcmPropertyEncryptor(Convert.ToBase64String(ivBytes), Convert.ToBase64String(keyBytes));
        var legacyEncryptor = new PropertyEncryptor(Convert.ToBase64String(ivBytes), Convert.ToBase64String(keyBytes));

        long scalarPvId;
        long arrayPvId;
        int configId;

        using (var context = _factory.GetContext())
        {
            var scalarProp = new Property { Name = "SqlMigScalar_" + Guid.NewGuid().ToString("N")[..8], Secure = true, IsArray = false };
            var arrayProp = new Property { Name = "SqlMigArray_" + Guid.NewGuid().ToString("N")[..8], Secure = true, IsArray = true };
            context.Properties.AddRange(scalarProp, arrayProp);
            context.SaveChanges();

            var scalarPv = new PropertyValue { Property = scalarProp, Value = legacyEncryptor.EncryptValue("sql-scalar-secret") };
            var arrayPv = new PropertyValue
            {
                Property = arrayProp,
                Value = JsonSerializer.Serialize(new[] { legacyEncryptor.EncryptValue("sql-arr-1"), legacyEncryptor.EncryptValue("sql-arr-2") })
            };
            var cfg = new ConfigValue { Key = "SqlMigCfg_" + Guid.NewGuid().ToString("N")[..8], Secure = true, Value = legacyEncryptor.EncryptValue("sql-cfg-secret") };

            context.PropertyValues.AddRange(scalarPv, arrayPv);
            context.ConfigValues.Add(cfg);
            context.SaveChanges();

            scalarPvId = scalarPv.Id;
            arrayPvId = arrayPv.Id;
            configId = cfg.Id;
        }

        var migrator = new EncryptionMigration(_factory, encryptor, NullLogger.Instance, batchSize: 10);
        var firstPass = migrator.MigratePropertyValues(dryRun: false, force: false);
        Assert.IsTrue(firstPass.Migrated >= 3);
        Assert.AreEqual(0, firstPass.Failed);

        var secondPass = migrator.MigratePropertyValues(dryRun: false, force: false);
        Assert.AreEqual(0, secondPass.Migrated, "Second pass must be idempotent and skip v2 values.");

        using (var context = _factory.GetContext())
        {
            var scalarVal = context.PropertyValues.Single(pv => pv.Id == scalarPvId).Value!;
            StringAssert.StartsWith(scalarVal, "v2:");
            Assert.AreEqual("sql-scalar-secret", encryptor.DecryptValue(scalarVal));

            var arrayVal = context.PropertyValues.Single(pv => pv.Id == arrayPvId).Value!;
            var elements = JsonSerializer.Deserialize<string[]>(arrayVal)!;
            Assert.IsTrue(elements.All(e => e.StartsWith("v2:")));
            CollectionAssert.AreEqual(new[] { "sql-arr-1", "sql-arr-2" }, elements.Select(e => encryptor.DecryptValue(e)).ToArray());

            var cfgVal = context.ConfigValues.Single(c => c.Id == configId).Value!;
            StringAssert.StartsWith(cfgVal, "v2:");
            Assert.AreEqual("sql-cfg-secret", encryptor.DecryptValue(cfgVal));
        }
    }
}
