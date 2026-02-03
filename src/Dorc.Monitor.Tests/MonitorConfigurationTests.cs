using Microsoft.Extensions.Configuration;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class MonitorConfigurationTests
    {
        private MonitorConfiguration CreateConfiguration(Dictionary<string, string?> settings)
        {
            var configurationRoot = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
            return new MonitorConfiguration(configurationRoot);
        }

        // --- LockAcquisitionTimeoutSeconds ---

        [TestMethod]
        public void LockAcquisitionTimeoutSeconds_WhenNotConfigured_ReturnsDefault5()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>());

            Assert.AreEqual(5, config.LockAcquisitionTimeoutSeconds);
        }

        [TestMethod]
        public void LockAcquisitionTimeoutSeconds_WhenConfigured_ReturnsConfiguredValue()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:LockAcquisitionTimeoutSeconds", "10" }
            });

            Assert.AreEqual(10, config.LockAcquisitionTimeoutSeconds);
        }

        [TestMethod]
        public void LockAcquisitionTimeoutSeconds_WhenZero_ReturnsDefault5()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:LockAcquisitionTimeoutSeconds", "0" }
            });

            Assert.AreEqual(5, config.LockAcquisitionTimeoutSeconds);
        }

        [TestMethod]
        public void LockAcquisitionTimeoutSeconds_WhenNegative_ReturnsDefault5()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:LockAcquisitionTimeoutSeconds", "-3" }
            });

            Assert.AreEqual(5, config.LockAcquisitionTimeoutSeconds);
        }

        [TestMethod]
        public void LockAcquisitionTimeoutSeconds_WhenNonNumeric_ReturnsDefault5()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:LockAcquisitionTimeoutSeconds", "abc" }
            });

            Assert.AreEqual(5, config.LockAcquisitionTimeoutSeconds);
        }

        // --- OAuthTokenRefreshCheckIntervalMinutes ---

        [TestMethod]
        public void OAuthTokenRefreshCheckIntervalMinutes_WhenNotConfigured_ReturnsDefault15()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>());

            Assert.AreEqual(15, config.OAuthTokenRefreshCheckIntervalMinutes);
        }

        [TestMethod]
        public void OAuthTokenRefreshCheckIntervalMinutes_WhenConfigured_ReturnsConfiguredValue()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:OAuthTokenRefreshCheckIntervalMinutes", "30" }
            });

            Assert.AreEqual(30, config.OAuthTokenRefreshCheckIntervalMinutes);
        }

        [TestMethod]
        public void OAuthTokenRefreshCheckIntervalMinutes_WhenZero_ReturnsDefault15()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:OAuthTokenRefreshCheckIntervalMinutes", "0" }
            });

            Assert.AreEqual(15, config.OAuthTokenRefreshCheckIntervalMinutes);
        }

        [TestMethod]
        public void OAuthTokenRefreshCheckIntervalMinutes_WhenNegative_ReturnsDefault15()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:OAuthTokenRefreshCheckIntervalMinutes", "-5" }
            });

            Assert.AreEqual(15, config.OAuthTokenRefreshCheckIntervalMinutes);
        }

        [TestMethod]
        public void OAuthTokenRefreshCheckIntervalMinutes_WhenNonNumeric_ReturnsDefault15()
        {
            var config = CreateConfiguration(new Dictionary<string, string?>
            {
                { "AppSettings:HighAvailability:OAuthTokenRefreshCheckIntervalMinutes", "xyz" }
            });

            Assert.AreEqual(15, config.OAuthTokenRefreshCheckIntervalMinutes);
        }
    }
}
