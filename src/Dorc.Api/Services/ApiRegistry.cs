using Dorc.Api.Interfaces;
using Dorc.Core.Account;
using Dorc.Core.Interfaces;
using Dorc.Core;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using System.DirectoryServices;
using System.Runtime.Versioning;
using log4net;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class ApiRegistry : ServiceRegistry
    {
        public ApiRegistry()
        {
            try
            {
                var logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);
                var domain = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")["DomainNameIntra"];

                For<IPropertiesService>().Use<PropertiesService>();
                For<IPropertyValuesService>().Use<PropertyValuesService>();

                For<IRequestService>().Use<RequestService>();
                For<ILog>().Use(logger);
                For<IDeployableBuildFactory>().Use<DeployableBuildFactory>();
                For<DirectorySearcher>().Use(serviceContext =>
                {
                    var directoryEntry = new DirectoryEntry();
                    var directorySearcher = new DirectorySearcher(directoryEntry);
                    return directorySearcher;
                }).Scoped();
                For<IActiveDirectorySearcher>().Use(_ =>
                {
                    try
                    {
                        if (domain != null)
                            return new ActiveDirectorySearcher(domain,
                                logger);
                    }
                    catch (Exception)
                    { }
                    return null;
                }).Singleton();
                For<IFileSystemHelper>().Use<FileSystemHelper>();
                For<IRequestsManager>().Use<RequestsManager>();
                For<ISqlUserPasswordReset>().Use<SqlUserPasswordReset>();
                For<IApiServices>().Use<ApiServices>();
                For<IManageUsers>().Use<ManageUsers>();
                For<IEnvironmentMapper>().Use<EnvironmentMapper>();
                For<IAccountExistenceChecker>().Use<AccountExistenceChecker>().Scoped();
            }
            catch (Exception e)
            {
                var log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);

                log.Error(e);
                throw;
            }
        }
    }
}