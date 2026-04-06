using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using NSubstitute;

namespace Dorc.TerraformRunner.Tests
{
    [TestClass]
    public class TerraformCodeSourceProviderFactoryTests
    {
        private IRunnerLogger _mockLogger = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = Substitute.For<IRunnerLogger>();
        }

        [TestMethod]
        public void GetProvider_SharedFolder_ReturnsSharedFolderProvider()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);
            var provider = factory.GetProvider(TerraformSourceType.SharedFolder);

            Assert.IsNotNull(provider);
            Assert.AreEqual(TerraformSourceType.SharedFolder, provider.SourceType);
            Assert.IsInstanceOfType(provider, typeof(SharedFolderCodeSourceProvider));
        }

        [TestMethod]
        public void GetProvider_Git_ReturnsGitCodeSourceProvider()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);
            var provider = factory.GetProvider(TerraformSourceType.Git);

            Assert.IsNotNull(provider);
            Assert.AreEqual(TerraformSourceType.Git, provider.SourceType);
            Assert.IsInstanceOfType(provider, typeof(GitCodeSourceProvider));
        }

        [TestMethod]
        public void GetProvider_AzureArtifact_ReturnsAzureArtifactProvider()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);
            var provider = factory.GetProvider(TerraformSourceType.AzureArtifact);

            Assert.IsNotNull(provider);
            Assert.AreEqual(TerraformSourceType.AzureArtifact, provider.SourceType);
            Assert.IsInstanceOfType(provider, typeof(AzureArtifactCodeSourceProvider));
        }

        [TestMethod]
        public void GetProvider_GitHubArtifact_ReturnsGitHubArtifactProvider()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);
            var provider = factory.GetProvider(TerraformSourceType.GitHubArtifact);

            Assert.IsNotNull(provider);
            Assert.AreEqual(TerraformSourceType.GitHubArtifact, provider.SourceType);
            Assert.IsInstanceOfType(provider, typeof(GitHubArtifactCodeSourceProvider));
        }

        [TestMethod]
        public void GetProvider_UnsupportedType_ThrowsNotSupportedException()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);

            Assert.ThrowsException<NotSupportedException>(() =>
                factory.GetProvider((TerraformSourceType)999));
        }

        [TestMethod]
        public void Factory_RegistersAllFourProviders()
        {
            var factory = new TerraformCodeSourceProviderFactory(_mockLogger);

            // Verify all 4 source types are registered
            Assert.IsNotNull(factory.GetProvider(TerraformSourceType.SharedFolder));
            Assert.IsNotNull(factory.GetProvider(TerraformSourceType.Git));
            Assert.IsNotNull(factory.GetProvider(TerraformSourceType.AzureArtifact));
            Assert.IsNotNull(factory.GetProvider(TerraformSourceType.GitHubArtifact));
        }
    }
}
