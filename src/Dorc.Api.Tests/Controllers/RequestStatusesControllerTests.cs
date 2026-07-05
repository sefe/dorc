using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RequestStatusesControllerTests
    {
        private IRequestsPersistentSource _requests;
        private IRequestsStatusPersistentSource _status;
        private IRolePrivilegesChecker _roles;
        private ISecurityPrivilegesChecker _security;
        private RequestStatusesController _controller;

        private const int RequestId = 11;
        private const int ResultId = 99;
        private const string EnvName = "TEST ENV";

        [TestInitialize]
        public void Setup()
        {
            _requests = Substitute.For<IRequestsPersistentSource>();
            _status = Substitute.For<IRequestsStatusPersistentSource>();
            _roles = Substitute.For<IRolePrivilegesChecker>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();

            _controller = new RequestStatusesController(
                _requests, _status, _roles, _security,
                Substitute.For<ILogger<RequestStatusesController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            _controller.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }));

            _requests.GetDeploymentResults(ResultId).Returns(new DeploymentResultApiModel { Id = ResultId, RequestId = RequestId });
            _requests.GetRequestForUser(RequestId, Arg.Any<IPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = RequestId, EnvironmentName = EnvName });
        }

        private static int? StatusOf(IActionResult r) => (r as ObjectResult)?.StatusCode;

        [TestMethod]
        public void Patch_WithoutModifyRights_ReturnsForbidden_AndDoesNotAppend()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.Patch(RequestId, ResultId, "tampered log");

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _status.DidNotReceive().AppendLogToJob(Arg.Any<int>(), Arg.Any<string>());
        }

        [TestMethod]
        public void Patch_WithModifyRights_AppendsLog()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.Patch(RequestId, ResultId, "legit log");

            Assert.IsInstanceOfType(result, typeof(OkResult));
            _status.Received(1).AppendLogToJob(ResultId, "legit log");
        }

        [TestMethod]
        public void Patch_AuthorizesOnResultOwner_NotCallerSuppliedRequestId()
        {
            // Attacker owns request 500 but not the environment owning ResultId's request.
            const int attackerOwnedRequest = 500;
            _requests.GetRequestForUser(attackerOwnedRequest, Arg.Any<IPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = attackerOwnedRequest, EnvironmentName = "ATTACKER ENV" });
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ATTACKER ENV").Returns(true);
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            // Caller pairs their own requestId with a foreign deploymentResultId.
            var result = _controller.Patch(attackerOwnedRequest, ResultId, "tampered");

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _status.DidNotReceive().AppendLogToJob(Arg.Any<int>(), Arg.Any<string>());
        }

        [TestMethod]
        public void Patch_UnknownDeploymentResult_ReturnsNotFound()
        {
            _requests.GetDeploymentResults(1234).Returns((DeploymentResultApiModel?)null);

            var result = _controller.Patch(RequestId, 1234, "x");

            Assert.AreEqual(StatusCodes.Status404NotFound, StatusOf(result));
        }

        [TestMethod]
        public void RawLog_InvalidPath_ReturnsBadRequest_AndDoesNotWrite()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.Post(RequestId, "http://evil/share");

            Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(result));
            _status.DidNotReceive().SetUncLogPathforRequest(Arg.Any<int>(), Arg.Any<string>());
        }

        [TestMethod]
        public void RawLog_ValidPathWithoutRights_ReturnsForbidden()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.Post(RequestId, @"\\server\share\log.txt");

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _status.DidNotReceive().SetUncLogPathforRequest(Arg.Any<int>(), Arg.Any<string>());
        }

        [TestMethod]
        public void RawLog_ValidPathWithRights_Writes()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.Post(RequestId, @"\\server\share\log.txt");

            Assert.IsInstanceOfType(result, typeof(OkResult));
            _status.Received(1).SetUncLogPathforRequest(RequestId, @"\\server\share\log.txt");
        }

        [TestMethod]
        public void GetLog_UnknownRequest_ReturnsNotFound_AndDoesNotReadLog()
        {
            _requests.GetRequestForUser(4321, Arg.Any<IPrincipal>()).Returns((DeploymentRequestApiModel?)null);

            var result = _controller.GetLog(4321);

            Assert.AreEqual(StatusCodes.Status404NotFound, StatusOf(result));
            _requests.DidNotReceive().GetRequestLog(4321);
        }

        [DataTestMethod]
        [DataRow(@"\\server\share\file.log", true)]
        [DataRow(@"\\server\share", true)]
        [DataRow(@"\\server", false)]        // no share segment
        [DataRow(@"C:\local\path", false)]   // local path
        [DataRow("http://evil/share", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        [DataRow(@"\\server\share\..\..\..\other", false)]  // traversal off the share
        [DataRow(@"\\server\share\log.txt:hidden", false)]  // alternate data stream
        [DataRow(@"\\server\C$\Windows\Temp\x", false)]     // colon rejected (drive-qualified)
        public void IsValidUncPath_ValidatesAsExpected(string path, bool expected)
        {
            Assert.AreEqual(expected, RequestStatusesController.IsValidUncPath(path));
        }

        [TestMethod]
        public void GetLog_WithoutModifyRights_ReturnsForbidden_AndDoesNotReadLog()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.GetLog(RequestId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _requests.DidNotReceive().GetRequestLog(RequestId);
        }

        [TestMethod]
        public void GetLog_WithModifyRights_ReturnsLog()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);
            _requests.GetRequestLog(RequestId).Returns("the log");

            var result = _controller.GetLog(RequestId);

            Assert.AreEqual(StatusCodes.Status200OK, StatusOf(result));
            _requests.Received(1).GetRequestLog(RequestId);
        }
    }
}
