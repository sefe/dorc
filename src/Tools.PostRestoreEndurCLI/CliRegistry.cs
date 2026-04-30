using System;
using Dorc.Core;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tools.PostRestoreEndurCLI
{
    public class CliRegistry : ServiceRegistry
    {
        public CliRegistry()
        {
            try
            {
                For<ILoggerFactory>().Use(_ => LoggerFactory.Create(builder => builder.AddConsole()));
                For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("PostRestoreEndurCLI"));
                For(typeof(ILogger<>)).Use(typeof(Logger<>));

                var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true).Build();
                For<IConfiguration>().Use(configuration);
                this.AddHttpClient();
                this.AddHttpClient("GitHubActions", client =>
                {
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("DORC", "1.0"));
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                    client.Timeout = TimeSpan.FromSeconds(30);
                });
                For<IGitHubHostValidator>().Use<GitHubHostValidator>().Singleton();
                For<IBuildServerClientFactory>().Use<BuildServerClientFactory>();

                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
                For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error in CliRegistry: {e}");
                throw;
            }
        }
    }
}