using Dorc.Monitor;
using Microsoft.Extensions.Configuration;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Binding/parsing behaviour of the Monitor's remaining configuration
    /// surface. (The previous MonitorConfigurationTests file was deleted with
    /// the RabbitMQ/HA config keys it covered.)
    /// </summary>
    [TestClass]
    public class MonitorConfigurationTests
    {
        private static IConfigurationRoot Config(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static MonitorConfiguration Sut(Dictionary<string, string?>? values = null)
            => new(Config(values ?? new Dictionary<string, string?>()));

        [TestMethod]
        public void IsProduction_DefaultsFalse_AndParsesTrue()
        {
            Assert.IsFalse(Sut().IsProduction);
            Assert.IsTrue(Sut(new() { ["AppSettings:IsProduction"] = "true" }).IsProduction);
        }

        [TestMethod]
        public void RequestProcessingIterationDelay_ParsesValue_AndDefaultsTo1000OnGarbageOrAbsent()
        {
            Assert.AreEqual(2500, Sut(new() { ["AppSettings:RequestProcessingIterationDelayMs"] = "2500" })
                .RequestProcessingIterationDelayMs);
            // Absent or unparseable must fall back to 1000ms — the previous
            // int.TryParse zeroed the out param on failure, yielding a 0ms
            // busy-spin (with a forced full GC per iteration) whenever the key
            // was missing or garbage.
            Assert.AreEqual(1000, Sut().RequestProcessingIterationDelayMs);
            Assert.AreEqual(1000, Sut(new() { ["AppSettings:RequestProcessingIterationDelayMs"] = "not-a-number" })
                .RequestProcessingIterationDelayMs);
            // An explicit 0 is a deliberate operator opt-in and must be honoured.
            Assert.AreEqual(0, Sut(new() { ["AppSettings:RequestProcessingIterationDelayMs"] = "0" })
                .RequestProcessingIterationDelayMs);
        }

        [TestMethod]
        public void ServiceName_Missing_ThrowsWithActionableMessage()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _ = Sut().ServiceName);
            StringAssert.Contains(ex.Message, "Service name");
        }

        [TestMethod]
        public void DOrcConnectionString_MissingThrows_PresentReturns()
        {
            Assert.Throws<InvalidOperationException>(() => _ = Sut().DOrcConnectionString);
            Assert.AreEqual("Server=.;Database=dorc",
                Sut(new() { ["ConnectionStrings:DOrcConnectionString"] = "Server=.;Database=dorc" })
                    .DOrcConnectionString);
        }

        [TestMethod]
        public void RefDataApiUrl_MissingThrows_PresentReturns()
        {
            Assert.Throws<InvalidOperationException>(() => _ = Sut().RefDataApiUrl);
            Assert.AreEqual("https://dorc.local/api",
                Sut(new() { ["AppSettings:RefDataApiUrl"] = "https://dorc.local/api" }).RefDataApiUrl);
        }

        [DataTestMethod]
        [DataRow("ClientId")]
        [DataRow("ClientSecret")]
        [DataRow("Scope")]
        public void OAuthValues_MissingThrowNamingTheKey(string key)
        {
            var sut = Sut();
            var ex = Assert.Throws<InvalidOperationException>(() => _ = key switch
            {
                "ClientId" => sut.DorcApiClientId,
                "ClientSecret" => sut.DorcApiClientSecret,
                _ => sut.DorcApiScope
            });
            StringAssert.Contains(ex.Message, key);
        }

        [TestMethod]
        public void DisableSignalR_DefaultsFalse()
        {
            Assert.IsFalse(Sut().DisableSignalR);
            Assert.IsTrue(Sut(new() { ["AppSettings:DisableSignalR"] = "true" }).DisableSignalR);
        }

        [TestMethod]
        public void Environment_DefaultsToUnknown()
        {
            Assert.AreEqual("unknown", Sut().Environment);
            Assert.AreEqual("uat", Sut(new() { ["AppSettings:Environment"] = "uat" }).Environment);
        }

        [TestMethod]
        public void MaxConcurrentDeployments_ZeroMeansUnlimited_NegativeAndGarbageFallBackToZero()
        {
            Assert.AreEqual(0, Sut().MaxConcurrentDeployments);
            Assert.AreEqual(4, Sut(new() { ["AppSettings:MaxConcurrentDeployments"] = "4" }).MaxConcurrentDeployments);
            Assert.AreEqual(0, Sut(new() { ["AppSettings:MaxConcurrentDeployments"] = "-2" }).MaxConcurrentDeployments);
            Assert.AreEqual(0, Sut(new() { ["AppSettings:MaxConcurrentDeployments"] = "lots" }).MaxConcurrentDeployments);
        }

        [TestMethod]
        public void OAuthClientConfiguration_FromMonitorConfiguration_MapsAllFields()
        {
            var sut = Sut(new()
            {
                ["AppSettings:RefDataApiUrl"] = "https://dorc.local/api",
                ["AppSettings:DorcApi:ClientId"] = "client",
                ["AppSettings:DorcApi:ClientSecret"] = "secret",
                ["AppSettings:DorcApi:Scope"] = "scope"
            });

            var oauth = OAuthClientConfiguration.FromMonitorConfiguration(sut);

            Assert.AreEqual("https://dorc.local/api", oauth.BaseUrl);
            Assert.AreEqual("client", oauth.ClientId);
            Assert.AreEqual("secret", oauth.ClientSecret);
            Assert.AreEqual("scope", oauth.Scope);
        }
    }
}
