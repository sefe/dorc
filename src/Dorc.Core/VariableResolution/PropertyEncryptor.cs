using Dorc.PersistentData;
using System.Security.Cryptography;

namespace Dorc.Core.VariableResolution
{
    public class PropertyEncryptor : IPropertyEncryptor
    {
        private readonly Aes _provider;

        public PropertyEncryptor(string iv, string key)
        {
            try
            {
                _provider = Aes.Create();
                _provider.IV = Convert.FromBase64String(iv);
                _provider.Key = Convert.FromBase64String(key);
            }
            catch (Exception)
            {
                _provider = Aes.Create();
            }
        }

        public string? DecryptValue(string value)
        {
            if (value == null) return null;
            var decryptor = _provider.CreateDecryptor();

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
            var encryptor = _provider.CreateEncryptor();

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