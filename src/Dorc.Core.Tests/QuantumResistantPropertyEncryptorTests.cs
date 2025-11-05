using Dorc.Core.VariableResolution;
using System.Security.Cryptography;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class QuantumResistantPropertyEncryptorTests
    {
        private QuantumResistantPropertyEncryptor _encryptor;
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
        }

        [TestMethod]
        public void EncryptValue_ReturnsVersionedString()
        {
            var plaintext = "test secret value";
            var encrypted = _encryptor.EncryptValue(plaintext);

            Assert.IsNotNull(encrypted);
            Assert.IsTrue(encrypted.StartsWith("v2:"), "Encrypted value should start with v2: prefix");
        }

        [TestMethod]
        public void DecryptValue_SuccessfullyDecryptsV2Format()
        {
            var plaintext = "test secret value";
            var encrypted = _encryptor.EncryptValue(plaintext);
            var decrypted = _encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void EncryptDecrypt_RoundTrip_MultipleValues()
        {
            var testValues = new[]
            {
                "simple value",
                "value with special chars !@#$%^&*()",
                "value with unicode: こんにちは",
                "very long value " + new string('x', 1000),
                ""
            };

            foreach (var value in testValues)
            {
                var encrypted = _encryptor.EncryptValue(value);
                var decrypted = _encryptor.DecryptValue(encrypted);
                Assert.AreEqual(value, decrypted, $"Failed for value: {value}");
            }
        }

        [TestMethod]
        public void EncryptValue_GeneratesDifferentCiphertexts()
        {
            var plaintext = "test value";
            var encrypted1 = _encryptor.EncryptValue(plaintext);
            var encrypted2 = _encryptor.EncryptValue(plaintext);

            Assert.AreNotEqual(encrypted1, encrypted2, "Same plaintext should produce different ciphertexts due to random nonce");
        }

        [TestMethod]
        public void DecryptValue_HandlesNullValue()
        {
            var result = _encryptor.DecryptValue(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void DecryptValue_SupportsLegacyAESFormat()
        {
            var legacyEncryptor = new PropertyEncryptor(_testIv, _testKey);
            var plaintext = "legacy value";
            var legacyEncrypted = legacyEncryptor.EncryptValue(plaintext);

            var decrypted = _encryptor.DecryptValue(legacyEncrypted);
            Assert.AreEqual(plaintext, decrypted, "Should decrypt legacy AES format");
        }

        [TestMethod]
        public void DecryptValue_SupportsLegacyV1Format()
        {
            var legacyEncryptor = new PropertyEncryptor(_testIv, _testKey);
            var plaintext = "v1 legacy value";
            var legacyEncrypted = "v1:" + legacyEncryptor.EncryptValue(plaintext);

            var decrypted = _encryptor.DecryptValue(legacyEncrypted);
            Assert.AreEqual(plaintext, decrypted, "Should decrypt v1: prefixed legacy format");
        }

        [TestMethod]
        public void MigrateFromLegacy_ConvertsToV2Format()
        {
            var legacyEncryptor = new PropertyEncryptor(_testIv, _testKey);
            var plaintext = "value to migrate";
            var legacyEncrypted = legacyEncryptor.EncryptValue(plaintext);

            var migrated = _encryptor.MigrateFromLegacy(legacyEncrypted);

            Assert.IsTrue(migrated.StartsWith("v2:"), "Migrated value should use v2 format");

            var decrypted = _encryptor.DecryptValue(migrated);
            Assert.AreEqual(plaintext, decrypted, "Migrated value should decrypt correctly");
        }

        [TestMethod]
        public void MigrateFromLegacy_SkipsAlreadyMigrated()
        {
            var plaintext = "already migrated";
            var v2Encrypted = _encryptor.EncryptValue(plaintext);

            var migrated = _encryptor.MigrateFromLegacy(v2Encrypted);

            Assert.AreEqual(v2Encrypted, migrated, "Already migrated values should not be re-encrypted");
        }

        [TestMethod]
        public void MigrateFromLegacy_HandlesNullValue()
        {
            var result = _encryptor.MigrateFromLegacy(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        [ExpectedException(typeof(CryptographicException))]
        public void DecryptValue_ThrowsOnTamperedData()
        {
            var plaintext = "test value";
            var encrypted = _encryptor.EncryptValue(plaintext);

            var tamperedData = encrypted.Substring(0, encrypted.Length - 5) + "XXXXX";

            _encryptor.DecryptValue(tamperedData);
        }

        [TestMethod]
        public void EncryptValue_UsesAuthenticatedEncryption()
        {
            var plaintext = "test value";
            var encrypted = _encryptor.EncryptValue(plaintext);

            var encryptedBytes = Convert.FromBase64String(encrypted.Substring(3));

            Assert.IsTrue(encryptedBytes.Length >= 12 + 16, "Should contain nonce (12) + tag (16) at minimum");
        }

        [TestMethod]
        public void Constructor_HandlesShorterKeyByHashing()
        {
            var shortKey = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
            var encryptor = new QuantumResistantPropertyEncryptor(_testIv, shortKey);

            var plaintext = "test";
            var encrypted = encryptor.EncryptValue(plaintext);
            var decrypted = encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void Constructor_HandlesExactly32ByteKey()
        {
            var key32Bytes = new byte[32];
            RandomNumberGenerator.Fill(key32Bytes);
            var key32 = Convert.ToBase64String(key32Bytes);

            var encryptor = new QuantumResistantPropertyEncryptor(_testIv, key32);

            var plaintext = "test with 32 byte key";
            var encrypted = encryptor.EncryptValue(plaintext);
            var decrypted = encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void Constructor_HandlesLongerKeyByTruncating()
        {
            var longKeyBytes = new byte[64];
            RandomNumberGenerator.Fill(longKeyBytes);
            var longKey = Convert.ToBase64String(longKeyBytes);

            var encryptor = new QuantumResistantPropertyEncryptor(_testIv, longKey);

            var plaintext = "test with long key";
            var encrypted = encryptor.EncryptValue(plaintext);
            var decrypted = encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void DecryptValue_HandlesEmptyString()
        {
            var encrypted = _encryptor.EncryptValue("");
            var decrypted = _encryptor.DecryptValue(encrypted);

            Assert.AreEqual("", decrypted);
        }

        [TestMethod]
        public void EncryptValue_ProducesBase64Output()
        {
            var plaintext = "test";
            var encrypted = _encryptor.EncryptValue(plaintext);
            var base64Part = encrypted.Substring(3);

            try
            {
                Convert.FromBase64String(base64Part);
                Assert.IsTrue(true, "Output should be valid base64");
            }
            catch (FormatException)
            {
                Assert.Fail("Output is not valid base64");
            }
        }

        [TestMethod]
        public void DecryptValue_FailsGracefullyOnCorruptedTag()
        {
            var plaintext = "test";
            var encrypted = _encryptor.EncryptValue(plaintext);
            var bytes = Convert.FromBase64String(encrypted.Substring(3));
            
            bytes[bytes.Length - 1] ^= 0xFF;
            var corrupted = "v2:" + Convert.ToBase64String(bytes);

            try
            {
                _encryptor.DecryptValue(corrupted);
                Assert.Fail("Should throw CryptographicException");
            }
            catch (CryptographicException)
            {
                Assert.IsTrue(true, "Expected exception thrown");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(CryptographicException))]
        public void DecryptValue_ThrowsOnTooShortData()
        {
            var shortData = Convert.ToBase64String(new byte[10]);
            _encryptor.DecryptValue("v2:" + shortData);
        }

        [TestMethod]
        public void EncryptValue_MinimumNonceSize()
        {
            var plaintext = "a";
            var encrypted = _encryptor.EncryptValue(plaintext);
            var bytes = Convert.FromBase64String(encrypted.Substring(3));

            Assert.IsTrue(bytes.Length >= 12 + 1 + 16, "Should have nonce(12) + ciphertext(>=1) + tag(16)");
        }

        [TestMethod]
        public void DecryptValue_PreservesWhitespace()
        {
            var whitespaceValue = "  \t\n  spaces and tabs  \r\n  ";
            var encrypted = _encryptor.EncryptValue(whitespaceValue);
            var decrypted = _encryptor.DecryptValue(encrypted);

            Assert.AreEqual(whitespaceValue, decrypted);
        }

        [TestMethod]
        public void EncryptValue_HandlesLargeData()
        {
            var largeData = new string('A', 10000);
            var encrypted = _encryptor.EncryptValue(largeData);
            var decrypted = _encryptor.DecryptValue(encrypted);

            Assert.AreEqual(largeData, decrypted);
        }

        [TestMethod]
        public void Constructor_HandlesInvalidKeyGracefully()
        {
            var encryptor = new QuantumResistantPropertyEncryptor("invalid", "invalid");
            
            var plaintext = "test";
            var encrypted = encryptor.EncryptValue(plaintext);
            var decrypted = encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void EncryptValue_UniqueNoncePerEncryption()
        {
            var plaintext = "test";
            var encrypted1 = _encryptor.EncryptValue(plaintext);
            var encrypted2 = _encryptor.EncryptValue(plaintext);
            var encrypted3 = _encryptor.EncryptValue(plaintext);

            var bytes1 = Convert.FromBase64String(encrypted1.Substring(3));
            var bytes2 = Convert.FromBase64String(encrypted2.Substring(3));
            var bytes3 = Convert.FromBase64String(encrypted3.Substring(3));

            var nonce1 = bytes1.Take(12).ToArray();
            var nonce2 = bytes2.Take(12).ToArray();
            var nonce3 = bytes3.Take(12).ToArray();

            Assert.IsFalse(nonce1.SequenceEqual(nonce2), "Nonces should be unique");
            Assert.IsFalse(nonce2.SequenceEqual(nonce3), "Nonces should be unique");
            Assert.IsFalse(nonce1.SequenceEqual(nonce3), "Nonces should be unique");
        }

        [TestMethod]
        public void QuantumResistantEncryption_UsesAesGcm256()
        {
            var plaintext = "quantum resistant test";
            var encrypted = _encryptor.EncryptValue(plaintext);
            
            Assert.IsTrue(encrypted.StartsWith("v2:"), "Should use v2 format");
            
            var decrypted = _encryptor.DecryptValue(encrypted);
            Assert.AreEqual(plaintext, decrypted, "AES-GCM-256 should decrypt correctly");
        }
    }
}
