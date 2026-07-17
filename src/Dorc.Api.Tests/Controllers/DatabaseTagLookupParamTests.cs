using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// docs/database-tags IS S-004: tag lookup parameters must be single non-empty
    /// tags. An empty needle would match every untagged database (HLPS round-2 NEW-1)
    /// and a ';'-bearing one would perform adjacent-sublist matching. An OMITTED
    /// optional dbType keeps its no-filter semantics (SC-4 reconciliation).
    /// </summary>
    [TestClass]
    public class DatabaseTagLookupParamTests
    {
        private static (RefDataDatabasesController controller, IDatabasesPersistentSource source) DatabasesController()
        {
            var source = Substitute.For<IDatabasesPersistentSource>();
            var controller = new RefDataDatabasesController(
                source,
                Substitute.For<IDatabasesAuditPersistentSource>(),
                Substitute.For<ISecurityPrivilegesChecker>(),
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<IClaimsPrincipalReader>());
            return (controller, source);
        }

        private static (RefDataDatabaseUsersController controller, IUserPermsPersistentSource source) UsersController()
        {
            var source = Substitute.For<IUserPermsPersistentSource>();
            var controller = new RefDataDatabaseUsersController(
                Substitute.For<IManageUsers>(),
                Substitute.For<IEnvironmentMapper>(),
                source);
            return (controller, source);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Endur;Reporting")]
        public void GetByType_RejectsInvalidTagWithoutCallingTheSource(string type)
        {
            var (controller, source) = DatabasesController();

            var result = controller.GetByType("Endur DV 10", type);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, "type");
            source.DidNotReceiveWithAnyArgs().GetDatabaseByType(default(string), default);
        }

        [TestMethod]
        public void GetByType_ValidSingleTag_CallsThrough()
        {
            var (controller, source) = DatabasesController();
            source.GetDatabaseByType("Endur DV 10", "Endur").Returns(new DatabaseApiModel { Name = "D1" });

            var result = controller.GetByType("Endur DV 10", "Endur");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Endur;Ops")]
        public void GetDbUsersPermissions_RejectsSuppliedInvalidTag(string dbType)
        {
            var (controller, source) = UsersController();

            var result = controller.GetDbUsersPermissions("s1", "db1", dbType);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, "dbType");
            source.DidNotReceiveWithAnyArgs().GetUserDbPermissions(default!, default!, default);
        }

        [TestMethod]
        public void GetDbUsersPermissions_OmittedDbType_KeepsNoFilterSemantics()
        {
            // The absent-param regression demanded by the IS: omission is not an
            // empty parameter and must keep flowing through as "no filter".
            var (controller, source) = UsersController();

            var result = controller.GetDbUsersPermissions("s1", "db1");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            Assert.AreEqual(200, ((ObjectResult)result).StatusCode);
            source.Received(1).GetUserDbPermissions("s1", "db1", null);
        }
    }
}
