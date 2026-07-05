using Dorc.Core.VariableResolution;
using System.Security.Cryptography;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class PropertyEncryptorTests
    {
        private PropertyEncryptor _encryptor;
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

            _encryptor = new PropertyEncryptor(_testIv, _testKey);
        }

        [TestMethod]
        public void EncryptValue_ReturnsNonEmptyString()
        {
            var plaintext = "test secret";
            var encrypted = _encryptor.EncryptValue(plaintext);

            Assert.IsNotNull(encrypted);
            Assert.IsTrue(encrypted.Length > 0);
        }

        [TestMethod]
        public void DecryptValue_SuccessfullyDecryptsLegacyAES()
        {
            var plaintext = "legacy test value";
            var encrypted = _encryptor.EncryptValue(plaintext);
            var decrypted = _encryptor.DecryptValue(encrypted);

            Assert.AreEqual(plaintext, decrypted);
        }

        [TestMethod]
        public void EncryptDecrypt_RoundTrip_LegacyFormat()
        {
            var testValues = new[]
            {
                "simple",
                "with spaces and special !@#",
                "unicode: 日本語",
                new string('x', 500),
                ""
            };

            foreach (var value in testValues)
            {
                var encrypted = _encryptor.EncryptValue(value);
                var decrypted = _encryptor.DecryptValue(encrypted);
                Assert.AreEqual(value, decrypted, $"Failed for: {value}");
            }
        }

        [TestMethod]
        public void DecryptValue_HandlesNull()
        {
            var result = _encryptor.DecryptValue(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void DecryptValue_ThrowsOnInvalidBase64()
        {
            Assert.Throws<ApplicationException>(() => _encryptor.DecryptValue("not valid base64!!!"));
        }

        [TestMethod]
        public void Constructor_WithInvalidIvOrKey_DoesNotThrow()
        {
            // Construction must not throw, so a deployment that never touches
            // legacy values is unaffected by a malformed legacy IV/Key.
            var encryptor = new PropertyEncryptor("invalid-iv", "invalid-key");
            Assert.IsNotNull(encryptor);
        }

        [TestMethod]
        public void DecryptValue_WithInvalidIvOrKey_FailsLoudly_InsteadOfSilentRandomKey()
        {
            // Previously the constructor silently substituted a fresh random key,
            // producing undecryptable ciphertext and masking misconfiguration.
            // It must now fail loudly with a clear message when actually used.
            var encryptor = new PropertyEncryptor("invalid-iv", "invalid-key");

            var ex = Assert.Throws<InvalidOperationException>(
                () => encryptor.DecryptValue("v1data"));
            Assert.IsTrue(ex.Message.Contains("Legacy PropertyEncryptor could not be initialised"));
        }

        [TestMethod]
        public void EncryptValue_WithInvalidIvOrKey_FailsLoudly()
        {
            var encryptor = new PropertyEncryptor("invalid-iv", "invalid-key");

            Assert.Throws<InvalidOperationException>(
                () => encryptor.EncryptValue("anything"));
        }
    }
}
