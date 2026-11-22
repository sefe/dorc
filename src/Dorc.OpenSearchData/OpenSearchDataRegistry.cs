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
            var fallbackConfigRoot = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            For<IOpenSearchClient>().Use(ctx =>
            {
                var configRoot = ctx.TryGetInstance<IConfigurationRoot>() ?? fallbackConfigRoot;
                return InitializeOpenSearchLogger(configRoot.GetSection("OpenSearchSettings"));
            });

            For<IDeploymentLogService>().Use(ctx =>
            {
                var configRoot = ctx.TryGetInstance<IConfigurationRoot>() ?? fallbackConfigRoot;
                var deploymentResultIndex = configRoot.GetSection("OpenSearchSettings")["DeploymentResultIndex"];
                if (String.IsNullOrEmpty(deploymentResultIndex))
                    throw new Exception("'OpenSearchSettings.DeploymentResultIndex' not set in the Runner appsettings");
                return new DeploymentLogService(
                    ctx.GetInstance<IOpenSearchClient>(),
                    ctx.GetInstance<Microsoft.Extensions.Logging.ILogger<DeploymentLogService>>(),
                    deploymentResultIndex);
            }).Scoped();
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
