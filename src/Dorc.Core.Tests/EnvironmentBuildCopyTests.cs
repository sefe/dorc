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
            environmentsPersistentSource.GetEnvironment("TargetEnv", user)
                .Returns(new EnvironmentApiModel { EnvironmentId = 2, EnvironmentName = "TargetEnv" });
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

            // Verify that component statuses belonging to a different project are not copied into this project's request
            environmentsPersistentSource.GetEnvironmentComponentStatuses(1)
                .Returns(new List<EnvironmentComponentStatusModel>
                {
                    new EnvironmentComponentStatusModel
                    {
                        Component = "502",
                        BuildNumber = "9.9.9",
                        DropLocation = "\\\\share\\other",
                        Uri = "vstfs:///Build/Build/99",
                        Project = "OtherProject"
                    }
                });
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                submitter.DeployCopyEnvBuildWithComponentNames("SourceEnv", "TargetEnv", "HierProj", "502", user));

            // Verify null sourceEnvironment, null targetEnvironment, and null project guards
            environmentsPersistentSource.GetEnvironment("MissingSource", user).Returns((EnvironmentApiModel)null!);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                submitter.CopyEnvBuildWithComponentIds("MissingSource", "TargetEnv", "HierProj", new[] { 501 }, user));

            environmentsPersistentSource.GetEnvironment("MissingTarget", user).Returns((EnvironmentApiModel)null!);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                submitter.CopyEnvBuildWithComponentIds("SourceEnv", "MissingTarget", "HierProj", new[] { 501 }, user));

            projectsPersistentSource.GetProject("MissingProj").Returns((ProjectApiModel)null!);
            Assert.ThrowsExactly<Dorc.Core.Exceptions.WrongComponentsException>(() =>
                submitter.CopyEnvBuildAllComponents("SourceEnv", "TargetEnv", "MissingProj", user));
            Assert.ThrowsExactly<Dorc.Core.Exceptions.WrongComponentsException>(() =>
                submitter.CopyEnvBuildWithComponentIds("SourceEnv", "TargetEnv", "MissingProj", new[] { 501 }, user));

            // Verify case-insensitive component matching and composite (BuildNumber, DropLocation, Uri, Project) batching
            manageProjectsSource.GetOrderedComponents(Arg.Any<IEnumerable<string>>())
                .Returns(new List<ComponentApiModel> { leafChildA, leafChildB });
            environmentsPersistentSource.GetEnvironmentComponentStatuses(1)
                .Returns(new List<EnvironmentComponentStatusModel>
                {
                    new EnvironmentComponentStatusModel
                    {
                        Component = "childleafa",
                        BuildNumber = "1.0.0",
                        DropLocation = "\\\\share\\dropA",
                        Uri = "vstfs:///Build/Build/10",
                        Project = "HierProj"
                    },
                    new EnvironmentComponentStatusModel
                    {
                        Component = "CHILDLEAFB",
                        BuildNumber = "1.0.0",
                        DropLocation = "\\\\share\\dropB",
                        Uri = "vstfs:///Build/Build/11",
                        Project = "HierProj"
                    }
                });
            var compositeBatchIds = submitter.CopyEnvBuildWithComponentIds("SourceEnv", "TargetEnv", "HierProj", new[] { 501, 502 }, user);
            Assert.AreEqual(2, compositeBatchIds.Count);
        }

        [TestMethod]
        public void EnumerableExtensions_Batch_HandlesNullKeysDefaultFirstKeysAndMaterializesIndependentBatches()
        {
            var emptyBatches = System.Linq.Enumerable.ToList(new int[0].Batch(x => x));
            Assert.AreEqual(0, emptyBatches.Count);

            // Key = 0 (default(int)) on first element must NOT start a spurious empty batch
            var intItems = new[]
            {
                (Key: 0, Val: "a"),
                (Key: 0, Val: "b"),
                (Key: 1, Val: "c"),
                (Key: 0, Val: "d")
            };
            var intBatches = System.Linq.Enumerable.ToList(intItems.Batch(x => x.Key));
            Assert.AreEqual(3, intBatches.Count);
            Assert.AreEqual(2, System.Linq.Enumerable.Count(intBatches[0]));
            Assert.AreEqual(1, System.Linq.Enumerable.Count(intBatches[1]));
            Assert.AreEqual(1, System.Linq.Enumerable.Count(intBatches[2]));

            // Null string keys and non-null string keys must not throw NRE or alias underlying lists
            var strItems = new[]
            {
                (Key: (string?)null, Val: "n1"),
                (Key: (string?)null, Val: "n2"),
                (Key: (string?)"k1", Val: "v1"),
                (Key: (string?)null, Val: "n3")
            };
            var strBatches = System.Linq.Enumerable.ToList(strItems.Batch(x => x.Key));
            Assert.AreEqual(3, strBatches.Count);
            Assert.AreEqual(2, System.Linq.Enumerable.Count(strBatches[0]));
            Assert.AreEqual("n1", System.Linq.Enumerable.First(strBatches[0]).Val);
            Assert.AreEqual("v1", System.Linq.Enumerable.First(strBatches[1]).Val);
            Assert.AreEqual("n3", System.Linq.Enumerable.First(strBatches[2]).Val);
        }

        [TestMethod]
        public void BundledRequestVariableResolver_LoadProperties_IsolatesUnpinnedJobPropertiesWhilePreservingPinnedBundleProperties()
        {
            var loader = Substitute.For<Dorc.Core.VariableResolution.IBundledRequestVariableLoader>();
            var exprEvaluator = Substitute.For<Dorc.Core.VariableResolution.IPropertyExpressionEvaluator>();
            var propEvaluator = Substitute.For<Dorc.Core.VariableResolution.IPropertyEvaluator>();

            exprEvaluator.Evaluate(Arg.Any<object>())
                .Returns(ci => ci.ArgAt<object>(0));
            propEvaluator.Evaluate(
                Arg.Any<Dorc.Core.VariableResolution.IVariableResolver>(),
                Arg.Any<string>())
                .Returns(ci => new Dorc.ApiModel.MonitorRunnerApi.VariableValue
                {
                    Value = ci.ArgAt<string>(1),
                    Type = typeof(string)
                });

            var currentStep = 1;
            loader.GetVariables().Returns(_ => currentStep == 1
                ? new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "StepOnlyVar", PropertyValue = "Step1Val" },
                    new RequestProperty { PropertyName = "PinnedBundleVar", PropertyValue = "ShouldNotOverridePinned" }
                }
                : new List<RequestProperty>
                {
                    new RequestProperty { PropertyName = "Step2Var", PropertyValue = "Step2Val" }
                });

            var resolver = new Dorc.Core.VariableResolution.BundledRequestVariableResolver(
                loader,
                exprEvaluator,
                propEvaluator);

            resolver.SetPropertyValue("PinnedBundleVar", "PinnedVal");

            // Step 1 load
            var loadedStep1 = resolver.LoadProperties();
            Assert.IsTrue(loadedStep1.ContainsKey("StepOnlyVar"));
            Assert.AreEqual("Step1Val", resolver.GetPropertyValue("StepOnlyVar")?.Value);
            Assert.AreEqual("PinnedVal", resolver.GetPropertyValue("PinnedBundleVar")?.Value);

            // Step 2 load: StepOnlyVar from Step 1 must not leak into Step 2, while PinnedBundleVar remains
            currentStep = 2;
            var loadedStep2 = resolver.LoadProperties();
            Assert.IsFalse(loadedStep2.ContainsKey("StepOnlyVar"));
            Assert.IsNull(resolver.GetPropertyValue("StepOnlyVar"));
            Assert.AreEqual("Step2Val", resolver.GetPropertyValue("Step2Var")?.Value);
            Assert.AreEqual("PinnedVal", resolver.GetPropertyValue("PinnedBundleVar")?.Value);
        }

        [TestMethod]
        public void DaemonStatusProbe_ValidatesEnvironmentAndServerMembershipBeforeImpersonation()
        {
            var configValuesSource = Substitute.For<IConfigValuesPersistentSource>();
            var logger = Substitute.For<ILogger<DaemonStatusProbe>>();
            var envSource = Substitute.For<IEnvironmentsPersistentSource>();
            var serversSource = Substitute.For<IServersPersistentSource>();
            var daemonsSource = Substitute.For<IDaemonsPersistentSource>();
            var obsSource = Substitute.For<IDaemonObservationPersistentSource>();
            var configSettings = Substitute.For<Dorc.Core.Configuration.IConfigurationSettings>();
            var daemonAudit = Substitute.For<IDaemonAuditPersistentSource>();
            var serversAudit = Substitute.For<IServersAuditPersistentSource>();
            var claimsReader = Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>();

            var probe = new DaemonStatusProbe(
                configValuesSource,
                logger,
                envSource,
                serversSource,
                daemonsSource,
                obsSource,
                configSettings,
                daemonAudit,
                serversAudit,
                claimsReader);

            var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("TestAuth"));

            Assert.Throws<ArgumentException>(() => probe.ChangeDaemonState(null!, user));
            Assert.Throws<ArgumentException>(() => probe.ChangeDaemonState(new DaemonStatus { EnvName = " " }, user));

            envSource.GetEnvironment("MissingEnv", user).Returns((EnvironmentApiModel)null!);
            Assert.Throws<InvalidOperationException>(() =>
                probe.ChangeDaemonState(new DaemonStatus { EnvName = "MissingEnv", ServerName = "SRV01", DaemonName = "Svc1", Status = "Starting" }, user));

            envSource.GetEnvironment("ValidEnv", user).Returns(new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "ValidEnv", EnvironmentIsProd = false });
            serversSource.GetServersForEnvId(10).Returns(new List<ServerApiModel>
            {
                new ServerApiModel { ServerId = 1, Name = "ALLOWED-SRV" }
            });

            // Attempting to control a daemon on a server not attached to ValidEnv must throw UnauthorizedAccessException
            Assert.Throws<UnauthorizedAccessException>(() =>
                probe.ChangeDaemonState(new DaemonStatus
                {
                    EnvName = "ValidEnv",
                    ServerName = "UNATTACHED-SRV",
                    DaemonName = "Svc1",
                    Status = "Restarting"
                }, user));

            // Environment with no servers and empty credentials returns empty statuses without calling LogonUser
            serversSource.GetServersForEnvId(10).Returns(new List<ServerApiModel>());
            var emptyStatuses = probe.GetDaemonStatuses("ValidEnv", user);
            Assert.AreEqual(0, emptyStatuses.Count);
        }
    }
}
