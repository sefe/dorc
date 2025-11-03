using Dorc.PersistentData;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.Core.VariableResolution
{
    public class QuantumResistantPropertyEncryptor : IPropertyEncryptor
    {
        private const string VersionPrefix = "v2:";
        private const string LegacyVersionPrefix = "v1:";
        private readonly byte[] _key;
        private readonly PropertyEncryptor _legacyEncryptor;

        public QuantumResistantPropertyEncryptor(string iv, string key)
        {
            try
            {
                var keyBytes = Convert.FromBase64String(key);
                if (keyBytes.Length < 32)
                {
                    using (var sha256 = SHA256.Create())
                    {
                        _key = sha256.ComputeHash(keyBytes);
                    }
                }
                else
                {
                    _key = new byte[32];
                    Array.Copy(keyBytes, _key, 32);
                }
                
                _legacyEncryptor = new PropertyEncryptor(iv, key);
            }
            catch (Exception)
            {
                using (var sha256 = SHA256.Create())
                {
                    _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                }
                _legacyEncryptor = new PropertyEncryptor(iv, key);
            }
        }

        public string? DecryptValue(string value)
        {
            if (value == null) return null;

            if (value.StartsWith(VersionPrefix))
            {
                return DecryptValueV2(value.Substring(VersionPrefix.Length));
            }
            else if (value.StartsWith(LegacyVersionPrefix))
            {
                return _legacyEncryptor.DecryptValue(value.Substring(LegacyVersionPrefix.Length));
            }
            else
            {
                return _legacyEncryptor.DecryptValue(value);
            }
        }

        public string EncryptValue(string value)
        {
            return VersionPrefix + EncryptValueV2(value);
        }

        private string DecryptValueV2(string encryptedValue)
        {
            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedValue);

                if (encryptedBytes.Length < 12 + 16)
                    throw new CryptographicException("Invalid encrypted data format");

                var nonce = new byte[12];
                var tag = new byte[16];
                var ciphertext = new byte[encryptedBytes.Length - 12 - 16];

                Array.Copy(encryptedBytes, 0, nonce, 0, 12);
                Array.Copy(encryptedBytes, 12, ciphertext, 0, ciphertext.Length);
                Array.Copy(encryptedBytes, 12 + ciphertext.Length, tag, 0, 16);

                using (var aesGcm = new AesGcm(_key, 16))
                {
                    var plaintext = new byte[ciphertext.Length];
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                    return Encoding.UTF8.GetString(plaintext);
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decrypt value with quantum-resistant algorithm", ex);
            }
        }

        private string EncryptValueV2(string value)
        {
            try
            {
                var plaintext = Encoding.UTF8.GetBytes(value);
                var nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);

                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[16];

                using (var aesGcm = new AesGcm(_key, 16))
                {
                    aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
                }

                var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
                Array.Copy(nonce, 0, result, 0, nonce.Length);
                Array.Copy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
                Array.Copy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

                return Convert.ToBase64String(result);
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to encrypt value with quantum-resistant algorithm", ex);
            }
        }

        public string? MigrateFromLegacy(string? legacyEncryptedValue)
        {
            if (legacyEncryptedValue == null) return null;
            
            if (legacyEncryptedValue.StartsWith(VersionPrefix))
            {
                return legacyEncryptedValue;
            }

            var decryptedValue = legacyEncryptedValue.StartsWith(LegacyVersionPrefix) 
                ? _legacyEncryptor.DecryptValue(legacyEncryptedValue.Substring(LegacyVersionPrefix.Length))
                : _legacyEncryptor.DecryptValue(legacyEncryptedValue);

            if (decryptedValue == null) return null;

            return EncryptValue(decryptedValue);
        }
    }
}
