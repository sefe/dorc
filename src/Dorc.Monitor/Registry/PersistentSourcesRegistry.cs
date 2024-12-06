using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.Monitor.Registry
{
    public static class PersistentSourcesRegistry
    {
        public static void Register(IServiceCollection collection)
        {
            collection.AddTransient<IRequestsPersistentSource, RequestsPersistentSource>();
            collection.AddTransient<IDeploymentRequestProcessesPersistentSource, DeploymentRequestProcessesPersistentSource>();
            collection.AddTransient<IPropertyValuesPersistentSource, PropertyValuesPersistentSource>();
            collection.AddTransient<IEnvironmentsPersistentSource, EnvironmentsPersistentSource>();
            collection.AddTransient<IManageProjectsPersistentSource, ManageProjectsPersistentSource>();
            collection.AddTransient<IComponentsPersistentSource, ComponentsPersistentSource>();
            collection.AddTransient<IPropertiesPersistentSource, PropertiesPersistentSource>();
            collection.AddTransient<IServersPersistentSource, ServersPersistentSource>();
            collection.AddTransient<IDaemonsPersistentSource, DaemonsPersistentSource>();
            collection.AddTransient<IDatabasesPersistentSource, DatabasesPersistentSource>();
            collection.AddTransient<IUserPermsPersistentSource, UserPermsPersistentSource>();
            collection.AddTransient<IAccessControlPersistentSource, AccessControlPersistentSource>();
            collection.AddTransient<IProjectsPersistentSource, ProjectsPersistentSource>();
            collection.AddTransient<ISecureKeyPersistentDataSource, SecureKeyPersistentDataSource>();
            collection.AddTransient<IConfigValuesPersistentSource, ConfigValuesPersistentSource>();
        }
    }
}
