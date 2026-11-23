using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Org.OpenAPITools.Model;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class AzureDevOpsDeployableBuildTests
    {
        [TestMethod]
        public void IsValid_UnpinnedBuildMatchingBuildNumber_ReturnsTrue()
        {
            RequestDto request = new RequestDto
            {
                BuildText = "buildText",
                BuildNum = "buildNum",
                Project = "someProject",
                BuildUrl = "http://some_url",
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "someProject",
                    ArtefactsUrl = "http://some_url",
                    ArtefactsSubPaths = "adoProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedAdoClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedAdoClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                    {
                        new BuildDefinitionReference
                            {Name = "BuildDef1"},
                        new BuildDefinitionReference
                            {Name = "buildText"},
                    });

            mockedAdoClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Build>
                {
                    new Build
                        {BuildNumber = "build1", KeepForever = false, Uri =  "vstfs://some_path",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"} },
                    new Build
                        {BuildNumber = "buildNum", KeepForever = false, Uri = "vstfs://some_path",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();

            var mockedRequestSubmitter = Substitute.For<IDeploymentRequestSubmitter>();
            var adoBuild = new AzureDevOpsDeployableBuild(mockedAdoClient, mockedLog, mockedProjectsPds, mockedRequestSubmitter, mockedReqPs);
            var result = adoBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValid_PinnedBuildWhenNoBuildsKeepForever_ReturnsFalse()
        {
            RequestDto request = new RequestDto
            {
                BuildText = "buildText",
                BuildNum = "buildNum",
                Project = "someProject",
                BuildUrl = "http://some_url",
                Pinned = true
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "someProject",
                    ArtefactsUrl = "http://some_url",
                    ArtefactsSubPaths = "adoProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedAdoClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedAdoClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "BuildDef2"},
                });
            mockedAdoClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Build>
                {
                    new Build
                    {
                        BuildNumber = "buildText_build1", KeepForever = false, Uri = "vstfs://some_path"
                    },
                    new Build
                    {
                        BuildNumber = "buildText_buildNum", KeepForever = false, Uri = "vstfs://some_path"
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedRequestSubmitter = Substitute.For<IDeploymentRequestSubmitter>();
            var adoBuild = new AzureDevOpsDeployableBuild(mockedAdoClient, mockedLog, mockedProjectsPds, mockedRequestSubmitter, mockedReqPs);
            var result = adoBuild.IsValid(new BuildDetails(request));
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValid_PinnedBuildWhenMatchingBuildHasKeepForever_ReturnsTrue()
        {
            RequestDto request = new RequestDto
            {
                BuildText = "buildText",
                BuildNum = "buildNum",
                Project = "someProject",
                BuildUrl = "http://some_url",
                Pinned = true
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "someProject",
                    ArtefactsUrl = "http://some_url",
                    ArtefactsSubPaths = "adoProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedAdoClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedAdoClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedAdoClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Build>
                {
                        new Build {BuildNumber = "build1", KeepForever = false,Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                        new Build {BuildNumber = "buildNum", KeepForever = true,Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedRequestSubmitter = Substitute.For<IDeploymentRequestSubmitter>();
            var adoBuild = new AzureDevOpsDeployableBuild(mockedAdoClient, mockedLog, mockedProjectsPds, mockedRequestSubmitter, mockedReqPs);
            var result = adoBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValid_LatestBuildNum_SelectsFirstReturnedBuild()
        {
            RequestDto request = new RequestDto
            {
                BuildText = "buildText",
                BuildNum = "latest",
                Project = "someProject",
                BuildUrl = "http://some_url",
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "someProject",
                    ArtefactsUrl = "http://some_url",
                    ArtefactsSubPaths = "adoProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedAdoClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedAdoClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedAdoClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Build>
                {
                    new Build
                    {
                        BuildNumber = "buildNum2", KeepForever = false, Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                    new Build
                    {
                        BuildNumber = "buildNum1", KeepForever = false, Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedRequestSubmitter = Substitute.For<IDeploymentRequestSubmitter>();
            var adoBuild = new AzureDevOpsDeployableBuild(mockedAdoClient, mockedLog, mockedProjectsPds, mockedRequestSubmitter, mockedReqPs);
            var result = adoBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
            Assert.AreEqual("vstfs://some_path2", adoBuild.AzureDevOpsBuildUrl);
        }

        [TestMethod]
        public void IsValid_SpecificBuildNum_SelectsExactMatchingBuildUri()
        {
            RequestDto request = new RequestDto
            {
                BuildText = "buildText",
                BuildNum = "19.01.01.1",
                Project = "someProject",
                BuildUrl = "http://some_url",
                Pinned = false
            };
            var mockedProjectsPds = Substitute.For<IProjectsPersistentSource>();
            var mockedReqPs = Substitute.For<IRequestsPersistentSource>();
            mockedProjectsPds.GetProject(Arg.Any<string>())
                .Returns(new ProjectApiModel
                {
                    ProjectName = "someProject",
                    ArtefactsUrl = "http://some_url",
                    ArtefactsSubPaths = "adoProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedAdoClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedAdoClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedAdoClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Build>
                {
                    new Build
                    {
                        BuildNumber = "19.01.01.2", KeepForever = false, Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                    new Build
                    {
                        BuildNumber = "19.01.01.1", KeepForever = false, Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedRequestSubmitter = Substitute.For<IDeploymentRequestSubmitter>();
            var adoBuild = new AzureDevOpsDeployableBuild(mockedAdoClient, mockedLog, mockedProjectsPds, mockedRequestSubmitter, mockedReqPs);
            var result = adoBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
            Assert.AreEqual("vstfs://some_path1", adoBuild.AzureDevOpsBuildUrl);
        }
    }
}