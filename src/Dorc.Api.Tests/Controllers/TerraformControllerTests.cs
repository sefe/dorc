using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class TerraformControllerTests
    {
        private IRequestsPersistentSource _requests = null!;
        private ISecurityPrivilegesChecker _security = null!;
        private IClaimsPrincipalReader _claimsReader = null!;
        private IAzureStorageAccountWorker _storage = null!;
        private TerraformController _controller = null!;

        private const int DeploymentResultId = 42;
        private const int RequestId = 1001;
        private const string EnvName = "TEVO DV 11";
        private const string ProjectName = "TEVO";

        [TestInitialize]
        public void Setup()
        {
            _requests = Substitute.For<IRequestsPersistentSource>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsReader = Substitute.For<IClaimsPrincipalReader>();
            _storage = Substitute.For<IAzureStorageAccountWorker>();

            _controller = new TerraformController(
                NullLogger<TerraformController>.Instance,
                _requests,
                _security,
                _claimsReader,
                _storage)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            _controller.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "TestAuth"));
        }

        private void GivenStandardDeploymentResult(string status = "WaitingConfirmation")
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns(new DeploymentResultApiModel
            {
                Id = DeploymentResultId,
                RequestId = RequestId,
                Status = status
            });
            _requests.GetRequest(RequestId).Returns(new DeploymentRequestApiModel
            {
                Id = RequestId,
                EnvironmentName = EnvName,
                Project = ProjectName
            });
        }

        // ---------- View ----------

        [TestMethod]
        public void GetTerraformPlan_EnvOwner_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);
            _storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("plan-content");

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_ProjectOwnerOnly_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);
            _security.IsProjectOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), ProjectName).Returns(true);
            _storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("plan-content");

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_NeitherOwnerNorAdmin_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);
            _security.IsProjectOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(false);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public void GetTerraformPlan_DeploymentResultMissing_Returns404()
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns((DeploymentResultApiModel?)null);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
        }

        [TestMethod]
        public void GetTerraformPlan_RequestLookupNull_Returns403()
        {
            _requests.GetDeploymentResults(DeploymentResultId).Returns(new DeploymentResultApiModel
            {
                Id = DeploymentResultId,
                RequestId = RequestId
            });
            _requests.GetRequest(RequestId).Returns((DeploymentRequestApiModel?)null);

            var result = _controller.GetTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        // ---------- Confirm ----------

        [TestMethod]
        public void ConfirmTerraformPlan_CanModifyEnvironment_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.ConfirmTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void ConfirmTerraformPlan_CannotModifyEnvironment_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.ConfirmTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        // ---------- Decline ----------

        [TestMethod]
        public void DeclineTerraformPlan_CanModifyEnvironment_Returns200()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(true);

            var result = _controller.DeclineTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [TestMethod]
        public void DeclineTerraformPlan_CannotModifyEnvironment_Returns403()
        {
            GivenStandardDeploymentResult();
            _security.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), EnvName).Returns(false);

            var result = _controller.DeclineTerraformPlan(DeploymentResultId);

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }
    }
}
