using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.TerraformRunner.State;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformCloudTargetingTests
    {
        private const string SubNp = "42ffa1fc-0000-4000-8000-00000000aaaa";
        private const string SubPr = "d9c9e54f-0000-4000-8000-00000000bbbb";

        private static VariableValueCloudResources Resource(
            string name = "SMT-NP",
            string provider = "Azure",
            string resourceType = "Subscription",
            string? resourceIdentifier = null,
            string? subscription = null)
            => new()
            {
                Name = name,
                Provider = provider,
                ResourceType = resourceType,
                ResourceIdentifier = resourceIdentifier ?? string.Empty,
                Subscription = subscription ?? string.Empty,
            };

        private static IDictionary<string, VariableValue> Properties(params VariableValueCloudResources[] resources)
            => new Dictionary<string, VariableValue>
            {
                [PropertyValueScopeOptionsFixed.EnvironmentCloudResources] = new VariableValue
                {
                    Value = resources,
                    Type = resources.GetType()
                }
            };

        [TestMethod]
        public void ResolveSubscription_ReturnsNull_WhenNoCloudResources()
        {
            Assert.IsNull(TerraformCloudTargeting.ResolveSubscription(null, out var warning));
            Assert.IsNull(warning);

            Assert.IsNull(TerraformCloudTargeting.ResolveSubscription(
                new Dictionary<string, VariableValue>(), out warning));
            Assert.IsNull(warning);

            Assert.IsNull(TerraformCloudTargeting.ResolveSubscription(Properties(), out warning));
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_ReadsGuidFromResourceIdentifier()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(Resource(resourceIdentifier: SubNp)), out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.AreEqual("SMT-NP", target.ResourceName);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_FallsBackToSubscriptionField()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(Resource(resourceIdentifier: "SMT-NP display name", subscription: $" {SubNp} ")),
                out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_MatchesResourceTypeCaseInsensitively_AndIgnoresOtherTypes()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(
                    Resource(name: "traveler-kafka", resourceType: "Kafka Cluster", resourceIdentifier: SubPr),
                    Resource(name: "sub", resourceType: " subscription ", resourceIdentifier: SubNp)),
                out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.AreEqual("sub", target.ResourceName);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_DuplicateSubscriptionResourcesAgreeingOnGuid_Resolve()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(
                    Resource(name: "a", resourceIdentifier: SubNp),
                    Resource(name: "b", resourceIdentifier: SubNp.ToUpperInvariant())),
                out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_ReturnsNullWithWarning_OnConflictingSubscriptions()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(
                    Resource(name: "np", resourceIdentifier: SubNp),
                    Resource(name: "pr", resourceIdentifier: SubPr)),
                out var warning);

            Assert.IsNull(target);
            Assert.IsNotNull(warning);
            StringAssert.Contains(warning, "Ambiguous");
            StringAssert.Contains(warning, "np");
            StringAssert.Contains(warning, "pr");
        }

        [TestMethod]
        public void ResolveSubscription_ReturnsNullWithWarning_WhenSubscriptionResourceCarriesNoGuid()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(Resource(name: "named-only", resourceIdentifier: "SMT-NP", subscription: "SMT-NP")),
                out var warning);

            Assert.IsNull(target);
            Assert.IsNotNull(warning);
            StringAssert.Contains(warning, "named-only");
            StringAssert.Contains(warning, "no subscription GUID");
        }

        [TestMethod]
        public void ResolveSubscription_InvalidResourceDoesNotBlockAValidOne()
        {
            var target = TerraformCloudTargeting.ResolveSubscription(
                Properties(
                    Resource(name: "named-only", resourceIdentifier: "SMT-NP"),
                    Resource(name: "good", resourceIdentifier: SubNp)),
                out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            // The misconfigured sibling is still surfaced.
            Assert.IsNotNull(warning);
            StringAssert.Contains(warning, "named-only");
        }

        [TestMethod]
        public void ResolveSubscription_ReadsJsonElementValue()
        {
            // Defensive path: the value arrives as a raw JsonElement instead
            // of the typed array the pipe converter normally produces.
            var json = JsonSerializer.SerializeToElement(new[] { Resource(resourceIdentifier: SubNp) });
            var properties = new Dictionary<string, VariableValue>
            {
                [PropertyValueScopeOptionsFixed.EnvironmentCloudResources] = new VariableValue
                {
                    Value = json,
                    Type = typeof(JsonElement)
                }
            };

            var target = TerraformCloudTargeting.ResolveSubscription(properties, out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_SurvivesPipeSerializationRoundTrip()
        {
            // End-to-end shape check: VariableValueJsonConverter is what the
            // monitor/runner pipe uses, so a value that round-trips through it
            // must still resolve.
            var options = new JsonSerializerOptions { Converters = { new VariableValueJsonConverter() } };
            var wire = JsonSerializer.Serialize(
                Properties(Resource(resourceIdentifier: SubNp)), options);
            var received = JsonSerializer.Deserialize<Dictionary<string, VariableValue>>(wire, options);

            var target = TerraformCloudTargeting.ResolveSubscription(received, out var warning);

            Assert.IsNotNull(target);
            Assert.AreEqual(SubNp, target.SubscriptionId);
            Assert.IsNull(warning);
        }

        [TestMethod]
        public void ResolveSubscription_IgnoresMalformedValues()
        {
            var properties = new Dictionary<string, VariableValue>
            {
                [PropertyValueScopeOptionsFixed.EnvironmentCloudResources] = new VariableValue
                {
                    Value = "not json at all",
                    Type = typeof(string)
                }
            };

            Assert.IsNull(TerraformCloudTargeting.ResolveSubscription(properties, out var warning));
            Assert.IsNull(warning);
        }
    }
}
