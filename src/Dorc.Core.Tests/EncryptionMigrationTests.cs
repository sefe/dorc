using Dorc.Core.VariableResolution;
using NSubstitute;
using System.Security.Cryptography;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EncryptionMigrationTests
    {
        private AesGcmPropertyEncryptor _encryptor;
        private PropertyEncryptor _legacyEncryptor;
        private string _testKey;
        private string _testIv;

        [TestInitialize]
        public void Setup()
        {
            var keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            _testKey = Convert.ToBase64String(keyBytes);

            var ivBytes = new byte[16];
            RandomNumberGenerator.Fill(ivBytes);
            _testIv = Convert.ToBase64String(ivBytes);

            _encryptor = new AesGcmPropertyEncryptor(_testIv, _testKey);
            _legacyEncryptor = new PropertyEncryptor(_testIv, _testKey);
        }

        [TestMethod]
        public void MigrateFromLegacy_ConvertsUnversionedToV2()
        {
            var plaintext = "secret to migrate";
            var legacyEncrypted = _legacyEncryptor.EncryptValue(plaintext);

            var migrated = _encryptor.MigrateFromLegacy(legacyEncrypted);

            Assert.IsTrue(migrated.StartsWith("v2:"), "Should add v2 prefix");
            
            var decrypted = _encryptor.DecryptValue(migrated);
            Assert.AreEqual(plaintext, decrypted, "Should decrypt to original value");
        }

        [TestMethod]
        public void MigrateFromLegacy_ConvertsV1ToV2()
        {
            var plaintext = "v1 secret";
            var legacyEncrypted = "v1:" + _legacyEncryptor.EncryptValue(plaintext);

            var migrated = _encryptor.MigrateFromLegacy(legacyEncrypted);

            Assert.IsTrue(migrated.StartsWith("v2:"), "Should convert v1 to v2");
            
            var decrypted = _encryptor.DecryptValue(migrated);
            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void MigrateFromLegacy_PreservesV2Values()
        {
            var plaintext = "already v2";
            var v2Encrypted = _encryptor.EncryptValue(plaintext);

            var migrated = _encryptor.MigrateFromLegacy(v2Encrypted);

            Assert.AreEqual(v2Encrypted, migrated, "V2 values should not be re-encrypted");
        }

        [TestMethod]
        public void MigrateFromLegacy_HandlesMultipleValues()
        {
            var testCases = new[]
            {
                "password123",
                "connection-string",
                "api-key-secret",
                "token-value",
                "certificate-data"
            };

            foreach (var plaintext in testCases)
            {
                var legacyEncrypted = _legacyEncryptor.EncryptValue(plaintext);
                var migrated = _encryptor.MigrateFromLegacy(legacyEncrypted);
                
                Assert.IsTrue(migrated.StartsWith("v2:"));
                
                var decrypted = _encryptor.DecryptValue(migrated);
                Assert.AreEqual(plaintext, decrypted, $"Failed for: {plaintext}");
            }
        }

        [TestMethod]
        public void MigrateFromLegacy_NullValueReturnsNull()
        {
            var result = _encryptor.MigrateFromLegacy(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void BackwardCompatibility_DecryptsAllFormats()
        {
            var plaintext = "test value";

            var unversioned = _legacyEncryptor.EncryptValue(plaintext);
            var v1Prefixed = "v1:" + _legacyEncryptor.EncryptValue(plaintext);
            var v2Format = _encryptor.EncryptValue(plaintext);

            Assert.AreEqual(plaintext, _encryptor.DecryptValue(unversioned), "Should decrypt unversioned");
            Assert.AreEqual(plaintext, _encryptor.DecryptValue(v1Prefixed), "Should decrypt v1:");
            Assert.AreEqual(plaintext, _encryptor.DecryptValue(v2Format), "Should decrypt v2:");
        }

        [TestMethod]
        public void Migration_IdempotentForV2Values()
        {
            var plaintext = "test";
            var v2Value = _encryptor.EncryptValue(plaintext);

            var firstMigration = _encryptor.MigrateFromLegacy(v2Value);
            var secondMigration = _encryptor.MigrateFromLegacy(firstMigration);

            Assert.AreEqual(firstMigration, secondMigration, "Multiple migrations should be idempotent");
        }

        [TestMethod]
        public void DecryptValue_DifferentiatesVersionsByPrefix()
        {
            var plaintext = "versioning test";

            var unversioned = _legacyEncryptor.EncryptValue(plaintext);
            Assert.IsFalse(unversioned.StartsWith("v1:") || unversioned.StartsWith("v2:"));

            var v2 = _encryptor.EncryptValue(plaintext);
            Assert.IsTrue(v2.StartsWith("v2:"));
        }

        [TestMethod]
        public void MigrateFromLegacy_PreservesDataIntegrity()
        {
            var complexData = "username:admin;password:P@ssw0rd!;token:abc123;expires:2025-12-31";
            var legacyEncrypted = _legacyEncryptor.EncryptValue(complexData);

            var migrated = _encryptor.MigrateFromLegacy(legacyEncrypted);
            var decrypted = _encryptor.DecryptValue(migrated);

            Assert.AreEqual(complexData, decrypted, "Complex data should be preserved exactly");
        }

        [TestMethod]
        public void EncryptionMigration_MigratePropertyValues_MigratesScalarArrayAndConfigValues()
        {
            var propScalar = new Dorc.PersistentData.Model.Property { Id = 1, Name = "ScalarSecret", Secure = true, IsArray = false };
            var propArray = new Dorc.PersistentData.Model.Property { Id = 2, Name = "ArraySecret", Secure = true, IsArray = true };

            var legacyScalar = _legacyEncryptor.EncryptValue("scalar-val");
            var v2Scalar = _encryptor.EncryptValue("already-v2");
            var v1Scalar = "v1:" + _legacyEncryptor.EncryptValue("v1-val");

            var legacyArrayJson = System.Text.Json.JsonSerializer.Serialize(new string?[]
            {
                _legacyEncryptor.EncryptValue("arr-1"),
                _encryptor.EncryptValue("arr-2-v2"),
                "v1:" + _legacyEncryptor.EncryptValue("arr-3-v1"),
                null
            });
            var v2ArrayJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                _encryptor.EncryptValue("v2-a"),
                _encryptor.EncryptValue("v2-b")
            });
            var v1ArrayJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "v1:" + _legacyEncryptor.EncryptValue("v1-a"),
                _encryptor.EncryptValue("v2-b")
            });

            var properties = new List<Dorc.PersistentData.Model.Property> { propScalar, propArray };
            var propertyValues = new List<Dorc.PersistentData.Model.PropertyValue>
            {
                new() { Id = 10, Property = propScalar, Value = legacyScalar },
                new() { Id = 11, Property = propScalar, Value = v2Scalar },
                new() { Id = 12, Property = propScalar, Value = v1Scalar },
                new() { Id = 13, Property = propScalar, Value = "invalid-base64-!!!" },
                new() { Id = 20, Property = propArray, Value = legacyArrayJson },
                new() { Id = 21, Property = propArray, Value = v2ArrayJson },
                new() { Id = 22, Property = propArray, Value = v1ArrayJson },
                new() { Id = 23, Property = propArray, Value = "null" }
            };

            var configValues = new List<Dorc.PersistentData.Model.ConfigValue>
            {
                new() { Id = 100, Key = "CfgLegacy", Secure = true, Value = _legacyEncryptor.EncryptValue("cfg-legacy") },
                new() { Id = 101, Key = "CfgV2", Secure = true, Value = _encryptor.EncryptValue("cfg-v2") },
                new() { Id = 102, Key = "CfgV1", Secure = true, Value = "v1:" + _legacyEncryptor.EncryptValue("cfg-v1") },
                new() { Id = 103, Key = "CfgBad", Secure = true, Value = "invalid-base64-!!!" }
            };

            var propertiesDbSet = CreateQueryableDbSet(properties);
            var propertyValuesDbSet = CreateQueryableDbSet(propertyValues);
            var configValuesDbSet = CreateQueryableDbSet(configValues);
            var context = NSubstitute.Substitute.For<Dorc.PersistentData.Contexts.IDeploymentContext>();
            context.Properties.Returns(propertiesDbSet);
            context.PropertyValues.Returns(propertyValuesDbSet);
            context.ConfigValues.Returns(configValuesDbSet);

            var contextFactory = NSubstitute.Substitute.For<Dorc.PersistentData.Contexts.IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(context);

            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger>();
            var migrator = new Tools.EncryptionMigrationCLI.EncryptionMigration(contextFactory, _encryptor, logger, batchSize: 1);

            // 1. Dry run without force
            var dryRunResult = migrator.MigratePropertyValues(dryRun: true, force: false);
            Assert.AreEqual(12, dryRunResult.Total);
            Assert.AreEqual(3, dryRunResult.Migrated);
            Assert.AreEqual(7, dryRunResult.Skipped);
            Assert.AreEqual(2, dryRunResult.Failed);
            Assert.AreEqual(legacyScalar, propertyValues[0].Value, "Dry run must not mutate values");

            // 2. Live run without force (batchSize=1 exercises batch saving)
            var liveResult = migrator.MigratePropertyValues(dryRun: false, force: false);
            Assert.AreEqual(3, liveResult.Migrated);
            Assert.AreEqual(7, liveResult.Skipped);
            Assert.AreEqual(2, liveResult.Failed);
            Assert.IsTrue(propertyValues[0].Value.StartsWith("v2:"));
            Assert.AreEqual("scalar-val", _encryptor.DecryptValue(propertyValues[0].Value));

            var migratedArray = System.Text.Json.JsonSerializer.Deserialize<string?[]>(propertyValues[4].Value)!;
            Assert.IsTrue(migratedArray[0]!.StartsWith("v2:"));
            Assert.AreEqual("arr-1", _encryptor.DecryptValue(migratedArray[0]!));
            Assert.IsTrue(migratedArray[2]!.StartsWith("v1:"), "Unforced v1 array element should stay v1");
            Assert.IsNull(migratedArray[3]);
            Assert.IsTrue(configValues[0].Value.StartsWith("v2:"));

            // 3. Forced live run with larger batchSize exercises final-batch saving and v1 -> v2 migration
            var migratorFinalBatch = new Tools.EncryptionMigrationCLI.EncryptionMigration(contextFactory, _encryptor, logger, batchSize: 50);
            var forcedResult = migratorFinalBatch.MigratePropertyValues(dryRun: false, force: true);
            Assert.AreEqual(4, forcedResult.Migrated);
            Assert.IsTrue(propertyValues[2].Value.StartsWith("v2:"));
            Assert.IsTrue(configValues[2].Value.StartsWith("v2:"));
        }

        private static Microsoft.EntityFrameworkCore.DbSet<T> CreateQueryableDbSet<T>(List<T> items) where T : class
        {
            var queryable = items.AsQueryable();
            var dbSet = NSubstitute.Substitute.For<Microsoft.EntityFrameworkCore.DbSet<T>, IQueryable<T>>();
            ((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
            ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
            ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
            ((IQueryable<T>)dbSet).GetEnumerator().Returns(_ => queryable.GetEnumerator());
            return dbSet;
        }
    }
}
