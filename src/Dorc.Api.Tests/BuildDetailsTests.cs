using Dorc.Api.Model;
using Dorc.ApiModel;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class BuildDetailsTests
    {
        [TestMethod]
        public void BuildTypeTest()
        {
            var tfsBuildUrl = "http://tfdv/tfs/somebuild";
            var tfsBuildDetails = new BuildDetails(tfsBuildUrl);
            Assert.AreEqual(BuildType.TfsBuild, tfsBuildDetails.Type);
            var fileShareBuildUrl = "file://path_to_some_share";
            var fileShareBuildDetails = new BuildDetails(fileShareBuildUrl);
            Assert.AreEqual(BuildType.FileShareBuild, fileShareBuildDetails.Type);
            var wrongBuildUrl = "type://adsfadfasdfafd";
            var wrongBuildDetails = new BuildDetails(wrongBuildUrl);
            Assert.AreEqual(BuildType.UnknownBuildType, wrongBuildDetails.Type);
        }

        [TestMethod]
        public void BuildType_GitHub_HttpUrl_ReturnsGitHubBuild()
        {
            var request = new RequestDto { BuildUrl = "https://api.github.com/repos/owner/repo" };
            var details = new BuildDetails(request, SourceControlType.GitHub);
            Assert.AreEqual(BuildType.GitHubBuild, details.Type);
        }

        [TestMethod]
        public void BuildType_GitHub_NumericRunId_ReturnsGitHubBuild()
        {
            var request = new RequestDto { BuildUrl = "12345678901" };
            var details = new BuildDetails(request, SourceControlType.GitHub);
            Assert.AreEqual(BuildType.GitHubBuild, details.Type);
        }

        [TestMethod]
        public void BuildType_AzureDevOps_HttpUrl_ReturnsTfsBuild()
        {
            var request = new RequestDto { BuildUrl = "http://tfs/tfs/build" };
            var details = new BuildDetails(request, SourceControlType.AzureDevOps);
            Assert.AreEqual(BuildType.TfsBuild, details.Type);
        }

        [TestMethod]
        public void BuildType_GitHub_DefaultSourceControlType_HttpUrl_ReturnsTfsBuild()
        {
            // Without specifying GitHub source control type, http URL should be TFS
            var request = new RequestDto { BuildUrl = "http://some_url" };
            var details = new BuildDetails(request);
            Assert.AreEqual(BuildType.TfsBuild, details.Type);
        }

        [TestMethod]
        public void BuildType_EmptyUrl_ReturnsUnknown()
        {
            var request = new RequestDto { BuildUrl = "" };
            var details = new BuildDetails(request, SourceControlType.GitHub);
            Assert.AreEqual(BuildType.UnknownBuildType, details.Type);
        }

        [TestMethod]
        public void BuildType_NullUrl_ReturnsUnknown()
        {
            var details = new BuildDetails((string)null!);
            Assert.AreEqual(BuildType.UnknownBuildType, details.Type);
        }
    }
}