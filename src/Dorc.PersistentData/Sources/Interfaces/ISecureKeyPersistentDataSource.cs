namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ISecureKeyPersistentDataSource
    {
        string GetInitialisationVector();
        string GetSymmetricKey();
    }
}