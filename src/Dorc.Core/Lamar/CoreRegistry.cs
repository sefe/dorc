using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;

namespace Dorc.Core.Lamar
{
    public class CoreRegistry : ServiceRegistry
    {
        public CoreRegistry()
        {
            For<IServiceStatus>().Use<ServiceStatus>();

            For<IDeployLibrary>().Use<DeployLibrary>();

            For<IPropertyEncryptor>().Use(x =>
            {
                var secureKeyPersistentDataSource = x.GetInstance<ISecureKeyPersistentDataSource>();
                return new PropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
                    secureKeyPersistentDataSource.GetSymmetricKey());
            });

            For<IVariableScopeOptionsResolver>().Use(context => 
            {
                return new VariableScopeOptionsResolver(
                    context.GetInstance<IPropertiesPersistentSource>(),
                    context.GetInstance<IServersPersistentSource>(),
                    context.GetInstance<IDaemonsPersistentSource>(),
                    context.GetInstance<IDatabasesPersistentSource>(),
                    context.GetInstance<IUserPermsPersistentSource>(),
                    context.GetInstance<IEnvironmentsPersistentSource>(),
                    context.GetInstance<log4net.ILog>(),
                    context
                );
            });

            For<IPropertyEvaluator>().Use<PropertyEvaluator>();
            For<ISecurityPrivilegesChecker>().Use<SecurityPrivilegesChecker>();
            For<IRolePrivilegesChecker>().Use<RolePrivilegesChecker>();

            For<IEnvBackups>().Use<EnvSnapBackups>();
            For<IPropertyExpressionEvaluator>().Use<PropertyExpressionEvaluator>();
        }
    }
}
