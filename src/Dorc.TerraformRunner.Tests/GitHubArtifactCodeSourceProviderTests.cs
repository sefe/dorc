using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.TerraformRunner.Tests
{
    [TestClass]
    public class GitHubArtifactCodeSourceProviderTests
    {
        private IRunnerLogger _mockLogger = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = Substitute.For<IRunnerLogger>();
            _mockLogger.FileLogger.Returns(Substitute.For<ILogger>());
        }

        [TestMethod]
        public void SourceType_ReturnsGitHubArtifact()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            Assert.AreEqual(TerraformSourceType.GitHubArtifact, provider.SourceType);
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_MissingOwner_ThrowsInvalidOperationException()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            var scriptGroup = new ScriptGroup
            {
                GitHubOwner = null,
                GitHubRepo = "repo",
                GitHubRunId = "123",
                GitHubToken = "token"
            };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, "/tmp/workdir", CancellationToken.None));
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_MissingRepo_ThrowsInvalidOperationException()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            var scriptGroup = new ScriptGroup
            {
                GitHubOwner = "owner",
                GitHubRepo = null,
                GitHubRunId = "123",
                GitHubToken = "token"
            };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, "/tmp/workdir", CancellationToken.None));
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_MissingRunId_ThrowsInvalidOperationException()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            var scriptGroup = new ScriptGroup
            {
                GitHubOwner = "owner",
                GitHubRepo = "repo",
                GitHubRunId = null,
                GitHubToken = "token"
            };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, "/tmp/workdir", CancellationToken.None));
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_MissingToken_ThrowsArgumentException()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            var scriptGroup = new ScriptGroup
            {
                GitHubOwner = "owner",
                GitHubRepo = "repo",
                GitHubRunId = "123",
                GitHubToken = null
            };

            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, "/tmp/workdir", CancellationToken.None));
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_EmptyOwnerAndRepo_ThrowsInvalidOperationException()
        {
            var provider = new GitHubArtifactCodeSourceProvider(_mockLogger);
            var scriptGroup = new ScriptGroup
            {
                GitHubOwner = "",
                GitHubRepo = "",
                GitHubRunId = "",
                GitHubToken = "token"
            };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, "/tmp/workdir", CancellationToken.None));
        }
    }
}
