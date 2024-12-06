namespace Dorc.PersistentData
{
    public interface IPropertyEncryptor
    {
        string? DecryptValue(string value);
        string EncryptValue(string value);
    }
}