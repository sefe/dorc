using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers
{
    /// <summary>
    /// Tag lookup parameters must be single non-empty
    /// tags. An empty needle would match every untagged database
    /// and a ';'-bearing one would perform adjacent-sublist matching.
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

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Endur;Reporting")]
        public void GetByTag_RejectsInvalidTagWithoutCallingTheSource(string tag)
        {
            var (controller, source) = DatabasesController();

            var result = controller.GetByTag("Endur DV 10", tag);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, "'tag'");
            source.DidNotReceiveWithAnyArgs().GetDatabaseByTag(default(string), default);
        }

        [TestMethod]
        public void GetByTag_ValidSingleTag_CallsThrough()
        {
            var (controller, source) = DatabasesController();
            source.GetDatabaseByTag("Endur DV 10", "Endur").Returns(new DatabaseApiModel { Name = "D1" });

            var result = controller.GetByTag("Endur DV 10", "Endur");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            source.Received(1).GetDatabaseByTag("Endur DV 10", "Endur");
        }

        [TestMethod]
        public void GetByTag_TrimsTheNeedleAtTheBoundary()
        {
            // The trim has to happen here, once, so every layer below sees the same
            // needle. Asserting only the status code would pass with the trim deleted,
            // and the untrimmed needle would then miss a tag stored without padding.
            var (controller, source) = DatabasesController();
            source.GetDatabaseByTag("Endur DV 10", "Endur").Returns(new DatabaseApiModel { Name = "D1" });

            var result = controller.GetByTag("Endur DV 10", "  Endur  ");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            source.Received(1).GetDatabaseByTag("Endur DV 10", "Endur");
        }

        [TestMethod]
        public void GetByTag_DuplicateTagOnEnvironment_ReturnsBadRequestWithMessage()
        {
            var (controller, source) = DatabasesController();
            source
                .When(s => s.GetDatabaseByTag("Endur DV 10", "Endur"))
                .Do(_ => throw new InvalidOperationException(
                    "Environment 'Endur DV 10' has multiple databases tagged 'Endur' ('D1' on 's1', 'D2' on 's2'). Ensure at most one database on the environment carries the 'Endur' tag."));

            var result = controller.GetByTag("Endur DV 10", "Endur");

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            StringAssert.Contains((string)((BadRequestObjectResult)result).Value!, "multiple databases tagged 'Endur'");
        }
    }
}
