using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core;
using NSubstitute;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Dorc.Api.Tests
{
    // S-005: the primary-side seam (WorkerDaemonOperations) and the HTTP client legs it
    // rides on (daemons/probe, daemons/change-state, remote-server/reboot).
    [TestClass]
    public class WorkerDaemonOperationsTests
    {
        [TestMethod]
        public void ProbeStatuses_RoundTripsTheWireShape()
        {
            var client = Substitute.For<IWindowsWorkerClient>();
            WorkerDaemonProbeRequestApiModel? seen = null;
            client.ProbeDaemonStatusesAsync(Arg.Any<WorkerDaemonProbeRequestApiModel>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    seen = ci.Arg<WorkerDaemonProbeRequestApiModel>();
                    return Task.FromResult(new List<WorkerDaemonApiModel>
                    {
                        new() { EnvName = "E", ServerName = "S", DaemonName = "D", Status = "Running", ServerId = 1, DaemonId = 2 }
                    });
                });

            var credential = new WorkerDaemonCredentialApiModel { Username = "u", Domain = "d", Password = "p" };
            var daemons = new List<DaemonStatus>
            {
                new() { EnvName = "E", ServerName = "S", DaemonName = "D", ServerId = 1, DaemonId = 2 }
            };

            var result = new WorkerDaemonOperations(client).ProbeStatuses(credential, daemons);

            Assert.IsNotNull(seen);
            Assert.AreSame(credential, seen!.Credential);
            Assert.AreEqual(1, seen.Daemons.Count);
            Assert.AreEqual("S", seen.Daemons[0].ServerName);
            Assert.AreEqual(2, seen.Daemons[0].DaemonId);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Running", result[0].Status);
            Assert.AreEqual(1, result[0].ServerId);
            Assert.AreEqual(2, result[0].DaemonId);
        }

        [TestMethod]
        public void ChangeState_CarriesTheActionInStatus()
        {
            var client = Substitute.For<IWindowsWorkerClient>();
            WorkerDaemonStateChangeRequestApiModel? seen = null;
            client.ChangeDaemonStateAsync(Arg.Any<WorkerDaemonStateChangeRequestApiModel>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    seen = ci.Arg<WorkerDaemonStateChangeRequestApiModel>();
                    return Task.FromResult(new WorkerDaemonApiModel { Status = "Running" });
                });

            var result = new WorkerDaemonOperations(client).ChangeState(
                null,
                new DaemonStatus { EnvName = "E", ServerName = "S", DaemonName = "D", Status = "Starting" });

            Assert.AreEqual("Starting", seen!.Daemon.Status);
            Assert.AreEqual("Running", result!.Status);
        }

        [TestMethod]
        public async Task ProbeDaemonStatuses_PostsToTheProbeEndpoint()
        {
            var handler = new CapturingCannedHandler(HttpStatusCode.OK,
                JsonSerializer.Serialize(new[] { new WorkerDaemonApiModel { Status = "Running" } }));
            var client = NewClient(handler);

            var result = await client.ProbeDaemonStatusesAsync(new WorkerDaemonProbeRequestApiModel
            {
                Daemons = new List<WorkerDaemonApiModel> { new() { ServerName = "S", DaemonName = "D" } }
            });

            Assert.AreEqual("/daemons/probe", handler.LastRequestPath);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Running", result[0].Status);
        }

        [TestMethod]
        public async Task ChangeDaemonState_WorkerRejection_SurfacesAsRejection()
        {
            var handler = new CapturingCannedHandler(HttpStatusCode.BadRequest,
                "{\"error\":\"Failed to log on as the supplied deploy account: bad password\"}");
            var client = NewClient(handler);

            var ex = await Assert.ThrowsExactlyAsync<WorkerRequestRejectedException>(
                () => client.ChangeDaemonStateAsync(new WorkerDaemonStateChangeRequestApiModel()));

            Assert.AreEqual("/daemons/change-state", handler.LastRequestPath);
            Assert.AreEqual("Failed to log on as the supplied deploy account: bad password", ex.Message);
        }

        [TestMethod]
        public async Task RebootServer_PostsServerNameToTheRebootEndpoint()
        {
            var handler = new CapturingCannedHandler(HttpStatusCode.OK, "{\"result\":true}");
            var client = NewClient(handler);

            await client.RebootServerAsync("SRV 01");

            Assert.AreEqual("/remote-server/reboot", handler.LastRequestPath);
            StringAssert.Contains(handler.LastRequestQuery, "serverName=SRV%2001");
        }

        [TestMethod]
        public async Task ProbeDaemonStatuses_ConnectionFailure_ThrowsWorkerUnavailable()
        {
            var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
            var client = NewClient(handler);

            var ex = await Assert.ThrowsExactlyAsync<WorkerUnavailableException>(
                () => client.ProbeDaemonStatusesAsync(new WorkerDaemonProbeRequestApiModel()));
            Assert.AreEqual("daemons/probe", ex.Endpoint);
        }

        private static HttpWindowsWorkerClient NewClient(HttpMessageHandler handler)
            => new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5005/") });

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _ex;
            public ThrowingHandler(Exception ex) => _ex = ex;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(_ex);
        }

        private sealed class CapturingCannedHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            private readonly List<HttpResponseMessage> _issuedResponses = new();

            public string? LastRequestPath { get; private set; }
            public string? LastRequestQuery { get; private set; }

            public CapturingCannedHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestPath = request.RequestUri?.AbsolutePath;
                LastRequestQuery = request.RequestUri?.Query;
                var response = new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                };
                _issuedResponses.Add(response);
                return Task.FromResult(response);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (var response in _issuedResponses)
                    {
                        response.Dispose();
                    }
                    _issuedResponses.Clear();
                }

                base.Dispose(disposing);
            }
        }
    }
}
