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
    public class BundledRequestsControllerDeleteTests
    {
        private IBundledRequestsPersistentSource _bundledRequestsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private BundledRequestsController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _bundledRequestsPersistentSource = Substitute.For<IBundledRequestsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            var logger = Substitute.For<ILogger<BundledRequestsController>>();

            _controller = new BundledRequestsController(
                logger,
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
            const int bundleId = 42;
            _bundledRequestsPersistentSource.GetBundleById(bundleId)
                .Returns(new BundledRequestsApiModel { Id = bundleId, ProjectId = 7 });
            _securityPrivilegesChecker.CanModifyProject(_user, 7).Returns(true);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(200, objectResult.StatusCode);
            _bundledRequestsPersistentSource.Received(1).DeleteRequestFromBundle(bundleId);
        }

        [TestMethod]
        public void Delete_UserWithoutModifyRights_Forbidden()
        {
            // Arrange
            const int bundleId = 42;
            _bundledRequestsPersistentSource.GetBundleById(bundleId)
                .Returns(new BundledRequestsApiModel { Id = bundleId, ProjectId = 7 });
            _securityPrivilegesChecker.CanModifyProject(_user, 7).Returns(false);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
        }

        [TestMethod]
        public void Delete_NonExistentBundle_NotFound()
        {
            // Arrange
            const int bundleId = 999;
            _bundledRequestsPersistentSource.GetBundleById(bundleId).Returns((BundledRequestsApiModel?)null);

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(404, objectResult.StatusCode);
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
            _securityPrivilegesChecker.DidNotReceive().CanModifyProject(Arg.Any<ClaimsPrincipal>(), Arg.Any<int>());
        }

        [TestMethod]
        public void Delete_BundleWithoutProject_Forbidden()
        {
            // Arrange — a bundle with no owning project cannot be authorized; fail closed.
            const int bundleId = 42;
            _bundledRequestsPersistentSource.GetBundleById(bundleId)
                .Returns(new BundledRequestsApiModel { Id = bundleId, ProjectId = null });

            // Act
            var result = _controller.Delete(bundleId);

            // Assert
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            _bundledRequestsPersistentSource.DidNotReceive().DeleteRequestFromBundle(Arg.Any<int>());
        }
    }
}
