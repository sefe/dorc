using Dorc.Api.Interfaces;
using Dorc.Core;
using Dorc.Core.Account;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Lamar;
using System.DirectoryServices;
using System.Runtime.Versioning;

namespace Dorc.Api.Configuration
{
    [SupportedOSPlatform("windows")]
    public class DependencyRegistry : ServiceRegistry
    {
        public ApiRegistry()
        {
            var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            var configSettings = new ConfigurationSettings(configuration);
            var domain = configSettings.GetConfigurationDomainNameIntra();

            For<IPropertiesService>().Use<PropertiesService>();
            For<IPropertyValuesService>().Use<PropertyValuesService>();
                
            For<IRequestService>().Use<RequestService>();

            For<IDeployableBuildFactory>().Use<DeployableBuildFactory>();
            For<DirectorySearcher>().Use(serviceContext =>
            {
                var directoryEntry = new DirectoryEntry();
                var directorySearcher = new DirectorySearcher(directoryEntry);
                return directorySearcher;
            }).Scoped();
            
            For<IDirectorySearcherFactory>().Use<DirectorySearcherFactory>().Singleton()
                .Ctor<string>().Is(configSettings.GetConfigurationDomainNameIntra())
                .Ctor<TimeSpan?>().Is(configSettings.GetADUserCacheTimeSpan());
            For<IActiveDirectorySearcher>().Use(context =>
            {
                var factory = context.GetRequiredService<IDirectorySearcherFactory>();
                return factory.GetOAuthDirectorySearcher();
            }).Singleton();

            For<IUserGroupsReaderFactory>().Use<UserGroupReaderFactory>().Singleton();

            For<IFileSystemHelper>().Use<FileSystemHelper>();
            For<IRequestsManager>().Use<RequestsManager>();
            For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
            For<IApiServices>().Use<ApiServices>();
            For<IManageUsers>().Use<ManageUsers>();
            For<IEnvironmentMapper>().Use<EnvironmentMapper>();
            For<IAccountExistenceChecker>().Use<AccountExistenceChecker>().Scoped();
        }
    }
}