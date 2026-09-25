using Dorc.Monitor.TerraformSourceConfig;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Deciding that a repository "is Azure DevOps" is what causes an Entra access token —
    /// issued to DOrc's own application registration, scoped to no particular repository — to be
    /// attached to the clone. The repository URL is project configuration, settable at
    /// per-project modify rights.
    ///
    /// The decision was a substring test over the whole URL, so any attacker-chosen host that
    /// merely mentioned dev.azure.com collected the token.
    /// </summary>
    [TestClass]
    public class TerraformSourceHostClassificationTests
    {
        [TestMethod]
        [DataRow("https://dev.azure.com/org/project/_git/repo")]
        [DataRow("https://myorg.visualstudio.com/project/_git/repo")]
        [DataRow("https://visualstudio.com/project/_git/repo")]
        [DataRow("HTTPS://DEV.AZURE.COM/org/_git/repo")]
        public void RecognisesAGenuineAzureDevOpsRepository(string url)
        {
            Assert.IsTrue(TerraformSourceConfigurator.IsAzureDevOpsRepository(url), url);
        }

        [TestMethod]
        [DataRow("https://dev.azure.com.attacker.net/org/_git/repo")]
        [DataRow("https://visualstudio.com.attacker.net/org/_git/repo")]
        [DataRow("https://attacker.net/dev.azure.com/_git/repo")]
        [DataRow("https://attacker.net/?x=dev.azure.com")]
        [DataRow("https://attacker.net#dev.azure.com")]
        [DataRow("https://dev.azure.com@attacker.net/org/_git/repo")]
        public void DoesNotRecogniseAHostThatMerelyMentionsOne(string url)
        {
            Assert.IsFalse(TerraformSourceConfigurator.IsAzureDevOpsRepository(url),
                $"'{url}' would have been handed an Entra access token.");
        }

        [TestMethod]
        [DataRow("https://github.corp.example.com/infra/terraform.git")]
        [DataRow("not-a-url")]
        [DataRow("")]
        public void DoesNotRecogniseSomethingThatIsNotAzureDevOpsAtAll(string url)
        {
            Assert.IsFalse(TerraformSourceConfigurator.IsAzureDevOpsRepository(url));
        }
    }
}
