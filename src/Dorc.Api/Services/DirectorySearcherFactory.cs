using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.IdentityServer;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Services
{
    public class DirectorySearcherFactory : IDirectorySearcherFactory
    {
        private readonly ActiveDirectorySearcher _adSearcher;
        private readonly AzureEntraSearcher _azEntraSearcher;
        private readonly IActiveDirectorySearcher _oauthDirectorySearcher;

        public DirectorySearcherFactory(IConfigurationSettings config,
            IMemoryCache cache,
            IConfigurationSecretsReader secretsReader,
            ILoggerFactory loggerFactory)
        {
            _adSearcher = new ActiveDirectorySearcher(config.GetConfigurationDomainNameIntra(), loggerFactory.CreateLogger<ActiveDirectorySearcher>());
            _azEntraSearcher = new AzureEntraSearcher(config, loggerFactory.CreateLogger<AzureEntraSearcher>());

            var searchersList = new List<IActiveDirectorySearcher>();
            if (config.GetIsUseAdAsSearcher() == true)
            {
                searchersList.Add(_adSearcher);
            }
            else
            {
                var identityServerSearcher = new IdentityServerSearcher(config, secretsReader, loggerFactory.CreateLogger<IdentityServerSearcher>(), loggerFactory);
                searchersList.Add(identityServerSearcher);
                searchersList.Add(_azEntraSearcher);
            }

            var compositeOauthSearcher = new CompositeActiveDirectorySearcher(
                    searchersList,
                    loggerFactory.CreateLogger<CompositeActiveDirectorySearcher>());

            _oauthDirectorySearcher = compositeOauthSearcher;
        }

        public IActiveDirectorySearcher GetActiveDirectorySearcher()
        {
            return _adSearcher;
        }

        public IActiveDirectorySearcher GetEntraSearcher()
        {
            return _azEntraSearcher;
        }

        public IActiveDirectorySearcher GetOAuthDirectorySearcher()
        {
            return _oauthDirectorySearcher;
        }
    }
}
