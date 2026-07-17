using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// HLPS env-details-component-tabs §5.7 / SC-6 (first half): attached environment
    /// components are exposed as deployment variables — conditionally, with the shared
    /// per-tag semantics (space→underscore; scalar-when-one / array-when-many).
    /// </summary>
    [TestClass]
    public class VariableScopeOptionsResolverComponentVariablesTests
    {
        private IContainersPersistentSource _containers = null!;
        private ICloudResourcesPersistentSource _cloudResources = null!;
        private IApiRegistrationsPersistentSource _apiRegistrations = null!;
        private IDatabasesPersistentSource _databases = null!;
        private IServersPersistentSource _servers = null!;
        private List<(string Name, object? Value)> _calls = null!;
        private VariableScopeOptionsResolver _resolver = null!;
        private EnvironmentApiModel _environment = null!;

        [TestInitialize]
        public void Init()
        {
            var properties = Substitute.For<IPropertiesPersistentSource>();
            _servers = Substitute.For<IServersPersistentSource>();
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            _databases = Substitute.For<IDatabasesPersistentSource>();
            var userPerms = Substitute.For<IUserPermsPersistentSource>();
            _containers = Substitute.For<IContainersPersistentSource>();
            _cloudResources = Substitute.For<ICloudResourcesPersistentSource>();
            _apiRegistrations = Substitute.For<IApiRegistrationsPersistentSource>();

            _servers.GetServersForEnvId(Arg.Any<int>()).Returns(Array.Empty<ServerApiModel>());
            _databases.GetDatabasesForEnvironmentName(Arg.Any<string>())
                .Returns(Array.Empty<DatabaseApiModel>());
            _containers.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<ContainerApiModel>());
            _cloudResources.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<CloudResourceApiModel>());
            _apiRegistrations.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<ApiRegistrationApiModel>());

            _resolver = new VariableScopeOptionsResolver(properties, _servers, daemons,
                _databases, userPerms, _containers, _cloudResources, _apiRegistrations);

            _environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = "IAR DV 07",
                Details = new EnvironmentDetailsApiModel { FileShare = @"\\share" }
            };
            _calls = new List<(string, object?)>();
        }

        private IVariableResolver CreateRecordingVariableResolver()
        {
            var variableResolver = Substitute.For<IVariableResolver>();
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<VariableValue?>()))
                .Do(ci => _calls.Add((ci.ArgAt<string>(0), ci.ArgAt<VariableValue?>(1))));
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<string>()))
                .Do(ci => _calls.Add((ci.ArgAt<string>(0), ci.ArgAt<string>(1))));
            return variableResolver;
        }

        private object? ValueOf(string name)
        {
            var matches = _calls.Where(c => c.Name == name).ToArray();
            Assert.AreEqual(1, matches.Length, $"Expected exactly one emission of '{name}'");
            return matches[0].Value;
        }

        private void AssertNotEmitted(string name) =>
            Assert.IsFalse(_calls.Any(c => c.Name == name), $"'{name}' should not be emitted");

        [TestMethod]
        public void NoComponentsAttached_EmitsNoComponentVariables()
        {
            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            AssertNotEmitted("EnvironmentContainers");
            AssertNotEmitted("EnvironmentCloudResources");
            AssertNotEmitted("EnvironmentApiRegistrations");
            Assert.IsFalse(_calls.Any(c => c.Name.StartsWith("ContainerNames_")));
            Assert.IsFalse(_calls.Any(c => c.Name.StartsWith("CloudResourceNames_")));
            Assert.IsFalse(_calls.Any(c => c.Name.StartsWith("ApiRegistrationNames_")));
        }

        [TestMethod]
        public void Containers_EmitCollectionAndPerTagVariables_WithBothQuirks()
        {
            _containers.GetForEnvironmentId(42).Returns(new[]
            {
                new ContainerApiModel
                { Name = "web01", Image = "nginx:1", Registry = "reg", HostServerName = "h1", Tags = "edge;web tier" },
                new ContainerApiModel
                { Name = "web02", Image = "nginx:1", Tags = "web tier" }
            });

            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            var collection = (VariableValueContainers[])((VariableValue)ValueOf("EnvironmentContainers")!).Value;
            Assert.AreEqual(2, collection.Length);
            Assert.AreEqual("web01", collection[0].Name);
            Assert.AreEqual("nginx:1", collection[0].Image);
            Assert.AreEqual("reg", collection[0].Registry);
            Assert.AreEqual("h1", collection[0].HostServerName);
            Assert.AreEqual("edge;web tier", collection[0].Tags);

            // scalar-when-one via the string overload
            Assert.AreEqual("web01", ValueOf("ContainerNames_edge"));
            // array-when-many, space → underscore
            CollectionAssert.AreEqual(new[] { "web01", "web02" },
                (string[])((VariableValue)ValueOf("ContainerNames_web_tier")!).Value);
        }

        [TestMethod]
        public void CloudResources_EmitCollectionAndPerTagVariables()
        {
            _cloudResources.GetForEnvironmentId(42).Returns(new[]
            {
                new CloudResourceApiModel
                {
                    Name = "kv", Provider = "Azure", ResourceType = "KeyVault",
                    ResourceIdentifier = "/subs/1/kv", Subscription = "sub-1", Tags = "secure"
                }
            });

            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            var collection = (VariableValueCloudResources[])((VariableValue)ValueOf("EnvironmentCloudResources")!).Value;
            Assert.AreEqual(1, collection.Length);
            Assert.AreEqual("Azure", collection[0].Provider);
            Assert.AreEqual("KeyVault", collection[0].ResourceType);
            Assert.AreEqual("/subs/1/kv", collection[0].ResourceIdentifier);
            Assert.AreEqual("sub-1", collection[0].Subscription);
            Assert.AreEqual("kv", ValueOf("CloudResourceNames_secure"));
        }

        [TestMethod]
        public void ApiRegistrations_EmitCollectionAndPerTagVariables()
        {
            _apiRegistrations.GetForEnvironmentId(42).Returns(new[]
            {
                new ApiRegistrationApiModel
                {
                    Name = "orders", BaseUrl = "https://o.local", Version = "v2",
                    HealthCheckUrl = "https://o.local/hc", Tags = "core"
                }
            });

            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            var collection = (VariableValueApiRegistrations[])((VariableValue)ValueOf("EnvironmentApiRegistrations")!).Value;
            Assert.AreEqual(1, collection.Length);
            Assert.AreEqual("https://o.local", collection[0].BaseUrl);
            Assert.AreEqual("v2", collection[0].Version);
            Assert.AreEqual("https://o.local/hc", collection[0].HealthCheckUrl);
            Assert.AreEqual("orders", ValueOf("ApiRegistrationNames_core"));
        }

        [TestMethod]
        public void UntaggedItems_AppearInCollectionButYieldNoPerTagVariables()
        {
            _containers.GetForEnvironmentId(42).Returns(new[]
            {
                new ContainerApiModel { Name = "tagged", Image = "i", Tags = "edge" },
                new ContainerApiModel { Name = "untagged-null", Image = "i", Tags = null },
                new ContainerApiModel { Name = "untagged-empty", Image = "i", Tags = "" }
            });

            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            var collection = (VariableValueContainers[])((VariableValue)ValueOf("EnvironmentContainers")!).Value;
            Assert.AreEqual(3, collection.Length);
            Assert.AreEqual("tagged", ValueOf("ContainerNames_edge"));
            Assert.AreEqual(1, _calls.Count(c => c.Name.StartsWith("ContainerNames_")));
        }

        [TestMethod]
        public void AllThreeTypesAttached_EmitAllCollections()
        {
            _containers.GetForEnvironmentId(42).Returns(new[]
                { new ContainerApiModel { Name = "c", Image = "i" } });
            _cloudResources.GetForEnvironmentId(42).Returns(new[]
                { new CloudResourceApiModel { Name = "r", Provider = "p", ResourceType = "t", ResourceIdentifier = "id" } });
            _apiRegistrations.GetForEnvironmentId(42).Returns(new[]
                { new ApiRegistrationApiModel { Name = "a", BaseUrl = "u" } });

            _resolver.SetPropertyValues(CreateRecordingVariableResolver(), _environment);

            Assert.IsNotNull(ValueOf("EnvironmentContainers"));
            Assert.IsNotNull(ValueOf("EnvironmentCloudResources"));
            Assert.IsNotNull(ValueOf("EnvironmentApiRegistrations"));
        }
    }
}
