using Dorc.ApiModel;
using Dorc.Core.BuildServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class BuildServerClientFactoryTests
    {
        private ILoggerFactory _loggerFactory = null!;
        private IConfiguration _configuration = null!;
        private IHttpClientFactory _httpClientFactory = null!;

        [TestInitialize]
        public void Setup()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            _configuration = Substitute.For<IConfiguration>();
            var appSettingsSection = Substitute.For<IConfigurationSection>();
            appSettingsSection[Arg.Any<string>()].Returns((string?)null);
            var hostsSection = Substitute.For<IConfigurationSection>();
            appSettingsSection.GetSection("AllowedGitHubEnterpriseHosts").Returns(hostsSection);
            _configuration.GetSection("AppSettings").Returns(appSettingsSection);

            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        }

        [TestMethod]
        public void Create_AzureDevOps_ReturnsAzureDevOpsBuildServerClient()
        {
            var factory = new BuildServerClientFactory(_loggerFactory, _configuration, _httpClientFactory);
            var client = factory.Create(SourceControlType.AzureDevOps);

            Assert.IsNotNull(client);
            Assert.IsInstanceOfType(client, typeof(AzureDevOpsBuildServerClient));
        }

        [TestMethod]
        public void Create_GitHub_ReturnsGitHubActionsBuildServerClient()
        {
            var factory = new BuildServerClientFactory(_loggerFactory, _configuration, _httpClientFactory);
            var client = factory.Create(SourceControlType.GitHub);

            Assert.IsNotNull(client);
            Assert.IsInstanceOfType(client, typeof(GitHubActionsBuildServerClient));
        }

        [TestMethod]
        public void Create_UnsupportedType_ThrowsNotSupportedException()
        {
            var factory = new BuildServerClientFactory(_loggerFactory, _configuration, _httpClientFactory);

            Assert.ThrowsException<NotSupportedException>(() =>
                factory.Create((SourceControlType)999));
        }

        [TestMethod]
        public void Create_AzureDevOps_MultipleCalls_ReturnsFreshInstances()
        {
            var factory = new BuildServerClientFactory(_loggerFactory, _configuration, _httpClientFactory);
            var client1 = factory.Create(SourceControlType.AzureDevOps);
            var client2 = factory.Create(SourceControlType.AzureDevOps);

            Assert.AreNotSame(client1, client2);
        }

        [TestMethod]
        public void Create_GitHub_MultipleCalls_ReturnsFreshInstances()
        {
            var factory = new BuildServerClientFactory(_loggerFactory, _configuration, _httpClientFactory);
            var client1 = factory.Create(SourceControlType.GitHub);
            var client2 = factory.Create(SourceControlType.GitHub);

            Assert.AreNotSame(client1, client2);
        }
    }
}
