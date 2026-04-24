using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using System.ServiceProcess;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Environment = System.Environment;

namespace Dorc.Core
{
    [SupportedOSPlatform("windows")]
    public class ServiceStatus : IServiceStatus
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
        private readonly string _domainName;

        public ServiceStatus(IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ServiceStatus> logger, IEnvironmentsPersistentSource environmentsPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IDaemonObservationPersistentSource daemonObservationPersistentSource,
            IConfigurationSettings configurationSettingsEngine)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _daemonObservationPersistentSource = daemonObservationPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _configValuesPersistentSource = configValuesPersistentSource;
            _logger = logger;

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

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);

        private List<DaemonStatus> GetDaemonStatusesForEnvironment(EnvironmentApiModel? environment)
        {
            GetUsernameAndPassword(environment, out var user, out var pwd);

            var domainName = _domainName;

            var servers = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId).ToList();
            var daemons = BuildDaemonList(environment, servers);

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pwd))
            {
                const int logon32ProviderDefault = 0;
                // This parameter causes LogonUser to create a primary token.
                const int logon32LogonInteractive = 2;

                bool returnValue = LogonUser(user, domainName, pwd,
                    logon32LogonInteractive, logon32ProviderDefault,
                    out var safeAccessTokenHandle);

                if (false == returnValue)
                {
                    int ret = Marshal.GetLastWin32Error();
                    Console.WriteLine("LogonUser failed with error code : {0}", ret);
                    throw new System.ComponentModel.Win32Exception(ret);
                }

                List<DaemonStatus> probeResults = [];
                WindowsIdentity.RunImpersonated(
                    safeAccessTokenHandle,
                    () =>
                    {
                        probeResults = ProbeDaemonStatuses(daemons);
                    }
                );

                return probeResults;
            }

            return daemons;
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

        private List<DaemonStatus> ProbeDaemonStatuses(List<DaemonStatus> daemons)
        {
            var resultsDict = new ConcurrentDictionary<int, DaemonStatus>();

            try
            {
                Parallel.ForEach(daemons, (daemon, _, index) =>
                {
                    try
                    {
                        var ping = new Ping();
                        var oPingReply = ping.Send(daemon.ServerName ?? string.Empty, 5000);
                        if (oPingReply == null || oPingReply.Status != IPStatus.Success)
                        {
                            var unreachable = new DaemonStatus
                            {
                                EnvName = daemon.EnvName,
                                ServerName = daemon.ServerName,
                                DaemonName = daemon.DaemonName,
                                ServerId = daemon.ServerId,
                                DaemonId = daemon.DaemonId,
                                Status = null,
                                ErrorMessage = "Server unreachable: ping " +
                                    (oPingReply?.Status.ToString() ?? "no reply")
                            };
                            resultsDict.TryAdd((int)index, unreachable);
                            RecordObservation(unreachable);
                            return;
                        }

                        try
                        {
                            _logger.LogDebug("Server is alive: " + daemon.ServerName);

                            using (var serviceController = new ServiceController(daemon.DaemonName, daemon.ServerName))
                            {
                                var resultItem = new DaemonStatus
                                {
                                    EnvName = daemon.EnvName,
                                    ServerName = daemon.ServerName,
                                    DaemonName = daemon.DaemonName,
                                    ServerId = daemon.ServerId,
                                    DaemonId = daemon.DaemonId,
                                    Status = serviceController.Status.ToString()
                                };
                                resultsDict.TryAdd((int)index, resultItem);
                                RecordObservation(resultItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Error retrieving daemon info for " +
                                         daemon.DaemonName + Environment.NewLine +
                                         "        " + ex.Message + Environment.NewLine +
                                         "        " + ex.InnerException);

                            var queryFailed = new DaemonStatus
                            {
                                EnvName = daemon.EnvName,
                                ServerName = daemon.ServerName,
                                DaemonName = daemon.DaemonName,
                                ServerId = daemon.ServerId,
                                DaemonId = daemon.DaemonId,
                                Status = null,
                                ErrorMessage = "Daemon query failed: " + ex.Message
                            };
                            resultsDict.TryAdd((int)index, queryFailed);
                            RecordObservation(queryFailed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error, couldn't ping: " + daemon.ServerName +
                                     Environment.NewLine + ex.Message);

                        var pingFailed = new DaemonStatus
                        {
                            EnvName = daemon.EnvName,
                            ServerName = daemon.ServerName,
                            DaemonName = daemon.DaemonName,
                            ServerId = daemon.ServerId,
                            DaemonId = daemon.DaemonId,
                            Status = null,
                            ErrorMessage = "Server unreachable: " + ex.Message
                        };
                        resultsDict.TryAdd((int)index, pingFailed);
                        RecordObservation(pingFailed);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error probing daemon statuses" + Environment.NewLine + ex.Message);
            }

            return resultsDict.OrderBy(kvp => kvp.Key)
                      .Select(kvp => kvp.Value)
                      .ToList();
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

            var domainName = _domainName;

            const int logon32ProviderDefault = 0;
            // This parameter causes LogonUser to create a primary token.
            const int logon32LogonInteractive = 2;

            bool returnValue = LogonUser(user, domainName, pwd,
                logon32LogonInteractive, logon32ProviderDefault,
                out var safeAccessTokenHandle);

            if (false == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                Console.WriteLine("LogonUser failed with error code : {0}", ret);
                throw new System.ComponentModel.Win32Exception(ret);
            }

            return WindowsIdentity.RunImpersonated(
                safeAccessTokenHandle,
                () =>
                {
                    using var sc = new ServiceController(daemonStatus.DaemonName, daemonStatus.ServerName);
                    switch (daemonStatus.Status.ToLower())
                    {
                        case "start":
                            {
                                sc.Start();
                                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                return GetDaemonStatus(daemonStatus.EnvName, daemonStatus.ServerName, daemonStatus.DaemonName);
                            }
                        case "stop":
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                return GetDaemonStatus(daemonStatus.EnvName, daemonStatus.ServerName, daemonStatus.DaemonName);
                            }
                        case "restart":
                            {
                                if (sc.CanStop)
                                {
                                    sc.Stop();
                                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                    sc.Start();
                                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                    return GetDaemonStatus(daemonStatus.EnvName, daemonStatus.ServerName, daemonStatus.DaemonName);
                                }

                                return new DaemonStatus();
                            }
                        default:
                            {
                                return new DaemonStatus();
                            }
                    }
                }
            );
        }

        private DaemonStatus? GetDaemonStatus(string envName, string server, string daemonName)
        {
            try
            {
                using var sc = new ServiceController(daemonName, server);
                string status;
                switch (sc.Status)
                {
                    case ServiceControllerStatus.Running:
                        status = "Running";
                        break;
                    case ServiceControllerStatus.Stopped:
                        status = "Stopped";
                        break;
                    case ServiceControllerStatus.Paused:
                        status = "Paused";
                        break;
                    case ServiceControllerStatus.StopPending:
                        status = "Stopping";
                        break;
                    case ServiceControllerStatus.StartPending:
                        status = "Starting";
                        break;
                    default:
                        status = "Status Changing";
                        break;
                }

                return new DaemonStatus
                {
                    EnvName = envName,
                    ServerName = server,
                    DaemonName = daemonName,
                    Status = status
                };
            }
            catch (Exception ex)
            {
                return new DaemonStatus
                {
                    EnvName = envName,
                    ServerName = server,
                    DaemonName = daemonName,
                    Status = null,
                    ErrorMessage = "Daemon query failed: " + ex.Message
                };
            }
        }
    }
}
