using Dorc.Core.Configuration;
﻿using Dorc.Core.Interfaces;
using Dorc.Core.Secrets;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Lamar
{
    public class CoreRegistry : ServiceRegistry
    {
        public CoreRegistry()
        {
            var fallbackConfiguration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

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

            // Prefer the host's IConfiguration when registered so environment-variable
            // overrides behave identically in Dorc.Api and Dorc.Monitor, falling back to
            // the local ConfigurationBuilder when resolved outside a host.
            For<IConfigurationSettings>().Use(ctx =>
                new ConfigurationSettings(
                    ctx.TryGetInstance<IConfiguration>()
                    ?? fallbackConfiguration));
            For<IConfigurationSecretsReader>().Use<OnePasswordSecretsReader>().Singleton();
            For<ConfigValueDeploymentCredentialSource>().Use<ConfigValueDeploymentCredentialSource>();
            For<VaultDeploymentCredentialSource>().Use<VaultDeploymentCredentialSource>();

            // Where deployment credentials come from is a deployment-time choice, resolved once
            // here rather than four times across two processes. The configuration-value source
            // is the default: switching where the whole estate's deployment credentials come
            // from is a deliberate act, not something a release performs on an operator's
            // behalf. Selecting the vault-backed source is what removes those credentials from
            // DOrc's database.
            For<IDeploymentCredentialSource>().Use(context =>
            {
                var configuration = context.GetInstance<IConfigurationSettings>();

                return configuration.GetDeploymentCredentialsFromVault()
                    ? context.GetInstance<VaultDeploymentCredentialSource>()
                    : context.GetInstance<ConfigValueDeploymentCredentialSource>();
            });
        }
    }
}
