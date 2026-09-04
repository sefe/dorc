using Dorc.Api.Interfaces;
using Dorc.Core;
using Dorc.Core.Account;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Windows;
using Lamar;

namespace Dorc.Api.Services
{
    public class ApiRegistry : ServiceRegistry
    {
        public ApiRegistry()
        {
            For<IPropertiesService>().Use<PropertiesService>();
            For<IPropertyValuesService>().Use<PropertyValuesService>();

            For<IRequestService>().Use<RequestService>();

            For<IDeployableBuildFactory>().Use<DeployableBuildFactory>();
            For<GitHubDeployableBuild>().Use<GitHubDeployableBuild>().Transient();
            For<Func<GitHubDeployableBuild>>().Use(ctx => () => ctx.GetInstance<GitHubDeployableBuild>());

            // Graph is the default directory implementation (HLPS-api-split.md D-2,
            // SPEC-S-001 §2.5). On Windows hosts the on-prem AD searcher is retained as a
            // fallback consulted only when Graph throws; it lives in Dorc.Core.Windows so
            // the primary compile graph stays free of System.DirectoryServices (SC-1).
            For<IActiveDirectorySearcher>().Use(ctx => CreateDirectorySearcher(ctx)).Singleton();
            For<IUserGroupReader>().Use<CachedUserGroupReader>().Singleton();
            For<IDirectorySearchService>().Use<EntraDirectorySearchService>().Scoped();

            For<IFileSystemHelper>().Use<FileSystemHelper>();
            For<IGitHubHostValidator>().Use<GitHubHostValidator>().Singleton();
            For<IBuildServerClientFactory>().Use<BuildServerClientFactory>();
            For<IRequestsManager>().Use<RequestsManager>();
            For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
            For<IApiServices>().Use<ApiServices>();
            For<IManageUsers>().Use<ManageUsers>();
            For<IEnvironmentMapper>().Use<EnvironmentMapper>();
            For<IAccountExistenceChecker>().Use<AccountExistenceChecker>().Scoped();
        }

        private static IActiveDirectorySearcher CreateDirectorySearcher(IServiceContext ctx)
        {
            var config = ctx.GetInstance<IConfigurationSettings>();
            var loggerFactory = ctx.GetInstance<ILoggerFactory>();
            var graphSearcher = new AzureEntraSearcher(config, loggerFactory.CreateLogger<AzureEntraSearcher>());

            if (!OperatingSystem.IsWindows() || !config.GetAdFallbackEnabled())
            {
                return graphSearcher;
            }

            var log = loggerFactory.CreateLogger<FallbackDirectorySearcher>();
            var domainName = config.GetConfigurationDomainNameIntra();
            if (string.IsNullOrWhiteSpace(domainName))
            {
                log.LogWarning(
                    "AD fallback is enabled but AppSettings:DomainNameIntra is not configured; running Graph-only.");
                return graphSearcher;
            }

            try
            {
                var adSearcher = new ActiveDirectorySearcher(
                    domainName, loggerFactory.CreateLogger<ActiveDirectorySearcher>());
                return new FallbackDirectorySearcher(graphSearcher, adSearcher, log);
            }
            catch (Exception ex)
            {
                // Constructing the AD searcher binds to the domain; an unreachable or
                // untrusted domain must not take the whole API down when Graph works.
                log.LogWarning(ex,
                    "Could not initialise the AD fallback searcher for domain {Domain}; running Graph-only.",
                    domainName);
                return graphSearcher;
            }
        }
    }
}
