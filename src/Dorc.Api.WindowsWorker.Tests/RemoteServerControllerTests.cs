using System.Text.Json;
using Dorc.Api.WindowsWorker.Controllers;
using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Api.WindowsWorker.Tests
{
    [TestClass]
    public class RemoteServerControllerTests
    {
        [TestMethod]
        public void OperatingSystem_ResponseMatchesPreSplitFixture()
        {
            var fixture = LoadFixture();
            var mapped = RemoteRegistryOperatingSystemReader.MapValues(
                fixture.Registry.ProductName,
                fixture.Registry.CurrentVersion);
            var controller = new RemoteServerController(
                new CannedReader(mapped),
                NullLogger<RemoteServerController>.Instance);

            var result = controller.GetOperatingSystem(fixture.Request.ServerName);

            var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            var body = Assert.IsInstanceOfType<ServerOperatingSystemApiModel>(ok.Value);
            Assert.AreEqual(fixture.Response.ProductName, body.ProductName);
            Assert.AreEqual(fixture.Response.CurrentVersion, body.CurrentVersion);
        }

        [TestMethod]
        public void OperatingSystem_MissingRegistryKeyPreservesPreSplit400()
        {
            var controller = new RemoteServerController(
                new CannedReader(null),
                NullLogger<RemoteServerController>.Instance);

            var result = controller.GetOperatingSystem("SERVER01");

            Assert.IsInstanceOfType<BadRequestObjectResult>(result.Result);
        }

        private static RegistryFixture LoadFixture()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "registry-operating-system.json");
            return JsonSerializer.Deserialize<RegistryFixture>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("Registry parity fixture is invalid.");
        }

        private sealed class CannedReader : IRemoteServerOperatingSystemReader
        {
            private readonly ServerOperatingSystemApiModel? _result;

            public CannedReader(ServerOperatingSystemApiModel? result)
            {
                _result = result;
            }

            public ServerOperatingSystemApiModel? Read(string serverName) => _result;
        }

        private sealed class RegistryFixture
        {
            public required RegistryRequest Request { get; init; }
            public required RegistryValues Registry { get; init; }
            public required RegistryResponse Response { get; init; }
        }

        private sealed class RegistryRequest
        {
            public required string ServerName { get; init; }
        }

        private sealed class RegistryValues
        {
            public required string ProductName { get; init; }
            public required string CurrentVersion { get; init; }
        }

        private sealed class RegistryResponse
        {
            public required string ProductName { get; init; }
            public required string CurrentVersion { get; init; }
        }
    }
}
