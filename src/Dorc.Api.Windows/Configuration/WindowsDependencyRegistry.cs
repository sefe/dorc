using Dorc.Api.Windows.Identity;
using Dorc.Api.Windows.Deployment;
using Dorc.Api.Windows.Interfaces;
using Dorc.Core;
using Dorc.Core.Account;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Lamar;
using System.DirectoryServices;
using System.Runtime.Versioning;

namespace Dorc.Api.Windows.Configuration
{
    /// <summary>
    /// Configures dependency injection for Windows-specific components
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsDependencyRegistry : ServiceRegistry
    {
        public WindowsDependencyRegistry()
        {
            var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            var configSettings = new ConfigurationSettings(configuration);

            // Identity & Directory Services
            ConfigureIdentityServices(configSettings);

            // Deployment
            ConfigureDeploymentServices();
        }

        private void ConfigureIdentityServices(ConfigurationSettings configSettings)
        {
            For<DirectorySearcher>().Use(serviceContext =>
            {
                var directoryEntry = new DirectoryEntry();
                var directorySearcher = new DirectorySearcher(directoryEntry);
                return directorySearcher;
            }).Scoped();

            For<IDirectorySearchProvider>().Use<DirectorySearchProvider>().Singleton()
                .Ctor<string>().Is(configSettings.GetConfigurationDomainNameIntra())
                .Ctor<TimeSpan?>().Is(configSettings.GetADUserCacheTimeSpan());

            For<IActiveDirectorySearcher>().Use(context =>
            {
                var provider = context.GetRequiredService<IDirectorySearchProvider>();
                return provider.GetOAuthDirectorySearcher();
            }).Singleton();

            For<IUserGroupProvider>().Use<UserGroupProvider>().Singleton();
            For<IAccountExistenceChecker>().Use<AccountExistenceChecker>().Scoped();
        }

        private void ConfigureDeploymentServices()
        {
            For<IProperties>().Use<Properties>();
            For<IPropertyValues>().Use<PropertyValues>();
            For<IRequests>().Use<Requests>();
            For<IFileOperations>().Use<FileOperations>();
            For<IRequestsManager>().Use<RequestsManager>();
            For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
            For<IManageUsers>().Use<ManageUsers>();
            For<IEnvironmentMapper>().Use<EnvironmentMapper>();
        }
    }
}
