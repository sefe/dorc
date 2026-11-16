using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// S-008 sweep item (deferred from the S-005 gate): the runner deserializes
    /// VariableValue payloads via VariableValueJsonConverter's Type.FullName
    /// resolution — prove the new component DTO arrays round-trip exactly like
    /// VariableValueServers[] does.
    /// </summary>
    [TestClass]
    public class VariableValueComponentSerializationTests
    {
        private static VariableValue RoundTrip(VariableValue value)
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<VariableValue>(json)!;
        }

        [TestMethod]
        public void ContainersArray_RoundTripsThroughVariableValueJson()
        {
            var payload = new[]
            {
                new VariableValueContainers
                { Name = "web01", Image = "nginx:1", Registry = "reg", HostServerName = "h1", Tags = "edge" }
            };

            var result = RoundTrip(new VariableValue { Value = payload, Type = payload.GetType() });

            var typed = (VariableValueContainers[])result.Value;
            Assert.AreEqual("web01", typed[0].Name);
            Assert.AreEqual("nginx:1", typed[0].Image);
            Assert.AreEqual("reg", typed[0].Registry);
            Assert.AreEqual("h1", typed[0].HostServerName);
            Assert.AreEqual("edge", typed[0].Tags);
        }

        [TestMethod]
        public void CloudResourcesArray_RoundTripsThroughVariableValueJson()
        {
            var payload = new[]
            {
                new VariableValueCloudResources
                {
                    Name = "kv", Provider = "Azure", ResourceType = "KeyVault",
                    ResourceIdentifier = "/subs/1/kv", Subscription = "s", Tags = "secure"
                }
            };

            var result = RoundTrip(new VariableValue { Value = payload, Type = payload.GetType() });

            var typed = (VariableValueCloudResources[])result.Value;
            Assert.AreEqual("Azure", typed[0].Provider);
            Assert.AreEqual("/subs/1/kv", typed[0].ResourceIdentifier);
        }

        [TestMethod]
        public void ApiRegistrationsArray_RoundTripsThroughVariableValueJson()
        {
            var payload = new[]
            {
                new VariableValueApiRegistrations
                { Name = "orders", BaseUrl = "https://o", Version = "v2", HealthCheckUrl = "https://o/hc", Tags = "core" }
            };

            var result = RoundTrip(new VariableValue { Value = payload, Type = payload.GetType() });

            var typed = (VariableValueApiRegistrations[])result.Value;
            Assert.AreEqual("https://o", typed[0].BaseUrl);
            Assert.AreEqual("v2", typed[0].Version);
        }
    }
}
