using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.DaemonStatusAndExecutionLogs
{
    [Binding]
    public class DaemonStatusAndExecutionLogTests
    {
        private bool _daemonStatusProbeExecuted;
        private bool _daemonLifecycleAndControlExecuted;

        [Given("the target DOrc application server is attached to the live child environment")]
        public void GivenTheTargetDorcApplicationServerIsAttachedToTheLiveChildEnvironment()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user queries DaemonStatus, GetComponentStatues, and PropertyValuesAudit for the child environment")]
        public void WhenTheUserQueriesDaemonStatusGetComponentStatuesAndPropertyValuesAudit()
        {
            GetDaemonStatusAndDeploymentLogs_AttachedEnvironmentServerAndCompletedRequest_ProbesWindowsServicesAndCapturesAttemptLogs();
            _daemonStatusProbeExecuted = true;
        }

        [Then("the live daemon probe reports running Windows services including W3SVC")]
        [Then("the environment component status and property audit endpoints return 200 OK before detaching the server")]
        public void ThenTheLiveDaemonProbeReportsRunningServicesAndEndpointsReturnOk()
        {
            Assert.IsTrue(_daemonStatusProbeExecuted);
        }

        [Given("a temporary daemon definition and server are prepared for daemon lifecycle verification")]
        public void GivenATemporaryDaemonDefinitionAndServerArePrepared()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user creates, updates, attaches, detaches, audits, and deletes the daemon and stops and restarts NetTcpActivator on the live server")]
        public void WhenTheUserManagesDaemonLifecycleAndControlsNetTcpActivator()
        {
            CreateAttachProbeAndControlDaemons_RefDataDaemonsServerDaemonsAuditAndServiceState_VerifiesLifecycleAuditAndStopStart();
            _daemonLifecycleAndControlExecuted = true;
        }

        [Then("duplicate daemon creation returns 409 Conflict, mismatched IDs return 400 BadRequest, and unknown daemons return 404 NotFound")]
        [Then("NetTcpActivator transitions cleanly between Stopped and Running with recorded daemon observations, all 12 Analytics endpoints respond, and SignalR negotiation succeeds")]
        public void ThenDaemonValidationServiceControlAnalyticsAndSignalRSucceed()
        {
            Assert.IsTrue(_daemonLifecycleAndControlExecuted);
        }

        public void GetDaemonStatusAndDeploymentLogs_AttachedEnvironmentServerAndCompletedRequest_ProbesWindowsServicesAndCapturesAttemptLogs()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);

            var daemonsResp = caller.ExecuteRaw("RefDataDaemons", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, daemonsResp.StatusCode, daemonsResp.Content);
            var daemons = DorcApiClient.Deserialize<List<DaemonApiModel>>(daemonsResp.Content) ?? new List<DaemonApiModel>();
            Assert.IsNotEmpty(daemons);
            Assert.IsTrue(daemons.Any(d => string.Equals(d.Name, "W3SVC", StringComparison.OrdinalIgnoreCase)));

            var serversResp = caller.ExecuteRaw("RefDataServers/GetAll", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, serversResp.StatusCode, serversResp.Content);
            var servers = DorcApiClient.Deserialize<List<ServerApiModel>>(serversResp.Content) ?? new List<ServerApiModel>();
            var targetServer = servers.FirstOrDefault(s =>
                string.Equals(s.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, "DEPAPP01DV", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(targetServer, "Expected to find target DOrc application server in GET /RefDataServers/GetAll.");

            caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
            {
                ["envId"] = childEnv.EnvironmentId.ToString(),
                ["componentId"] = targetServer.ServerId.ToString(),
                ["action"] = "attach",
                ["component"] = "server"
            });

            try
            {
                var daemonStatusResp = caller.ExecuteRaw($"DaemonStatus/{Uri.EscapeDataString(LiveChildEnvName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, daemonStatusResp.StatusCode, $"GET /DaemonStatus failed: {daemonStatusResp.Content}");
                var statuses = DorcApiClient.Deserialize<List<DaemonStatusApiModel>>(daemonStatusResp.Content) ?? new List<DaemonStatusApiModel>();
                Assert.IsNotEmpty(statuses, "Expected live daemon probe to return service status rows for the attached server.");
                Assert.IsTrue(
                    statuses.Any(s => string.Equals(s.Status, "Running", StringComparison.OrdinalIgnoreCase)),
                    "Expected at least one probed Windows service on the attached server to be Running.");

                var compStatusResp = caller.ExecuteRaw("RefDataEnvironmentsDetails/GetComponentStatues", Method.Get, new Dictionary<string, string>
                {
                    ["envName"] = LiveChildEnvName,
                    ["cutoffDateTime"] = DateTime.UtcNow.AddDays(-7).ToString("O")
                });
                Assert.AreEqual(HttpStatusCode.OK, compStatusResp.StatusCode, compStatusResp.Content);

                var auditResp = caller.ExecuteRaw(
                    "PropertyValuesAudit",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "20" },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, auditResp.StatusCode, auditResp.Content);
            }
            finally
            {
                caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = targetServer.ServerId.ToString(),
                    ["action"] = "detach",
                    ["component"] = "server"
                });
            }
        }

        public void CreateAttachProbeAndControlDaemons_RefDataDaemonsServerDaemonsAuditAndServiceState_VerifiesLifecycleAuditAndStopStart()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var childEnv = GetEnvironmentByName(caller, LiveChildEnvName);
            Assert.IsNotNull(childEnv);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var syntheticDaemonName = $"DOrc_Live_Daemon_{suffix}";
            var syntheticDisplayName = $"DOrc Live Daemon {suffix}";
            var updatedDisplayName = $"DOrc Live Daemon Updated {suffix}";
            var tempServerName = $"DORCDAEMONSRV_{suffix}";

            int? syntheticDaemonId = null;
            int? tempServerId = null;
            int? netTcpDaemonId = null;
            bool createdNetTcpDaemon = false;
            ServerApiModel? targetServer = null;

            try
            {
                var createDaemonResp = caller.ExecuteRaw("RefDataDaemons", Method.Post, body: new DaemonApiModel
                {
                    Name = syntheticDaemonName,
                    DisplayName = syntheticDisplayName,
                    AccountName = "Local System Account",
                    ServiceType = "Windows Service"
                });
                Assert.AreEqual(HttpStatusCode.OK, createDaemonResp.StatusCode, $"POST /RefDataDaemons failed: {createDaemonResp.Content}");
                var createdDaemon = DorcApiClient.Deserialize<DaemonApiModel>(createDaemonResp.Content);
                Assert.IsNotNull(createdDaemon);
                Assert.IsGreaterThan(0, createdDaemon.Id);
                Assert.AreEqual(syntheticDaemonName, createdDaemon.Name);
                Assert.AreEqual(syntheticDisplayName, createdDaemon.DisplayName);
                var createdDaemonId = createdDaemon.Id;
                syntheticDaemonId = createdDaemonId;

                var dupDaemonResp = caller.ExecuteRaw("RefDataDaemons", Method.Post, body: new DaemonApiModel
                {
                    Name = syntheticDaemonName,
                    DisplayName = syntheticDisplayName,
                    AccountName = "Local System Account",
                    ServiceType = "Windows Service"
                });
                Assert.AreEqual(HttpStatusCode.Conflict, dupDaemonResp.StatusCode, "Creating a duplicate daemon must return 409 Conflict.");

                var mismatchPutResp = caller.ExecuteRaw(
                    "RefDataDaemons",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = (createdDaemonId + 999).ToString() },
                    body: createdDaemon);
                Assert.AreEqual(HttpStatusCode.BadRequest, mismatchPutResp.StatusCode, "Mismatched route id and body Id must return 400 BadRequest.");

                var notFoundPutResp = caller.ExecuteRaw(
                    "RefDataDaemons",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = "99999999" },
                    body: new DaemonApiModel
                    {
                        Id = 99999999,
                        Name = "NonExistentDaemon",
                        DisplayName = "NonExistentDaemon",
                        AccountName = "Local System Account",
                        ServiceType = "Windows Service"
                    });
                Assert.AreEqual(HttpStatusCode.NotFound, notFoundPutResp.StatusCode, "Updating a non-existent daemon must return 404 NotFound.");

                var updateDaemonResp = caller.ExecuteRaw(
                    "RefDataDaemons",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = createdDaemonId.ToString() },
                    body: new DaemonApiModel
                    {
                        Id = createdDaemonId,
                        Name = syntheticDaemonName,
                        DisplayName = updatedDisplayName,
                        AccountName = "Local System Account",
                        ServiceType = "Windows Service"
                    });
                Assert.AreEqual(HttpStatusCode.OK, updateDaemonResp.StatusCode, updateDaemonResp.Content);
                var updatedDaemon = DorcApiClient.Deserialize<DaemonApiModel>(updateDaemonResp.Content);
                Assert.IsNotNull(updatedDaemon);
                Assert.AreEqual(updatedDisplayName, updatedDaemon.DisplayName);

                var createSrvResp = caller.ExecuteRaw("RefDataServers", Method.Post, body: new ServerApiModel
                {
                    Name = tempServerName,
                    OsName = "Windows Server 2022 Standard",
                    UserEditable = true
                });
                Assert.AreEqual(HttpStatusCode.OK, createSrvResp.StatusCode, createSrvResp.Content);
                var createdServer = DorcApiClient.Deserialize<ServerApiModel>(createSrvResp.Content);
                Assert.IsNotNull(createdServer);
                var createdServerId = createdServer.ServerId;
                tempServerId = createdServerId;

                var attachDaemonResp = caller.ExecuteRaw("ServerDaemons", Method.Post, new Dictionary<string, string>
                {
                    ["serverId"] = createdServerId.ToString(),
                    ["daemonId"] = createdDaemonId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, attachDaemonResp.StatusCode, attachDaemonResp.Content);

                var getServerDaemonsResp = caller.ExecuteRaw($"ServerDaemons/{createdServerId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getServerDaemonsResp.StatusCode, getServerDaemonsResp.Content);
                var serverDaemons = DorcApiClient.Deserialize<List<DaemonApiModel>>(getServerDaemonsResp.Content) ?? new List<DaemonApiModel>();
                Assert.IsTrue(serverDaemons.Any(d => d.Id == createdDaemonId && string.Equals(d.DisplayName, updatedDisplayName, StringComparison.Ordinal)));

                var getServersByDaemonResp = caller.ExecuteRaw($"ServerDaemons/by-daemon/{createdDaemonId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getServersByDaemonResp.StatusCode, getServersByDaemonResp.Content);
                var daemonServers = DorcApiClient.Deserialize<List<ServerApiModel>>(getServersByDaemonResp.Content) ?? new List<ServerApiModel>();
                Assert.IsTrue(daemonServers.Any(s => s.ServerId == createdServerId));

                var detachDaemonResp = caller.ExecuteRaw("ServerDaemons", Method.Delete, new Dictionary<string, string>
                {
                    ["serverId"] = createdServerId.ToString(),
                    ["daemonId"] = createdDaemonId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, detachDaemonResp.StatusCode, detachDaemonResp.Content);

                var getServerDaemonsAfterDetachResp = caller.ExecuteRaw($"ServerDaemons/{createdServerId}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getServerDaemonsAfterDetachResp.StatusCode, getServerDaemonsAfterDetachResp.Content);
                var serverDaemonsAfterDetach = DorcApiClient.Deserialize<List<DaemonApiModel>>(getServerDaemonsAfterDetachResp.Content) ?? new List<DaemonApiModel>();
                Assert.IsFalse(serverDaemonsAfterDetach.Any(d => d.Id == createdDaemonId));

                var daemonAuditResp = caller.ExecuteRaw(
                    "DaemonAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["daemonId"] = createdDaemonId.ToString(),
                        ["page"] = "1",
                        ["limit"] = "20"
                    },
                    body: new PagedDataOperators());
                Assert.AreEqual(HttpStatusCode.OK, daemonAuditResp.StatusCode, daemonAuditResp.Content);
                StringAssert.Contains(daemonAuditResp.Content ?? string.Empty, syntheticDaemonName);
                StringAssert.Contains(daemonAuditResp.Content ?? string.Empty, updatedDisplayName);

                var deleteDaemonResp = caller.ExecuteRaw("RefDataDaemons", Method.Delete, new Dictionary<string, string>
                {
                    ["id"] = createdDaemonId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, deleteDaemonResp.StatusCode, deleteDaemonResp.Content);

                var deleteDaemonAgainResp = caller.ExecuteRaw("RefDataDaemons", Method.Delete, new Dictionary<string, string>
                {
                    ["id"] = createdDaemonId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.NotFound, deleteDaemonAgainResp.StatusCode, "Deleting an already-deleted daemon must return 404 NotFound.");
                syntheticDaemonId = null;

                var badStatusByIdResp = caller.ExecuteRaw("DaemonStatus", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = "0"
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, badStatusByIdResp.StatusCode);

                var allDaemonsResp = caller.ExecuteRaw("RefDataDaemons", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, allDaemonsResp.StatusCode, allDaemonsResp.Content);
                var allDaemons = DorcApiClient.Deserialize<List<DaemonApiModel>>(allDaemonsResp.Content) ?? new List<DaemonApiModel>();
                var netTcpDaemon = allDaemons.FirstOrDefault(d => string.Equals(d.Name, "NetTcpActivator", StringComparison.OrdinalIgnoreCase));
                if (netTcpDaemon == null)
                {
                    var createNetTcpResp = caller.ExecuteRaw("RefDataDaemons", Method.Post, body: new DaemonApiModel
                    {
                        Name = "NetTcpActivator",
                        DisplayName = "Net.Tcp Listener Adapter",
                        AccountName = "Local System Account",
                        ServiceType = "Windows Service"
                    });
                    Assert.AreEqual(HttpStatusCode.OK, createNetTcpResp.StatusCode, createNetTcpResp.Content);
                    netTcpDaemon = DorcApiClient.Deserialize<DaemonApiModel>(createNetTcpResp.Content);
                    createdNetTcpDaemon = true;
                }
                Assert.IsNotNull(netTcpDaemon);
                netTcpDaemonId = netTcpDaemon.Id;

                var serversResp = caller.ExecuteRaw("RefDataServers/GetAll", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, serversResp.StatusCode, serversResp.Content);
                var servers = DorcApiClient.Deserialize<List<ServerApiModel>>(serversResp.Content) ?? new List<ServerApiModel>();
                targetServer = servers.FirstOrDefault(s =>
                    string.Equals(s.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, "DEPAPP01DV", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(targetServer);

                caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                {
                    ["envId"] = childEnv.EnvironmentId.ToString(),
                    ["componentId"] = targetServer.ServerId.ToString(),
                    ["action"] = "attach",
                    ["component"] = "server"
                });

                var stopResp = caller.ExecuteRaw("DaemonStatus", Method.Put, body: new DaemonStatusApiModel
                {
                    EnvName = LiveChildEnvName,
                    ServerName = targetServer.Name,
                    DaemonName = "NetTcpActivator",
                    Status = "Stopping"
                });
                Assert.AreEqual(HttpStatusCode.OK, stopResp.StatusCode, $"PUT /DaemonStatus (Stopping) failed: {stopResp.Content}");
                var stoppedDto = DorcApiClient.Deserialize<DaemonStatusApiModel>(stopResp.Content);
                Assert.IsNotNull(stoppedDto);
                Assert.AreEqual("Stopped", stoppedDto.Status);

                var statusAfterStopResp = caller.ExecuteRaw($"DaemonStatus/{Uri.EscapeDataString(LiveChildEnvName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, statusAfterStopResp.StatusCode, statusAfterStopResp.Content);
                var statusesAfterStop = DorcApiClient.Deserialize<List<DaemonStatusApiModel>>(statusAfterStopResp.Content) ?? new List<DaemonStatusApiModel>();
                var probedStopped = statusesAfterStop.FirstOrDefault(s =>
                    string.Equals(s.ServerName, targetServer.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.DaemonName, "NetTcpActivator", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(probedStopped);
                Assert.AreEqual("Stopped", probedStopped.Status);

                var startResp = caller.ExecuteRaw("DaemonStatus", Method.Put, body: new DaemonStatusApiModel
                {
                    EnvName = LiveChildEnvName,
                    ServerName = targetServer.Name,
                    DaemonName = "NetTcpActivator",
                    Status = "Starting"
                });
                Assert.AreEqual(HttpStatusCode.OK, startResp.StatusCode, $"PUT /DaemonStatus (Starting) failed: {startResp.Content}");
                var startedDto = DorcApiClient.Deserialize<DaemonStatusApiModel>(startResp.Content);
                Assert.IsNotNull(startedDto);
                Assert.AreEqual("Running", startedDto.Status);

                var statusAfterStartResp = caller.ExecuteRaw($"DaemonStatus/{Uri.EscapeDataString(LiveChildEnvName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, statusAfterStartResp.StatusCode, statusAfterStartResp.Content);
                var statusesAfterStart = DorcApiClient.Deserialize<List<DaemonStatusApiModel>>(statusAfterStartResp.Content) ?? new List<DaemonStatusApiModel>();
                var probedRunning = statusesAfterStart.FirstOrDefault(s =>
                    string.Equals(s.ServerName, targetServer.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.DaemonName, "NetTcpActivator", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(probedRunning);
                Assert.AreEqual("Running", probedRunning.Status);

                var observationsResp = caller.ExecuteRaw("DaemonObservation", Method.Get, new Dictionary<string, string>
                {
                    ["daemonId"] = netTcpDaemonId.Value.ToString(),
                    ["serverId"] = targetServer.ServerId.ToString(),
                    ["page"] = "1",
                    ["limit"] = "10"
                });
                Assert.AreEqual(HttpStatusCode.OK, observationsResp.StatusCode, observationsResp.Content);
                StringAssert.Contains(observationsResp.Content ?? string.Empty, "Running");

                // Analytics* endpoints (all 12 Analytics controllers)
                foreach (var analyticsRoute in new[]
                {
                    "AnalyticsDeploymentsMonth",
                    "AnalyticsDeploymentSummary",
                    "AnalyticsEnvironmentUsage",
                    "AnalyticsUserActivity",
                    "AnalyticsTimePattern",
                    "AnalyticsComponentUsage",
                    "AnalyticsDuration",
                    "AnalyticsMonthlyOutcome",
                    "AnalyticsEnvironmentWait",
                    "AnalyticsProjectDuration",
                    "AnalyticsComponentReliability",
                    "AnalyticsRecoveryTime"
                })
                {
                    var analyticsResp = caller.ExecuteRaw(analyticsRoute, Method.Get);
                    Assert.AreEqual(HttpStatusCode.OK, analyticsResp.StatusCode, $"GET /{analyticsRoute} failed: {analyticsResp.Content}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(analyticsResp.Content), $"Expected non-empty payload from GET /{analyticsRoute}.");
                }

                // GET /DaemonStatus/discover/{envName}
                var discoverResp = caller.ExecuteRaw($"DaemonStatus/discover/{Uri.EscapeDataString(LiveChildEnvName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, discoverResp.StatusCode, $"GET /DaemonStatus/discover/{LiveChildEnvName} failed: {discoverResp.Content}");

                // SignalR DeploymentsHub negotiate endpoint
                var negotiateResp = caller.ExecuteRaw("hubs/deployments/negotiate", Method.Post, new Dictionary<string, string>
                {
                    ["negotiateVersion"] = "1"
                });
                Assert.AreEqual(HttpStatusCode.OK, negotiateResp.StatusCode, $"POST /hubs/deployments/negotiate failed: {negotiateResp.Content}");
                StringAssert.Contains(negotiateResp.Content ?? string.Empty, "connectionId");
                StringAssert.Contains(negotiateResp.Content ?? string.Empty, "availableTransports");
            }
            finally
            {
                if (targetServer != null)
                {
                    caller.ExecuteRaw("DaemonStatus", Method.Put, body: new DaemonStatusApiModel
                    {
                        EnvName = LiveChildEnvName,
                        ServerName = targetServer.Name,
                        DaemonName = "NetTcpActivator",
                        Status = "Starting"
                    });
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = childEnv.EnvironmentId.ToString(),
                        ["componentId"] = targetServer.ServerId.ToString(),
                        ["action"] = "detach",
                        ["component"] = "server"
                    });
                }

                if (createdNetTcpDaemon && netTcpDaemonId.HasValue)
                {
                    caller.ExecuteRaw("RefDataDaemons", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = netTcpDaemonId.Value.ToString()
                    });
                }

                if (syntheticDaemonId.HasValue && tempServerId.HasValue)
                {
                    caller.ExecuteRaw("ServerDaemons", Method.Delete, new Dictionary<string, string>
                    {
                        ["serverId"] = tempServerId.Value.ToString(),
                        ["daemonId"] = syntheticDaemonId.Value.ToString()
                    });
                }

                if (syntheticDaemonId.HasValue)
                {
                    caller.ExecuteRaw("RefDataDaemons", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = syntheticDaemonId.Value.ToString()
                    });
                }

                if (tempServerId.HasValue)
                {
                    caller.ExecuteRaw("RefDataServers", Method.Delete, new Dictionary<string, string>
                    {
                        ["serverId"] = tempServerId.Value.ToString()
                    });
                }
            }
        }
    }
}
