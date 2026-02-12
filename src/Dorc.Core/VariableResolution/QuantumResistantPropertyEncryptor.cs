using Dorc.PersistentData;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.Core.VariableResolution
{
    public class QuantumResistantPropertyEncryptor : IPropertyEncryptor
    {
        private const string VersionPrefix = "v2:";
        private const string LegacyVersionPrefix = "v1:";
        private const int NonceSizeBytes = 12;
        private const int TagSizeBytes = 16;
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
            catch (FormatException)
            {
                using (var sha256 = SHA256.Create())
                {
                    _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                }
                _legacyEncryptor = new PropertyEncryptor(iv, key);
            }
            catch (ArgumentException)
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

                if (encryptedBytes.Length < NonceSizeBytes + TagSizeBytes)
                    throw new CryptographicException("Invalid encrypted data format");

                var nonce = new byte[NonceSizeBytes];
                var tag = new byte[TagSizeBytes];
                var ciphertext = new byte[encryptedBytes.Length - NonceSizeBytes - TagSizeBytes];

                Array.Copy(encryptedBytes, 0, nonce, 0, NonceSizeBytes);
                Array.Copy(encryptedBytes, NonceSizeBytes, ciphertext, 0, ciphertext.Length);
                Array.Copy(encryptedBytes, NonceSizeBytes + ciphertext.Length, tag, 0, TagSizeBytes);

                using (var aesGcm = new AesGcm(_key, TagSizeBytes))
                {
                    var plaintext = new byte[ciphertext.Length];
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                    return Encoding.UTF8.GetString(plaintext);
                }
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Failed to decrypt value with quantum-resistant algorithm", ex);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("Failed to decrypt value with quantum-resistant algorithm", ex);
            }
        }

        private string EncryptValueV2(string value)
        {
            try
            {
                var plaintext = Encoding.UTF8.GetBytes(value);
                var nonce = new byte[NonceSizeBytes];
                RandomNumberGenerator.Fill(nonce);

                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[TagSizeBytes];

                using (var aesGcm = new AesGcm(_key, TagSizeBytes))
                {
                    aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
                }

                var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
                Array.Copy(nonce, 0, result, 0, nonce.Length);
                Array.Copy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
                Array.Copy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

                return Convert.ToBase64String(result);
            }
            catch (CryptographicException ex)
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
