using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class TerraformSourceCredentialConfinementTests
    {
        [TestMethod]
        public void UnconfiguredAllowListRejectsGitSource()
        {
            var scriptGroup = Configure(new TestSourceHostAllowList(isUnconfigured: true));

            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitPat));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
        }

        [TestMethod]
        public void DisallowedHostRejectsGitSource()
        {
            var scriptGroup = Configure(new TestSourceHostAllowList(isUnconfigured: false));

            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitPat));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
        }

        private static ScriptGroup Configure(ISourceHostAllowList sourceHosts)
        {
            var configurator = new TerraformSourceConfigurator(
                Substitute.For<ILogger>(),
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IGitHubHostValidator>(),
                sourceHosts);
            var scriptGroup = new ScriptGroup();

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.Git },
                new DeploymentRequestApiModel(),
                new ProjectApiModel
                {
                    TerraformGitRepoUrl = "https://attacker.example/repository.git"
                },
                new Dictionary<string, VariableValue>());

            return scriptGroup;
        }

        private sealed class TestSourceHostAllowList(bool isUnconfigured) : ISourceHostAllowList
        {
            public bool IsArtefactSourceUnconfigured => isUnconfigured;
            public bool IsTerraformSourceUnconfigured => isUnconfigured;
            public bool IsUnconfigured => isUnconfigured;

            public bool IsArtefactSourceAllowed(string? url, out string reason)
            {
                reason = "host is not allowed";
                return false;
            }

            public bool IsTerraformSourceAllowed(string? url, out string reason)
            {
                reason = "host is not allowed";
                return false;
            }
        }
    }
}
