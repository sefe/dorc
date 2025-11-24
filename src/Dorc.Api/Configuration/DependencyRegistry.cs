using Dorc.Api.Interfaces;
using Dorc.Api.Deployment;
using Dorc.Api.Build;
using Dorc.Api.Identity;
using Dorc.Api.Infrastructure;
using Dorc.Api.Exceptions;
using Dorc.Api.Services;
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
        public DependencyRegistry()
        {
            var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            var configSettings = new ConfigurationSettings(configuration);
            var domain = configSettings.GetConfigurationDomainNameIntra();

            For<IProperties>().Use<Properties>();
            For<IPropertyValues>().Use<PropertyValues>();
                
            For<IRequests>().Use<Requests>();

            For<IDeployableBuildFactory>().Use<DeployableBuildFactory>();
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
                var factory = context.GetRequiredService<IDirectorySearchProvider>();
                return factory.GetOAuthDirectorySearcher();
            }).Singleton();

            For<IUserGroupProvider>().Use<UserGroupProvider>().Singleton();

            For<IFileOperations>().Use<FileOperations>();
            For<IRequestsManager>().Use<RequestsManager>();
            For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
            For<IApiServices>().Use<ApiServices>();
            For<IManageUsers>().Use<ManageUsers>();
            For<IEnvironmentMapper>().Use<EnvironmentMapper>();
            For<IAccountExistenceChecker>().Use<AccountExistenceChecker>().Scoped();
        }
    }
}