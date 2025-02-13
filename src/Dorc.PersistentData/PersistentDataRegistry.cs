using Microsoft.EntityFrameworkCore;
using Lamar;
using Microsoft.Extensions.Configuration;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData
{
    public class PersistentDataRegistry : ServiceRegistry
    {
        public PersistentDataRegistry()
        {
            For<DeploymentContext>().Use<DeploymentContext>().Scoped();
            For<DbContext>().Use(_ => _.GetInstance<DeploymentContext>()).Scoped();
            For<IDeploymentContext>().Use(_ => _.GetInstance<DeploymentContext>()).Scoped();

            var connectionString = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetConnectionString("DOrcConnectionString");
            For<IDeploymentContextFactory>().Use(new DeploymentContextFactory(connectionString));

            For<IAccessControlPersistentSource>().Use<AccessControlPersistentSource>().Scoped();
            For<IAccountPersistentSource>().Use<AccountPersistentSource>().Scoped();
            For<IAdGroupPersistentSource>().Use<AdGroupPersistentSource>().Scoped();
            For<IAnalyticsPersistentSource>().Use<AnalyticsPersistentSource>().Scoped();
            For<IBundledRequestsPersistentSource>().Use<BundledRequestsPersistentSource>().Scoped();
            For<IComponentsPersistentSource>().Use<ComponentsPersistentSource>().Scoped();
            For<IConfigValuesPersistentSource>().Use<ConfigValuesPersistentSource>().Scoped();
            For<IDaemonsPersistentSource>().Use<DaemonsPersistentSource>().Scoped();
            For<IDatabasesPersistentSource>().Use<DatabasesPersistentSource>().Scoped();
            For<IEnvironmentHistoryPersistentSource>().Use<EnvironmentHistoryPersistentSource>().Scoped();
            For<IEnvironmentsPersistentSource>().Use<EnvironmentsPersistentSource>().Scoped();
            For<IManageProjectsPersistentSource>().Use<ManageProjectsPersistentSource>().Scoped();
            For<IPermissionsPersistentSource>().Use<PermissionsPersistentSource>().Scoped();
            For<IProjectsPersistentSource>().Use<ProjectsPersistentSource>().Scoped();
            For<IPropertiesPersistentSource>().Use<PropertiesPersistentSource>().Scoped();
            For<IPropertyValuesAuditPersistentSource>().Use<PropertyValuesAuditPersistentSource>().Scoped();
            For<IPropertyValuesAuditPersistentSource>().Use<PropertyValuesAuditPersistentSource>().Scoped();
            For<IPropertyValuesPersistentSource>().Use<PropertyValuesPersistentSource>().Ctor<bool>("filtersMustMatch").Is(false).Scoped();
            For<IRequestsPersistentSource>().Use<RequestsPersistentSource>().Scoped();
            For<IRequestsStatusPersistentSource>().Use<RequestsStatusPersistentSource>().Scoped();
            For<IScriptsPersistentSource>().Use<ScriptsPersistentSource>().Scoped();
            For<ISecureKeyPersistentDataSource>().Use<SecureKeyPersistentDataSource>().Singleton(); // no need to read secure key every time from db
            For<ISecurityObjectFilter>().Use<SecurityObjectFilter>().Scoped();
            For<IServersPersistentSource>().Use<ServersPersistentSource>().Scoped();
            For<ISqlPortsPersistentSource>().Use<SqlPortsPersistentSource>().Scoped();
            For<IUserPermsPersistentSource>().Use<UserPermsPersistentSource>().Scoped();
            For<IUsersPersistentSource>().Use<UsersPersistentSource>().Scoped();
        }
    }
}
