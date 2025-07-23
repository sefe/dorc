using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataPermissionControllerTests
    {
        private IPermissionsPersistentSource _permissionsPersistentSource;
        private RefDataPermissionController _controller;

        [TestInitialize]
        public void Initialize()
        {
            _permissionsPersistentSource = Substitute.For<IPermissionsPersistentSource>();
            _controller = new RefDataPermissionController(_permissionsPersistentSource);
        }

        [TestMethod]
        public void Post_UserIsNotPowerUser_ReturnsForbidden()
        {
            // Arrange
            var permissionDto = new PermissionDto { DisplayName = "Test", PermissionName = "TestPermission" };
            
            // Create a user that is not a PowerUser (could be Admin or regular user)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "Admin") // Admin should not be allowed
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            // Act
            var result = _controller.Post(permissionDto) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.AreEqual("User must be part of the 'PowerUser' group to create new Permissions", result.Value);
        }

        [TestMethod]
        public void Post_UserIsPowerUser_ReturnsOk()
        {
            // Arrange
            var permissionDto = new PermissionDto { DisplayName = "Test", PermissionName = "TestPermission" };
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "PowerUser")
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            // Setup mock to not throw any exceptions
            // No setup needed for NSubstitute - it returns default values

            // Act
            var result = _controller.Post(permissionDto) as StatusCodeResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _permissionsPersistentSource.Received(1).CreatePermission(permissionDto);
        }

        [TestMethod]
        public void Post_UserIsRegularUser_ReturnsForbidden()
        {
            // Arrange
            var permissionDto = new PermissionDto { DisplayName = "Test", PermissionName = "TestPermission" };
            
            // Create a user with no roles (regular user)
            var claims = new List<Claim>();
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            // Act
            var result = _controller.Post(permissionDto) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.AreEqual("User must be part of the 'PowerUser' group to create new Permissions", result.Value);
        }
    }
}