using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataServersControllerTests
    {
        private readonly IServersPersistentSource _serversPersistentSource =
            Substitute.For<IServersPersistentSource>();
        private readonly IWindowsWorkerClient _windowsWorkerClient =
            Substitute.For<IWindowsWorkerClient>();

        [TestMethod]
        public async Task OperatingSystemProbeRejectsUnregisteredServer()
        {
            var controller = CreateController();
            _serversPersistentSource.GetServer("attacker.example", controller.User)
                .Returns((ServerApiModel?)null);

            var result = await controller.GetServerOperatingFromTarget(
                "attacker.example",
                CancellationToken.None);

            Assert.IsInstanceOfType<NotFoundObjectResult>(result);
            await _windowsWorkerClient.DidNotReceive()
                .GetServerOperatingSystemAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task OperatingSystemProbeUsesRegisteredCanonicalServerName()
        {
            var controller = CreateController();
            _serversPersistentSource.GetServer("server01", controller.User)
                .Returns(new ServerApiModel { ServerId = 12, Name = "SERVER01.corp.example" });
            _windowsWorkerClient
                .GetServerOperatingSystemAsync("SERVER01.corp.example", CancellationToken.None)
                .Returns(new ServerOperatingSystemApiModel());

            var result = await controller.GetServerOperatingFromTarget(
                "server01",
                CancellationToken.None);

            Assert.IsInstanceOfType<OkObjectResult>(result);
            await _windowsWorkerClient.Received(1)
                .GetServerOperatingSystemAsync(
                    "SERVER01.corp.example",
                    CancellationToken.None);
        }

        private RefDataServersController CreateController()
        {
            var controller = new RefDataServersController(
                Substitute.For<ISecurityPrivilegesChecker>(),
                _serversPersistentSource,
                Substitute.For<IServersAuditPersistentSource>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IDaemonsPersistentSource>(),
                Substitute.For<IClaimsPrincipalReader>(),
                _windowsWorkerClient);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity("test"))
                }
            };

            return controller;
        }
    }
}
