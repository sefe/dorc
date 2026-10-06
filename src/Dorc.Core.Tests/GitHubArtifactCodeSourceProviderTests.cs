using Dorc.TerraformRunner.CodeSources;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Covers <see cref="GitHubArtifactCodeSourceProvider.ValidateApiBaseHost"/>,
    /// which is the runner-side defence-in-depth against SSRF / token
    /// exfiltration through a misconfigured <c>GitHubApiBaseUrl</c>. The
    /// primary guard is upstream in the Monitor via <c>IGitHubHostValidator</c>;
    /// this test class verifies the runner itself still refuses to ship the
    /// bearer token to an unexpected host even if the upstream check were
    /// bypassed.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // these tests mutate a process-wide environment variable
    public class GitHubArtifactCodeSourceProviderTests
    {
        private const string EnvVarName = "DORC_GITHUB_ENTERPRISE_HOSTS";

        private string? _originalEnvVarValue;

        [TestInitialize]
        public void Setup()
        {
            // Snapshot whatever the test runner inherited so we can restore it
            // after each test; the method under test reads the env var at call
            // time, so we must leave the process clean between tests.
            _originalEnvVarValue = Environment.GetEnvironmentVariable(EnvVarName);
            Environment.SetEnvironmentVariable(EnvVarName, null);
        }

        [TestCleanup]
        public void Teardown()
        {
            Environment.SetEnvironmentVariable(EnvVarName, _originalEnvVarValue);
        }

        [TestMethod]
        public void ValidateApiBaseHost_AllowsApiGitHubCom()
        {
            // Should not throw
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://api.github.com");
        }

        [TestMethod]
        public void ValidateApiBaseHost_AllowsGithubCom()
        {
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.com");
        }

        [TestMethod]
        public void ValidateApiBaseHost_IsCaseInsensitiveOnDefaultHosts()
        {
            // Hostnames are case-insensitive per RFC 3986 — the allow-list
            // comparison must match that to avoid trivial bypass.
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://API.GITHUB.COM");
        }

        [TestMethod]
        public void ValidateApiBaseHost_RejectsHttpScheme()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("http://api.github.com"));

            StringAssert.Contains(ex.Message, "HTTPS");
        }

        [TestMethod]
        public void ValidateApiBaseHost_RejectsInvalidUri()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("not a url at all"));

            StringAssert.Contains(ex.Message, "valid URI");
        }

        [TestMethod]
        public void ValidateApiBaseHost_RejectsUnknownHostWithoutEnvVar()
        {
            // With no DORC_GITHUB_ENTERPRISE_HOSTS set, any non-default host
            // must be refused.
            var ex = Assert.Throws<ArgumentException>(() =>
                GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://evil.example"));

            StringAssert.Contains(ex.Message, "evil.example");
            StringAssert.Contains(ex.Message, "DORC_GITHUB_ENTERPRISE_HOSTS");
        }

        [TestMethod]
        public void ValidateApiBaseHost_AllowsHostListedInEnvVar()
        {
            Environment.SetEnvironmentVariable(EnvVarName, "github.mycorp.local");

            // Should not throw
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.mycorp.local/api/v3");
        }

        [TestMethod]
        public void ValidateApiBaseHost_EnvVarMatchIsCaseInsensitive()
        {
            Environment.SetEnvironmentVariable(EnvVarName, "GitHub.MyCorp.Local");

            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.mycorp.local/api/v3");
        }

        [TestMethod]
        public void ValidateApiBaseHost_EnvVarAcceptsMultipleHostsAndTrimsWhitespace()
        {
            Environment.SetEnvironmentVariable(EnvVarName,
                " github.corpA.com , github.corpB.com ,github.corpC.com");

            // All three, despite varied spacing
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.corpA.com/api/v3");
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.corpB.com/api/v3");
            GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.corpC.com/api/v3");
        }

        [TestMethod]
        public void ValidateApiBaseHost_RejectsHostNotInEnvVarEvenWhenOtherHostsAre()
        {
            Environment.SetEnvironmentVariable(EnvVarName, "github.trusted.local");

            Assert.Throws<ArgumentException>(() =>
                GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://github.untrusted.local/api/v3"));
        }

        [TestMethod]
        public void ValidateApiBaseHost_EmptyEnvVarDoesNotAcceptArbitraryHosts()
        {
            Environment.SetEnvironmentVariable(EnvVarName, "");

            Assert.Throws<ArgumentException>(() =>
                GitHubArtifactCodeSourceProvider.ValidateApiBaseHost("https://anything.example"));
        }
    }
}
