using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using LibGit2Sharp;
using NSubstitute;
using Dorc.Terraform.Catalog;

namespace Dorc.TerraformRunner.Tests.CodeSources
{
    /// <summary>
    /// Tests for GitCodeSourceProvider ref resolution: after a full clone the
    /// requested ref may be a branch (local or remote-tracking), a tag (the
    /// catalog pins module versions as git tags), or a commit SHA. Uses real
    /// local git repositories created with LibGit2Sharp; a local-path clone
    /// never invokes the credentials provider, so a dummy PAT suffices.
    /// </summary>
    [TestClass]
    public class GitCodeSourceProviderTests
    {
        private const string TagName = "stock-modules/sql-database/v1.0.0";
        private const string BranchName = "feature/pinned";
        private const string FileName = "main.tf";
        private const string ContentV1 = "content-v1";
        private const string ContentV2 = "content-v2";

        private string _tempRoot = null!;
        private string _sourceRepoPath = null!;
        private string _firstCommitSha = null!;
        private GitCodeSourceProvider _provider = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Join(Directory.GetCurrentDirectory(), "gcsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _sourceRepoPath = CreateSourceRepository();
            _provider = new GitCodeSourceProvider(Substitute.For<IRunnerLogger>());
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (!Directory.Exists(_tempRoot)) return;
                // .git object files are read-only; clear attributes so Delete succeeds.
                foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_tempRoot, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best-effort */ }
        }

        /// <summary>
        /// Builds a source repo where the first commit (content-v1) carries a
        /// tag and a branch, and the default branch has advanced to a second
        /// commit (content-v2). Checking out the tag/branch/SHA must therefore
        /// yield v1, while the default branch yields v2.
        /// </summary>
        private string CreateSourceRepository()
        {
            var sourcePath = Path.Join(_tempRoot, "source");
            Directory.CreateDirectory(sourcePath);
            Repository.Init(sourcePath);

            using var repo = new Repository(sourcePath);
            var signature = new Signature("Test User", "test.user@example.com", DateTimeOffset.Now);

            File.WriteAllText(Path.Join(sourcePath, FileName), ContentV1);
            Commands.Stage(repo, FileName);
            var firstCommit = repo.Commit("v1", signature, signature);
            _firstCommitSha = firstCommit.Sha;

            repo.ApplyTag(TagName);
            repo.CreateBranch(BranchName);

            File.WriteAllText(Path.Join(sourcePath, FileName), ContentV2);
            Commands.Stage(repo, FileName);
            repo.Commit("v2", signature, signature);

            return sourcePath;
        }

        private ScriptGroup BuildScriptGroup(string gitRef)
        {
            return new ScriptGroup
            {
                TerraformSourceType = TerraformSourceType.Git,
                TerraformGitRepoUrl = _sourceRepoPath,
                TerraformGitBranch = gitRef,
                TerraformGitPat = "dummy-pat"
            };
        }

        private string NewWorkingDir() => Path.Join(_tempRoot, "work-" + Guid.NewGuid().ToString("N"));

