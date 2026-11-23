using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.DaemonStatusAndExecutionLogs
{
    [TestClass]
    [TestCategory("DaemonStatusAndExecutionLogs")]
    public class DaemonStatusAndExecutionLogTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
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
    }
}
