using Dorc.Monitor.HighAvailability;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;

namespace Dorc.Monitor.Tests.HighAvailability
{
    [TestClass]
    public class NoOpDistributedLockServiceTests
    {
        [TestMethod]
        public void IsEnabled_ShouldReturnFalse()
        {
            // Arrange
            var service = new NoOpDistributedLockService();

            // Act
            var isEnabled = service.IsEnabled;

            // Assert
            Assert.IsFalse(isEnabled);
        }

        [TestMethod]
        public async Task TryAcquireLockAsync_ShouldReturnNull()
        {
            // Arrange
            var service = new NoOpDistributedLockService();
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await service.TryAcquireLockAsync("test-resource", 5000, cancellationToken);

            // Assert
            Assert.IsNull(result);
        }
    }

    [TestClass]
    public class RabbitMqDistributedLockServiceTests
    {
        private ILogger<RabbitMqDistributedLockService> mockLogger;
        private IMonitorConfiguration mockConfiguration;
        private IHttpClientFactory mockHttpClientFactory;
        private HttpClient? httpClient;

        [TestInitialize]
        public void Setup()
        {
            mockLogger = Substitute.For<ILogger<RabbitMqDistributedLockService>>();
            mockConfiguration = Substitute.For<IMonitorConfiguration>();
            mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
            
            // Setup a default HttpClient that will be tracked for disposal
            httpClient = new HttpClient();
            mockHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // HttpClient created from IHttpClientFactory should NOT be manually disposed
            // The factory manages the lifetime and disposes them internally
        }

        [TestMethod]
        public void IsEnabled_WhenHADisabled_ShouldReturnFalse()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(false);
            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);

            // Act
            var isEnabled = service.IsEnabled;

            // Assert
            Assert.IsFalse(isEnabled);
        }

        [TestMethod]
        public void IsEnabled_WhenHAEnabled_ShouldReturnTrue()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(true);
            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);

            // Act
            var isEnabled = service.IsEnabled;

            // Assert
            Assert.IsTrue(isEnabled);
        }

        [TestMethod]
        public async Task TryAcquireLockAsync_WhenHADisabled_ShouldReturnNull()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(false);
            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await service.TryAcquireLockAsync("test-resource", 5000, cancellationToken);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task TryAcquireLockAsync_WhenRabbitMQNotAvailable_ShouldReturnNull()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(true);
            mockConfiguration.RabbitMqHostName.Returns("invalid-host-that-does-not-exist");
            mockConfiguration.RabbitMqPort.Returns(5672);
            mockConfiguration.RabbitMqOAuthClientId.Returns("test-client");
            mockConfiguration.RabbitMqOAuthClientSecret.Returns("test-secret");
            mockConfiguration.RabbitMqOAuthTokenEndpoint.Returns("http://localhost:9999/oauth/token");
            mockConfiguration.RabbitMqOAuthScope.Returns("");
            mockConfiguration.RabbitMqVirtualHost.Returns("/");

            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await service.TryAcquireLockAsync("test-resource", 5000, cancellationToken);

            // Assert
            // Should return null when RabbitMQ is not available
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Dispose_ShouldNotThrow()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(false);
            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);

            // Act & Assert - should not throw
            service.Dispose();
        }

        [TestMethod]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            mockConfiguration.HighAvailabilityEnabled.Returns(false);
            var service = new RabbitMqDistributedLockService(mockLogger, mockConfiguration, mockHttpClientFactory);

            // Act & Assert - should not throw
            service.Dispose();
            service.Dispose();
            service.Dispose();
        }
    }
}
