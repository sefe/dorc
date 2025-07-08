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
        private readonly IActiveDirectorySearcher _oauthDirectorySearcher;

        public DirectorySearcherFactory(IConfigurationSettings config,
            IMemoryCache cache,
            IConfigurationSecretsReader secretsReader,
            ILog log)
        {
            _adSearcher = new ActiveDirectorySearcher(config.GetConfigurationDomainNameIntra(), log);
            var azEntraSearcher = new AzureEntraSearcher(config, log);

            var searchersList = new List<IActiveDirectorySearcher>();
            if (config.GetIsUseAdAsSearcher() == true)
            {
                searchersList.Add(_adSearcher);
            }
            else
            {
                var identityServerSearcher = new IdentityServerSearcher(config, secretsReader, log);
                searchersList.Add(identityServerSearcher);
                searchersList.Add(azEntraSearcher);
            }

            var compositeOauthSearcher = new CompositeActiveDirectorySearcher(
                    searchersList,
                    log);

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
