using Dorc.Api.Model;
using Dorc.Api.Build;
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
    public class TfsBuildTests
    {
        //type: OK pined: no
        [TestMethod]
        public void TfsBuildValidTest1()
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
                    ArtefactsSubPaths = "tfsProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedTfsClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedTfsClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                    {
                        new BuildDefinitionReference
                            {Name = "BuildDef1"},
                        new BuildDefinitionReference
                            {Name = "buildText"},
                    });

            mockedTfsClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Org.OpenAPITools.Model.Build>
                {
                    new Org.OpenAPITools.Model.Build
                        {BuildNumber = "build1", KeepForever = false, Uri =  "vstfs://some_path",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"} },
                    new Org.OpenAPITools.Model.Build
                        {BuildNumber = "buildNum", KeepForever = false, Uri = "vstfs://some_path",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();

            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var tfsBuild = new AzureDevOpsDeployableBuild(mockedTfsClient, mockedLog, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var result = tfsBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
        }

        //type: OK pined: yes but not found
        [TestMethod]
        public void TfsBuildValidTest2()
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
                    ArtefactsSubPaths = "tfsProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedTfsClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedTfsClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "BuildDef2"},
                });
            mockedTfsClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Org.OpenAPITools.Model.Build>
                {
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "buildText_build1", KeepForever = false, Uri = "vstfs://some_path"
                    },
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "buildText_buildNum", KeepForever = false, Uri = "vstfs://some_path"
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var tfsBuild = new AzureDevOpsDeployableBuild(mockedTfsClient, mockedLog, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var result = tfsBuild.IsValid(new BuildDetails(request));
            Assert.IsFalse(result);
        }

        //type: OK pined: yes 
        [TestMethod]
        public void TfsBuildValidTest3()
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
                    ArtefactsSubPaths = "tfsProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedTfsClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedTfsClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedTfsClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Org.OpenAPITools.Model.Build>
                {
                        new Org.OpenAPITools.Model.Build {BuildNumber = "build1", KeepForever = false,Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                        new Org.OpenAPITools.Model.Build {BuildNumber = "buildNum", KeepForever = true,Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}},
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var tfsBuild = new AzureDevOpsDeployableBuild(mockedTfsClient, mockedLog, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var result = tfsBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
        }

        //type: OK pined: no get latest 
        [TestMethod]
        public void TfsBuildValidTest4()
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
                    ArtefactsSubPaths = "tfsProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedTfsClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedTfsClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedTfsClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Org.OpenAPITools.Model.Build>
                {
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "buildNum2", KeepForever = false, Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "buildNum1", KeepForever = false, Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var tfsBuild = new AzureDevOpsDeployableBuild(mockedTfsClient, mockedLog, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var result = tfsBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
            Assert.AreEqual("vstfs://some_path2", tfsBuild.AzureDevOpsBuildUrl);
        }

        //type: OK pined: no get specific build 
        [TestMethod]
        public void TfsBuildValidTest5()
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
                    ArtefactsSubPaths = "tfsProject",
                    ArtefactsBuildRegex = "Something here"
                });
            var mockedTfsClient = Substitute.For<IAzureDevOpsServerWebClient>();
            mockedTfsClient.GetBuildDefinitionsForProjects(Arg.Any<string>(), Arg.Any<string>(),
                        Arg.Any<string>())
                .Returns(new List<BuildDefinitionReference>
                {
                    new BuildDefinitionReference
                        {Name = "BuildDef1"},
                    new BuildDefinitionReference
                        {Name = "buildText"},
                });
            mockedTfsClient.GetBuildsFromBuildNumberAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), 10)
                .Returns(Task.Factory.StartNew(() => new List<Org.OpenAPITools.Model.Build>
                {
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "19.01.01.2", KeepForever = false, Uri = "vstfs://some_path2",
                        Project = new TeamProjectReference {Name="Build1"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                    new Org.OpenAPITools.Model.Build
                    {
                        BuildNumber = "19.01.01.1", KeepForever = false, Uri = "vstfs://some_path1",
                        Project = new TeamProjectReference {Name="Build2"},
                        Definition = new DefinitionReference { Name= "Definition1"}
                    },
                }));
            var mockedLog = new MockedLog<AzureDevOpsDeployableBuild>();
            var mockedDeployLibrary = Substitute.For<IDeployLibrary>();
            var tfsBuild = new AzureDevOpsDeployableBuild(mockedTfsClient, mockedLog, mockedProjectsPds, mockedDeployLibrary, mockedReqPs);
            var result = tfsBuild.IsValid(new BuildDetails(request));
            Assert.IsTrue(result);
            Assert.AreEqual("vstfs://some_path1", tfsBuild.AzureDevOpsBuildUrl);
        }
    }
}