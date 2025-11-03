namespace Dorc.PersistentData
{
    public static class PropertyValueMasking
    {
        public static string GenerateMaskedValue(string? encryptedValue, IPropertyEncryptor? encryptor)
        {
            if (string.IsNullOrEmpty(encryptedValue))
                return "********";

            int length = 8;

            if (encryptor != null)
            {
                try
                {
                    var decryptedValue = encryptor.DecryptValue(encryptedValue);
                    if (!string.IsNullOrEmpty(decryptedValue))
                    {
                        length = Math.Min(Math.Max(decryptedValue.Length, 8), 32);
                    }
                }
                catch
                {
                    length = 12;
                }
            }
            else
            {
                length = 12;
            }

            return new string('*', length);
        }
    }
}
