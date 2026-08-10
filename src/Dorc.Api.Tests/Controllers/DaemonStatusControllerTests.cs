using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// Reading daemon statuses drives a LogonUser in the API process with the deployment
    /// credential for the target environment's tier. Before S-002 neither Get overload
    /// checked authority: the id overload passed no principal to the probe at all, and the
    /// name overload passed one that the environment lookup uses to annotate ownership
    /// rather than to filter access.
    ///
    /// The refuse tests assert the probe was never invoked, not merely that the response is
    /// 403 - an implementation that probed first and filtered afterwards would still have
    /// performed the privileged logon.
    /// </summary>
    [TestClass]
    public class DaemonStatusControllerTests
    {
        private const int EnvironmentId = 11;
        private const string EnvironmentName = "PROD ENV 01";

        private IDaemonStatusProbe _daemonStatusProbe = null!;
        private ISecurityPrivilegesChecker _privilegesChecker = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IEnvironmentMapper _environmentMapper = null!;
        private DaemonStatusController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _daemonStatusProbe = Substitute.For<IDaemonStatusProbe>();
            _privilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _environmentMapper = Substitute.For<IEnvironmentMapper>();

            _controller = new DaemonStatusController(
                _daemonStatusProbe,
                _privilegesChecker,
                _environmentsPersistentSource,
                _environmentMapper)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };

            var environment = new EnvironmentApiModel
            {
                EnvironmentId = EnvironmentId,
                EnvironmentName = EnvironmentName
            };

            _environmentsPersistentSource.GetEnvironment(EnvironmentId, Arg.Any<ClaimsPrincipal>())
                .Returns(environment);
            _environmentsPersistentSource.GetEnvironment(EnvironmentName, Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns(environment);

            _daemonStatusProbe.GetDaemonStatuses(EnvironmentId).Returns(new List<DaemonStatus>());
            _daemonStatusProbe.GetDaemonStatuses(EnvironmentName, Arg.Any<ClaimsPrincipal>())
                .Returns(new List<DaemonStatus>());
        }

        private static int StatusOf(IActionResult result) => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? 0,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => 0
        };

        private void AssertProbeNotInvoked()
        {
            _daemonStatusProbe.DidNotReceiveWithAnyArgs().GetDaemonStatuses(default);
            _daemonStatusProbe.DidNotReceiveWithAnyArgs().GetDaemonStatuses(default!, default!);
        }

        [TestMethod]
        public void GetById_WithoutEnvironmentAuthority_Returns403AndDoesNotProbe()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvironmentName).Returns(false);

            var response = _controller.Get(EnvironmentId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertProbeNotInvoked();
        }

        [TestMethod]
        public void GetById_WithEnvironmentAuthority_ReturnsStatuses()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvironmentName).Returns(true);

            var response = _controller.Get(EnvironmentId);

            Assert.AreEqual(StatusCodes.Status200OK, StatusOf(response));
            _daemonStatusProbe.Received(1).GetDaemonStatuses(EnvironmentId);
        }

        [TestMethod]
        public void GetById_ConsultsTheCheckerWithTheResolvedEnvironmentName()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            _controller.Get(EnvironmentId);

            _privilegesChecker.Received(1).CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvironmentName);
        }

        [TestMethod]
        public void GetById_WhenEnvironmentNotFound_Returns404AndDoesNotProbe()
        {
            _environmentsPersistentSource.GetEnvironment(EnvironmentId, Arg.Any<ClaimsPrincipal>())
                .Returns((EnvironmentApiModel)null!);

            var response = _controller.Get(EnvironmentId);

            Assert.IsInstanceOfType(response, typeof(NotFoundObjectResult));
            AssertProbeNotInvoked();
        }

        [TestMethod]
        public void GetById_WithZeroId_ReturnsBadRequestAndDoesNotProbe()
        {
            var response = _controller.Get(0);

            Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(response));
            AssertProbeNotInvoked();
        }

        [TestMethod]
        public void GetByName_WithoutEnvironmentAuthority_Returns403AndDoesNotProbe()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvironmentName).Returns(false);

            var response = _controller.Get(EnvironmentName);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(response));
            AssertProbeNotInvoked();
        }

        [TestMethod]
        public void GetByName_WithEnvironmentAuthority_ReturnsStatuses()
        {
            _privilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvironmentName).Returns(true);

            var response = _controller.Get(EnvironmentName);

            Assert.AreEqual(StatusCodes.Status200OK, StatusOf(response));
            _daemonStatusProbe.Received(1).GetDaemonStatuses(EnvironmentName, Arg.Any<ClaimsPrincipal>());
        }

        [TestMethod]
        public void GetByName_WhenEnvironmentNotFound_Returns404AndDoesNotProbe()
        {
            _environmentsPersistentSource.GetEnvironment(EnvironmentName, Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns((EnvironmentApiModel)null!);

            var response = _controller.Get(EnvironmentName);

            Assert.IsInstanceOfType(response, typeof(NotFoundObjectResult));
            AssertProbeNotInvoked();
        }
    }
}
