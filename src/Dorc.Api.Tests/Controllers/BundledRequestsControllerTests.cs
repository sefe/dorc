using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class BundledRequestsControllerTests
    {
        private ILogger<BundledRequestsController> _logger;
        private IBundledRequestsPersistentSource _bundledRequestsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private BundledRequestsController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<BundledRequestsController>>();
            _bundledRequestsPersistentSource = Substitute.For<IBundledRequestsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();

            _controller = new BundledRequestsController(
                _logger,
                _bundledRequestsPersistentSource,
                _securityPrivilegesChecker)
            {
                ControllerContext = new ControllerContext()
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                new Claim(ClaimTypes.Name, "TestUser")
            }));
            _controller.HttpContext.User = _user;
        }

        [TestMethod]
        public void Delete_UserWithModifyRights_Success()
        {
            // Arrange
            var bundleId = 42;
            var projectId = 7;
            var bundle = new BundledRequestsApiModel { Id = bundleId, ProjectId = projectId, BundleName = "TestBundle" };

            _bundledRequestsPersistentSource.GetBundleById(bundleId).Returns(bundle);
            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(true);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(200, objectResult.StatusCode);
            _bundledRequestsPersistentSource.Received(1).DeleteRequestFromBundle(bundleId);
        }

        [TestMethod]
        public void Delete_UserWithoutModifyRights_Forbidden()
        {
            // Arrange
            var bundleId = 42;
            var projectId = 7;
            var bundle = new BundledRequestsApiModel { Id = bundleId, ProjectId = projectId, BundleName = "TestBundle" };

            _bundledRequestsPersistentSource.GetBundleById(bundleId).Returns(bundle);
            _securityPrivilegesChecker.CanModifyProject(_user, projectId).Returns(false);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            Assert.AreEqual("User does not have Modify rights on this Project", objectResult.Value);
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
        }

        [TestMethod]
        public void Delete_BundleWithoutProjectId_Forbidden()
        {
            // Arrange
            var bundleId = 42;
            var bundle = new BundledRequestsApiModel { Id = bundleId, ProjectId = null, BundleName = "TestBundle" };

            _bundledRequestsPersistentSource.GetBundleById(bundleId).Returns(bundle);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            _securityPrivilegesChecker.DidNotReceive().CanModifyProject(Arg.Any<ClaimsPrincipal>(), Arg.Any<int>());
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
        }

        [TestMethod]
        public void Delete_NonExistentBundle_NotFound()
        {
            // Arrange
            var bundleId = 999;

            _bundledRequestsPersistentSource.GetBundleById(bundleId).Returns((BundledRequestsApiModel?)null);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
        }
    }
}
