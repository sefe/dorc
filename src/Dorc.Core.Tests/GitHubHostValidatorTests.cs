using Dorc.Core.BuildServer;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class GitHubHostValidatorTests
    {
        private IGitHubHostValidator CreateValidator(string[]? enterpriseHosts = null)
        {
            var configData = new Dictionary<string, string?>();
            if (enterpriseHosts != null)
            {
                for (int i = 0; i < enterpriseHosts.Length; i++)
                    configData[$"AppSettings:AllowedGitHubEnterpriseHosts:{i}"] = enterpriseHosts[i];
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            return new GitHubHostValidator(configuration);
        }

        #region ValidateHost tests

        [TestMethod]
        public void ValidateHost_ApiGitHubCom_Succeeds()
        {
            var validator = CreateValidator();
            validator.ValidateHost("api.github.com");
        }

        [TestMethod]
        public void ValidateHost_GitHubCom_Succeeds()
        {
            var validator = CreateValidator();
            validator.ValidateHost("github.com");
        }

        [TestMethod]
        public void ValidateHost_CaseInsensitive_Succeeds()
        {
            var validator = CreateValidator();
            validator.ValidateHost("API.GITHUB.COM");
        }

        [TestMethod]
        public void ValidateHost_UnknownHost_Throws()
        {
            var validator = CreateValidator();
            Assert.Throws<ArgumentException>(() =>
                validator.ValidateHost("evil.example.com"));
        }

        [TestMethod]
        public void ValidateHost_InternalHost_Throws()
        {
            var validator = CreateValidator();
            Assert.Throws<ArgumentException>(() =>
                validator.ValidateHost("169.254.169.254"));
        }

        [TestMethod]
        public void ValidateHost_ConfiguredEnterpriseHost_Succeeds()
        {
            var validator = CreateValidator(new[] { "github.mycompany.com" });
            validator.ValidateHost("github.mycompany.com");
        }

        [TestMethod]
        public void ValidateHost_UnconfiguredEnterpriseHost_Throws()
        {
            var validator = CreateValidator(new[] { "github.mycompany.com" });
            Assert.Throws<ArgumentException>(() =>
                validator.ValidateHost("github.othercompany.com"));
        }

        #endregion

        #region GetApiBase tests

        [TestMethod]
        public void GetApiBase_PublicGitHub_ReturnsApiUrl()
        {
            var validator = CreateValidator();
            var result = validator.GetApiBase("https://api.github.com/repos/owner/repo");
            Assert.AreEqual("https://api.github.com", result);
        }

        [TestMethod]
        public void GetApiBase_GitHubCom_ReturnsApiUrl()
        {
            var validator = CreateValidator();
            var result = validator.GetApiBase("https://github.com/repos/owner/repo");
            Assert.AreEqual("https://api.github.com", result);
        }

        [TestMethod]
        public void GetApiBase_EnterpriseHost_ReturnsV3Url()
        {
            var validator = CreateValidator(new[] { "github.mycompany.com" });
            var result = validator.GetApiBase("https://github.mycompany.com/api/v3/repos/owner/repo");
            Assert.AreEqual("https://github.mycompany.com/api/v3", result);
        }

        [TestMethod]
        public void GetApiBase_EnterpriseHost_EnforcesHttps()
        {
            var validator = CreateValidator(new[] { "github.mycompany.com" });
            var result = validator.GetApiBase("http://github.mycompany.com/api/v3/repos/owner/repo");
            Assert.AreEqual("https://github.mycompany.com/api/v3", result);
        }

        [TestMethod]
        public void GetApiBase_UnknownHost_Throws()
        {
            var validator = CreateValidator();
            Assert.Throws<ArgumentException>(() =>
                validator.GetApiBase("https://evil.example.com/repos/owner/repo"));
        }

        [TestMethod]
        public void GetApiBase_MetadataEndpoint_Throws()
        {
            var validator = CreateValidator();
            Assert.Throws<ArgumentException>(() =>
                validator.GetApiBase("http://169.254.169.254/latest/meta-data/"));
        }

        #endregion
    }
}
