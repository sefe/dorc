using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.Core
{
    public class VariableScopeOptionsResolver : IVariableScopeOptionsResolver
    {
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly IUserPermsPersistentSource _userPermsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IServiceProvider? _serviceProvider;
        private readonly ILog _logger;

        public VariableScopeOptionsResolver(IPropertiesPersistentSource propertiesPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource,
            IUserPermsPersistentSource userPermsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ILog logger,
            IServiceProvider? serviceProvider = null)
        {
            _userPermsPersistentSource = userPermsPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
            _daemonsPersistentSource = daemonsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _propertiesPersistentSource = propertiesPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment)
        {
            // Set environment owner information
            SetEnvironmentOwnerProperties(variableResolver, environment);

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
                : environment.EnvironmentName.Replace(" ", "_");
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
            var serverTypeWithServerNames = new Dictionary<string, List<string>>();
            foreach (var server in serverApiModels)
            {
                var semicolonSeparatedServerTypes = server.ApplicationTags;
                var serverTypes =
                    semicolonSeparatedServerTypes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var serverType in serverTypes)
                    if (serverTypeWithServerNames.ContainsKey(serverType))
                        serverTypeWithServerNames[serverType].Add(server.Name);
                    else
                        serverTypeWithServerNames.Add(serverType, new List<string> { server.Name });
            }

            foreach (var serverType in serverTypeWithServerNames)
            {
                var sType = serverType.Key.Replace(" ", "_");
                if (serverType.Value.Count > 1)
                {
                    var serverNames = serverType.Value.ToArray();
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.ServerNames}{sType}",
                        new VariableValue { Value = serverNames, Type = serverNames.GetType() });

                }
                else
                    variableResolver.SetPropertyValue($"{PropertyValueScopeOptionsFixed.ServerNames}{sType}", serverType.Value.Single());
            }
        }

        private void SetEnvironmentOwnerProperties(IVariableResolver variableResolver, EnvironmentApiModel environment)
        {
            try
            {
                // Get the environment owner ID
                var ownerId = _environmentsPersistentSource.GetEnvironmentOwnerId(environment.EnvironmentId);
                
                if (!string.IsNullOrEmpty(ownerId))
                {
                    // Set the existing EnvOwner property with the owner ID
                    variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, ownerId);
                    
                    // Try to resolve the email address if directory searcher is available
                    IActiveDirectorySearcher? directorySearcher = null;
                    if (_serviceProvider != null)
                    {
                        try
                        {
                            directorySearcher = _serviceProvider.GetService(typeof(IActiveDirectorySearcher)) as IActiveDirectorySearcher;
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Could not resolve IActiveDirectorySearcher from service provider: {ex.Message}");
                        }
                    }
                    
                    if (directorySearcher != null)
                    {
                        try
                        {
                            var userData = directorySearcher.GetUserDataById(ownerId);
                            if (userData != null && !string.IsNullOrEmpty(userData.Email))
                            {
                                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, userData.Email);
                            }
                            else
                            {
                                _logger.Warn($"Could not resolve email for environment owner ID '{ownerId}' in environment '{environment.EnvironmentName}'");
                                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error resolving email for environment owner ID '{ownerId}' in environment '{environment.EnvironmentName}': {ex.Message}", ex);
                            variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
                        }
                    }
                    else
                    {
                        _logger.Debug($"Directory searcher not available, cannot resolve email for environment owner in environment '{environment.EnvironmentName}'");
                        variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
                    }
                }
                else
                {
                    _logger.Debug($"No environment owner ID found for environment '{environment.EnvironmentName}'");
                    variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, string.Empty);
                    variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting environment owner information for environment '{environment.EnvironmentName}': {ex.Message}", ex);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwner, string.Empty);
                variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentOwnerEmail, string.Empty);
            }
        }
    }

}