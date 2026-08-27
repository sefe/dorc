using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Environment = System.Environment;

namespace Dorc.Core
{
    // Platform-neutral since S-005: the ServiceController/LogonUser half moved behind
    // IDaemonOperations (implemented in Dorc.Api by a Windows-worker client), so this class
    // now only orchestrates — it builds the daemon list from the database, hands probing and
    // service control to the seam, and records observations for whatever came back.
    public class DaemonStatusProbe : IDaemonStatusProbe
    {
        private const string DORCProdDeployUsername = "DORC_ProdDeployUsername";
        private const string DORCProdDeployPassword = "DORC_ProdDeployPassword";
        private const string DORCNonProdDeployUsername = "DORC_NonProdDeployUsername";
        private const string DORCNonProdDeployPassword = "DORC_NonProdDeployPassword";

        private readonly ILogger _logger;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;
        private readonly IDaemonObservationPersistentSource _daemonObservationPersistentSource;
        private readonly IDaemonOperations _daemonOperations;
        private readonly string _domainName;

        private readonly IDaemonAuditPersistentSource _daemonAuditPersistentSource;
        private readonly IServersAuditPersistentSource _serversAuditPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public DaemonStatusProbe(IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<DaemonStatusProbe> logger,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IDaemonObservationPersistentSource daemonObservationPersistentSource,
            IConfigurationSettings configurationSettingsEngine,
            IDaemonAuditPersistentSource daemonAuditPersistentSource,
            IServersAuditPersistentSource serversAuditPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDaemonOperations daemonOperations)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _daemonObservationPersistentSource = daemonObservationPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _configValuesPersistentSource = configValuesPersistentSource;
            _logger = logger;
            _daemonAuditPersistentSource = daemonAuditPersistentSource;
            _serversAuditPersistentSource = serversAuditPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _daemonOperations = daemonOperations;

            _domainName = configurationSettingsEngine.GetConfigurationDomainNameIntra();
        }

