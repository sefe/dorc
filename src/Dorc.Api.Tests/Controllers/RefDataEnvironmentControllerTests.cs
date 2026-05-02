using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataEnvironmentsControllerTests
    {
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IRolePrivilegesChecker _rolePrivilegesChecker;
        private RefDataEnvironmentsController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _controller = new RefDataEnvironmentsController(_environmentsPersistentSource, _securityPrivilegesChecker, _rolePrivilegesChecker)
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
        public void AdminCreatingProdEnvironment_SuccessfulCreation()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "ProdEnvironment", EnvironmentIsProd = true };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.CreateEnvironment(model, _user).Returns(new EnvironmentApiModel { EnvironmentId = 3, EnvironmentName = "ProdEnvironment" });

            // Act
            var result = _controller.Post(model) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var environment = result.Value as EnvironmentApiModel;
            Assert.AreEqual(3, environment.EnvironmentId);
            Assert.AreEqual("ProdEnvironment", environment.EnvironmentName);
        }

        [TestMethod]
        public void UnauthorizedUserCreatingEnvironment_ReturnsForbidden()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "NewEnvironment", EnvironmentIsProd = false };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(false);

            // Act
            var result = _controller.Post(model) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [TestMethod]
        public void UnexpectedException_ReturnsBadRequest()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "FaultyEnvironment" };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.When(x => x.CreateEnvironment(Arg.Any<EnvironmentApiModel>(), Arg.Any<ClaimsPrincipal>()))
                .Do(x => { throw new Exception("Unexpected error"); });

            // Act
            var result = _controller.Post(model) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual("Unexpected error", result.Value);
        }

        [TestMethod]
        public void ValidEnvironment_CreatesNewEnvironment()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "NewEnvironment" };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.CreateEnvironment(model, _user).Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "NewEnvironment" });

            // Act
            var result = _controller.Post(model) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var environment = result.Value as EnvironmentApiModel;
            Assert.AreEqual(1, environment.EnvironmentId);
            Assert.AreEqual("NewEnvironment", environment.EnvironmentName);
        }

        [TestMethod]
        public void MissingEnvironmentName_ThrowsArgumentException()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "" };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.When(x => x.CreateEnvironment(Arg.Any<EnvironmentApiModel>(), Arg.Any<ClaimsPrincipal>()))
                .Do(x => throw new ArgumentException("EnvironmentName not set"));

            // Act
            var result = _controller.Post(model) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual("EnvironmentName not set", result.Value);
        }
    }
}
