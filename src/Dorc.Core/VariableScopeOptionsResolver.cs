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

        public VariableScopeOptionsResolver(IPropertiesPersistentSource propertiesPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IUserPermsPersistentSource userPermsPersistentSource)
        {
            _userPermsPersistentSource = userPermsPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
            _daemonsPersistentSource = daemonsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _propertiesPersistentSource = propertiesPersistentSource;
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
                // Pre-existing shape: a variable named after the whole joined tag
                // string. Rejoined here so the name keeps that shape now tags are rows.
                //
                // It is NOT guaranteed byte-identical to the old column. Tags come back
                // in PK order (Tag ASC) rather than the order someone typed them, and
                // they are trimmed. A server whose column read 'web;appserv' now emits
                // 'appserv;web', and one reading 'appserv ' now emits 'appserv'. Deploy
                // scripts referencing those old names resolve nothing — the estate audit
                // is what says whether any such name is in use.
                //
                // Coalesced because Join renders an empty set as null — right for the
                // column, fatal here. The name becomes a key in a ConcurrentDictionary,
                // which throws on a null key, so one untagged server would abort
                // variable resolution for the whole environment. Untagged servers held
                // '' in the column before tags became rows, so '' is also the shape
                // that matches.
                variableResolver.SetPropertyValue(TagString.Join(server.Tags) ?? string.Empty, server.Name);
            }

            var environmentServers =
                forEnvId.Select(
                    s =>
                        new VariableValueServers
                        {
                            Name = s.Name,
                            OsName = s.OsName,
                            Tags = TagString.Join(s.Tags),
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
            var endurDatabase = _databasesPersistentSource.GetDatabaseByTag(environment, "Endur");
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
                databaseApiModels.SingleOrDefault(d => TagString.HasTag(d.Tags, "Endur Reporting"));
            if (reportingDatabase != null)
            {
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ReportingDatabaseName, reportingDatabase.Name);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ReportingDatabaseServer, reportingDatabase.ServerName);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.SsisPackageServer, reportingDatabase.ServerName);
            }

            var externalDatabase =
                databaseApiModels.SingleOrDefault(d => TagString.HasTag(d.Tags, "Endur External"));
            if (externalDatabase != null)
            {
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ExternalDatabaseName, externalDatabase.Name);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ExternalDatabaseServer, externalDatabase.ServerName);
            }

            var databasePermissions = databaseApiModels
                .Select(GetDbPermission).ToArray();

            // Per-tag grouping: a database carrying several tags contributes to each
            // tag's variables; one carrying none contributes nothing (it used to throw
            // here).
            var dbTags = databaseApiModels
                .SelectMany(d => d.Tags ?? System.Array.Empty<string>())
                .Distinct(TagString.Comparer);

            foreach (var dbTag in dbTags)
            {
                var dbs = databaseApiModels.Where(d => TagString.HasTag(d.Tags, dbTag)).ToList();
                var propertyName = dbTag.Replace(" ", "_");

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

            AddPropertiesForServerNamesByTag(variableResolver, forEnvId);

            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DatabasePermissions,
                new VariableValue { Value = databasePermissions, Type = databasePermissions.GetType() });

            var ownerEmails = environment.Details?.EnvironmentOwnerEmails;
            if (ownerEmails is { Count: > 0 })
            {
                var emailsArray = ownerEmails.ToArray();
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwnerEmails,
                    new VariableValue { Value = emailsArray, Type = emailsArray.GetType() });
            }
        }

        private VariableValueDbPerm GetDbPermission(DatabaseApiModel databaseApiModel)
        {
            return new VariableValueDbPerm
            {
                Database = new DatabaseDefinition { Name = databaseApiModel.Name, Tags = TagString.Join(databaseApiModel.Tags) },
                Users = _userPermsPersistentSource.GetPermissions(databaseApiModel.Id)
                    .GroupBy(u => u.User, u => u.Role)
                    .Select(g => new DbUserRole(g.Key, g.ToArray()))
                    .ToArray()
            };
        }

        private static void AddPropertiesForServerNamesByTag(IVariableResolver variableResolver, IEnumerable<ServerApiModel> serverApiModels)
        {
            // Keyed with the tag comparer so 'Endur' and 'ENDUR' group together, as
            // they do in storage, rather than emitting two variables that differ only
            // in the casing of their name.
            var serverNamesByTag = new Dictionary<string, List<string>>(TagString.Comparer);
            foreach (var server in serverApiModels)
            {
                foreach (var serverTag in server.Tags ?? Array.Empty<string>())
                    if (serverNamesByTag.TryGetValue(serverTag, out var names))
                        names.Add(server.Name);
                    else
                        serverNamesByTag.Add(serverTag, new List<string> { server.Name });
            }

            foreach (var serverTag in serverNamesByTag)
            {
                var tagKey = serverTag.Key.Replace(" ", "_");
                if (serverTag.Value.Count > 1)
                {
                    var serverNames = serverTag.Value.ToArray();
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.ServerNames}{tagKey}",
                        new VariableValue { Value = serverNames, Type = serverNames.GetType() });

                }
                else
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.ServerNames}{tagKey}", serverTag.Value.Single());
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