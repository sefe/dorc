using Dorc.ApiModel;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;

namespace Dorc.Api.WindowsWorker.Services
{
    // The Windows-only half of the daemon-status path, moved verbatim from
    // Dorc.Core/DaemonStatusProbe.cs at S-005: ping + ServiceController probing and
    // service control, optionally impersonated (LogonUser) as the environment's deploy
    // account. No database access — the primary owns the daemon list and observation
    // recording.
    public class DaemonServiceOperations : IDaemonServiceOperations
    {
        private readonly ILogger<DaemonServiceOperations> _logger;

        public DaemonServiceOperations(ILogger<DaemonServiceOperations> logger)
        {
            _logger = logger;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string? lpszUsername, string? lpszDomain, string? lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);

        public List<WorkerDaemonApiModel> Probe(WorkerDaemonCredentialApiModel? credential, List<WorkerDaemonApiModel> daemons)
        {
            if (credential == null)
            {
                return ProbeStatuses(daemons);
            }

            List<WorkerDaemonApiModel> probeResults = [];
            RunImpersonated(credential, () => { probeResults = ProbeStatuses(daemons); });
            return probeResults;
        }

        /// <summary>
        /// Act on a daemon. Daemon.Status carries the action: Starting, Stopping or
        /// Restarting. Always impersonated — matching the pre-move behaviour, a missing
        /// credential fails the logon rather than silently running as the process identity.
        /// </summary>
        public WorkerDaemonApiModel? ChangeState(WorkerDaemonCredentialApiModel? credential, WorkerDaemonApiModel daemon)
        {
            var safeAccessTokenHandle = Logon(credential);

            using (safeAccessTokenHandle)
            {
                return WindowsIdentity.RunImpersonated(
                    safeAccessTokenHandle,
                    () =>
                    {
                        using var sc = new ServiceController(daemon.DaemonName, daemon.ServerName);
                        switch (daemon.Status)
                        {
                            case "Starting":
                                {
                                    sc.Start();
                                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                    return GetDaemonStatus(daemon.EnvName, daemon.ServerName, daemon.DaemonName);
                                }
                            case "Stopping":
                                {
                                    sc.Stop();
                                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                    return GetDaemonStatus(daemon.EnvName, daemon.ServerName, daemon.DaemonName);
                                }
                            case "Restarting":
                                {
                                    if (sc.CanStop)
                                    {
                                        sc.Stop();
                                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                                        sc.Start();
                                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                        return GetDaemonStatus(daemon.EnvName, daemon.ServerName, daemon.DaemonName);
                                    }

                                    return new WorkerDaemonApiModel();
                                }
                            default:
                                {
                                    return new WorkerDaemonApiModel();
                                }
                        }
                    }
                );
            }
        }

        private void RunImpersonated(WorkerDaemonCredentialApiModel credential, Action action)
        {
            var safeAccessTokenHandle = Logon(credential);

            using (safeAccessTokenHandle)
            {
                WindowsIdentity.RunImpersonated(safeAccessTokenHandle, action);
            }
        }

        private SafeAccessTokenHandle Logon(WorkerDaemonCredentialApiModel? credential)
        {
            const int logon32ProviderDefault = 0;
            // This parameter causes LogonUser to create a primary token.
            const int logon32LogonInteractive = 2;

            bool returnValue = LogonUser(credential?.Username, credential?.Domain, credential?.Password,
                logon32LogonInteractive, logon32ProviderDefault,
                out var safeAccessTokenHandle);

            if (false == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                _logger.LogError("LogonUser failed with error code: {ErrorCode}", ret);
                throw new System.ComponentModel.Win32Exception(ret);
            }

            return safeAccessTokenHandle;
        }

        private List<WorkerDaemonApiModel> ProbeStatuses(List<WorkerDaemonApiModel> daemons)
        {
            var resultsDict = new ConcurrentDictionary<int, WorkerDaemonApiModel>();

            try
            {
                Parallel.ForEach(daemons, (daemon, _, index) =>
                {
                    try
                    {
                        var ping = new Ping();
                        var oPingReply = ping.Send(daemon.ServerName ?? string.Empty, 5000);
                        if (oPingReply == null || oPingReply.Status != IPStatus.Success)
                            return;

                        try
                        {
                            _logger.LogDebug("Server is alive: {ServerName}", daemon.ServerName);

                            using (var serviceController = new ServiceController(daemon.DaemonName, daemon.ServerName))
                            {
                                var resultItem = new WorkerDaemonApiModel
                                {
                                    EnvName = daemon.EnvName,
                                    ServerName = daemon.ServerName,
                                    DaemonName = daemon.DaemonName,
                                    ServerId = daemon.ServerId,
                                    DaemonId = daemon.DaemonId,
                                    Status = serviceController.Status.ToString()
                                };
                                resultsDict.TryAdd((int)index, resultItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Error retrieving daemon info for {DaemonName}: {Message} {InnerException}",
                                daemon.DaemonName, ex.Message, ex.InnerException);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error, couldn't ping: {ServerName}: {Message}",
                            daemon.ServerName, ex.Message);
                        return;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error probing daemon statuses: {Message}", ex.Message);
            }

            return resultsDict.OrderBy(kvp => kvp.Key)
                      .Select(kvp => kvp.Value)
                      .ToList();
        }

        private WorkerDaemonApiModel GetDaemonStatus(string? envName, string? server, string? daemonName)
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

                return new WorkerDaemonApiModel
                {
                    EnvName = envName,
                    ServerName = server,
                    DaemonName = daemonName,
                    Status = status
                };
            }
            catch (Exception ex)
            {
                return new WorkerDaemonApiModel
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
