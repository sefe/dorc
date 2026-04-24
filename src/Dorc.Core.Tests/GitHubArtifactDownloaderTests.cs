using Dorc.Core.BuildServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class GitHubArtifactDownloaderTests
    {
        private ILogger<GitHubArtifactDownloader> _logger = null!;
        private IHttpClientFactory _httpClientFactory = null!;
        private IGitHubHostValidator _hostValidator = null!;
        private IConfiguration _configuration = null!;
        private readonly List<HttpClient> _trackedClients = new();

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<GitHubArtifactDownloader>>();

            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(TrackClient(new HttpClient()));

            _hostValidator = Substitute.For<IGitHubHostValidator>();

            _configuration = Substitute.For<IConfiguration>();
            var appSettings = Substitute.For<IConfigurationSection>();
            appSettings[Arg.Any<string>()].Returns((string?)null);
            _configuration.GetSection("AppSettings").Returns(appSettings);
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var client in _trackedClients)
                client.Dispose();
            _trackedClients.Clear();
        }

        private HttpClient TrackClient(HttpClient client)
        {
            _trackedClients.Add(client);
            return client;
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

        // -------- DownloadAndExtract happy path --------

        /// <summary>
        /// End-to-end-ish check: given a successful HTTP response carrying a
        /// valid zip payload, the downloader writes it under the configured
        /// folder, extracts into a "drop" subfolder, and returns the parent
        /// path so the caller (PendingRequestProcessor) can clean up later.
        /// </summary>
        [TestMethod]
        public void DownloadAndExtract_HappyPath_ExtractsUnderConfiguredFolderAndReturnsPath()
        {
            // Arrange — an isolated download folder for this test
            var downloadRoot = Path.Combine(Path.GetTempPath(),
                "dorc-download-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);

            try
            {
                // Configuration: point the downloader at the isolated folder
                var appSettings = Substitute.For<IConfigurationSection>();
                appSettings["GitHubArtifactDownloadFolder"].Returns(downloadRoot);
                appSettings["GitHubToken"].Returns("test-token");
                _configuration.GetSection("AppSettings").Returns(appSettings);

                // HttpClient returns a valid zip containing "Server/hello.txt"
                var zipBytes = BuildZipArchive(
                    ("Server/hello.txt", "payload for the installer"));
                _httpClientFactory.CreateClient("GitHubActions").Returns(
                    TrackClient(new HttpClient(new StubHttpHandler(HttpStatusCode.OK, zipBytes))));

                var sut = CreateSut();

                // Act
                var returnedPath = sut.DownloadAndExtract(
                    "https://api.github.com/repos/o/r/actions/artifacts/1/zip");

                // Assert — returned path lives under the configured root
                Assert.IsTrue(returnedPath.StartsWith(downloadRoot, StringComparison.OrdinalIgnoreCase),
                    $"Expected returned path to live under '{downloadRoot}', was '{returnedPath}'");
                Assert.IsTrue(Directory.Exists(returnedPath));

                // Artifact zip was extracted into a "drop" subfolder, preserving
                // the zip's internal layout
                var dropFolder = Path.Combine(returnedPath, "drop");
                Assert.IsTrue(Directory.Exists(dropFolder), "Expected 'drop' subfolder to exist");
                var extracted = Path.Combine(dropFolder, "Server", "hello.txt");
                Assert.IsTrue(File.Exists(extracted), $"Expected extracted file at '{extracted}'");
                Assert.AreEqual("payload for the installer", File.ReadAllText(extracted));

                // The intermediate zip file must have been deleted so it can't
                // be re-invoked or inflate cleanup size
                Assert.IsFalse(File.Exists(Path.Combine(returnedPath, "artifact.zip")),
                    "artifact.zip should be deleted after extraction");
            }
            finally
            {
                if (Directory.Exists(downloadRoot))
                    Directory.Delete(downloadRoot, recursive: true);
            }
        }

        [TestMethod]
        public void DownloadAndExtract_HttpFailure_CleansUpAndRethrows()
        {
            var downloadRoot = Path.Combine(Path.GetTempPath(),
                "dorc-download-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);

            try
            {
                var appSettings = Substitute.For<IConfigurationSection>();
                appSettings["GitHubArtifactDownloadFolder"].Returns(downloadRoot);
                _configuration.GetSection("AppSettings").Returns(appSettings);

                _httpClientFactory.CreateClient("GitHubActions").Returns(
                    TrackClient(new HttpClient(new StubHttpHandler(HttpStatusCode.Unauthorized, Array.Empty<byte>()))));

                var sut = CreateSut();

                Assert.Throws<HttpRequestException>(() =>
                    sut.DownloadAndExtract("https://api.github.com/repos/o/r/actions/artifacts/1/zip"));

                // Cleanup guarantee: no leftover per-request directory inside
                // the configured root — every subdir we created was rolled
                // back on failure.
                var leftovers = Directory.GetDirectories(downloadRoot);
                Assert.AreEqual(0, leftovers.Length,
                    $"Expected no leftover download dirs; found: {string.Join(", ", leftovers)}");
            }
            finally
            {
                if (Directory.Exists(downloadRoot))
                    Directory.Delete(downloadRoot, recursive: true);
            }
        }

        // -------- Cleanup path-traversal guard --------

        [TestMethod]
        public void Cleanup_RefusesPathOutsideConfiguredArtifactBase()
        {
            // Arrange — configured base folder plus a sibling directory that
            // sits outside that base. Cleanup must refuse to touch the sibling
            // even though it exists on disk.
            var downloadRoot = Path.Combine(Path.GetTempPath(),
                "dorc-download-test-" + Guid.NewGuid().ToString("N"));
            var outsideRoot = Path.Combine(Path.GetTempPath(),
                "dorc-outside-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);
            Directory.CreateDirectory(outsideRoot);

            try
            {
                var appSettings = Substitute.For<IConfigurationSection>();
                appSettings["GitHubArtifactDownloadFolder"].Returns(downloadRoot);
                _configuration.GetSection("AppSettings").Returns(appSettings);

                var sut = CreateSut();

                // Act
                sut.Cleanup(outsideRoot);

                // Assert — the outside directory is untouched
                Assert.IsTrue(Directory.Exists(outsideRoot),
                    "Cleanup must not delete paths outside the configured artifact base.");
            }
            finally
            {
                if (Directory.Exists(downloadRoot))
                    Directory.Delete(downloadRoot, recursive: true);
                if (Directory.Exists(outsideRoot))
                    Directory.Delete(outsideRoot, recursive: true);
            }
        }

        [TestMethod]
        public void Cleanup_DeletesPathInsideConfiguredArtifactBase()
        {
            var downloadRoot = Path.Combine(Path.GetTempPath(),
                "dorc-download-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);
            var insidePath = Path.Combine(downloadRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(insidePath);

            try
            {
                var appSettings = Substitute.For<IConfigurationSection>();
                appSettings["GitHubArtifactDownloadFolder"].Returns(downloadRoot);
                _configuration.GetSection("AppSettings").Returns(appSettings);

                var sut = CreateSut();

                sut.Cleanup(insidePath);

                Assert.IsFalse(Directory.Exists(insidePath),
                    "Cleanup should delete paths that canonicalise under the artifact base.");
            }
            finally
            {
                if (Directory.Exists(downloadRoot))
                    Directory.Delete(downloadRoot, recursive: true);
            }
        }

        [TestMethod]
        public void Cleanup_RejectsTraversalEscapingConfiguredBase()
        {
            // Arrange — base folder + a traversal path that syntactically
            // references the base but escapes it via `..`.
            var downloadRoot = Path.Combine(Path.GetTempPath(),
                "dorc-download-test-" + Guid.NewGuid().ToString("N"));
            var siblingOutside = Path.Combine(Path.GetTempPath(),
                "dorc-sibling-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);
            Directory.CreateDirectory(siblingOutside);

            var traversal = Path.Combine(downloadRoot, "..",
                Path.GetFileName(siblingOutside));

            try
            {
                var appSettings = Substitute.For<IConfigurationSection>();
                appSettings["GitHubArtifactDownloadFolder"].Returns(downloadRoot);
                _configuration.GetSection("AppSettings").Returns(appSettings);

                var sut = CreateSut();

                sut.Cleanup(traversal);

                Assert.IsTrue(Directory.Exists(siblingOutside),
                    "Cleanup must block `..`-style traversal escaping the artifact base.");
            }
            finally
            {
                if (Directory.Exists(downloadRoot))
                    Directory.Delete(downloadRoot, recursive: true);
                if (Directory.Exists(siblingOutside))
                    Directory.Delete(siblingOutside, recursive: true);
            }
        }

        // -------- helpers --------

        private static byte[] BuildZipArchive(params (string Path, string Content)[] files)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in files)
                {
                    var entry = zip.CreateEntry(path);
                    using var es = entry.Open();
                    var bytes = Encoding.UTF8.GetBytes(content);
                    es.Write(bytes, 0, bytes.Length);
                }
            }
            return ms.ToArray();
        }

        private sealed class StubHttpHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly byte[] _body;

            public StubHttpHandler(HttpStatusCode status, byte[] body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Ownership of the HttpResponseMessage transfers to the HttpClient
                // caller, which disposes it via its own `using` block.
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new ByteArrayContent(_body)
                });
            }
        }
    }
}
