using Dorc.Core.Interfaces;

namespace Dorc.Api.Windows.Interfaces
{
    /// <summary>
    /// Provides access to directory searchers for different authentication mechanisms
    /// </summary>
    public interface IDirectorySearchProvider
    {
        IActiveDirectorySearcher GetActiveDirectorySearcher();
        IActiveDirectorySearcher GetOAuthDirectorySearcher();
    }
}
