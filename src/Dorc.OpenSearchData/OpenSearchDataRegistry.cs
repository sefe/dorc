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

            if (String.IsNullOrEmpty(deploymentResultIndex))
                throw new Exception("'OpenSearchSettings.DeploymentResultIndex' not set in the Runner appsettings");

            For<IDeploymentLogService>().Use<DeploymentLogService>().Ctor<string>("deploymentResultIndex").Is(deploymentResultIndex).Scoped();
        }

        private IOpenSearchClient InitializeOpenSearchLogger(IConfigurationSection config)
        {
            var connectionUri = config["ConnectionUri"];
            if (String.IsNullOrEmpty(connectionUri))
                throw new Exception("'OpenSearchSettings.ConnectionUri' not set in the DOrc appsettings");

            var userName = config["UserName"];
            if (String.IsNullOrEmpty(userName))
                throw new Exception("'OpenSearchSettings.UserName' not set in the DOrc appsettings");

            var password = config["Password"];
            if (String.IsNullOrEmpty(password))
                throw new Exception("'OpenSearchSettings.Password' not set in the DOrc appsettings");

            var deploymentResultIndex = config["DeploymentResultIndex"];
            if (String.IsNullOrEmpty(deploymentResultIndex))
                throw new Exception("'OpenSearchSettings.DeploymentResultIndex' not set in the DOrc appsettings");

            var openSearchClientSettings = new ConnectionSettings(new Uri(connectionUri))
                .BasicAuthentication(userName, password)
                .DefaultIndex(deploymentResultIndex)
                .PrettyJson();
            var client = new OpenSearchClient(openSearchClientSettings);

            return client;
        }
    }
}
