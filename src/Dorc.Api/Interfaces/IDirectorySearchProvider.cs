using Dorc.Core;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Interfaces
{
    public interface IDirectorySearchProvider
    {
        IActiveDirectorySearcher GetActiveDirectorySearcher();
        IActiveDirectorySearcher GetOAuthDirectorySearcher();
    }
}
