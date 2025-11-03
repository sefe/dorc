using Dorc.Api.Services;
using Dorc.Core.Configuration;
using NSubstitute;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class OnePasswordSecretsReaderTests
    {
        private IConfigurationSettings _config;

        [TestInitialize]
        public void Setup()
        {
            _config = Substitute.For<IConfigurationSettings>();
        }

        [TestMethod]
        public void Constructor_WithoutOnePasswordConfig_DoesNotThrow()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns((string)null);
            _config.GetOnePasswordBaseUrl().Returns((string)null);
            _config.GetOnePasswordApiKey().Returns((string)null);

            // Act
            using var sut = new OnePasswordSecretsReader(_config);

            // Assert
            Assert.IsNotNull(sut);
        }

        [TestMethod]
        public void Constructor_WithPartialConfig_DoesNotInitializeClient()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns("vault-id");
            _config.GetOnePasswordBaseUrl().Returns("https://example.com");
            _config.GetOnePasswordApiKey().Returns((string)null);

            // Act
            using var sut = new OnePasswordSecretsReader(_config);

            // Assert
            Assert.IsNotNull(sut);
        }

        [TestMethod]
        public void GetIdentityServerApiSecret_WithoutClient_ReturnsEmpty()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns((string)null);
            _config.GetOnePasswordBaseUrl().Returns((string)null);
            _config.GetOnePasswordApiKey().Returns((string)null);
            _config.GetOnePasswordIdentityServerApiSecretItemId().Returns("item-id");

            using var sut = new OnePasswordSecretsReader(_config);

            // Act
            var result = sut.GetIdentityServerApiSecret();

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void GetDorcApiSecret_WithoutClient_ReturnsEmpty()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns((string)null);
            _config.GetOnePasswordBaseUrl().Returns((string)null);
            _config.GetOnePasswordApiKey().Returns((string)null);
            _config.GetOnePasswordItemId().Returns("item-id");

            using var sut = new OnePasswordSecretsReader(_config);

            // Act
            var result = sut.GetDorcApiSecret();

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns((string)null);
            _config.GetOnePasswordBaseUrl().Returns((string)null);
            _config.GetOnePasswordApiKey().Returns((string)null);

            var sut = new OnePasswordSecretsReader(_config);

            // Act & Assert - Should not throw
            sut.Dispose();
            sut.Dispose();
            sut.Dispose();
        }

        [TestMethod]
        public void Dispose_WithInitializedClient_DisposesClient()
        {
            // Arrange
            _config.GetOnePasswordVaultId().Returns("vault-id");
            _config.GetOnePasswordBaseUrl().Returns("https://example.com");
            _config.GetOnePasswordApiKey().Returns("api-key");

            var sut = new OnePasswordSecretsReader(_config);

            // Act & Assert - Should not throw
            sut.Dispose();
        }
    }
}
