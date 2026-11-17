using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.Notifications.Teams;
﻿using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Monitor.Registry
{
    public static class PersistentSourcesRegistry
    {
        public static void Register(IServiceCollection collection)
        {
            collection.AddOptions();
            collection.AddSingleton<ISourceHostAllowList, SourceHostAllowList>();

            collection.AddTransient<IRequestsPersistentSource, RequestsPersistentSource>();
            collection.AddTransient<IDeploymentRequestProcessesPersistentSource, DeploymentRequestProcessesPersistentSource>();
            collection.AddTransient<IPropertyValuesPersistentSource, PropertyValuesPersistentSource>();
            collection.AddTransient<IEnvironmentsPersistentSource, EnvironmentsPersistentSource>();
            collection.AddTransient<IEnvironmentTemplatesPersistentSource, EnvironmentTemplatesPersistentSource>();
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

            collection.AddSingleton<ITeardownFailureAlertPublisher>(sp =>
                new LoggingTeardownFailureAlertPublisher(
                    sp.GetRequiredService<ILogger<LoggingTeardownFailureAlertPublisher>>(),
                    (env, reason, ct) => LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(
                        sp.GetService<ITeamsConversationClient>(),
                        sp.GetService<IActiveDirectorySearcher>(),
                        sp.GetRequiredService<IOptions<TeamsBotOptions>>().Value,
                        env,
                        reason,
                        ct,
                        sp.GetService<IDeploymentNotificationSink>(),
                        sp.GetService<IDeploymentEventsPublisher>())));
            collection.AddHostedService<EphemeralEnvironmentReaperService>();
        }
    }
}
