using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataDatabasesControllerTests
    {
        private IDatabasesPersistentSource _databasesPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private RefDataDatabasesController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _databasesPersistentSource = Substitute.For<IDatabasesPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _controller = new RefDataDatabasesController(_databasesPersistentSource, _securityPrivilegesChecker, _environmentsPersistentSource)
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
        public void CreateDatabase_DuplicateNameAndServer_ReturnsBadRequest()
        {
            // Arrange
            var newDatabase = new DatabaseApiModel 
            { 
                Name = "TestDB", 
                ServerName = "TestServer",
                Type = "Application"
            };
            _databasesPersistentSource.When(x => x.AddDatabase(Arg.Any<DatabaseApiModel>()))
                .Do(x => throw new ArgumentException("Database already exists TestServer:TestDB"));

            // Act
            var result = _controller.Post(newDatabase) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual("Database already exists TestServer:TestDB", result.Value);
        }

        [TestMethod]
        public void UpdateDatabase_DuplicateNameAndServer_ReturnsBadRequest()
        {
            // Arrange
            var updateDatabase = new DatabaseApiModel 
            { 
                Id = 1,
                Name = "ExistingDB", 
                ServerName = "ExistingServer",
                Type = "Application"
            };
            _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(updateDatabase.Id)
                .Returns(new List<string> { "TestEnv" });
            _environmentsPersistentSource.GetEnvironment("TestEnv", _user)
                .Returns(new EnvironmentApiModel { EnvironmentName = "TestEnv" });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "TestEnv").Returns(true);
            _databasesPersistentSource.GetDatabase(updateDatabase.Id)
                .Returns(new DatabaseApiModel { Id = 1 });
            _databasesPersistentSource.When(x => x.UpdateDatabase(1, Arg.Any<DatabaseApiModel>(), _user))
                .Do(x => throw new ArgumentException("Database already exists ExistingServer:ExistingDB"));

            // Act
            var result = _controller.Put(1, updateDatabase) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual("Database already exists ExistingServer:ExistingDB", result.Value);
        }

        [TestMethod]
        public void CreateDatabase_UniqueNameAndServer_ReturnsSuccess()
        {
            // Arrange
            var newDatabase = new DatabaseApiModel 
            { 
                Name = "UniqueDB", 
                ServerName = "UniqueServer",
                Type = "Application"
            };
            var createdDatabase = new DatabaseApiModel 
            { 
                Id = 1,
                Name = "UniqueDB", 
                ServerName = "UniqueServer",
                Type = "Application"
            };
            _databasesPersistentSource.AddDatabase(newDatabase).Returns(createdDatabase);

            // Act
            var result = _controller.Post(newDatabase) as ObjectResult;

            // Assert
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var returnedDatabase = result.Value as DatabaseApiModel;
            Assert.AreEqual(1, returnedDatabase.Id);
            Assert.AreEqual("UniqueDB", returnedDatabase.Name);
            Assert.AreEqual("UniqueServer", returnedDatabase.ServerName);
        }
    }
}