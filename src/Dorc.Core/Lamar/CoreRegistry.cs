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
            // Platform-neutral since S-005: DaemonStatusProbe's ServiceController/LogonUser
            // half moved behind IDaemonOperations (implemented in Dorc.Api by the Windows-
            // worker client), so the registration no longer needs the IsWindows() guard the
            // gate used to track here.
            For<IDaemonStatusProbe>().Use<DaemonStatusProbe>();

            For<IDeployLibrary>().Use<DeployLibrary>();

            For<IPropertyEncryptor>().Use(x =>
            {
                var secureKeyPersistentDataSource = x.GetInstance<ISecureKeyPersistentDataSource>();
                return new AesGcmPropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
                    secureKeyPersistentDataSource.GetSymmetricKey());
            });

            For<IVariableScopeOptionsResolver>().Use<VariableScopeOptionsResolver>();

            For<IPropertyEvaluator>().Use<PropertyEvaluator>();
            For<ISecurityPrivilegesChecker>().Use<SecurityPrivilegesChecker>();
            For<IRolePrivilegesChecker>().Use<RolePrivilegesChecker>();

            For<IEnvBackups>().Use<EnvSnapBackups>();
            For<IPropertyExpressionEvaluator>().Use<PropertyExpressionEvaluator>();
        }
    }
}
