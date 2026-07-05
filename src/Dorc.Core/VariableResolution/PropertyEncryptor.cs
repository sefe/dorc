using Dorc.PersistentData;
using System.Security.Cryptography;

namespace Dorc.Core.VariableResolution
{
    public class PropertyEncryptor : IPropertyEncryptor
    {
        private readonly Aes? _provider;
        private readonly string? _initializationError;

        public PropertyEncryptor(string iv, string key)
        {
            try
            {
                var provider = Aes.Create();
                provider.IV = Convert.FromBase64String(iv);
                provider.Key = Convert.FromBase64String(key);
                _provider = provider;
            }
            catch (Exception ex)
            {
                // Do NOT silently fall back to a fresh random key. The old
                // behaviour (`_provider = Aes.Create()`) produced ciphertext that
                // could never be decrypted after a restart and masked a genuine
                // misconfiguration. Instead, record the failure and fail loudly
                // with a clear message if — and only if — this legacy encryptor is
                // actually used. Construction itself must not throw, so that a
                // deployment which never touches legacy (v1/unversioned) values is
                // unaffected by a malformed legacy IV/Key.
                _initializationError =
                    "Legacy PropertyEncryptor could not be initialised from the configured IV/Key. "
                    + "Legacy (v1) encrypted values cannot be decrypted until this is corrected. "
                    + "Underlying error: " + ex.Message;
            }
        }

        private Aes Provider =>
            _provider ?? throw new InvalidOperationException(_initializationError);

        public string? DecryptValue(string value)
        {
            if (value == null) return null;
            var decryptor = Provider.CreateDecryptor();

            Span<byte> buffer = new Span<byte>(new byte[value.Length]);
            if (!Convert.TryFromBase64String(value, buffer, out int bytesParsed))
                throw new ApplicationException($"Unable to convert {value} to base64 encoded string");

            using (var memoryStream = new MemoryStream(buffer.Slice(0, bytesParsed).ToArray()))
            {
                using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                {
                    using (var reader = new StreamReader(cryptoStream))
                    {
                        var unencryptedString = reader.ReadToEnd();
                        return unencryptedString;
                    }
                }
            }
        }

        public string EncryptValue(string value)
        {
            var encryptor = Provider.CreateEncryptor();

            using (var memoryStream = new MemoryStream())
            {
                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    using (var writer = new StreamWriter(cryptoStream))
                    {
                        writer.Write(value);
                    }

                    var encrypted = memoryStream.ToArray();
                    return Convert.ToBase64String(encrypted);
                }
            }
        }
    }
}