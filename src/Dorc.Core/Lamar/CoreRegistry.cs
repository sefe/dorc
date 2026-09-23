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
            // DaemonStatusProbe is Windows-only (Service Control Manager) and moves behind the
            // Windows worker at S-005. Guarded rather than suppressed: OperatingSystem.IsWindows()
            // is a platform guard CA1416 understands, so this satisfies the analyzer honestly
            // instead of silencing it — and a suppression here would be a hole in the very gate
            // that promotes CA1416 to an error. On Linux the registration is simply absent;
            // resolving IDaemonStatusProbe there is a configuration error by definition.
            if (OperatingSystem.IsWindows())
            {
                For<IDaemonStatusProbe>().Use<DaemonStatusProbe>();
            }

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
