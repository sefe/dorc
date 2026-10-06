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
            var scriptGroup = new ScriptGroup();

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                Configure(scriptGroup, new TestSourceHostAllowList(isUnconfigured: true)));

            StringAssert.Contains(refusal.Message, "no source host allow-list is configured");
            AssertNoSourceOrCredentialConfigured(scriptGroup);
        }

        [TestMethod]
        public void DisallowedHostRejectsGitSource()
        {
            var scriptGroup = new ScriptGroup();

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                Configure(scriptGroup, new TestSourceHostAllowList(isUnconfigured: false)));

            StringAssert.Contains(refusal.Message, "Withholding Git credentials");
            AssertNoSourceOrCredentialConfigured(scriptGroup);
        }

        private static void AssertNoSourceOrCredentialConfigured(ScriptGroup scriptGroup)
        {
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitPat));
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.AzureBearerToken));
        }

        private static void Configure(ScriptGroup scriptGroup, ISourceHostAllowList sourceHosts)
        {
            var configurator = new TerraformSourceConfigurator(
                Substitute.For<ILogger>(),
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<IGitHubHostValidator>(),
                sourceHosts);

            configurator.ConfigureScriptGroup(
                scriptGroup,
                new ComponentApiModel { TerraformSourceType = TerraformSourceType.Git },
                new DeploymentRequestApiModel(),
                new ProjectApiModel
                {
                    TerraformGitRepoUrl = "https://attacker.example/repository.git"
                },
                new Dictionary<string, VariableValue>());
        }

        private sealed class TestSourceHostAllowList(bool isUnconfigured) : ISourceHostAllowList
        {
            public bool IsArtefactSourceUnconfigured => isUnconfigured;
            public bool IsTerraformSourceUnconfigured => isUnconfigured;
            public bool IsUnconfigured => isUnconfigured;

            public PolicyDecision CheckArtefactSource(string? url) => PolicyDecision.Refuse("host is not allowed");

            public PolicyDecision CheckTerraformSource(string? url) => PolicyDecision.Refuse("host is not allowed");
        }
    }
}
