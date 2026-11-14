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
        public void Put_WhenCallerPassesOwnedNameButUnownedEnvironmentId_AuthorizesAgainstActualIdAndReturns403()
        {
            // Arrange: caller owns "MySandbox" (Id = 10) but passes EnvironmentId = 99 ("ProdEnv")
            var request = new EnvironmentApiModel
            {
                EnvironmentId = 99,
                EnvironmentName = "MySandbox",
                EnvironmentIsProd = false
            };
            _environmentsPersistentSource.GetEnvironment(99, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 99, EnvironmentName = "ProdEnv", EnvironmentIsProd = true });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "MySandbox").Returns(true);
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ProdEnv").Returns(false);

            // Act
            var result = _controller.Put(request) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().UpdateEnvironment(default!, default!);
        }

        [TestMethod]
        public void Put_WhenNonAdminFlipsIsProd_Returns403Forbidden()
        {
            // Arrange
            var request = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "DevEnv",
                EnvironmentIsProd = true
            };
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "DevEnv", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);

            // Act
            var result = _controller.Put(request) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            StringAssert.Contains(result.Value?.ToString(), "Only Admins can change the Production status");
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().UpdateEnvironment(default!, default!);
        }

        [TestMethod]
        public void Clone_WhenPowerUserCopiesInfrastructureMappingsFromProd_Returns403Forbidden()
        {
            // Arrange
            var request = new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 1,
                NewEnvironmentName = "ClonedFromProd",
                CopyServerMappings = true,
                CopyPropertyValues = false
            };
            _environmentsPersistentSource.GetEnvironment(1, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "ProdEnv", EnvironmentIsProd = true });
            _rolePrivilegesChecker.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);

            // Act
            var result = _controller.Clone(request) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            StringAssert.Contains(result.Value?.ToString(), "Only Admins can copy infrastructure mappings");
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().CloneEnvironment(default!, default!);
        }

        [TestMethod]
        public void Clone_WhenPowerUserCopiesPropertyValuesWithoutGrantOrReadSecrets_Returns403Forbidden()
        {
            // Arrange
            var request = new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 2,
                NewEnvironmentName = "ClonedWithoutSecrets",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false
            };
            _environmentsPersistentSource.GetEnvironment(2, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 2, EnvironmentName = "OtherTeamEnv", EnvironmentIsProd = false });
            _rolePrivilegesChecker.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            _securityPrivilegesChecker.CanGrantReadSecrets(Arg.Any<ClaimsPrincipal>(), "OtherTeamEnv").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "OtherTeamEnv").Returns(false);

            // Act
            var result = _controller.Clone(request) as ObjectResult;

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            StringAssert.Contains(result.Value?.ToString(), "requires Owner, Admin, or Read Secrets permission");
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().CloneEnvironment(default!, default!);
        }

        [TestMethod]
        public void Clone_WhenHumanOwnerCopiesPropertyValues_AllowsCloneViaCanGrantReadSecretsEvenWithoutCanReadSecrets()
        {
            var request = new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 2,
                NewEnvironmentName = "ClonedByHumanOwner",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false
            };
            _environmentsPersistentSource.GetEnvironment(2, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 2, EnvironmentName = "OwnedEnv", EnvironmentIsProd = false });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OwnedEnv").Returns(true);
            _securityPrivilegesChecker.CanGrantReadSecrets(Arg.Any<ClaimsPrincipal>(), "OwnedEnv").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "OwnedEnv").Returns(false);
            _environmentsPersistentSource.CloneEnvironment(request, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 22, EnvironmentName = "ClonedByHumanOwner" });

            var actionResult = _controller.Clone(request);
            Assert.IsInstanceOfType(actionResult, typeof(ObjectResult));
            var result = (ObjectResult)actionResult;

            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _environmentsPersistentSource.Received(1).CloneEnvironment(request, _user);
        }

        [TestMethod]
        public void Post_WhenParentIdIsSet_Returns400BadRequest()
        {
            var model = new EnvironmentApiModel { EnvironmentName = "ChildEnv", ParentId = 42 };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);

            var result = _controller.Post(model) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            StringAssert.Contains(result.Value?.ToString(), "SetParentForEnvironment");
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().CreateEnvironment(default!, default!);
        }

        [TestMethod]
        public void Put_WhenNonOwnerChangesEnvironmentSecure_Returns403Forbidden()
        {
            var request = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "DevEnv",
                EnvironmentSecure = false
            };
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "DevEnv", EnvironmentSecure = true });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DevEnv").Returns(false);

            var result = _controller.Put(request) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            StringAssert.Contains(result.Value?.ToString(), "Secure status");
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().UpdateEnvironment(default!, default!);
        }

        [TestMethod]
        public void Put_WhenNonOwnerOfParentSetsParentId_Returns403Forbidden()
        {
            var request = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "ChildEnv",
                ParentId = 20
            };
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = null });
            _environmentsPersistentSource.GetEnvironment(20, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "ParentEnv", EnvironmentSecure = true });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ParentEnv").Returns(false);

            var result = _controller.Put(request) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().SetParentForEnvironment(default, default, default!);
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().UpdateEnvironment(default!, default!);
        }

        [TestMethod]
        public void Put_WhenHumanOwnerOfSecureParentSetsParentId_AllowsViaCanGrantReadSecretsWithoutCanReadSecrets()
        {
            var request = new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "ChildEnv",
                ParentId = 20
            };
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = null });
            _environmentsPersistentSource.GetEnvironment(20, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "ParentEnv", EnvironmentSecure = true });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ParentEnv").Returns(true);
            _securityPrivilegesChecker.CanGrantReadSecrets(Arg.Any<ClaimsPrincipal>(), "ParentEnv").Returns(true);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ParentEnv").Returns(false);
            _environmentsPersistentSource.UpdateEnvironment(request, Arg.Any<ClaimsPrincipal>()).Returns(request);

            var result = _controller.Put(request) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _environmentsPersistentSource.Received(1).SetParentForEnvironment(20, 10, Arg.Any<IPrincipal>());
            _environmentsPersistentSource.Received(1).UpdateEnvironment(request, Arg.Any<ClaimsPrincipal>());
        }

        [TestMethod]
        public void Put_CoversAllValidationAndHierarchyCovenantBranches()
        {
            // 1. Invalid EnvironmentId <= 0 -> 400
            Assert.AreEqual(StatusCodes.Status400BadRequest, (_controller.Put(new EnvironmentApiModel { EnvironmentId = 0 }) as ObjectResult)?.StatusCode);

            // 2. Environment not found -> 404
            _environmentsPersistentSource.GetEnvironment(404, Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel)null!);
            Assert.AreEqual(StatusCodes.Status404NotFound, (_controller.Put(new EnvironmentApiModel { EnvironmentId = 404 }) as ObjectResult)?.StatusCode);

            // 3. Non-owner renaming environment -> 403
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "OldName" });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "OldName").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OldName").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "NewName" }) as ObjectResult)?.StatusCode);

            // 4. ParentId change when caller is not owner of child -> 403
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "OldName", ParentId = 20 }) as ObjectResult)?.StatusCode);

            // 5. ParentId change when child is Prod and caller is non-Admin -> 403
            _environmentsPersistentSource.GetEnvironment(11, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 11, EnvironmentName = "ProdChild", EnvironmentIsProd = true, ParentId = null });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ProdChild").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ProdChild").Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 11, EnvironmentName = "ProdChild", EnvironmentIsProd = true, ParentId = 20 }) as ObjectResult)?.StatusCode);

            // 6. ParentId change when detaching from existing parent and caller is not owner of existing parent -> 403
            _environmentsPersistentSource.GetEnvironment(12, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "AttachedChild", ParentId = 30 });
            _environmentsPersistentSource.GetEnvironment(30, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "OldParent", EnvironmentIsProd = false });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "AttachedChild").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "AttachedChild").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OldParent").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "AttachedChild", ParentId = null }) as ObjectResult)?.StatusCode);

            // 7. ParentId change when detaching from existing Prod parent and caller is non-Admin -> 403
            _environmentsPersistentSource.GetEnvironment(30, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "OldParent", EnvironmentIsProd = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OldParent").Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "AttachedChild", ParentId = null }) as ObjectResult)?.StatusCode);

            // 8. ParentId change when new parent is not found -> 400
            _environmentsPersistentSource.GetEnvironment(10, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = null });
            _securityPrivilegesChecker.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ChildEnv").Returns(true);
            _environmentsPersistentSource.GetEnvironment(999, Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel)null!);
            Assert.AreEqual(StatusCodes.Status400BadRequest,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = 999 }) as ObjectResult)?.StatusCode);

            // 9. ParentId change when new parent is Prod and caller is non-Admin -> 403
            _environmentsPersistentSource.GetEnvironment(40, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 40, EnvironmentName = "ProdParent", EnvironmentIsProd = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ProdParent").Returns(true);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = 40 }) as ObjectResult)?.StatusCode);

            // 10. ParentId change when new parent is Secure and caller lacks both CanGrantReadSecrets and CanReadSecrets -> 403
            _environmentsPersistentSource.GetEnvironment(41, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 41, EnvironmentName = "SecureParent", EnvironmentSecure = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "SecureParent").Returns(true);
            _securityPrivilegesChecker.CanGrantReadSecrets(Arg.Any<ClaimsPrincipal>(), "SecureParent").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "SecureParent").Returns(false);
            var attachDeniedResult = _controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = 41 }) as ObjectResult;
            Assert.AreEqual(StatusCodes.Status403Forbidden, attachDeniedResult?.StatusCode);
            StringAssert.Contains(attachDeniedResult?.Value?.ToString(), "requires Owner, Admin, or Read Secrets permission");

            // 11. ParentId change when SetParentForEnvironment throws -> 400 (exercising machine CanReadSecrets fallback)
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "SecureParent").Returns(true);
            _environmentsPersistentSource.When(x => x.SetParentForEnvironment(41, 10, Arg.Any<IPrincipal>()))
                .Do(_ => throw new ArgumentException("Hierarchy depth exceeded"));
            Assert.AreEqual(StatusCodes.Status400BadRequest,
                (_controller.Put(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ChildEnv", ParentId = 41 }) as ObjectResult)?.StatusCode);
        }

        [TestMethod]
        public void Clone_CoversAllAuthorizationAndInfrastructureFlags()
        {
            // 1. Source environment not found -> 400
            _environmentsPersistentSource.GetEnvironment(99, Arg.Any<ClaimsPrincipal>()).Returns((EnvironmentApiModel)null!);
            Assert.AreEqual(StatusCodes.Status400BadRequest,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 99, NewEnvironmentName = "Clone" }) as ObjectResult)?.StatusCode);

            // 2. Caller is neither PowerUser, Admin, nor Owner -> 403
            _environmentsPersistentSource.GetEnvironment(1, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "ProdEnv", EnvironmentIsProd = true });
            _rolePrivilegesChecker.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(false);
            _rolePrivilegesChecker.IsAdmin(Arg.Any<ClaimsPrincipal>()).Returns(false);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "ProdEnv").Returns(false);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 1, NewEnvironmentName = "Clone" }) as ObjectResult)?.StatusCode);

            // 3. PowerUser copying each remaining infrastructure type from Prod -> 403
            _rolePrivilegesChecker.IsPowerUser(Arg.Any<ClaimsPrincipal>()).Returns(true);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 1, NewEnvironmentName = "Clone", CopyDatabaseMappings = true }) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 1, NewEnvironmentName = "Clone", CopyContainerMappings = true }) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 1, NewEnvironmentName = "Clone", CopyCloudResourceMappings = true }) as ObjectResult)?.StatusCode);
            Assert.AreEqual(StatusCodes.Status403Forbidden,
                (_controller.Clone(new CloneEnvironmentRequest { SourceEnvironmentId = 1, NewEnvironmentName = "Clone", CopyApiRegistrationMappings = true }) as ObjectResult)?.StatusCode);

            // 4. Success path -> 200 (when infrastructure mappings are false on Prod for PowerUser)
            _securityPrivilegesChecker.CanReadSecrets(Arg.Any<ClaimsPrincipal>(), "ProdEnv").Returns(true);
            _environmentsPersistentSource.CloneEnvironment(Arg.Any<CloneEnvironmentRequest>(), Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentId = 50, EnvironmentName = "Clone" });
            var okResult = _controller.Clone(new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 1,
                NewEnvironmentName = "Clone",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false,
                CopyProjectMappings = false,
                CopyContainerMappings = false,
                CopyCloudResourceMappings = false,
                CopyApiRegistrationMappings = false
            }) as ObjectResult;
            Assert.AreEqual(StatusCodes.Status200OK, okResult?.StatusCode);
        }

        [TestMethod]
        public void RefDataEnvironmentsDetailsController_CoversPutAndSetParentForEnvironmentBranches()
        {
            var apiServices = Substitute.For<Dorc.Api.Interfaces.IApiServices>();
            var serversSource = Substitute.For<IServersPersistentSource>();
            var dbsSource = Substitute.For<IDatabasesPersistentSource>();
            var serversAudit = Substitute.For<IServersAuditPersistentSource>();
            var dbsAudit = Substitute.For<IDatabasesAuditPersistentSource>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();

            var detailsController = new RefDataEnvironmentsDetailsController(
                apiServices,
                serversSource,
                dbsSource,
                _environmentsPersistentSource,
                _securityPrivilegesChecker,
                serversAudit,
                dbsAudit,
                claimsReader,
                _rolePrivilegesChecker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = _user } }
            };

            // Put: null environment -> false
            _environmentsPersistentSource.GetEnvironment(404, _user).Returns((EnvironmentApiModel)null!);
            Assert.IsFalse(detailsController.Put(404, 1, "attach", "server").Result);

            // Put: non-owner -> false
            _environmentsPersistentSource.GetEnvironment(10, _user).Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "Env10" });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env10").Returns(false);
            Assert.IsFalse(detailsController.Put(10, 1, "attach", "server").Result);

            // Put: owner -> true
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env10").Returns(true);
            apiServices.ChangeEnvComponent<EnvironmentApiModel>(10, 1, "attach", "server", _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "Env10" });
            Assert.IsTrue(detailsController.Put(10, 1, "attach", "server").Result);

            // SetParentForEnvironment:
            // 1. Child not found -> false
            Assert.IsFalse(detailsController.SetParentForEnvironment(20, 404).Result);

            // 2. Non-owner of child -> false
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env10").Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(20, 10).Result);

            // 3. Prod child when non-Admin -> false
            _environmentsPersistentSource.GetEnvironment(11, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 11, EnvironmentName = "ProdChild", EnvironmentIsProd = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "ProdChild").Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(20, 11).Result);

            // 4. Existing parent: non-owner of existing parent -> false
            _environmentsPersistentSource.GetEnvironment(12, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "ChildWithParent", ParentId = 30 });
            _environmentsPersistentSource.GetEnvironment(30, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "OldParent", EnvironmentIsProd = false });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "ChildWithParent").Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "OldParent").Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(null, 12).Result);

            // 5. Existing parent is Prod and non-Admin -> false
            _environmentsPersistentSource.GetEnvironment(30, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "OldParent", EnvironmentIsProd = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "OldParent").Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(null, 12).Result);

            // 6. Detach when existing parent is non-Prod and caller is owner -> true
            _environmentsPersistentSource.GetEnvironment(30, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "OldParent", EnvironmentIsProd = false });
            Assert.IsTrue(detailsController.SetParentForEnvironment(null, 12).Result);

            // 7. New parent not found -> false
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Env10").Returns(true);
            _environmentsPersistentSource.GetEnvironment(999, _user).Returns((EnvironmentApiModel)null!);
            Assert.IsFalse(detailsController.SetParentForEnvironment(999, 10).Result);

            // 8. Non-owner of new parent -> false
            _environmentsPersistentSource.GetEnvironment(20, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "NewParent", EnvironmentIsProd = false, EnvironmentSecure = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "NewParent").Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(20, 10).Result);

            // 9. New parent is Prod and non-Admin -> false
            _environmentsPersistentSource.GetEnvironment(21, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 21, EnvironmentName = "ProdParent", EnvironmentIsProd = true });
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "ProdParent").Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            Assert.IsFalse(detailsController.SetParentForEnvironment(21, 10).Result);

            // 10. New parent is Secure and caller lacks both CanGrantReadSecrets and CanReadSecrets -> false
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "NewParent").Returns(true);
            _securityPrivilegesChecker.CanGrantReadSecrets(_user, "NewParent").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "NewParent").Returns(false);
            var deniedAttach = detailsController.SetParentForEnvironment(20, 10);
            Assert.IsFalse(deniedAttach.Result);
            StringAssert.Contains(deniedAttach.Message, "requires Owner, Admin, or Read Secrets permission");

            // 11a. Valid attach by human Owner/Admin (CanGrantReadSecrets == true, CanReadSecrets == false) -> true
            _securityPrivilegesChecker.CanGrantReadSecrets(_user, "NewParent").Returns(true);
            Assert.IsTrue(detailsController.SetParentForEnvironment(20, 10).Result);

            // 11b. Valid attach by machine principal with explicit ReadSecrets (CanGrantReadSecrets == false, CanReadSecrets == true) -> true
            _securityPrivilegesChecker.CanGrantReadSecrets(_user, "NewParent").Returns(false);
            _securityPrivilegesChecker.CanReadSecrets(_user, "NewParent").Returns(true);
            Assert.IsTrue(detailsController.SetParentForEnvironment(20, 10).Result);

            // 12. Exception in SetParentForEnvironment -> false
            _environmentsPersistentSource.When(x => x.SetParentForEnvironment(20, 10, _user))
                .Do(_ => throw new ArgumentException("Cycle detected"));
            Assert.IsFalse(detailsController.SetParentForEnvironment(20, 10).Result);
        }

        [TestMethod]
        public void NonAdminCannotChangeExecutionIdentityEvenIfEnvironmentOwnerOrPowerUser()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(_user, "Payments").Returns(true);

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }));
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            _environmentsPersistentSource.DidNotReceive().SetExecutionIdentityReference(
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<IPrincipal>());
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-5)]
        public void PutExecutionIdentityRejectsNonPositiveEnvironmentId(int invalidId)
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                invalidId,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            _environmentsPersistentSource.DidNotReceiveWithAnyArgs().SetExecutionIdentityReference(
                default,
                default,
                default!);
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
            _environmentsPersistentSource.GetEnvironment(12, _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "Payments" });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Payments").Returns(true);
            _environmentsPersistentSource.UpdateEnvironment(model, _user).Returns(model);

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.Put(model));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            _environmentsPersistentSource.Received(1).UpdateEnvironment(model, _user);
            _environmentsPersistentSource.DidNotReceive().SetExecutionIdentityReference(
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<IPrincipal>());
        }

        [TestMethod]
        public void AdminCanChangeExecutionIdentityThroughDedicatedEndpointAndReceivesCallerAwareProjection()
        {
            var rawPersisted = new EnvironmentApiModel
            {
                EnvironmentId = 12,
                EnvironmentName = "Payments",
                ExecutionIdentityReference = "payments-prod",
                UserEditable = false,
                IsOwner = false
            };
            var projected = new EnvironmentApiModel
            {
                EnvironmentId = 12,
                EnvironmentName = "Payments",
                EnvironmentIsProd = true,
                ExecutionIdentityReference = "payments-prod",
                UserEditable = true,
                IsOwner = true,
                ParentId = 1,
                ParentEnvironment = new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "ProdRoot" }
            };
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.SetExecutionIdentityReference(12, "payments-prod", _user)
                .Returns(rawPersisted);
            _environmentsPersistentSource.GetEnvironment(12, _user)
                .Returns(projected);

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }));
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreSame(projected, result.Value);
            Assert.IsTrue(((EnvironmentApiModel)result.Value!).UserEditable);
            Assert.IsNotNull(((EnvironmentApiModel)result.Value!).ParentEnvironment);
            _environmentsPersistentSource.Received(1).SetExecutionIdentityReference(12, "payments-prod", _user);
            _environmentsPersistentSource.Received(1).GetEnvironment(12, _user);
        }

        [TestMethod]
        public void PutExecutionIdentityReturnsNotFoundWhenReloadedProjectionIsNull()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource.SetExecutionIdentityReference(12, "payments-prod", _user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "Payments" });
            _environmentsPersistentSource.GetEnvironment(12, _user)
                .Returns((EnvironmentApiModel)null!);

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }));
            Assert.AreEqual(StatusCodes.Status404NotFound, result.StatusCode);
        }

        [TestMethod]
        public void InvalidExecutionIdentityReferenceReturnsBadRequest()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource
                .When(source => source.SetExecutionIdentityReference(12, "../secret", _user))
                .Do(_ => throw new ArgumentException("Invalid execution identity reference."));

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                12,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "../secret"
                }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        }

        [TestMethod]
        public void MissingEnvironmentForExecutionIdentityReturnsNotFound()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _environmentsPersistentSource
                .When(source => source.SetExecutionIdentityReference(404, "payments-prod", _user))
                .Do(_ => throw new KeyNotFoundException("Environment with ID 404 was not found."));

            var result = Assert.IsInstanceOfType<ObjectResult>(_controller.PutExecutionIdentity(
                404,
                new EnvironmentExecutionIdentityApiModel
                {
                    ExecutionIdentityReference = "payments-prod"
                }));
            Assert.AreEqual(StatusCodes.Status404NotFound, result.StatusCode);
        }
    }
}