        [TestMethod]
        public async Task CatalogReference_UsesVerifiedPin_AndRetainsDeprecatedReleases()
        {
            var manifest = new TerraformTemplateManifest(
                "module", "1.0.0", new TerraformTemplateSource("git", _sourceRepoPath, TagName,
                    CommitSha: _firstCommitSha),
                Array.Empty<TerraformTemplateParameter>(), Array.Empty<TerraformTemplateOutput>(),
                null, Array.Empty<string>(), null, new Dictionary<string, string>(), ">= 1.5.0",
                null, true, "Use 2.0.0");
            var catalog = Substitute.For<ITemplateCatalog>();
            catalog.GetAsync("module", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(manifest));
            var provider = new CatalogReferenceCodeSourceProvider(Substitute.For<IRunnerLogger>(), catalog);
            var group = new ScriptGroup {
                TerraformTemplateName = "module", TerraformTemplateVersion = "1.0.0",
                TerraformSourceType = TerraformSourceType.Catalog, TerraformGitPat = "dummy"
            };
            var workingDir = NewWorkingDir();
            await provider.ProvisionCodeAsync(group, workingDir, CancellationToken.None);
            Assert.AreEqual(ContentV1, File.ReadAllText(Path.Join(workingDir, FileName)));
            Assert.AreEqual(_firstCommitSha, group.TerraformResolvedGitSha);

            catalog.GetAsync("module", "1.0.0", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TerraformTemplateManifest?>(manifest with {
                    Source = manifest.Source with { CommitSha = null }
                }));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(group, NewWorkingDir(), CancellationToken.None));
        }

        [TestMethod]
        public async Task CatalogTag_IsNotShadowedByBranchWithSameName()
        {
            using (var repo = new Repository(_sourceRepoPath))
                repo.CreateBranch(TagName, repo.Head.Tip);
            var scriptGroup = BuildScriptGroup(TagName);
            var workingDir = NewWorkingDir();

            await _provider.ProvisionCodeAsync(scriptGroup, workingDir, CancellationToken.None, _firstCommitSha);

            Assert.AreEqual(ContentV1, File.ReadAllText(Path.Join(workingDir, FileName)));
            Assert.AreEqual(_firstCommitSha, scriptGroup.TerraformResolvedGitSha);
        }

        [TestMethod]
        public async Task CatalogTag_RejectsMovedTag()
        {
            using (var repo = new Repository(_sourceRepoPath))
            {
                repo.Tags.Remove(TagName);
                repo.ApplyTag(TagName);
            }
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                _provider.ProvisionCodeAsync(BuildScriptGroup(TagName), NewWorkingDir(),
                    CancellationToken.None, _firstCommitSha));
        }

        [TestMethod]
        public async Task CatalogTag_RejectsBranchOnlyAndAbbreviatedPin()
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                _provider.ProvisionCodeAsync(BuildScriptGroup(BranchName), NewWorkingDir(),
                    CancellationToken.None, _firstCommitSha));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                _provider.ProvisionCodeAsync(BuildScriptGroup(TagName), NewWorkingDir(),
                    CancellationToken.None, _firstCommitSha[..8]));
        }

        [TestMethod]
        public async Task CatalogTag_ResolvesAnnotatedFullyQualifiedTag()
        {
            using (var repo = new Repository(_sourceRepoPath))
            {
                var signature = new Signature("Test User", "test.user@example.com", DateTimeOffset.Now);
                repo.ApplyTag("annotated", _firstCommitSha, signature, "release");
            }
            var workingDir = NewWorkingDir();
            await _provider.ProvisionCodeAsync(BuildScriptGroup("refs/tags/annotated"), workingDir,
                CancellationToken.None, _firstCommitSha);
            Assert.AreEqual(ContentV1, File.ReadAllText(Path.Join(workingDir, FileName)));
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_TagRef_ChecksOutTaggedCommit()
        {
            var workingDir = NewWorkingDir();

            await _provider.ProvisionCodeAsync(BuildScriptGroup(TagName), workingDir, CancellationToken.None);

            var content = File.ReadAllText(Path.Join(workingDir, FileName));
            Assert.AreEqual(ContentV1, content, "Tag ref must check out the tagged commit, not the branch head");
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_RemoteBranchRef_ChecksOutBranch()
        {
            var workingDir = NewWorkingDir();

            await _provider.ProvisionCodeAsync(BuildScriptGroup(BranchName), workingDir, CancellationToken.None);

            var content = File.ReadAllText(Path.Join(workingDir, FileName));
            Assert.AreEqual(ContentV1, content, "Non-default branch must resolve via its remote-tracking ref");
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_CommitShaRef_ChecksOutCommit()
        {
            var workingDir = NewWorkingDir();

            await _provider.ProvisionCodeAsync(BuildScriptGroup(_firstCommitSha), workingDir, CancellationToken.None);

            var content = File.ReadAllText(Path.Join(workingDir, FileName));
            Assert.AreEqual(ContentV1, content, "Commit SHA ref must check out that commit");
        }

        [TestMethod]
        public async Task ProvisionCodeAsync_UnresolvableRef_ThrowsInvalidOperationException()
        {
            var workingDir = NewWorkingDir();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _provider.ProvisionCodeAsync(BuildScriptGroup("no-such-ref"), workingDir, CancellationToken.None));
        }
    }
}
