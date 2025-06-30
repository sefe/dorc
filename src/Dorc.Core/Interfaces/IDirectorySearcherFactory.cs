namespace Dorc.Core.Interfaces
{
    public interface IDirectorySearcherFactory
    {
        IActiveDirectorySearcher GetActiveDirectorySearcher();
        IActiveDirectorySearcher GetOAuthDirectorySearcher();
    }
}
