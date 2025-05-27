using Dorc.Api.Interfaces;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Services
{
    public class UserGroupReaderFactory : IUserGroupsReaderFactory
    {
        private readonly CachedUserGroupReader _winUserGroupReader;
        private readonly CachedUserGroupReader _oauthUserGroupsReader;

        public UserGroupReaderFactory(IConfigurationSettings config,
            IMemoryCache cache,
            ILog log)
        {
            var adSearcher = new ActiveDirectorySearcher(config.GetConfigurationDomainNameIntra(), log);
            var azEntraSearcher = new AzureEntraSearcher(config, log);

            _winUserGroupReader = new CachedUserGroupReader(config, cache, adSearcher);
            _oauthUserGroupsReader = new CachedUserGroupReader(config, cache, azEntraSearcher);
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
