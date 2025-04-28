using Dorc.OpenSearchData.Sources.Interfaces;
using Dorc.OpenSearchData.Sources;
using Lamar;
using Microsoft.Extensions.Configuration;
using OpenSearch.Client;

namespace Dorc.OpenSearchData
{
    public class OpenSearchDataRegistry : ServiceRegistry
    {
        public OpenSearchDataRegistry()
        {
            var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetSection("OpenSearchSettings");
            For<IOpenSearchClient>().Use(InitializeOpenSearchLogger(config));

            var deploymentResultIndex = config["DeploymentResultIndex"];
            For<IDeploymentResultLogOsSource>().Use<DeploymentResultLogOsSource>().Ctor<string>("deploymentResultIndex").Is(deploymentResultIndex).Scoped();
        }

        private IOpenSearchClient InitializeOpenSearchLogger(IConfigurationSection config)
        {
            var elasticClientSettings = new ConnectionSettings(new Uri(config["ConnectionUri"]))
                .BasicAuthentication(config["UserName"], config["Password"])
                .DefaultIndex(config["DeploymentResultIndex"]);
            var client = new OpenSearchClient(elasticClientSettings);

            return client;
        }
    }
}
