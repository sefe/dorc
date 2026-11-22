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
            if (withConverter)
            {
                return AzureDevOpsApiClientFactory.Create("https://dev.azure.com/").SerializerSettings;
            }

            return new JsonSerializerSettings
            {
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
                }
            };
        }

        /// <summary>
        /// The converter is only reached at runtime because the factory puts it
        /// on ApiClient.SerializerSettings. Nothing else asserts that wiring, so
        /// a generator change that stopped routing SerializerSettings into the
        /// codec would leave every converter test above green while every real
        /// Azure DevOps list call broke.
        /// </summary>
        [TestMethod]
        public void FactoryWiresTheConverterIntoTheApiClient()
        {
            var client = AzureDevOpsApiClientFactory.Create("https://dev.azure.com/");

            Assert.IsTrue(
                client.SerializerSettings.Converters.OfType<AzureDevOpsListResponseConverter>().Any(),
                "AzureDevOpsApiClientFactory must register the list-envelope converter.");
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

        /// <summary>
        /// Asserts on the message, not merely the exception type: deserializing
        /// this payload throws either way, because enumerating a JObject yields
        /// JProperty tokens that ToObject rejects on its own. Only the message
        /// distinguishes the guard below from that incidental failure, so
        /// without this the test would pass with the guard removed.
        /// </summary>
        [TestMethod]
        public void ThrowsWhenEnvelopeValueIsASingleObject()
        {
            var json = @"{""count"":1,""value"":{""id"":1}}";

            var exception = Assert.ThrowsExactly<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<Build>>(json, ProductionSettings()));

            StringAssert.Contains(exception.Message, "but the payload was");
        }

        [TestMethod]
        public void ToleratesUnknownAzureDevOpsBuildStatusEnumValuesSuchAsAbandoned()
        {
            var definitionsJson = @"{""count"":2,""value"":[{""id"":8730,""name"":""trading-analytics-lng-global-balance"",""latestBuild"":{""id"":1031047,""status"":""abandoned""}},{""id"":8731,""name"":""amp-basebuild"",""latestBuild"":{""id"":1031048,""status"":""completed"",""result"":""succeeded""}}]}";

            var definitions = JsonConvert.DeserializeObject<List<BuildDefinitionReference>>(definitionsJson, ProductionSettings());

            Assert.IsNotNull(definitions);
            Assert.AreEqual(2, definitions.Count);
            Assert.IsNull(definitions[0].LatestBuild.Status);
            Assert.AreEqual(Build.StatusEnum.Completed, definitions[1].LatestBuild.Status);
            Assert.AreEqual(Build.ResultEnum.Succeeded, definitions[1].LatestBuild.Result);

            var buildsJson = @"{""count"":2,""value"":[{""id"":1031047,""buildNumber"":""20261119.1"",""status"":""abandoned""},{""id"":1031048,""buildNumber"":""20261119.2"",""status"":""completed""}]}";

            var builds = JsonConvert.DeserializeObject<List<Build>>(buildsJson, ProductionSettings());

            Assert.IsNotNull(builds);
            Assert.AreEqual(2, builds.Count);
            Assert.IsNull(builds[0].Status);
            Assert.AreEqual(Build.StatusEnum.Completed, builds[1].Status);
        }

        [TestMethod]
        public void ExtractContinuationToken_ReturnsTokenOnlyWhenHeaderPresent()
        {
            Assert.IsNull(Dorc.Core.AzureDevOpsServer.AzureDevOpsServerWebClient.ExtractContinuationToken(null));

            var page1Headers = new Org.OpenAPITools.Client.Multimap<string, string>
            {
                { "Content-Type", "application/json" },
                { "x-ms-continuationtoken", "token-page-2" }
            };
            Assert.AreEqual(
                "token-page-2",
                Dorc.Core.AzureDevOpsServer.AzureDevOpsServerWebClient.ExtractContinuationToken(page1Headers));

            var page2Headers = new Org.OpenAPITools.Client.Multimap<string, string>
            {
                { "Content-Type", "application/json" }
            };
            Assert.IsNull(
                Dorc.Core.AzureDevOpsServer.AzureDevOpsServerWebClient.ExtractContinuationToken(page2Headers));
        }
    }
}
