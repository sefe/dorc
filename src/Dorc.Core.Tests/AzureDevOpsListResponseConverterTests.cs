using Dorc.AzureDevOps.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Org.OpenAPITools.Model;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class AzureDevOpsListResponseConverterTests
    {
        /// <summary>
        /// Mirrors the serializer the generated ApiClient actually uses, so
        /// these tests exercise the configuration that occurs at runtime
        /// rather than a bare one. Deserializing through plain Newtonsoft
        /// settings hid a defect where date-shaped strings were rewritten.
        /// </summary>
        private static JsonSerializerSettings ProductionSettings(bool withConverter = true)
        {
            var settings = new JsonSerializerSettings
            {
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
                }
            };
            if (withConverter)
            {
                settings.Converters.Add(new AzureDevOpsListResponseConverter());
            }
            return settings;
        }

        [TestMethod]
        public void UnwrapsCountValueEnvelopeIntoList()
        {
            var json = @"{""count"":2,""value"":[{""id"":1,""buildNumber"":""1.0.1""},{""id"":2,""buildNumber"":""1.0.2""}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings());

            Assert.IsNotNull(builds);
            Assert.AreEqual(2, builds.Count);
            Assert.AreEqual("1.0.1", builds[0].BuildNumber);
            Assert.AreEqual("1.0.2", builds[1].BuildNumber);
        }

        /// <summary>
        /// Proves the converter is doing the unwrapping: without it, the same
        /// payload fails. Guards against the converter being silently dropped
        /// from ApiClient.SerializerSettings by a future generator change.
        /// </summary>
        [TestMethod]
        public void EnvelopeFailsToDeserializeWithoutTheConverter()
        {
            var json = @"{""count"":1,""value"":[{""id"":1}]}";

            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings(withConverter: false)));
        }

        [TestMethod]
        public void DeserializesBareArrayUnchanged()
        {
            var json = @"[{""id"":7,""name"":""drop""}]";

            var artifacts = JsonConvert.DeserializeObject<List<BuildArtifact>>(json, ProductionSettings());

            Assert.IsNotNull(artifacts);
            Assert.AreEqual(1, artifacts.Count);
            Assert.AreEqual("drop", artifacts[0].Name);
        }

        [TestMethod]
        public void DeserializesListsNestedInsideModels()
        {
            var json = @"{""count"":1,""value"":[{""id"":3,""tags"":[""nightly"",""release""]}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings());

            Assert.IsNotNull(builds);
            Assert.AreEqual(1, builds.Count);
            Assert.AreEqual(2, builds[0].Tags.Count);
            Assert.AreEqual("nightly", builds[0].Tags[0]);
        }

        /// <summary>
        /// A timestamp-shaped string must survive as the exact text the server
        /// sent. Loading the tree with the reader's default DateParseHandling
        /// turned "2019-08-24T14:15:22Z" into "08/24/2019 14:15:22".
        /// </summary>
        [TestMethod]
        public void PreservesTimestampShapedStringsVerbatim()
        {
            var json = @"{""count"":1,""value"":[{""id"":1,""sourceVersion"":""2019-08-24T14:15:22Z""}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings());

            Assert.IsNotNull(builds);
            Assert.AreEqual("2019-08-24T14:15:22Z", builds[0].SourceVersion);
        }

        [TestMethod]
        public void PreservesTimestampShapedStringsInsideCollections()
        {
            var json = @"{""count"":1,""value"":[{""id"":1,""tags"":[""2019-08-24T14:15:22Z""]}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings());

            Assert.IsNotNull(builds);
            Assert.AreEqual("2019-08-24T14:15:22Z", builds[0].Tags[0]);
        }

        [TestMethod]
        public void ReturnsNullForNullToken()
        {
            var builds = JsonConvert.DeserializeObject<List<Build>>("null", ProductionSettings());

            Assert.IsNull(builds);
        }

        [TestMethod]
        public void ThrowsWhenObjectHasNoValueProperty()
        {
            var json = @"{""message"":""not an envelope""}";

            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings()));
        }

        /// <summary>
        /// An explicit null value must not read as "no results": token["value"]
        /// yields a JValue of type Null rather than a C# null, and enumerating
        /// it silently produced an empty list.
        /// </summary>
        [TestMethod]
        public void ThrowsWhenEnvelopeValueIsNull()
        {
            var json = @"{""count"":1,""value"":null}";

            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings()));
        }

        [TestMethod]
        public void ThrowsWhenPayloadIsAScalar()
        {
            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(@"""hello""", ProductionSettings()));
        }

        [TestMethod]
        public void ThrowsWhenEnvelopeValueIsASingleObject()
        {
            var json = @"{""count"":1,""value"":{""id"":1}}";

            Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings()));
        }
    }
}
