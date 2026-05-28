using Dorc.Api.Interfaces;
using Dorc.Core;
using Dorc.Core.Account;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
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

            // Graph-backed AD replacement — single implementation, no composite/factory.
            // See HLPS-api-split.md D-2 and SPEC-S-001 §2.5.
            For<IActiveDirectorySearcher>().Use<AzureEntraSearcher>().Singleton();
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
    }
}
