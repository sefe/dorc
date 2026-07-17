using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Core
{
    public class VariableScopeOptionsResolver : IVariableScopeOptionsResolver
    {
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly IUserPermsPersistentSource _userPermsPersistentSource;
        private readonly IContainersPersistentSource _containersPersistentSource;
        private readonly ICloudResourcesPersistentSource _cloudResourcesPersistentSource;
        private readonly IApiRegistrationsPersistentSource _apiRegistrationsPersistentSource;

        public VariableScopeOptionsResolver(IPropertiesPersistentSource propertiesPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IUserPermsPersistentSource userPermsPersistentSource,
            IContainersPersistentSource containersPersistentSource,
            ICloudResourcesPersistentSource cloudResourcesPersistentSource,
            IApiRegistrationsPersistentSource apiRegistrationsPersistentSource)
        {
            _userPermsPersistentSource = userPermsPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
            _daemonsPersistentSource = daemonsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _propertiesPersistentSource = propertiesPersistentSource;
            _containersPersistentSource = containersPersistentSource;
            _cloudResourcesPersistentSource = cloudResourcesPersistentSource;
            _apiRegistrationsPersistentSource = apiRegistrationsPersistentSource;
        }

        public void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment)
        {
            var databasesForEnvId = _databasesPersistentSource.GetDatabasesForEnvironmentName(environment.EnvironmentName);
            var serverApiModels = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId);

            var servers = new List<string>();

            var serversForEnvId = serverApiModels;
            var forEnvId = serversForEnvId as ServerApiModel[] ?? serversForEnvId.ToArray();
            foreach (var server in forEnvId)
            {
                servers.Add(server.Name);
                variableResolver.SetPropertyValue(server.ApplicationTags, server.Name);
            }

            var environmentServers =
                forEnvId.Select(
                    s =>
                        new VariableValueServers
                        {
                            Name = s.Name,
                            OsName = s.OsName,
                            ApplicationServerName = s.ApplicationTags,
                            Services =
                                _daemonsPersistentSource.GetDaemonsForServer(s.ServerId).Select(svc => new VariableValueDaemons
                                { Name = svc.Name, DisplayName = svc.DisplayName, AccountName = svc.AccountName, ServiceType = svc.ServiceType })
                                    .ToArray()
                        }).ToArray();

            var serversArray = servers.ToArray();
            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.AllServers, new VariableValue { Value = serversArray, Type = serversArray.GetType() });
            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentServers,
                new VariableValue { Value = environmentServers, Type = environmentServers.GetType() });

            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EndurFileShare, environment.Details.FileShare);

            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EndurConfigurationFile,
                _propertiesPersistentSource.GetConfigurationFilePath(environment));
            var endurDatabase = _databasesPersistentSource.GetDatabaseByType(environment, "Endur");
            if (endurDatabase != null)
            {
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EndurDatabaseName, endurDatabase.Name);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EndurDatabaseServer, endurDatabase.ServerName);
            }

            var environmentShortName = endurDatabase != null
                ? endurDatabase.Name.Replace("END_DB_", string.Empty)
                : GetShortNameFromEnvironmentName(environment.EnvironmentName);
            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentShortName, environmentShortName);

            var databaseApiModels = databasesForEnvId as DatabaseApiModel[] ?? databasesForEnvId.ToArray();
            var reportingDatabase =
                databaseApiModels.SingleOrDefault(d => d.Type == "Endur Reporting");
            if (reportingDatabase != null)
            {
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ReportingDatabaseName, reportingDatabase.Name);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ReportingDatabaseServer, reportingDatabase.ServerName);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.SsisPackageServer, reportingDatabase.ServerName);
            }

            var externalDatabase =
                databaseApiModels.SingleOrDefault(d => d.Type == "Endur External");
            if (externalDatabase != null)
            {
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ExternalDatabaseName, externalDatabase.Name);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ExternalDatabaseServer, externalDatabase.ServerName);
            }

            var databasePermissions = databaseApiModels
                .Select(GetDbPermission).ToArray();

            var dbTypes = databaseApiModels.Select(d => d.Type).Distinct();

            foreach (var dbType in dbTypes)
            {
                var dbs = databaseApiModels.Where(d => d.Type.Equals(dbType)).ToList();
                var propertyName = dbType.Replace(" ", "_");

                if (dbs.Count == 1)
                {
                    var dbServerName = dbs.First().ServerName;
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.DbServer}{propertyName}",
                        new VariableValue { Value = dbServerName, Type = dbServerName.GetType() }
                    );
                }
                else
                {
                    var serverNames = dbs.Select(d => d.ServerName).ToArray();
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.DbServer}{propertyName}",
                        new VariableValue { Value = serverNames, Type = serverNames.GetType() }
                    );
                }

                if (dbs.Count == 1)
                {
                    var dbName = dbs.First().Name;
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.DbName}{propertyName}",
                        new VariableValue { Value = dbName, Type = dbName.GetType() }
                    );
                }
                else
                {
                    var dbNames = dbs.Select(d => d.Name).ToArray();
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.DbName}{propertyName}",
                        new VariableValue { Value = dbNames, Type = dbNames.GetType() }
                    );
                }
            }

            AddPropertiesForServerNamesByType(variableResolver, forEnvId);

            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DatabasePermissions,
                new VariableValue { Value = databasePermissions, Type = databasePermissions.GetType() });

            var ownerEmails = environment.Details?.EnvironmentOwnerEmails;
            if (ownerEmails is { Count: > 0 })
            {
                var emailsArray = ownerEmails.ToArray();
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwnerEmails,
                    new VariableValue { Value = emailsArray, Type = emailsArray.GetType() });
            }

            AddEnvironmentComponentProperties(variableResolver, environment.EnvironmentId);
        }

        // Emission is conditional by design (HLPS env-details-component-tabs §5.7.3):
        // environments with no attached components produce exactly the pre-change
        // variable set, so the integration is inert when unused.
        private void AddEnvironmentComponentProperties(IVariableResolver variableResolver, int environmentId)
        {
            var containers = _containersPersistentSource.GetForEnvironmentId(environmentId).ToArray();
            if (containers.Length > 0)
            {
                var values = containers.Select(c => new VariableValueContainers
                {
                    Name = c.Name,
                    Image = c.Image,
                    Registry = c.Registry,
                    HostServerName = c.HostServerName,
                    Tags = c.Tags
                }).ToArray();
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentContainers,
                    new VariableValue { Value = values, Type = values.GetType() });
                AddPropertiesForNamesByTag(variableResolver, PropertyValueScopeOptionsFixed.ContainerNames,
                    containers.Select(c => (c.Name, c.Tags)));
            }

            var cloudResources = _cloudResourcesPersistentSource.GetForEnvironmentId(environmentId).ToArray();
            if (cloudResources.Length > 0)
            {
                var values = cloudResources.Select(c => new VariableValueCloudResources
                {
                    Name = c.Name,
                    Provider = c.Provider,
                    ResourceType = c.ResourceType,
                    ResourceIdentifier = c.ResourceIdentifier,
                    Subscription = c.Subscription,
                    Tags = c.Tags
                }).ToArray();
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentCloudResources,
                    new VariableValue { Value = values, Type = values.GetType() });
                AddPropertiesForNamesByTag(variableResolver, PropertyValueScopeOptionsFixed.CloudResourceNames,
                    cloudResources.Select(c => (c.Name, c.Tags)));
            }

            var apiRegistrations = _apiRegistrationsPersistentSource.GetForEnvironmentId(environmentId).ToArray();
            if (apiRegistrations.Length > 0)
            {
                var values = apiRegistrations.Select(a => new VariableValueApiRegistrations
                {
                    Name = a.Name,
                    BaseUrl = a.BaseUrl,
                    Version = a.Version,
                    HealthCheckUrl = a.HealthCheckUrl,
                    Tags = a.Tags
                }).ToArray();
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentApiRegistrations,
                    new VariableValue { Value = values, Type = values.GetType() });
                AddPropertiesForNamesByTag(variableResolver, PropertyValueScopeOptionsFixed.ApiRegistrationNames,
                    apiRegistrations.Select(a => (a.Name, a.Tags)));
            }
        }

        private VariableValueDbPerm GetDbPermission(DatabaseApiModel databaseApiModel)
        {
            return new VariableValueDbPerm
            {
                Database = new DatabaseDefinition { Name = databaseApiModel.Name, Type = databaseApiModel.Type },
                Users = _userPermsPersistentSource.GetPermissions(databaseApiModel.Id)
                    .GroupBy(u => u.User, u => u.Role)
                    .Select(g => new DbUserRole(g.Key, g.ToArray()))
                    .ToArray()
            };
        }

        private static void AddPropertiesForServerNamesByType(IVariableResolver variableResolver, IEnumerable<ServerApiModel> serverApiModels)
        {
            AddPropertiesForNamesByTag(variableResolver, PropertyValueScopeOptionsFixed.ServerNames,
                serverApiModels.Select(s => (s.Name, s.ApplicationTags)));
        }

        // Shared per-tag name-list emission for servers and environment components.
        // Deliberate semantics carried over from the original server-only code: spaces in
        // tag names become underscores, and a tag held by exactly one item emits a scalar
        // string while multiple items emit a string array. Null/empty tags yield no
        // per-tag variable (the original inline code threw NRE on a null tag string).
        private static void AddPropertiesForNamesByTag(IVariableResolver variableResolver, string prefix,
            IEnumerable<(string Name, string Tags)> taggedItems)
        {
            var tagWithNames = new Dictionary<string, List<string>>();
            foreach (var item in taggedItems)
            {
                if (string.IsNullOrEmpty(item.Tags))
                    continue;

                var tags = item.Tags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tag in tags)
                    if (tagWithNames.TryGetValue(tag, out var names))
                        names.Add(item.Name);
                    else
                        tagWithNames.Add(tag, new List<string> { item.Name });
            }

            foreach (var tag in tagWithNames)
            {
                var tagName = tag.Key.Replace(" ", "_");
                if (tag.Value.Count > 1)
                {
                    var names = tag.Value.ToArray();
                    variableResolver.SetPropertyValue($"{prefix}{tagName}",
                        new VariableValue { Value = names, Type = names.GetType() });
                }
                else
                    variableResolver.SetPropertyValue($"{prefix}{tagName}", tag.Value.Single());
            }
        }

        internal static string GetShortNameFromEnvironmentName(string environmentName)
        {
            var parts = environmentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[^2] + parts[^1];

            parts = environmentName.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[^2] + parts[^1];

            return environmentName;
        }
    }

}