using System.Collections.Generic;
using System.Security.Claims;
using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Dorc.Core.Exceptions;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EnvironmentBuildCopyTests
    {
        [TestMethod]
        public void DeployCopyEnvBuildWithComponentNames_FlattensHierarchyAndMatchesByNameBeforeNumericIdFallback()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "DOMAIN\\tester") }, "TestAuth"));
            var projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            var componentsPersistentSource = Substitute.For<IComponentsPersistentSource>();
            var manageProjectsSource = Substitute.For<IManageProjectsPersistentSource>();
            var environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            var requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "HierProj" };
            projectsPersistentSource.GetProject("HierProj").Returns(project);
            projectsPersistentSource.GetProject(10).Returns(project);

            var leafChildA = new ComponentApiModel
            {
                ComponentId = 501,
                ComponentName = "ChildLeafA",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            var leafChildB = new ComponentApiModel
            {
                ComponentId = 502,
                ComponentName = "ChildLeafB",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            var parentGroup = new ComponentApiModel
            {
                ComponentId = 500,
                ComponentName = "ParentGroup",
                IsEnabled = true,
                Children = new List<ComponentApiModel> { leafChildA, leafChildB }
            };

            projectsPersistentSource.GetComponentsForProject(10).Returns(new List<ComponentApiModel> { parentGroup });
            componentsPersistentSource.GetComponentByName("ChildLeafA").Returns(new ComponentApiModel
            {
                ComponentId = 501,
                ComponentName = "ChildLeafA",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            });

            manageProjectsSource.GetOrderedComponents(Arg.Any<IEnumerable<string>>())
                .Returns(new List<ComponentApiModel> { leafChildA, leafChildB });

            environmentsPersistentSource.GetEnvironment("SourceEnv", user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "SourceEnv" });
            environmentsPersistentSource.GetEnvironmentComponentStatuses(1)
                .Returns(new List<EnvironmentComponentStatusModel>
                {
                    new EnvironmentComponentStatusModel
                    {
                        Component = "ChildLeafA",
                        BuildNumber = "1.0.0",
                        DropLocation = "\\\\share\\drop",
                        Uri = "vstfs:///Build/Build/1",
                        Project = "HierProj"
                    },
                    new EnvironmentComponentStatusModel
                    {
                        Component = "ChildLeafB",
                        BuildNumber = "1.0.0",
                        DropLocation = "\\\\share\\drop",
                        Uri = "vstfs:///Build/Build/1",
                        Project = "HierProj"
                    }
                });
            requestsPersistentSource.SubmitRequest(Arg.Any<DeploymentRequest>()).Returns(777);

            var submitter = new DeploymentRequestSubmitter(
                projectsPersistentSource,
                componentsPersistentSource,
                manageProjectsSource,
                environmentsPersistentSource,
                Substitute.For<ILoggerFactory>(),
                requestsPersistentSource,
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IBuildServerClientFactory>());

            var idsByNameAndId = submitter.DeployCopyEnvBuildWithComponentNames("SourceEnv", "TargetEnv", "HierProj", "ChildLeafA;502", user);
            Assert.AreEqual(1, idsByNameAndId.Count);
            Assert.AreEqual(777, idsByNameAndId[0]);

            var idsByIds = submitter.CopyEnvBuildWithComponentIds("SourceEnv", "TargetEnv", "HierProj", new[] { 501 }, user);
            Assert.AreEqual(1, idsByIds.Count);

            var idsAll = submitter.CopyEnvBuildAllComponents("SourceEnv", "TargetEnv", "HierProj", user);
            Assert.AreEqual(1, idsAll.Count);

            // Verify that a numeric-named component takes precedence over another component whose numeric ComponentId matches the token
            var numericNamedLeaf = new ComponentApiModel
            {
                ComponentId = 999,
                ComponentName = "502",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };
            parentGroup.Children.Add(numericNamedLeaf);
            var requestedPairs = new List<string>();
            manageProjectsSource.GetOrderedComponents(Arg.Do<IEnumerable<string>>(names => requestedPairs = new List<string>(names)))
                .Returns(new List<ComponentApiModel> { numericNamedLeaf });
            environmentsPersistentSource.GetEnvironmentComponentStatuses(1)
                .Returns(new List<EnvironmentComponentStatusModel>
                {
                    new EnvironmentComponentStatusModel
                    {
                        Component = "502",
                        BuildNumber = "1.0.0",
                        DropLocation = "\\\\share\\drop",
                        Uri = "vstfs:///Build/Build/1",
                        Project = "HierProj"
                    }
                });
            var idsNumericNamePrecedence = submitter.DeployCopyEnvBuildWithComponentNames("SourceEnv", "TargetEnv", "HierProj", "502", user);
            Assert.AreEqual(1, idsNumericNamePrecedence.Count);
            CollectionAssert.AreEqual(new[] { "502" }, requestedPairs);

            Assert.ThrowsExactly<WrongComponentsException>(() =>
                submitter.DeployCopyEnvBuildWithComponentNames("SourceEnv", "TargetEnv", "HierProj", "NonExistentComp", user));
        }
    }
}
