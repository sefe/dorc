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
using System.Security.Principal;

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

        [TestMethod]
        public void DeletingEnvironmentWithoutOwnership_ReturnsForbiddenSayingWhoCanDelete()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "SomeoneElsesEnvironment" };
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "SomeoneElsesEnvironment").Returns(false);

            // Act
            var result = _controller.Delete(model) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            var message = result.Value as string;
            Assert.IsNotNull(message);
            StringAssert.Contains(message, "SomeoneElsesEnvironment");
            _environmentsPersistentSource.DidNotReceive().DeleteEnvironment(Arg.Any<EnvironmentApiModel>(), Arg.Any<IPrincipal>());
        }

        [TestMethod]
        public void DeletingEnvironmentThatIsNotThere_ReturnsNotFound()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "GoneEnvironment" };
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "GoneEnvironment").Returns(true);
            _environmentsPersistentSource.DeleteEnvironment(model, _user).Returns(false);

            // Act
            var result = _controller.Delete(model) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status404NotFound, result.StatusCode);
            var message = result.Value as string;
            Assert.IsNotNull(message);
            StringAssert.Contains(message, "GoneEnvironment");
        }

        [TestMethod]
        public void DeletingOwnEnvironment_ReturnsTrue()
        {
            // Arrange
            var model = new EnvironmentApiModel { EnvironmentName = "MyEnvironment" };
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "MyEnvironment").Returns(true);
            _environmentsPersistentSource.DeleteEnvironment(model, _user).Returns(true);

            // Act
            var result = _controller.Delete(model) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreEqual(true, result.Value);
        }

        [TestMethod]
        public void NonAdminCannotChangeExecutionIdentity()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);

            var result = _controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            _environmentsPersistentSource.DidNotReceive().SetExecutionIdentityReference(
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<IPrincipal>());
        }

        [TestMethod]
        public void GenericEnvironmentUpdateDoesNotUseExecutionIdentityPersistencePath()
        {
            var model = new EnvironmentApiModel
            {
                EnvironmentId = 12,
                EnvironmentName = "Payments",
                ExecutionIdentityReference = "attempted-bypass"
            };
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Payments").Returns(true);
            _environmentsPersistentSource.UpdateEnvironment(model, _user).Returns(model);

            var result = _controller.Put(model) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _environmentsPersistentSource.Received(1).UpdateEnvironment(model, _user);
            _environmentsPersistentSource.DidNotReceive().SetExecutionIdentityReference(
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<IPrincipal>());
        }

        [TestMethod]
        public void AdminCanChangeExecutionIdentityThroughDedicatedEndpoint()
        {
            var updated = new EnvironmentApiModel
            {
                EnvironmentId = 12,
                EnvironmentName = "Payments",
                ExecutionIdentityReference = "payments-prod"
            };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.SetExecutionIdentityReference(12, "payments-prod", _user)
                .Returns(updated);

            var result = _controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(updated, result.Value);
        }

        [TestMethod]
        public void InvalidExecutionIdentityReferenceReturnsBadRequest()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource
                .When(source => source.SetExecutionIdentityReference(12, "../secret", _user))
                .Do(_ => throw new ArgumentException("Invalid execution identity reference."));

            var result = _controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "../secret"
                }) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        }

        [TestMethod]
        public void MissingEnvironmentForExecutionIdentityReturnsNotFound()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource
                .When(source => source.SetExecutionIdentityReference(404, "payments-prod", _user))
                .Do(_ => throw new KeyNotFoundException("Environment with ID 404 was not found."));

            var result = _controller.PutExecutionIdentity(
                404,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status404NotFound, result.StatusCode);
        }
    }
}
