using Dorc.Api.Windows.Interfaces;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Windows.Windows.Services
{
    public class UserGroupReaderFactory : IUserGroupsReaderFactory
    {
        private readonly CachedUserGroupReader _winUserGroupReader;
        private readonly CachedUserGroupReader _oauthUserGroupsReader;

        public UserGroupReaderFactory(IConfigurationSettings config,
            IMemoryCache cache,
            IDirectorySearcherFactory searcherFactory)
        {
            _winUserGroupReader = new CachedUserGroupReader(config, cache, searcherFactory.GetActiveDirectorySearcher());
            _oauthUserGroupsReader = new CachedUserGroupReader(config, cache, searcherFactory.GetOAuthDirectorySearcher());
        }

        public IUserGroupReader GetWinAuthUserGroupsReader()
        {
            return _winUserGroupReader;
        }

        public IUserGroupReader GetOAuthUserGroupsReader()
        {
            return _oauthUserGroupsReader;
        }
    }
}
