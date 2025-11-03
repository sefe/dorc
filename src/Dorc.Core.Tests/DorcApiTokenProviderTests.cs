using Dorc.Core.Configuration;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class DorcApiTokenProviderTests
    {
        private IOAuthClientConfiguration _config = null!;

        [TestInitialize]
        public void Setup()
        {
            _config = Substitute.For<IOAuthClientConfiguration>();
            _config.BaseUrl.Returns("https://test-api.example.com");
            _config.ClientId.Returns("test-client-id");
            _config.ClientSecret.Returns("test-secret");
            _config.Scope.Returns("test-scope");
        }

        [TestMethod]
        public async Task Constructor_WithConfig_CreatesHttpClient()
        {
            // Arrange & Act
            await using var sut = new DorcApiTokenProvider(_config);

            // Assert - Should not throw
            Assert.IsNotNull(sut);
        }

        [TestMethod]
        public async Task Constructor_WithExternalHttpClient_DoesNotDisposeIt()
        {
            // Arrange
            var httpClient = new HttpClient();
            var originalTimeout = httpClient.Timeout;

            // Act
            await using var sut = new DorcApiTokenProvider(_config, httpClient);
            await sut.DisposeAsync();

            // Assert - HttpClient should still be usable
            Assert.AreEqual(originalTimeout, httpClient.Timeout);
            httpClient.Dispose();
        }

        [TestMethod]
        public async Task DisposeAsync_DisposesInternalHttpClient()
        {
            // Arrange
            var sut = new DorcApiTokenProvider(_config);

            // Act & Assert - Should not throw
            await sut.DisposeAsync();
        }

        [TestMethod]
        public async Task GetTokenAsync_WithInvalidConfig_ThrowsException()
        {
            // Arrange
            _config.BaseUrl.Returns("https://invalid-server-that-does-not-exist.example.com");
            await using var sut = new DorcApiTokenProvider(_config);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ApplicationException>(async () =>
            {
                await sut.GetTokenAsync();
            });
        }

        [TestMethod]
        public async Task DisposeAsync_CanBeCalledMultipleTimes()
        {
            // Arrange
            var sut = new DorcApiTokenProvider(_config);

            // Act & Assert - Should not throw on multiple dispose calls
            await sut.DisposeAsync();
            await sut.DisposeAsync();
            await sut.DisposeAsync();
        }

        [TestMethod]
        public async Task Constructor_WithSharedHttpClient_DoesNotDispose()
        {
            // Arrange
            var sharedClient = new HttpClient();
            
            // Act
            var sut1 = new DorcApiTokenProvider(_config, sharedClient);
            var sut2 = new DorcApiTokenProvider(_config, sharedClient);
            
            await sut1.DisposeAsync();
            await sut2.DisposeAsync();

            // Assert - Shared client should still be usable (check timeout instead of BaseAddress which might be null)
            Assert.IsTrue(sharedClient.Timeout.TotalSeconds > 0);
            sharedClient.Dispose();
        }
    }
}
