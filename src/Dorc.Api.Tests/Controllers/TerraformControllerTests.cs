using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class TerraformControllerTests
    {
        private IRequestsPersistentSource _requests;
        private ISecurityPrivilegesChecker _security;
        private IClaimsPrincipalReader _claims;
        private IAzureStorageAccountWorker _storage;
        private TerraformController _controller;

        private const int ResultId = 42;
        private const int RequestId = 7;
        private const string EnvName = "TEST ENV";

        [TestInitialize]
        public void Setup()
        {
            _requests = Substitute.For<IRequestsPersistentSource>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _claims = Substitute.For<IClaimsPrincipalReader>();
            _storage = Substitute.For<IAzureStorageAccountWorker>();

            _controller = new TerraformController(
                Substitute.For<ILogger<TerraformController>>(),
                _requests, _security, _claims, _storage)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            _controller.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }));

            _requests.GetDeploymentResults(ResultId).Returns(new DeploymentResultApiModel
            {
                Id = ResultId,
                RequestId = RequestId,
                Status = DeploymentResultStatus.WaitingConfirmation.ToString()
            });
            _requests.GetRequestForUser(RequestId, Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns(new DeploymentRequestApiModel { Id = RequestId, EnvironmentName = EnvName });
            _claims.GetUserName(Arg.Any<ClaimsPrincipal>()).Returns("TestUser");
        }

        private static int? StatusOf(IActionResult result) => (result as ObjectResult)?.StatusCode;

        [TestMethod]
        public void Confirm_WithoutModifyRights_ReturnsForbidden_AndDoesNotChangeStatus()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _requests.DidNotReceive().UpdateRequestStatus(Arg.Any<int>(), Arg.Any<DeploymentRequestStatus>());
        }

        [TestMethod]
        public void Confirm_WithModifyRights_ConfirmsAndUpdatesStatus()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.ConfirmTerraformPlan(ResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            _requests.Received(1).UpdateRequestStatus(RequestId, DeploymentRequestStatus.Confirmed);
        }

        [TestMethod]
        public void Decline_WithoutModifyRights_ReturnsForbidden_AndDoesNotChangeStatus()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.DeclineTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _requests.DidNotReceive().UpdateRequestStatus(Arg.Any<int>(), Arg.Any<DeploymentRequestStatus>());
        }

        [TestMethod]
        public void View_WithoutModifyRights_ReturnsForbidden()
        {
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.GetTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
        }

        [TestMethod]
        public void Confirm_WhenOwningEnvironmentCannotBeResolved_FailsClosed()
        {
            // Request (and therefore the environment) cannot be resolved.
            _requests.GetRequestForUser(RequestId, Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns((DeploymentRequestApiModel?)null);
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);

            var result = _controller.ConfirmTerraformPlan(ResultId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, StatusOf(result));
            _security.DidNotReceive().CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>());
            _requests.DidNotReceive().UpdateRequestStatus(Arg.Any<int>(), Arg.Any<DeploymentRequestStatus>());
        }
    }
}
