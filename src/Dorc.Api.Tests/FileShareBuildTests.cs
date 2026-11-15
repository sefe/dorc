using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// The drop location is not merely data. It is published to deployment scripts as
    /// $DropFolder$, and DOrc's own RunDeployment.ps1 dot-sources DeploymentCommon.ps1 from
    /// it - so an unconfined drop location is arbitrary code execution as the deployment
    /// account, available to anyone who can submit a request.
    ///
    /// Validation previously checked only that the URL was well formed, named a file share
    /// and pointed at a directory that existed. It now also requires the location to lie
    /// beneath one of the project's configured artefact roots, matching the confinement the
    /// Azure DevOps build path already gets from validating against the project's own
    /// build definitions.
    /// </summary>
    [TestClass]
    public class FileShareBuildTests
    {
        private const string ProjectName = "TestProject";
        private const string ArtefactRoot = @"\\buildserver\drops\TestProject";

        private IFileSystemHelper _fileSystemHelper = null!;
        private IDeployLibrary _deployLibrary = null!;
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private IProjectsPersistentSource _projectsPersistentSource = null!;
        private ISourceHostAllowList _sourceHosts = null!;

        [TestInitialize]
        public void Setup()
        {
            _fileSystemHelper = Substitute.For<IFileSystemHelper>();
            _fileSystemHelper.DirectoryExists(Arg.Any<string>()).Returns(true);

            _deployLibrary = Substitute.For<IDeployLibrary>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _sourceHosts = BuildAllowList("buildserver");

            _projectsPersistentSource.GetProject(ProjectName).Returns(new ProjectApiModel
            {
                ProjectName = ProjectName,
                ArtefactsUrl = ArtefactRoot
            });
        }

        private FileShareDeployableBuild NewBuild(ISourceHostAllowList? sourceHosts = null) =>
            new(_fileSystemHelper, _deployLibrary, _requestsPersistentSource, _projectsPersistentSource, sourceHosts ?? _sourceHosts);

        private static ISourceHostAllowList BuildAllowList(params string[] artefactHosts)
        {
            var entries = new Dictionary<string, string?>();
            for (var i = 0; i < artefactHosts.Length; i++)
            {
                entries[$"{SourceHostAllowList.ArtefactHostsSetting}:{i}"] = artefactHosts[i];
            }

            return new SourceHostAllowList(new ConfigurationBuilder().AddInMemoryCollection(entries).Build());
        }

        private static BuildDetails BuildAt(string url) =>
            new(new RequestDto
            {
                BuildUrl = url,
                BuildText = "buildText",
                BuildNum = "buildNum",
                Project = ProjectName
            }, SourceControlType.FileShare);

        [TestMethod]
        public void AcceptsABuildBeneathTheProjectsArtefactRoot()
        {
            Assert.IsTrue(NewBuild().IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
        }

        [TestMethod]
        public void AcceptsTheArtefactRootItself()
        {
            Assert.IsTrue(NewBuild().IsValid(BuildAt(ArtefactRoot)));
        }

        /// <summary>
        /// The weakness this step closes: any reachable share was previously accepted.
        /// </summary>
        [TestMethod]
        public void RejectsABuildOnAnUnrelatedShare()
        {
            var build = NewBuild();

            Assert.IsFalse(build.IsValid(BuildAt(@"\\attacker\share\payload")));
            StringAssert.Contains(build.ValidationResult, "outside the artefacts location");
        }

        [TestMethod]
        public void RejectsTraversalOutOfTheArtefactRoot()
        {
            Assert.IsFalse(NewBuild().IsValid(BuildAt(ArtefactRoot + @"\..\..\OtherProject\drop")));
        }

        [TestMethod]
        public void AcceptsAnyOfSeveralConfiguredArtefactRoots()
        {
            _projectsPersistentSource.GetProject(ProjectName).Returns(new ProjectApiModel
            {
                ProjectName = ProjectName,
                ArtefactsUrl = $@"\\buildserver\drops\Other;{ArtefactRoot}"
            });

            Assert.IsTrue(NewBuild().IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
        }

        /// <summary>
        /// An absent allow-list confines nothing. Admitting everything on that basis would
        /// make the check vacuous for exactly the projects least configured.
        /// </summary>
        [TestMethod]
        public void RejectsWhenTheProjectHasNoArtefactRootConfigured()
        {
            _projectsPersistentSource.GetProject(ProjectName).Returns(new ProjectApiModel
            {
                ProjectName = ProjectName,
                ArtefactsUrl = string.Empty
            });

            var build = NewBuild();

            Assert.IsFalse(build.IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
            StringAssert.Contains(build.ValidationResult, "no artefacts location configured");
        }

        [TestMethod]
        public void RejectsWhenTheProjectCannotBeResolved()
        {
            _projectsPersistentSource.GetProject(ProjectName).Returns((ProjectApiModel)null!);

            Assert.IsFalse(NewBuild().IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
        }

        // --- pre-existing behaviour, retained -----------------------------------------

        [TestMethod]
        public void RejectsAWrongBuildType()
        {
            Assert.IsFalse(NewBuild().IsValid(BuildAt("ftp://some_path")));
        }

        [TestMethod]
        public void RejectsADirectoryThatDoesNotExist()
        {
            _fileSystemHelper.DirectoryExists(Arg.Any<string>()).Returns(false);

            Assert.IsFalse(NewBuild().IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
        }

        [TestMethod]
        public void RejectsAMalformedUrl()
        {
            Assert.IsFalse(NewBuild().IsValid(BuildAt("|not a url|")));
        }

        [TestMethod]
        public void RejectsUnrelatedShareBeforeCallingDirectoryExists()
        {
            var build = NewBuild();

            Assert.IsFalse(build.IsValid(BuildAt("file://attacker.example/share/payload")));
            _fileSystemHelper.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
        }

        [TestMethod]
        public void RejectsRemoteShareBeforeCallingDirectoryExistsWhenAllowListIsUnconfigured()
        {
            var build = NewBuild(BuildAllowList());

            Assert.IsFalse(build.IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
            StringAssert.Contains(build.ValidationResult, SourceHostAllowList.ArtefactHostsSetting);
            _fileSystemHelper.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
        }

        [TestMethod]
        public void RejectsRemoteShareBeforeCallingDirectoryExistsWhenHostIsDisallowed()
        {
            var build = NewBuild(BuildAllowList("other-host.corp"));

            Assert.IsFalse(build.IsValid(BuildAt(ArtefactRoot + @"\1.2.3")));
            StringAssert.Contains(build.ValidationResult, "not permitted");
            _fileSystemHelper.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
        }

        [TestMethod]
        [DataRow(@"\\buildserver@SSL@443\drops\TestProject\1.2.3")]
        [DataRow(@"\\buildserver@80\drops\TestProject\1.2.3")]
        [DataRow("file://buildserver@SSL@443/drops/TestProject/1.2.3")]
        public void RejectsWebDavUncSyntaxBeforeCallingDirectoryExists(string webDavUrl)
        {
            var build = NewBuild();

            Assert.IsFalse(build.IsValid(BuildAt(webDavUrl)));
            _fileSystemHelper.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
        }

        [TestMethod]
        public void AcceptsExtendedLengthUncBuildWhenHostIsPermitted()
        {
            Assert.IsTrue(NewBuild().IsValid(BuildAt(@"\\?\UNC\buildserver\drops\TestProject\1.2.3")));
            _fileSystemHelper.Received(1).DirectoryExists(@"\\buildserver\drops\TestProject\1.2.3");
        }

        [TestMethod]
        public void AcceptsLocalDiskBuildWhenArtefactHostAllowListIsUnconfigured()
        {
            _projectsPersistentSource.GetProject(ProjectName).Returns(new ProjectApiModel
            {
                ProjectName = ProjectName,
                ArtefactsUrl = @"C:\drops\TestProject"
            });

            var build = NewBuild(BuildAllowList());

            Assert.IsTrue(build.IsValid(BuildAt(@"file:///C:/drops/TestProject/1.2.3")));
        }
    }
}
