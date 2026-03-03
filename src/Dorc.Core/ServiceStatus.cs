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
        private readonly string _domainName;

        public ServiceStatus(IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ServiceStatus> logger, IEnvironmentsPersistentSource environmentsPersistentSource,
            IServersPersistentSource serversPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IConfigurationSettings configurationSettingsEngine)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _configValuesPersistentSource = configValuesPersistentSource;
            _logger = logger;

            _domainName = configurationSettingsEngine.GetConfigurationDomainNameIntra();
        }

        public List<ServicesAndStatus> GetServicesAndStatus(int envId)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envId, null);
            return GetServicesAndStatusForEnvironment(environment);
        }

        public List<ServicesAndStatus> GetServicesAndStatus(string envName, ClaimsPrincipal principal)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envName, principal);
            return GetServicesAndStatusForEnvironment(environment);
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);
        private List<ServicesAndStatus> GetServicesAndStatusForEnvironment(EnvironmentApiModel? environment)
        {
            GetUsernameAndPassword(environment, out var user, out var pwd);

            var domainName = _domainName;

            var servers = _serversPersistentSource.GetServersForEnvId(environment.EnvironmentId).ToList();
            var sas = BuildServicesEnvironment(environment, servers);

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pwd))
            {
                const int logon32ProviderDefault = 0;
                //This parameter causes LogonUser to create a primary token.   
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

                List<ServicesAndStatus> probeResults = [];
                WindowsIdentity.RunImpersonated(
                    safeAccessTokenHandle,
                    // User action  
                    () =>
                    {
                        probeResults = ProbeServiceStatuses(sas);
                    }
                );

                PersistDiscoveredMappings(probeResults, servers);
                return probeResults;
            }

            return sas;
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

        private List<ServicesAndStatus> BuildServicesEnvironment(EnvironmentApiModel? environment,
            List<ServerApiModel> servers)
        {
            var iResults = new List<ServicesAndStatus>();

            try
            {
                foreach (var serverApiModel in servers)
                {
                    try
                    {
                        var daemons = _daemonsPersistentSource.GetDaemonsForServer(serverApiModel.ServerId);

                        // No mappings yet — fall back to all daemons so discovery can happen
                        if (!daemons.Any())
                        {
                            daemons = _daemonsPersistentSource.GetDaemons();
                        }

                        foreach (var daemonApiModel in daemons)
                        {
                            try
                            {
                                iResults.Add(new ServicesAndStatus
                                {
                                    ServerName = serverApiModel.Name,
                                    ServiceName = daemonApiModel.Name,
                                    EnvName = environment.EnvironmentName
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogInformation("Error retrieving servicesAndStatus info for " +
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
                _logger.LogInformation("Error building list of servers/services" + Environment.NewLine + ex.Message);
            }

            return iResults;
        }

        private List<ServicesAndStatus> ProbeServiceStatuses(List<ServicesAndStatus> sas)
        {
            var resultsDict = new ConcurrentDictionary<int, ServicesAndStatus>();

            try
            {
                Parallel.ForEach(sas, (sa, _, index) =>
                {
                    try
                    {
                        var ping = new Ping();
                        var oPingReply = ping.Send(sa.ServerName ?? string.Empty, 5000);
                        if (oPingReply == null || oPingReply.Status != IPStatus.Success)
                            return;

                        try
                        {
                            _logger.LogDebug("Server is alive: " + sa.ServerName);

                            using (var serviceController = new ServiceController(sa.ServiceName, sa.ServerName))
                            {
                                var resultItem = new ServicesAndStatus
                                {
                                    EnvName = sa.EnvName,
                                    ServerName = sa.ServerName,
                                    ServiceName = sa.ServiceName,
                                    ServiceStatus = serviceController.Status.ToString()
                                };
                                resultsDict.TryAdd((int)index, resultItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Error retrieving servicesAndStatus info for " +
                                         sa.ServiceName + Environment.NewLine +
                                         "        " + ex.Message + Environment.NewLine +
                                         "        " + ex.InnerException);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error, couldn't ping: " + sa.ServerName +
                                     Environment.NewLine + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error building list of servers/services" + Environment.NewLine + ex.Message);
            }

            return resultsDict.OrderBy(kvp => kvp.Key)
                      .Select(kvp => kvp.Value)
                      .ToList();
        }

        private void PersistDiscoveredMappings(List<ServicesAndStatus> confirmedResults,
            List<ServerApiModel> servers)
        {
            try
            {
                var confirmedByServer = confirmedResults
                    .GroupBy(r => r.ServerName)
                    .ToDictionary(g => g.Key!, g => g.Select(r => r.ServiceName));

                foreach (var (serverName, serviceNames) in confirmedByServer)
                {
                    var server = servers.FirstOrDefault(s => s.Name == serverName);
                    if (server != null)
                    {
                        _daemonsPersistentSource.DiscoverAndMapDaemonsForServer(
                            server.ServerId, serviceNames);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist discovered daemon mappings");
            }
        }

        /// <summary>
        ///     Make action with the servicesAndStatus. Actions may be: start, stop, restart. Returns new servicesAndStatus status.
        /// </summary>
        /// <param name="servicesAndStatus"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        public ServicesAndStatus? ChangeServiceState(ServicesAndStatus servicesAndStatus, ClaimsPrincipal principal)
        {
            var environment =
                _environmentsPersistentSource.GetEnvironment(servicesAndStatus.EnvName, principal);

            GetUsernameAndPassword(environment, out var user, out var pwd);

            var domainName = _domainName;


            const int logon32ProviderDefault = 0;
            //This parameter causes LogonUser to create a primary token.   
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
                // User action  
                () =>
                {
                    var sc = new ServiceController(servicesAndStatus.ServiceName, servicesAndStatus.ServerName);
                    switch (servicesAndStatus.ServiceStatus.ToLower())
                    {
                        case "start":
                            {
                                sc.Start();
                                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                return GetServiceStatus(servicesAndStatus.EnvName, servicesAndStatus.ServerName, servicesAndStatus.ServiceName);
                            }
                        case "stop":
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                return GetServiceStatus(servicesAndStatus.EnvName, servicesAndStatus.ServerName, servicesAndStatus.ServiceName);
                            }
                        case "restart":
                            {
                                if (sc.CanStop)
                                {
                                    sc.Stop();
                                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                    sc.Start();
                                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                    return GetServiceStatus(servicesAndStatus.EnvName, servicesAndStatus.ServerName, servicesAndStatus.ServiceName);
                                }

                                return new ServicesAndStatus();
                            }
                        default:
                            {
                                return new ServicesAndStatus();
                            }
                    }
                }
            );
        }

        public ServicesAndStatus? GetServiceStatus(string envName, string server, string service)
        {
            try
            {
                var sc = new ServiceController(service, server);
                string status;
                switch (sc.Status)
                {
                    case ServiceControllerStatus.Running:
                        {
                            status = "Running";
                            break;
                        }
                    case ServiceControllerStatus.Stopped:
                        {
                            status = "Stopped";
                            break;
                        }

                    case ServiceControllerStatus.Paused:
                        {
                            status = "Paused";
                            break;
                        }
                    case ServiceControllerStatus.StopPending:
                        {
                            status = "Stopping";
                            break;
                        }

                    case ServiceControllerStatus.StartPending:
                        {
                            status = "Starting";
                            break;
                        }

                    default:
                        {
                            status = "Status Changing";
                            break;
                        }
                }

                var result = new ServicesAndStatus
                {
                    EnvName = envName,
                    ServerName = server,
                    ServiceName = service,
                    ServiceStatus = status
                };
                return result;
            }
            catch
            {
                var result = new ServicesAndStatus();
                return result;
            }
        }
    }
}