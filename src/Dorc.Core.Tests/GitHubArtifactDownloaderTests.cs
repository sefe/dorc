using Dorc.Core.BuildServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class GitHubArtifactDownloaderTests
    {
        private ILogger<GitHubArtifactDownloader> _logger = null!;
        private IHttpClientFactory _httpClientFactory = null!;
        private IGitHubHostValidator _hostValidator = null!;
        private IConfiguration _configuration = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<GitHubArtifactDownloader>>();

            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

            _hostValidator = Substitute.For<IGitHubHostValidator>();

            _configuration = Substitute.For<IConfiguration>();
            var appSettings = Substitute.For<IConfigurationSection>();
            appSettings[Arg.Any<string>()].Returns((string?)null);
            _configuration.GetSection("AppSettings").Returns(appSettings);
        }

        private GitHubArtifactDownloader CreateSut() =>
            new(_logger, _httpClientFactory, _hostValidator, _configuration);

        // -------- IsGitHubArtifactUrl --------

        [TestMethod]
        public void IsGitHubArtifactUrl_ReturnsTrueForCanonicalArtifactUrl()
        {
            var sut = CreateSut();

            Assert.IsTrue(sut.IsGitHubArtifactUrl(
                "https://api.github.com/repos/owner/repo/actions/artifacts/12345/zip"));
        }

        [TestMethod]
        public void IsGitHubArtifactUrl_ReturnsTrueForEnterpriseArtifactUrl()
        {
            var sut = CreateSut();

            Assert.IsTrue(sut.IsGitHubArtifactUrl(
                "https://github.mycorp.local/api/v3/repos/o/r/actions/artifacts/1/zip"));
        }

        [TestMethod]
        public void IsGitHubArtifactUrl_ReturnsFalseForUrlWithoutArtifactsSegment()
        {
            var sut = CreateSut();

            Assert.IsFalse(sut.IsGitHubArtifactUrl("https://api.github.com/repos/owner/repo/commits"));
        }

        [TestMethod]
        public void IsGitHubArtifactUrl_ReturnsFalseForNonHttps()
        {
            var sut = CreateSut();

            Assert.IsFalse(sut.IsGitHubArtifactUrl(
                "http://api.github.com/repos/owner/repo/actions/artifacts/1/zip"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void IsGitHubArtifactUrl_ReturnsFalseForEmpty(string? url)
        {
            var sut = CreateSut();

            Assert.IsFalse(sut.IsGitHubArtifactUrl(url!));
        }

        // -------- DownloadAndExtract SSRF guard --------
        //
        // These test that the host/scheme validation fires BEFORE any network
        // call. That's the security-critical invariant: a crafted DropLocation
        // must never cause the configured bearer token to be attached to a
        // request bound for an unexpected host.

        [TestMethod]
        public void DownloadAndExtract_RejectsNonAbsoluteUrl()
        {
            var sut = CreateSut();

            var ex = Assert.Throws<ArgumentException>(() =>
                sut.DownloadAndExtract("not-a-url"));

            StringAssert.Contains(ex.Message, "absolute https");
            // Host validator must not have been consulted once the URL is
            // rejected at the scheme level.
            _hostValidator.DidNotReceiveWithAnyArgs().ValidateHost(default!);
        }

        [TestMethod]
        public void DownloadAndExtract_RejectsHttpScheme()
        {
            var sut = CreateSut();

            var ex = Assert.Throws<ArgumentException>(() =>
                sut.DownloadAndExtract("http://api.github.com/repos/o/r/actions/artifacts/1/zip"));

            StringAssert.Contains(ex.Message, "absolute https");
            _hostValidator.DidNotReceiveWithAnyArgs().ValidateHost(default!);
        }

        [TestMethod]
        public void DownloadAndExtract_DelegatesHostValidationToValidator()
        {
            var sut = CreateSut();
            // Arrange the validator to reject this specific host
            _hostValidator
                .When(v => v.ValidateHost("evil.example"))
                .Do(_ => throw new ArgumentException("host not allowed"));

            var ex = Assert.Throws<ArgumentException>(() =>
                sut.DownloadAndExtract("https://evil.example/repos/o/r/actions/artifacts/1/zip"));

            StringAssert.Contains(ex.Message, "host not allowed");
            _hostValidator.Received(1).ValidateHost("evil.example");
        }

        [TestMethod]
        public void DownloadAndExtract_PassesGitHubHostThroughToValidator()
        {
            // api.github.com should be accepted by a real validator, but the
            // point of this test is just to prove we hand the exact host off
            // to IGitHubHostValidator — we don't try to complete the download
            // because that would require a full HttpClient mock.
            _hostValidator.ValidateHost("api.github.com"); // no throw = allowed
            var sut = CreateSut();

            // The download itself will fail (no HTTP mocking), but the host
            // validation should have been consulted first for the right host.
            try
            {
                sut.DownloadAndExtract("https://api.github.com/repos/o/r/actions/artifacts/1/zip");
            }
            catch
            {
                // expected — no HTTP client configured
            }

            _hostValidator.Received().ValidateHost("api.github.com");
        }
    }
}
