using Dorc.Api.Model;

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
    }
}