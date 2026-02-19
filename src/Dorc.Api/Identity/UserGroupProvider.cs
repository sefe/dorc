using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Identity
{
    public class UserGroupProvider : IUserGroupProvider
    {
        private readonly CachedUserGroupReader _winUserGroupReader;
        private readonly CachedUserGroupReader _oauthUserGroupsReader;

        public UserGroupProvider(IConfigurationSettings config,
            IMemoryCache cache,
            IDirectorySearchProvider searcherFactory)
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
