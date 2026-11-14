using Dorc.Core.Configuration;
using Dorc.Core.Secrets;
using Dorc.Core.Security;
﻿using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dorc.Monitor.Registry
{
    public static class PersistentSourcesRegistry
    {
        public static void Register(IServiceCollection collection)
        {
            collection.AddSingleton<ISourceHostAllowList, SourceHostAllowList>();
            collection.AddSingleton<IScriptScopeConfigValues>(sp =>
                new ScriptScopeConfigValues(
                    sp.GetService<IConfiguration>()
                    ?? sp.GetService<IConfigurationRoot>()
                    ?? new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json", optional: true)
                        .AddEnvironmentVariables()
                        .Build()));

            // Prefer the host's IConfiguration when registered so environment-variable
            // overrides for DeploymentCredentialsFromVault and item IDs behave identically
            // in Dorc.Monitor and Dorc.Api.
            collection.TryAddTransient<IConfigurationSettings>(sp =>
                new ConfigurationSettings(
                    sp.GetService<IConfiguration>()
                    ?? sp.GetService<IConfigurationRoot>()
                    ?? new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json", optional: true)
                        .AddEnvironmentVariables()
                        .Build()));
            collection.TryAddSingleton<IConfigurationSecretsReader, OnePasswordSecretsReader>();
            collection.TryAddTransient<ConfigValueDeploymentCredentialSource>();
            collection.TryAddTransient<VaultDeploymentCredentialSource>();
            collection.TryAddTransient<IDeploymentCredentialSource>(sp =>
                sp.GetRequiredService<IConfigurationSettings>().GetDeploymentCredentialsFromVault()
                    ? sp.GetRequiredService<VaultDeploymentCredentialSource>()
                    : sp.GetRequiredService<ConfigValueDeploymentCredentialSource>());

            collection.AddTransient<IRequestsPersistentSource, RequestsPersistentSource>();
            collection.AddTransient<IDeploymentRequestProcessesPersistentSource, DeploymentRequestProcessesPersistentSource>();
            collection.AddTransient<IPropertyValuesPersistentSource, PropertyValuesPersistentSource>();
            collection.AddTransient<IEnvironmentsPersistentSource, EnvironmentsPersistentSource>();
            collection.AddTransient<IManageProjectsPersistentSource, ManageProjectsPersistentSource>();
            collection.AddTransient<IComponentsPersistentSource, ComponentsPersistentSource>();
            collection.AddTransient<IPropertiesPersistentSource, PropertiesPersistentSource>();
            collection.AddTransient<IServersPersistentSource, ServersPersistentSource>();
            collection.AddTransient<IDaemonsPersistentSource, DaemonsPersistentSource>();
            collection.AddTransient<IContainersPersistentSource, ContainersPersistentSource>();
            collection.AddTransient<ICloudResourcesPersistentSource, CloudResourcesPersistentSource>();
            collection.AddTransient<IApiRegistrationsPersistentSource, ApiRegistrationsPersistentSource>();
            // Cloud resource audit is written by the Terraform registrar when deployments
            // register/attach resources; container and API registration audits are API-only.
            collection.AddTransient<ICloudResourceAuditPersistentSource, CloudResourceAuditPersistentSource>();
            collection.AddTransient<IDaemonObservationPersistentSource, DaemonObservationPersistentSource>();
            collection.AddTransient<IDatabasesPersistentSource, DatabasesPersistentSource>();
            collection.AddTransient<IUserPermsPersistentSource, UserPermsPersistentSource>();
            collection.AddTransient<IAccessControlPersistentSource, AccessControlPersistentSource>();
            collection.AddTransient<IProjectsPersistentSource, ProjectsPersistentSource>();
            collection.AddSingleton<ISecureKeyPersistentDataSource, SecureKeyPersistentDataSource>();
            collection.AddTransient<IConfigValuesPersistentSource, ConfigValuesPersistentSource>();
            collection.AddTransient<IScriptsAuditPersistentSource, ScriptsAuditPersistentSource>();
            collection.AddTransient<IScriptsPersistentSource, ScriptsPersistentSource>();
        }
    }
}
