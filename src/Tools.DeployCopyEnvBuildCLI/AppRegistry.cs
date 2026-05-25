using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Tools.DeployCopyEnvBuildCLI
{
    public class AppRegistry : ServiceRegistry
    {
        public AppRegistry()
        {
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            For<IConfiguration>().Use(configBuilder);
            For<ILoggerFactory>().Use(_ => LoggerFactory.Create(builder => builder.AddConsole()));
            For(typeof(ILogger<>)).Use(typeof(Logger<>));

            For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            For<IDeploymentEventsPublisher>().Use<NullDeploymentEventsPublisher>();

            // Add HTTP client support for BuildServerClientFactory
            this.AddHttpClient();
            this.AddHttpClient("GitHubActions", client =>
            {
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("DORC", "1.0"));
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Register missing dependencies
            For<IGitHubHostValidator>().Use<GitHubHostValidator>().Singleton();
            For<IBuildServerClientFactory>().Use<BuildServerClientFactory>();
        }
    }
}
