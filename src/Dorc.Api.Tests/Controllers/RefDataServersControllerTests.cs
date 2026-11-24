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
using System.Text.Json;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    public class RefDataServersControllerTests
    {
        private ISecurityPrivilegesChecker _securityPrivilegesChecker = null!;
        private IServersPersistentSource _serversPersistentSource = null!;
        private IServersAuditPersistentSource _serversAuditPersistentSource = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IDaemonsPersistentSource _daemonsPersistentSource = null!;
        private IClaimsPrincipalReader _claimsPrincipalReader = null!;
        private RefDataServersController _controller = null!;
        private ClaimsPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _securityPrivilegesChecker = Substitute.For<ISecurityPrivilegesChecker>();
            _serversPersistentSource = Substitute.For<IServersPersistentSource>();
            _serversAuditPersistentSource = Substitute.For<IServersAuditPersistentSource>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _daemonsPersistentSource = Substitute.For<IDaemonsPersistentSource>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();

            _controller = new RefDataServersController(
                _securityPrivilegesChecker,
                _serversPersistentSource,
                _serversAuditPersistentSource,
                _environmentsPersistentSource,
                _daemonsPersistentSource,
                _claimsPrincipalReader)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }));
            _controller.HttpContext.User = _user;

            _claimsPrincipalReader.GetUserFullDomainName(Arg.Any<System.Security.Principal.IPrincipal>())
                .Returns("DOMAIN\\TestUser");
        }

        [TestMethod]
        public void Post_WithTagsArray_PersistsTagsAndWritesAuditWithTagsArray()
        {
            var newServer = new ServerApiModel
            {
                Name = "NPVM01",
                OsName = "Windows Server 2022",
                Tags = new[] { "PATCH", "PATCH_SCOPE_NP", "PATCH_MODE_AUTO" },
                UserEditable = true
            };
            var createdServer = new ServerApiModel
            {
                ServerId = 101,
                Name = "NPVM01",
                OsName = "Windows Server 2022",
                Tags = new[] { "PATCH", "PATCH_SCOPE_NP", "PATCH_MODE_AUTO" },
                UserEditable = true
            };

            _serversPersistentSource.GetServer("NPVM01", _user).Returns((ServerApiModel)null!);
            _serversPersistentSource.AddServer(newServer, _user).Returns(createdServer);

            var result = _controller.Post(newServer) as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var returned = result.Value as ServerApiModel;
            Assert.IsNotNull(returned);
            Assert.AreEqual(101, returned.ServerId);
            CollectionAssert.AreEqual(new[] { "PATCH", "PATCH_SCOPE_NP", "PATCH_MODE_AUTO" }, returned.Tags.ToArray());

            _serversAuditPersistentSource.Received(1).InsertServerAudit(
                "DOMAIN\\TestUser",
                ActionType.Create,
                101,
                Arg.Is<string?>(before => before == null),
                Arg.Is<string>(json =>
                    json.Contains("\"Tags\"") &&
                    json.Contains("\"PATCH_SCOPE_NP\"") &&
                    !json.Contains("ApplicationTags")));
        }

        [TestMethod]
        public void Put_WithUpdatedTagsArray_UpdatesServerAndWritesBeforeAfterAuditWithTagsArray()
        {
            var beforeServer = new ServerApiModel
            {
                ServerId = 200,
                Name = "PRVM01",
                OsName = "Windows Server 2022",
                Tags = new[] { "AppServer" },
                EnvironmentNames = new List<string> { "Application Production" },
                UserEditable = true
            };
            var updateRequest = new ServerApiModel
            {
                ServerId = 200,
                Name = "PRVM01",
                OsName = "Windows Server 2022",
                Tags = new[] { "AppServer", "PATCH", "PATCH_SCOPE_PR", "PATCH_MODE_MANUAL" },
                EnvironmentNames = new List<string> { "Application Production" },
                UserEditable = true
            };

            _serversPersistentSource.GetEnvironmentNamesForServerId(200)
                .Returns(new List<string> { "Application Production" });
            _environmentsPersistentSource.GetEnvironment("Application Production", _user)
                .Returns(new EnvironmentApiModel { EnvironmentName = "Application Production" });
            _securityPrivilegesChecker.CanModifyEnvironment(_user, "Application Production").Returns(true);
            _serversPersistentSource.GetServer("PRVM01", _user).Returns(beforeServer);
            _serversPersistentSource.GetServer(200, _user).Returns(beforeServer, updateRequest);
            _serversPersistentSource.UpdateServer(200, updateRequest, _user).Returns(updateRequest);

            var result = _controller.Put(200, updateRequest) as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            var returned = result.Value as ServerApiModel;
            Assert.IsNotNull(returned);
            CollectionAssert.AreEqual(
                new[] { "AppServer", "PATCH", "PATCH_SCOPE_PR", "PATCH_MODE_MANUAL" },
                returned.Tags.ToArray());

            _serversAuditPersistentSource.Received(1).InsertServerAudit(
                "DOMAIN\\TestUser",
                ActionType.Update,
                200,
                Arg.Is<string>(fromJson =>
                    fromJson.Contains("\"Tags\"") &&
                    fromJson.Contains("\"AppServer\"") &&
                    !fromJson.Contains("ApplicationTags")),
                Arg.Is<string>(toJson =>
                    toJson.Contains("\"Tags\"") &&
                    toJson.Contains("\"PATCH_MODE_MANUAL\"") &&
                    !toJson.Contains("ApplicationTags")));
        }

        [TestMethod]
        public void GetAppServersByEnvName_MapsTagLinksToTagsArray()
        {
            var serverEntity = new Server
            {
                Id = 301,
                Name = "APPSRV01",
                OsName = "Windows Server 2022",
                TagLinks = new List<ServerTag>
                {
                    new ServerTag { ServerId = 301, Tag = "appserv" },
                    new ServerTag { ServerId = 301, Tag = "PATCH" }
                }
            };
            _serversPersistentSource.GetAppServerDetails("Endur DV 01")
                .Returns(new List<Server> { serverEntity });

            var result = _controller.GetAppServersByEnvName("Endur DV 01") as OkObjectResult;

            Assert.IsNotNull(result);
            var list = result.Value as List<ServerApiModel>;
            Assert.IsNotNull(list);
            Assert.AreEqual(1, list.Count);
            CollectionAssert.AreEqual(new[] { "appserv", "PATCH" }, list[0].Tags.ToArray());
        }

        [TestMethod]
        public void ServerApiModel_JsonSerializationAndDeserialization_UsesTagsStringArrayAndIgnoresLegacyApplicationTags()
        {
            const string legacyPayload = """
                {
                    "ServerId": 200,
                    "Name": "PRVM01",
                    "OsName": "Windows Server 2022",
                    "ApplicationTags": "PATCH;PATCH_SCOPE_PR;PATCH_MODE_MANUAL"
                }
                """;
            var deserializedFromLegacy = JsonSerializer.Deserialize<ServerApiModel>(legacyPayload);
            Assert.IsNotNull(deserializedFromLegacy);
            Assert.IsEmpty(deserializedFromLegacy.Tags,
                "Sending the removed ApplicationTags string property without Tags (string[]) leaves Tags empty.");

            const string validPayload = """
                {
                    "ServerId": 200,
                    "Name": "PRVM01",
                    "OsName": "Windows Server 2022",
                    "Tags": ["PATCH", "PATCH_SCOPE_PR", "PATCH_MODE_MANUAL"]
                }
                """;
            var deserializedFromTags = JsonSerializer.Deserialize<ServerApiModel>(validPayload);
            Assert.IsNotNull(deserializedFromTags);
            CollectionAssert.AreEqual(
                new[] { "PATCH", "PATCH_SCOPE_PR", "PATCH_MODE_MANUAL" },
                deserializedFromTags.Tags.ToArray());

            var serialized = JsonSerializer.Serialize(deserializedFromTags);
            StringAssert.Contains(serialized, "\"Tags\":[\"PATCH\",\"PATCH_SCOPE_PR\",\"PATCH_MODE_MANUAL\"]");
            Assert.IsFalse(serialized.Contains("ApplicationTags", StringComparison.OrdinalIgnoreCase),
                "Serialized ServerApiModel must never contain ApplicationTags.");
        }
    }
}
