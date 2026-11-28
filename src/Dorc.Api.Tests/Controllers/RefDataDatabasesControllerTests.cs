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
    public class RefDataDatabasesControllerTests
    {
        private IDatabasesPersistentSource _databasesPersistentSource;
        private IDatabasesAuditPersistentSource _databasesAuditPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private IEnvironmentsPersistentSource _environmentsPersistentSource;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private RefDataDatabasesController _controller;
        private ClaimsPrincipal _user;

        [TestInitialize]
        public void Setup()
        {
            _databasesPersistentSource = Substitute.For<IDatabasesPersistentSource>();
            _databasesAuditPersistentSource = Substitute.For<IDatabasesAuditPersistentSource>();
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _controller = new RefDataDatabasesController(
                _databasesPersistentSource,
                _databasesAuditPersistentSource,
                _securityPrivilegesChecker,
                _environmentsPersistentSource,
                _claimsPrincipalReader)
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

            _claimsPrincipalReader.GetUserFullDomainName(Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns("DOMAIN\\TestUser");
        }

        [TestMethod]
        public void CreateDatabase_DuplicateNameAndServer_ReturnsBadRequest()
        {
            // Arrange
            var newDatabase = new DatabaseApiModel
            {
                Name = "TestDB",
                ServerName = "TestServer",
                Tags = new[] { "Application" }
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
                Tags = new[] { "Application" }
            };
            _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(updateDatabase.Id)
                .Returns(new List<string> { "TestEnv" });
            _environmentsPersistentSource.GetEnvironment("TestEnv", _user)
                .Returns(new EnvironmentApiModel { EnvironmentName = "TestEnv" });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "TestEnv").Returns(true);
            _databasesPersistentSource.GetDatabase(updateDatabase.Id)
                .Returns(new DatabaseApiModel { Id = 1 });
            _databasesPersistentSource.When(x => x.UpdateDatabase(1, Arg.Any<DatabaseApiModel>(), Arg.Any<System.Security.Claims.ClaimsPrincipal>()))
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
                Tags = new[] { "Application" }
            };
            var createdDatabase = new DatabaseApiModel
            {
                Id = 1,
                Name = "UniqueDB",
                ServerName = "UniqueServer",
                Tags = new[] { "Application" }
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
            CollectionAssert.AreEqual(new[] { "Application" }, returnedDatabase.Tags.ToArray());
        }

        [TestMethod]
        public void UpdateDatabase_WithUpdatedTagsArray_UpdatesDatabaseAndWritesBeforeAfterAuditWithTagsArray()
        {
            var beforeDatabase = new DatabaseApiModel
            {
                Id = 10,
                Name = "EndurDB",
                ServerName = "SQL01",
                Tags = new[] { "Endur" }
            };
            var updatedDatabase = new DatabaseApiModel
            {
                Id = 10,
                Name = "EndurDB",
                ServerName = "SQL01",
                Tags = new[] { "Endur", "Endur Reporting" }
            };

            _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(10)
                .Returns(new List<string> { "TestEnv" });
            _environmentsPersistentSource.GetEnvironment("TestEnv", _user)
                .Returns(new EnvironmentApiModel { EnvironmentName = "TestEnv" });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "TestEnv").Returns(true);
            _databasesPersistentSource.GetDatabase(10).Returns(beforeDatabase, updatedDatabase);
            _databasesPersistentSource.UpdateDatabase(10, updatedDatabase, _user).Returns(updatedDatabase);

            var actionResult = _controller.Put(10, updatedDatabase);
            Assert.IsInstanceOfType<ObjectResult>(actionResult);
            var result = (ObjectResult)actionResult;
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.IsInstanceOfType<DatabaseApiModel>(result.Value);
            var returned = (DatabaseApiModel)result.Value;
            CollectionAssert.AreEqual(new[] { "Endur", "Endur Reporting" }, returned.Tags.ToArray());

            _databasesAuditPersistentSource.Received(1).InsertDatabaseAudit(
                "DOMAIN\\TestUser",
                ActionType.Update,
                10,
                Arg.Is<string>(fromJson =>
                    fromJson.Contains("\"Tags\"") &&
                    fromJson.Contains("\"Endur\"") &&
                    !fromJson.Contains("\"Type\"")),
                Arg.Is<string>(toJson =>
                    toJson.Contains("\"Tags\"") &&
                    toJson.Contains("\"Endur Reporting\"") &&
                    !toJson.Contains("\"Type\"")));
        }

        [TestMethod]
        public void DatabaseApiModel_JsonSerializationAndDeserialization_UsesTagsStringArrayAndIgnoresLegacyType()
        {
            const string legacyPayload = """
                {
                    "Id": 10,
                    "Name": "EndurDB",
                    "ServerName": "SQL01",
                    "Type": "Endur;Endur Reporting"
                }
                """;
            var deserializedFromLegacy = System.Text.Json.JsonSerializer.Deserialize<DatabaseApiModel>(legacyPayload);
            Assert.IsNotNull(deserializedFromLegacy);
            Assert.IsEmpty(deserializedFromLegacy.Tags,
                "Sending the removed Type string property without Tags (string[]) leaves Tags empty.");

            const string validPayload = """
                {
                    "Id": 10,
                    "Name": "EndurDB",
                    "ServerName": "SQL01",
                    "Tags": ["Endur", "Endur Reporting"]
                }
                """;
            var deserializedFromTags = System.Text.Json.JsonSerializer.Deserialize<DatabaseApiModel>(validPayload);
            Assert.IsNotNull(deserializedFromTags);
            CollectionAssert.AreEqual(new[] { "Endur", "Endur Reporting" }, deserializedFromTags.Tags.ToArray());

            var serialized = System.Text.Json.JsonSerializer.Serialize(deserializedFromTags);
            StringAssert.Contains(serialized, "\"Tags\":[\"Endur\",\"Endur Reporting\"]");
            Assert.IsFalse(serialized.Contains("\"Type\"", StringComparison.OrdinalIgnoreCase),
                "Serialized DatabaseApiModel must never contain the removed Type property.");
        }
    }
}