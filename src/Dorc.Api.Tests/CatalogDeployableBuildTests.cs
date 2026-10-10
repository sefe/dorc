using System.Security.Claims;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class CatalogDeployableBuildTests
    {
        private IDeployLibrary _mockedDeployLibrary = null!;
        private IRequestsPersistentSource _mockedReqPs = null!;
        private CatalogDeployableBuild _catalogBuild = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            _mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            _catalogBuild = new CatalogDeployableBuild(_mockedDeployLibrary, _mockedReqPs);
        }

        [TestMethod]
        public void IsValid_CatalogSentinelBuildUrl_ReturnsTrue()
        {
            var buildDetails = new BuildDetails(BuildDetails.CatalogSentinel);

            var result = _catalogBuild.IsValid(buildDetails);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValid_NonCatalogBuildUrl_ReturnsFalse()
        {
            var buildDetails = new BuildDetails("file://some_path");

            var result = _catalogBuild.IsValid(buildDetails);

            Assert.IsFalse(result);
            Assert.IsFalse(string.IsNullOrEmpty(_catalogBuild.ValidationResult));
        }

        [TestMethod]
        public void Process_NullComponents_ReturnsZeroIdWithoutSubmitting()
        {
            var request = new RequestDto
            {
                Project = "MyProject",
                Environment = "MyEnv",
                BuildUrl = BuildDetails.CatalogSentinel,
                Components = null
            };

            var result = _catalogBuild.Process(request, new ClaimsPrincipal());

            Assert.AreEqual(0, result.Id);
            Assert.AreEqual("Catalog request has no components.", result.Status);
            _mockedDeployLibrary.DidNotReceiveWithAnyArgs().SubmitRequest(
                default!, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void Process_EmptyComponents_ReturnsZeroIdWithoutSubmitting()
        {
            var request = new RequestDto
            {
                Project = "MyProject",
                Environment = "MyEnv",
                BuildUrl = BuildDetails.CatalogSentinel,
                Components = new List<string>()
            };

            var result = _catalogBuild.Process(request, new ClaimsPrincipal());

            Assert.AreEqual(0, result.Id);
            Assert.AreEqual("Catalog request has no components.", result.Status);
            _mockedDeployLibrary.DidNotReceiveWithAnyArgs().SubmitRequest(
                default!, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void Process_ValidRequest_SubmitsWithCatalogFlagAndReturnsRequestStatus()
        {
            var user = new ClaimsPrincipal();
            var request = new RequestDto
            {
                Project = "MyProject",
                Environment = "MyEnv",
                BuildUrl = BuildDetails.CatalogSentinel,
                Components = new List<string> { "CompA" }
            };

            // Stubbed only for isCatalog == true: an isCatalog:false call
            // would return 0 and take the "zero result" branch instead.
            _mockedDeployLibrary.SubmitRequest(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<List<string>>(), Arg.Any<List<RequestProperty>>(), Arg.Any<ClaimsPrincipal>(), true)
                .Returns(777);

            var expectedStatus = new RequestStatusDto { Id = 777, Status = "Pending" };
            _mockedReqPs.GetRequestStatus(777).Returns(expectedStatus);

            var result = _catalogBuild.Process(request, user);

            Assert.AreSame(expectedStatus, result);
            _mockedDeployLibrary.Received(1).SubmitRequest(
                "MyProject", "MyEnv", string.Empty, string.Empty,
                Arg.Is<List<string>>(c => c.Count == 1 && c[0] == "CompA"),
                Arg.Any<List<RequestProperty>>(), user, true);
        }

        [TestMethod]
        public void Process_DeployLibraryReturnsZero_ReturnsZeroResultStatus()
        {
            var request = new RequestDto
            {
                Project = "MyProject",
                Environment = "MyEnv",
                BuildUrl = BuildDetails.CatalogSentinel,
                Components = new List<string> { "CompA" }
            };

            _mockedDeployLibrary.SubmitRequest(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<List<string>>(), Arg.Any<List<RequestProperty>>(), Arg.Any<ClaimsPrincipal>(), true)
                .Returns(0);

            var result = _catalogBuild.Process(request, new ClaimsPrincipal());

            Assert.AreEqual(0, result.Id);
            Assert.AreEqual("DeployLibrary has returned zero result", result.Status);
        }
    }
}
