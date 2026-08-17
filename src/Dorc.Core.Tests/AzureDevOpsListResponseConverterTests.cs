using Dorc.AzureDevOps.Client;
using Newtonsoft.Json;
using Org.OpenAPITools.Model;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class AzureDevOpsListResponseConverterTests
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new AzureDevOpsListResponseConverter() }
        };

        [TestMethod]
        public void UnwrapsCountValueEnvelopeIntoList()
        {
            var json = @"{""count"":2,""value"":[{""id"":1,""buildNumber"":""1.0.1""},{""id"":2,""buildNumber"":""1.0.2""}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, Settings);

            Assert.IsNotNull(builds);
            Assert.AreEqual(2, builds.Count);
            Assert.AreEqual("1.0.1", builds[0].BuildNumber);
            Assert.AreEqual("1.0.2", builds[1].BuildNumber);
        }

        [TestMethod]
        public void DeserializesBareArrayUnchanged()
        {
            var json = @"[{""id"":7,""name"":""drop""}]";

            var artifacts = JsonConvert.DeserializeObject<List<BuildArtifact>>(json, Settings);

            Assert.IsNotNull(artifacts);
            Assert.AreEqual(1, artifacts.Count);
            Assert.AreEqual("drop", artifacts[0].Name);
        }

        [TestMethod]
        public void DeserializesListsNestedInsideModels()
        {
            var json = @"{""count"":1,""value"":[{""id"":3,""tags"":[""nightly"",""release""]}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, Settings);

            Assert.IsNotNull(builds);
            Assert.AreEqual(1, builds.Count);
            Assert.AreEqual(2, builds[0].Tags.Count);
            Assert.AreEqual("nightly", builds[0].Tags[0]);
        }

        [TestMethod]
        public void ReturnsNullForNullToken()
        {
            var builds = JsonConvert.DeserializeObject<List<Build>>("null", Settings);

            Assert.IsNull(builds);
        }

        [TestMethod]
        public void ThrowsWhenObjectHasNoValueProperty()
        {
            var json = @"{""message"":""not an envelope""}";

            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, Settings));
        }
    }
}