        public List<DaemonStatus> GetDaemonStatuses(int envId)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envId, null);
            return GetDaemonStatusesForEnvironment(environment);
        }

        public List<DaemonStatus> GetDaemonStatuses(string envName, ClaimsPrincipal principal)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envName, principal);
            return GetDaemonStatusesForEnvironment(environment);
        }

        private List<DaemonStatus> GetDaemonStatusesForEnvironment(EnvironmentApiModel? environment)
        {
            var credential = GetDeployCredential(environment);

            var servers = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId).ToList();
            var daemons = BuildDaemonList(environment, servers);

            // Pre-S-005 behaviour, preserved: without a configured deploy credential this
            // path does not probe at all — it returns the unprobed list (no Status values).
            if (credential == null)
            {
                return daemons;
            }

            return ProbeAndRecord(credential, daemons);
        }

        public List<DaemonStatus> DiscoverAllDaemonsForEnvironment(string envName, ClaimsPrincipal principal)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envName, principal);
            return DiscoverAllDaemonsForEnvironmentInternal(environment);
        }

        private List<DaemonStatus> DiscoverAllDaemonsForEnvironmentInternal(EnvironmentApiModel? environment)
        {
            var credential = GetDeployCredential(environment);

            var servers = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId).ToList();
            var daemons = BuildDaemonListForDiscovery(environment, servers);

            // Unlike GetDaemonStatuses, discovery probes either way — impersonated when a
            // deploy credential is configured, as the process identity otherwise.
            return ProbeAndRecord(credential, daemons);
        }

        private List<DaemonStatus> ProbeAndRecord(WorkerDaemonCredentialApiModel? credential, List<DaemonStatus> daemons)
        {
            var probed = _daemonOperations.ProbeStatuses(credential, daemons);

            foreach (var status in probed)
            {
                RecordObservation(status);
            }

            return probed;
        }

        private WorkerDaemonCredentialApiModel? GetDeployCredential(EnvironmentApiModel? environment)
        {
            GetUsernameAndPassword(environment, out var user, out var pwd);

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
            {
                return null;
            }

            return new WorkerDaemonCredentialApiModel
            {
                Username = user,
                Domain = _domainName,
                Password = pwd
            };
        }

        private void GetUsernameAndPassword(EnvironmentApiModel? environment, out string user, out string pwd)
        {
            if (environment.EnvironmentIsProd)
            {
                user = _configValuesPersistentSource.GetConfigValue(DORCProdDeployUsername);
                pwd = _configValuesPersistentSource.GetConfigValue(DORCProdDeployPassword);
            }
            else
            {
                user = _configValuesPersistentSource
                    .GetConfigValue(DORCNonProdDeployUsername);
                pwd = _configValuesPersistentSource
                    .GetConfigValue(DORCNonProdDeployPassword);
            }
        }

        private List<DaemonStatus> BuildDaemonList(EnvironmentApiModel? environment,
            List<ServerApiModel> servers)
        {
            var iResults = new List<DaemonStatus>();

            try
            {
                foreach (var serverApiModel in servers)
                {
                    try
                    {
                        var daemons = _daemonsPersistentSource.GetDaemonsForServer(serverApiModel.ServerId);

                        // No mappings yet - fall back to all daemons so discovery can happen
                        if (!daemons.Any())
                        {
                            daemons = _daemonsPersistentSource.GetDaemons();
                        }

                        foreach (var daemonApiModel in daemons)
                        {
                            try
                            {
                                iResults.Add(new DaemonStatus
                                {
                                    ServerName = serverApiModel.Name,
                                    DaemonName = daemonApiModel.Name,
                                    EnvName = environment.EnvironmentName,
                                    ServerId = serverApiModel.ServerId,
                                    DaemonId = daemonApiModel.Id
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogInformation("Error retrieving daemon info for " +
                                             daemonApiModel.Name + Environment.NewLine +
                                             "        " + ex.Message + Environment.NewLine +
                                             "        " + ex.InnerException);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation("Error, couldn't ping: " + serverApiModel.Name +
                                     Environment.NewLine + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error building list of servers/daemons" + Environment.NewLine + ex.Message);
            }

            return iResults;
        }

        private List<DaemonStatus> BuildDaemonListForDiscovery(EnvironmentApiModel? environment,
            List<ServerApiModel> servers)
        {
            var iResults = new List<DaemonStatus>();

            try
            {
                var daemons = _daemonsPersistentSource.GetDaemons();

                foreach (var serverApiModel in servers)
                {
                    try
                    {
                        foreach (var daemonApiModel in daemons)
                        {
                            try
                            {
                                iResults.Add(new DaemonStatus
                                {
                                    ServerName = serverApiModel.Name,
                                    DaemonName = daemonApiModel.Name,
                                    EnvName = environment.EnvironmentName,
                                    ServerId = serverApiModel.ServerId,
                                    DaemonId = daemonApiModel.Id
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogInformation("Error retrieving daemon info for {DaemonName}{NewLine}        {Message}{NewLine}        {InnerException}",
                                             SanitizeForLog(daemonApiModel.Name),
                                             Environment.NewLine,
                                             ex.Message,
                                             ex.InnerException);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation("Error, couldn't ping: {ServerName}{NewLine}{Message}",
                                     SanitizeForLog(serverApiModel.Name),
                                     Environment.NewLine,
                                     SanitizeForLog(ex.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error building list of servers/daemons for discovery" + Environment.NewLine + ex.Message);
            }

            return iResults;
        }

        /// <summary>
        /// Record a daemon-probe observation. Best-effort — failures are logged and swallowed so
        /// that observation-write errors do not alter the probe's returned status (HLPS C-03).
        /// </summary>
        private void RecordObservation(DaemonStatus status)
        {
            if (!status.ServerId.HasValue || !status.DaemonId.HasValue)
                return;

            try
            {
                _daemonObservationPersistentSource.InsertObservation(
                    status.ServerId.Value,
                    status.DaemonId.Value,
                    DateTime.Now,
                    status.Status,
                    status.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to record daemon observation for {Server}/{Daemon}",
                    status.ServerName, status.DaemonName);
            }
        }

        /// <summary>
        ///     Act on a daemon. Actions may be: start, stop, restart. Returns the new daemon status.
        /// </summary>
        public DaemonStatus? ChangeDaemonState(DaemonStatus daemonStatus, ClaimsPrincipal principal)
        {
            var environment =
                _environmentsPersistentSource.GetEnvironment(daemonStatus.EnvName, principal);

            GetUsernameAndPassword(environment, out var user, out var pwd);

            // Pre-S-005 behaviour, preserved: state changes always impersonate the deploy
            // account — a missing credential fails the logon rather than silently running as
            // the process identity.
            var credential = new WorkerDaemonCredentialApiModel
            {
                Username = user,
                Domain = _domainName,
                Password = pwd
            };

            return _daemonOperations.ChangeState(credential, daemonStatus);
        }

        public DiscoverDaemonsResult DiscoverAndMapDaemons(string envName, ClaimsPrincipal principal)
        {
            var result = new DiscoverDaemonsResult { Success = true };

            try
            {
                var environment = _environmentsPersistentSource.GetEnvironment(envName, principal);
                if (environment == null)
                {
                    result.Success = false;
                    result.Errors.Add($"Environment '{envName}' not found");
                    return result;
                }

                // Get all servers in the environment
                var servers = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId).ToList();
                result.ServersProcessed = servers.Count;

                // Discover ALL daemons on all servers (ignoring existing mappings)
                var daemonStatuses = DiscoverAllDaemonsForEnvironmentInternal(environment);
                // Filter to only successfully discovered daemons
                var discoveredDaemons = daemonStatuses
                    .Where(ds => ds.ServerId.HasValue &&
                                 ds.DaemonId.HasValue &&
                                 !string.IsNullOrEmpty(ds.Status))
                    .ToList();

                result.DaemonsDiscovered = discoveredDaemons.Count;

                var username = _claimsPrincipalReader.GetUserFullDomainName(principal);

                // Group by unique server-daemon combinations
                var uniqueMappings = discoveredDaemons
                    .GroupBy(ds => new { ds.ServerId, ds.DaemonId })
                    .Select(g => g.First())
                    .ToList();

                // Cache per-server existing mappings to avoid N+1 queries.
                var serverDaemonIdsCache = new Dictionary<int, HashSet<int>>();

                foreach (var daemon in uniqueMappings)
                {
                    try
                    {
                        if (!daemon.ServerId.HasValue || !daemon.DaemonId.HasValue)
                            continue;

                        var serverId = daemon.ServerId.Value;
                        var daemonId = daemon.DaemonId.Value;

                        if (!serverDaemonIdsCache.TryGetValue(serverId, out var mappedDaemonIds))
                        {
                            mappedDaemonIds = _daemonsPersistentSource.GetDaemonsForServer(serverId)
                                .Select(d => d.Id)
                                .ToHashSet();
                            serverDaemonIdsCache[serverId] = mappedDaemonIds;
                        }

                        if (mappedDaemonIds.Contains(daemonId))
                            continue; // Mapping already exists, skip

                        // Create the mapping
                        if (_daemonsPersistentSource.AttachDaemonToServer(serverId, daemonId))
                        {
                            result.MappingsCreated++;

                            // Audit the auto-mapping
                            var payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                ServerId = serverId,
                                DaemonId = daemonId,
                                ServerName = daemon.ServerName,
                                DaemonName = daemon.DaemonName,
                                Status = daemon.Status,
                                Source = "Auto-Discovery",
                                Environment = envName
                            });

                            _daemonAuditPersistentSource.InsertDaemonAudit(
                                username,
                                Dorc.PersistentData.Model.ActionType.Attach,
                                daemonId,
                                fromValue: null,
                                toValue: payload);

                            _serversAuditPersistentSource.InsertServerAudit(
                                username,
                                Dorc.PersistentData.Model.ActionType.Attach,
                                serverId,
                                fromValue: null,
                                toValue: payload);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Error mapping daemon '{daemon.DaemonName}' to server '{daemon.ServerName}': {ex.Message}");
                        _logger.LogError(ex, "Error mapping daemon {DaemonName} to server {ServerName}",
                            daemon.DaemonName, daemon.ServerName);
                    }
                }

                // Convert all discovered daemons to API models for UI display
                result.DiscoveredDaemons = discoveredDaemons
                    .Select(ds => new DaemonStatusApiModel
                    {
                        ServerName = ds.ServerName,
                        DaemonName = ds.DaemonName,
                        Status = ds.Status,
                        EnvName = ds.EnvName,
                        ErrorMessage = ds.ErrorMessage
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Fatal error during daemon discovery: {ex.Message}");
                _logger.LogError(ex, "Fatal error during daemon discovery for environment {EnvName}", SanitizeForLog(envName));
            }

            return result;
        }
        private static string SanitizeForLog(string? input)
        {
            return string.IsNullOrEmpty(input)
                ? string.Empty
                : input.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
    }
}
