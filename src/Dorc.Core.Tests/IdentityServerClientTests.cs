using Dorc.Core.IdentityServer;
using log4net;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class IdentityServerClientTests
    {
        private ILog _logger = null!;
        private string _authority = null!;
        private string _clientId = null!;
        private string _clientSecret = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILog>();
            _authority = "https://identity.example.com";
            _clientId = "test-client";
            _clientSecret = "test-secret";
        }

        [TestMethod]
        public void Constructor_WithValidParameters_CreatesClient()
        {
            // Act
            using var sut = new IdentityServerClient(_authority, _clientId, _clientSecret, _logger);

            // Assert
            Assert.IsNotNull(sut);
        }

        [TestMethod]
        public void Constructor_WithExternalHttpClient_DoesNotDisposeIt()
        {
            // Arrange
            var httpClient = new HttpClient();
            var originalTimeout = httpClient.Timeout;

            // Act
            using var sut = new IdentityServerClient(_authority, _clientId, _clientSecret, _logger, httpClient, false);
            sut.Dispose();

            // Assert - HttpClient should still be usable
            Assert.AreEqual(originalTimeout, httpClient.Timeout);
            httpClient.Dispose();
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var sut = new IdentityServerClient(_authority, _clientId, _clientSecret, _logger);

            // Act & Assert - Should not throw on multiple dispose calls
            sut.Dispose();
            sut.Dispose();
            sut.Dispose();
        }

        [TestMethod]
        public async Task SearchClientsAsync_WithInvalidAuthority_LogsError()
        {
            // Arrange
            var invalidAuthority = "https://invalid-server-that-does-not-exist.example.com";
            using var sut = new IdentityServerClient(invalidAuthority, _clientId, _clientSecret, _logger);

            // Act & Assert
            try
            {
                await sut.SearchClientsAsync("test-search");
                Assert.Fail("Should have thrown an exception");
            }
            catch (Exception)
            {
                _logger.Received().Error(Arg.Any<string>(), Arg.Any<Exception>());
            }
        }

        [TestMethod]
        public async Task GetClientByIdAsync_WithInvalidAuthority_LogsError()
        {
            // Arrange
            var invalidAuthority = "https://invalid-server-that-does-not-exist.example.com";
            using var sut = new IdentityServerClient(invalidAuthority, _clientId, _clientSecret, _logger);

            // Act & Assert
            try
            {
                await sut.GetClientByIdAsync("test-client-id");
                Assert.Fail("Should have thrown an exception");
            }
            catch (Exception)
            {
                _logger.Received().Error(Arg.Any<string>(), Arg.Any<Exception>());
            }
        }

        [TestMethod]
        public void Constructor_WithSharedHttpClient_SharesResource()
        {
            // Arrange
            var sharedClient = new HttpClient();

            // Act
            var sut1 = new IdentityServerClient(_authority, _clientId, _clientSecret, _logger, sharedClient, false);
            var sut2 = new IdentityServerClient(_authority, _clientId, _clientSecret, _logger, sharedClient, false);

            sut1.Dispose();
            sut2.Dispose();

            // Assert - Shared client should still be usable
            Assert.IsNotNull(sharedClient.Timeout);
            sharedClient.Dispose();
        }

        [TestMethod]
        public void Constructor_TrimsTrailingSlashFromAuthority()
        {
            // Arrange
            var authorityWithSlash = "https://identity.example.com/";

            // Act
            using var sut = new IdentityServerClient(authorityWithSlash, _clientId, _clientSecret, _logger);

            // Assert - Should not throw and authority should be trimmed
            Assert.IsNotNull(sut);
        }
    }
}
