using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataProjectsControllerDeleteTests
    {
        private IProjectsPersistentSource _projectsPersistentSource;
        private IAccessControlPersistentSource _accessControlPersistentSource;
        private IActiveDirectorySearcher _activeDirectorySearcher;
        private IRolePrivilegesChecker _rolePrivilegesChecker;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private IDeploymentDefinitions _definitions;
        private RefDataProjectsController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            _activeDirectorySearcher = Substitute.For<IActiveDirectorySearcher>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            _definitions = Substitute.For<IDeploymentDefinitions>();

            _controller = new RefDataProjectsController(
                _projectsPersistentSource,
                _accessControlPersistentSource,
                _activeDirectorySearcher,
                _rolePrivilegesChecker,
                _securityPrivilegesChecker,
                _claimsPrincipalReader, _definitions)
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
        public void DeleteProject_AdminDeletingExistingProject_Success()
        {
            // Arrange
            var projectId = 123;
            var project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" };
            
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _projectsPersistentSource.GetProject(projectId).Returns(project);
            _securityPrivilegesChecker.CanModifyProject(_user, project.ProjectName).Returns(true);

            // Act
            var result = _controller.Delete(projectId, "expected");

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(200, objectResult.StatusCode);
            _definitions.Received(1).DeleteProject(projectId, "expected", Arg.Any<string>(), Arg.Any<Func<int, bool>>());
        }

        [TestMethod]
        public void DeleteProject_PowerUserWithPermissionsDeletingExistingProject_Success()
        {
            // Arrange
            var projectId = 123;
            var project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" };

            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _securityPrivilegesChecker.CanModifyProject(_user, project.ProjectName).Returns(true);
            _projectsPersistentSource.GetProject(projectId).Returns(project);

            // Act
            var result = _controller.Delete(projectId, "expected");

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(200, objectResult.StatusCode);
            _definitions.Received(1).DeleteProject(projectId, "expected", Arg.Any<string>(), Arg.Any<Func<int, bool>>());
        }

        [TestMethod]
        public void DeleteProject_PowerUserWithoutPermissionsDeletingExistingProject_Forbidden()
        {
            // Arrange
            var projectId = 123;
            var project = new ProjectApiModel { ProjectId = projectId, ProjectName = "TestProject" };

            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _securityPrivilegesChecker.CanModifyProject(_user, project.ProjectName).Returns(false);
            _projectsPersistentSource.GetProject(projectId).Returns(project);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            Assert.AreEqual("Projects can only be deleted by privileged users or Admins!", objectResult.Value);
            _projectsPersistentSource.DidNotReceive().DeleteProject(Arg.Any<int>());
        }

        [TestMethod]
        public void DeleteProject_RegularUserAttempting_Forbidden()
        {
            // Arrange
            var projectId = 123;
            
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(false);
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(403, objectResult.StatusCode);
            Assert.AreEqual("Projects can only be deleted by privileged users or Admins!", objectResult.Value);
            _projectsPersistentSource.DidNotReceive().DeleteProject(Arg.Any<int>());
        }

        [TestMethod]
        public void DeleteProject_NonExistentProject_NotFound()
        {
            // Arrange
            var projectId = 999;
            
            _rolePrivilegesChecker.IsPowerUser(_user).Returns(true);
            _projectsPersistentSource.GetProject(projectId).Returns((ProjectApiModel?)null);

            // Act
            var result = _controller.Delete(projectId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(404, objectResult.StatusCode);
            _projectsPersistentSource.DidNotReceive().DeleteProject(Arg.Any<int>());
        }

        [TestMethod]
        public void PostProject_WhenM2MClientNotInDirectory_FallsBackToClaimsPrincipalIdentity()
        {
            // Arrange
            var project = new ProjectApiModel
            {
                ProjectName = "M2MProject",
                ArtefactsUrl = "file://depapp01dv/DropFolder"
            };

            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _projectsPersistentSource.GetProject("M2MProject").Returns((ProjectApiModel?)null);
            _projectsPersistentSource.ProjectArtifactsUriHttpValid(project).Returns(false);
            _projectsPersistentSource.ProjectArtifactsUriFileValid(project).Returns(true);
            _claimsPrincipalReader.GetUserLogin(_user).Returns("dorc-cli");
            _claimsPrincipalReader.GetUserId(_user).Returns("dorc-cli");
            _claimsPrincipalReader.GetUserName(_user).Returns(string.Empty);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("dorc-cli");
            _activeDirectorySearcher
                .When(s => s.GetUserData("dorc-cli"))
                .Do(_ => throw new ArgumentException("User with name 'dorc-cli' not found in any source"));
            _definitions
                .CreateProject(project, "dorc-cli", Arg.Any<AccessControlApiModel>(), Arg.Any<Action>())
                .Returns(project);

            // Act
            var result = _controller.Post(project);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            _definitions.Received(1).CreateProject(
                project,
                "dorc-cli",
                Arg.Is<AccessControlApiModel>(ac =>
                    ac.Sid == "dorc-cli" &&
                    ac.Pid == "dorc-cli" &&
                    ac.Name == "dorc-cli" &&
                    ac.Allow == (int)(AccessLevel.Owner | AccessLevel.Write)),
                Arg.Any<Action>());
        }

        [TestMethod]
        public void PostProject_WhenDirectoryThrowsInvalidOperationAndClaimHasDisplayName_UsesClaimDisplayName()
        {
            // Arrange
            var project = new ProjectApiModel
            {
                ProjectName = "M2MProjectNamed",
                ArtefactsUrl = "file://depapp01dv/DropFolder"
            };

            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
            _projectsPersistentSource.GetProject("M2MProjectNamed").Returns((ProjectApiModel?)null);
            _projectsPersistentSource.ProjectArtifactsUriHttpValid(project).Returns(true);
            _claimsPrincipalReader.GetUserLogin(_user).Returns("dorc-cli");
            _claimsPrincipalReader.GetUserId(_user).Returns("dorc-cli-id");
            _claimsPrincipalReader.GetUserName(_user).Returns("DOrc CLI M2M");
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("dorc-cli");
            _activeDirectorySearcher
                .When(s => s.GetUserData("dorc-cli"))
                .Do(_ => throw new InvalidOperationException("IdentityServer unavailable"));
            _definitions
                .CreateProject(project, "dorc-cli", Arg.Any<AccessControlApiModel>(), Arg.Any<Action>())
                .Returns(project);

            // Act
            var result = _controller.Post(project);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            _definitions.Received(1).CreateProject(
                project,
                "dorc-cli",
                Arg.Is<AccessControlApiModel>(ac =>
                    ac.Sid == "dorc-cli-id" &&
                    ac.Pid == "dorc-cli-id" &&
                    ac.Name == "DOrc CLI M2M"),
                Arg.Any<Action>());
        }

        [TestMethod]
        public void ClaimsPrincipalReaderFactory_WhenUseAdSidsEnabledAndM2MClient_UsesOAuthClientClaimsWithoutQueryingAd()
        {
            var config = Substitute.For<Dorc.Core.Configuration.IConfigurationSettings>();
            config.GetIsUseAdSidsForAccessControl().Returns(true);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = "Bearer test-token";
            var m2mIdentity = new ClaimsIdentity(
                new[]
                {
                    new Claim("m2m", "True"),
                    new Claim("client_id", "dorc-cli")
                },
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
            var m2mPrincipal = new ClaimsPrincipal(m2mIdentity);
            httpContext.User = m2mPrincipal;

            var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Returns(httpContext);

            var adReader = Substitute.For<Dorc.Api.Interfaces.IUserGroupReader>();
            var oauthReader = Substitute.For<Dorc.Api.Interfaces.IUserGroupReader>();
            var readerFactory = Substitute.For<Dorc.Api.Interfaces.IUserGroupsReaderFactory>();
            readerFactory.GetWinAuthUserGroupsReader().Returns(adReader);
            readerFactory.GetOAuthUserGroupsReader().Returns(oauthReader);

            var sut = new Dorc.Api.Security.ClaimsPrincipalReaderFactory(config, httpContextAccessor, readerFactory);

            var userId = sut.GetUserId(m2mPrincipal);
            var sids = sut.GetSidsForUser(m2mPrincipal);

            Assert.AreEqual("dorc-cli", userId);
            CollectionAssert.Contains(sids, "dorc-cli");
            adReader.DidNotReceiveWithAnyArgs().GetUserData(default!);
            adReader.DidNotReceiveWithAnyArgs().GetSidsForUser(default!);
        }

        [TestMethod]
        public void ClaimsPrincipalReaderFactory_WhenUseAdSidsEnabledAndNonM2MUser_QueriesAdUserGroupReader()
        {
            var config = Substitute.For<Dorc.Core.Configuration.IConfigurationSettings>();
            config.GetIsUseAdSidsForAccessControl().Returns(true);

            var httpContext = new DefaultHttpContext();
            var winIdentity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "DOMAIN\\testuser")
                },
                Microsoft.AspNetCore.Authentication.Negotiate.NegotiateDefaults.AuthenticationScheme);
            var winPrincipal = new ClaimsPrincipal(winIdentity);
            httpContext.User = winPrincipal;

            var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Returns(httpContext);

            var adReader = Substitute.For<Dorc.Api.Interfaces.IUserGroupReader>();
            adReader.GetUserData("testuser").Returns(new UserElementApiModel { Sid = "S-1-5-21-999" });
            adReader.GetSidsForUser("testuser").Returns(new List<string> { "S-1-5-21-999" });

            var oauthReader = Substitute.For<Dorc.Api.Interfaces.IUserGroupReader>();
            var readerFactory = Substitute.For<Dorc.Api.Interfaces.IUserGroupsReaderFactory>();
            readerFactory.GetWinAuthUserGroupsReader().Returns(adReader);
            readerFactory.GetOAuthUserGroupsReader().Returns(oauthReader);

            var sut = new Dorc.Api.Security.ClaimsPrincipalReaderFactory(config, httpContextAccessor, readerFactory);

            var userId = sut.GetUserId(winPrincipal);
            var sids = sut.GetSidsForUser(winPrincipal);

            Assert.AreEqual("S-1-5-21-999", userId);
            CollectionAssert.Contains(sids, "S-1-5-21-999");
            adReader.Received(1).GetUserData("testuser");
            adReader.Received(1).GetSidsForUser("testuser");
        }
    }
}