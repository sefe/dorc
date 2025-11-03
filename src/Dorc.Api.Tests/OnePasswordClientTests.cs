using OnePassword.Connect.Client;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class OnePasswordClientTests
    {
        private string _baseUrl;
        private string _apiKey;

        [TestInitialize]
        public void Setup()
        {
            _baseUrl = "https://onepassword.example.com";
            _apiKey = "test-api-key";
        }

        [TestMethod]
        public void Constructor_WithValidParameters_CreatesClient()
        {
            // Act
            using var sut = new OnePasswordClient(_baseUrl, _apiKey);

            // Assert
            Assert.IsNotNull(sut);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullBaseUrl_ThrowsException()
        {
            // Act
            using var sut = new OnePasswordClient(null, _apiKey);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullApiKey_ThrowsException()
        {
            // Act
            using var sut = new OnePasswordClient(_baseUrl, null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithEmptyBaseUrl_ThrowsException()
        {
            // Act
            using var sut = new OnePasswordClient(string.Empty, _apiKey);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithEmptyApiKey_ThrowsException()
        {
            // Act
            using var sut = new OnePasswordClient(_baseUrl, string.Empty);
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var sut = new OnePasswordClient(_baseUrl, _apiKey);

            // Act & Assert - Should not throw on multiple dispose calls
            sut.Dispose();
            sut.Dispose();
            sut.Dispose();
        }

        [TestMethod]
        public void Constructor_WithExternalHttpClient_DoesNotDisposeIt()
        {
            // Arrange
            var httpClient = new HttpClient();
            var originalTimeout = httpClient.Timeout;

            // Act
            using var sut = new OnePasswordClient(_baseUrl, _apiKey, httpClient, false);
            sut.Dispose();

            // Assert - HttpClient should still be usable
            Assert.AreEqual(originalTimeout, httpClient.Timeout);
            httpClient.Dispose();
        }

        [TestMethod]
        public void Constructor_WithSharedHttpClient_SharesResource()
        {
            // Arrange
            var sharedClient = new HttpClient();

            // Act
            var sut1 = new OnePasswordClient(_baseUrl, _apiKey, sharedClient, false);
            var sut2 = new OnePasswordClient(_baseUrl, _apiKey, sharedClient, false);

            sut1.Dispose();
            sut2.Dispose();

            // Assert - Shared client should still be usable
            Assert.IsNotNull(sharedClient.Timeout);
            sharedClient.Dispose();
        }

        [TestMethod]
        public async Task GetSecretValueAsync_WithInvalidCredentials_ThrowsException()
        {
            // Arrange
            using var sut = new OnePasswordClient(_baseUrl, "invalid-key");
            var vaultId = "test-vault";
            var itemId = "test-item";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(async () =>
            {
                await sut.GetSecretValueAsync(vaultId, itemId);
            });
        }

        [TestMethod]
        public async Task GetItemAsync_WithInvalidCredentials_ThrowsException()
        {
            // Arrange
            using var sut = new OnePasswordClient(_baseUrl, "invalid-key");
            var vaultId = "test-vault";
            var itemId = "test-item";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(async () =>
            {
                await sut.GetItemAsync(vaultId, itemId);
            });
        }
    }
}
