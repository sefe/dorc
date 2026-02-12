using Dorc.Core.VariableResolution;
using System.Security.Cryptography;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EncryptionMigrationTests
    {
        private QuantumResistantPropertyEncryptor _encryptor;
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

            _encryptor = new QuantumResistantPropertyEncryptor(_testIv, _testKey);
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
    }
}
