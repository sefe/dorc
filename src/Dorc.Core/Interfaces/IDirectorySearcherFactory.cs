namespace Dorc.Core.Interfaces
{
    public interface IDirectorySearcherFactory
    {
        IActiveDirectorySearcher GetActiveDirectorySearcher();
        IActiveDirectorySearcher GetEntraSearcher();
        IActiveDirectorySearcher GetOAuthDirectorySearcher();
    }
}
