using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Two credentials can be attached to a Terraform git clone, and neither is scoped to a
    /// repository: the Terraform PAT, and an Entra access token issued to DOrc's own
    /// application registration. The repository URL that attracts them is project
    /// configuration, settable at per-project modify rights.
    ///
    /// The credentials callback previously ignored the URL it was asked about. libgit2 invokes
    /// it for every URL it authenticates against during a clone, redirect targets included — so
    /// a repository that redirected elsewhere collected the credential without the redirect
    /// ever appearing in project configuration for anyone to see.
    /// </summary>
    [TestClass]
    public class GitSourceCredentialBindingTests
    {
        private const string ConfiguredRepository = "https://github.corp.example.com/infra/terraform.git";

        private static GitCodeSourceProvider NewProvider() =>
            new(Substitute.For<IRunnerLogger>());

        private static ScriptGroup WithPat(string pat = "the-pat") =>
            new() { TerraformGitRepoUrl = ConfiguredRepository, TerraformGitPat = pat };

        [TestMethod]
        public void SuppliesTheCredentialForTheConfiguredRepository()
        {
            var credentials = NewProvider().CreateCredentials(
                WithPat(), ConfiguredRepository, isGitHub: true, isAzureDevOps: false);

            Assert.AreEqual("the-pat", credentials.Password);
        }

        /// <summary>
        /// The redirect case, directly.
        /// </summary>
        [TestMethod]
        [DataRow("https://attacker.net/infra/terraform.git")]
        [DataRow("https://github.corp.example.com.attacker.net/infra/terraform.git")]
        [DataRow("https://attacker.net/github.corp.example.com/terraform.git")]
        public void RefusesToAuthenticateToAnyOtherHost(string redirected)
        {
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                NewProvider().CreateCredentials(WithPat(), redirected, isGitHub: true, isAzureDevOps: false));

            StringAssert.Contains(refusal.Message, "Refusing to authenticate");
        }

        [TestMethod]
        public void RefusesWhenTheRequestedUrlNamesNoHost()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NewProvider().CreateCredentials(WithPat(), "not-a-url", isGitHub: true, isAzureDevOps: false));
        }

        /// <summary>
        /// Host classification decides which of the two credentials is offered, so a substring
        /// test here picks the Entra token for an attacker's host just as readily.
        /// </summary>
        [TestMethod]
        [DataRow("https://github.com/org/repo.git", "github.com", true)]
        [DataRow("https://raw.github.com/org/repo.git", "github.com", true)]
        [DataRow("https://github.com.attacker.net/org/repo.git", "github.com", false)]
        [DataRow("https://attacker.net/github.com/repo.git", "github.com", false)]
        [DataRow("https://attacker.net/?x=github.com", "github.com", false)]
        public void ClassifiesTheHostByAuthorityRatherThanSubstring(string url, string host, bool expected)
        {
            Assert.AreEqual(expected, GitCodeSourceProvider.IsHost(url, host), url);
        }
    }
}
