using System.Text.Json;
using Dorc.Api.WindowsWorker.Controllers;
using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Api.WindowsWorker.Tests
{
    [TestClass]
    public class DaemonsControllerTests
    {
        [TestMethod]
        public void Probe_ResponseMatchesPreSplitFixture()
        {
            var fixture = LoadFixture();
            var operations = new CannedOperations(fixture.Response);
            var controller = new DaemonsController(
                operations,
                NullLogger<DaemonsController>.Instance);

            var result = controller.Probe(new WorkerDaemonProbeRequestApiModel
            {
                Daemons = fixture.Request.Daemons
            });

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            var body = Assert.IsInstanceOfType<List<WorkerDaemonApiModel>>(ok.Value);
            Assert.HasCount(fixture.Response.Count, body);
            Assert.AreEqual(fixture.Response[0].EnvName, body[0].EnvName);
            Assert.AreEqual(fixture.Response[0].ServerName, body[0].ServerName);
            Assert.AreEqual(fixture.Response[0].DaemonName, body[0].DaemonName);
            Assert.AreEqual(fixture.Response[0].Status, body[0].Status);
            Assert.AreEqual(fixture.Response[0].ServerId, body[0].ServerId);
            Assert.AreEqual(fixture.Response[0].DaemonId, body[0].DaemonId);
        }

        [TestMethod]
        public void ChangeState_ResponsePreservesDaemonShape()
        {
            var expected = LoadFixture().Response[0];
            var controller = new DaemonsController(
                new CannedOperations([expected]),
                NullLogger<DaemonsController>.Instance);

            var result = controller.ChangeState(new WorkerDaemonStateChangeRequestApiModel
            {
                Daemon = new WorkerDaemonApiModel
                {
                    ServerName = expected.ServerName,
                    DaemonName = expected.DaemonName,
                    Status = "Starting"
                }
            });

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            var body = Assert.IsInstanceOfType<WorkerDaemonApiModel>(ok.Value);
            Assert.AreEqual(expected.Status, body.Status);
            Assert.AreEqual(expected.ServerId, body.ServerId);
            Assert.AreEqual(expected.DaemonId, body.DaemonId);
        }

        private static DaemonFixture LoadFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "daemon-probe.json");
            return JsonSerializer.Deserialize<DaemonFixture>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("Daemon parity fixture is invalid.");
        }

        private sealed class CannedOperations : IDaemonServiceOperations
        {
            private readonly List<WorkerDaemonApiModel> _response;

            public CannedOperations(List<WorkerDaemonApiModel> response)
            {
                _response = response;
            }

            public List<WorkerDaemonApiModel> Probe(
                WorkerDaemonCredentialApiModel? credential,
                List<WorkerDaemonApiModel> daemons)
                => _response;

            public WorkerDaemonApiModel? ChangeState(
                WorkerDaemonCredentialApiModel? credential,
                WorkerDaemonApiModel daemon)
                => _response.Single();
        }

        private sealed class DaemonFixture
        {
            public required DaemonRequest Request { get; init; }
            public required List<WorkerDaemonApiModel> Response { get; init; }
        }

        private sealed class DaemonRequest
        {
            public required List<WorkerDaemonApiModel> Daemons { get; init; }
        }
    }
}
