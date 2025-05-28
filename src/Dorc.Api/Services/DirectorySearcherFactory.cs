using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.IdentityServer;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Services
{
    public class DirectorySearcherFactory : IDirectorySearcherFactory
    {
        private readonly ActiveDirectorySearcher _adSearcher;
        private readonly CompositeActiveDirectorySearcher _oauthDirectorySearcher;

        public DirectorySearcherFactory(IConfigurationSettings config,
            IMemoryCache cache,
            IConfigurationSecretsReader secretsReader,
            ILog log)
        {
            var adSearcher = new ActiveDirectorySearcher(config.GetConfigurationDomainNameIntra(), log);
            var azEntraSearcher = new AzureEntraSearcher(config, log);
            var identityServerSearcher = new IdentityServerSearcher(config, secretsReader, log);
            var compositeOauthSearcher = new CompositeActiveDirectorySearcher(
                new List<IActiveDirectorySearcher> { azEntraSearcher, identityServerSearcher },
                log);

            _adSearcher = adSearcher;
            _oauthDirectorySearcher = compositeOauthSearcher;
        }

        public IActiveDirectorySearcher GetActiveDirectorySearcher()
        {
            return _adSearcher;
        }

        public IActiveDirectorySearcher GetOAuthDirectorySearcher()
        {
            return _oauthDirectorySearcher;
        }
    }
}
