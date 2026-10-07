using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
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

        [TestInitialize]
        public void Setup()
        {
            _fileSystemHelper = Substitute.For<IFileSystemHelper>();
            _fileSystemHelper.DirectoryExists(Arg.Any<string>()).Returns(true);

            _deployLibrary = Substitute.For<IDeployLibrary>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();

            _projectsPersistentSource.GetProject(ProjectName).Returns(new ProjectApiModel
            {
                ProjectName = ProjectName,
                ArtefactsUrl = ArtefactRoot
            });
        }

        private FileShareDeployableBuild NewBuild() =>
            new(_fileSystemHelper, _deployLibrary, _requestsPersistentSource, _projectsPersistentSource);

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
    }
}
