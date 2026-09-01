using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    [TestClass]
    public class ProjectsPersistentSourceTests
    {
        private const string ArtefactHost = "buildserver.corp.example.com";
        private const string TerraformHost = "github.corp.example.com";

        private IDeploymentContextFactory _contextFactory = null!;
        private ProjectsPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            var settings = new Dictionary<string, string?>
            {
                [SourceHostAllowList.ArtefactHostsSetting + ":0"] = ArtefactHost,
                [SourceHostAllowList.TerraformHostsSetting + ":0"] = TerraformHost
            };

            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _source = new ProjectsPersistentSource(
                _contextFactory,
                Substitute.For<IEnvironmentsPersistentSource>(),
                Substitute.For<ILogger<ProjectsPersistentSource>>(),
                Substitute.For<IClaimsPrincipalReader>(),
                new SourceHostAllowList(
                    new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
        }

        [TestMethod]
        public void UpdateProject_RejectsDisallowedLaterArtefactRootBeforePersistence()
        {
            var project = ValidProject();
            project.ArtefactsUrl += "; https://attacker.net/drops";

            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.UpdateProject(project));

            StringAssert.Contains(refusal.Message, "attacker.net");
            _contextFactory.DidNotReceive().GetContext();
        }

        [TestMethod]
        public void InsertProject_RejectsDisallowedTerraformHostBeforePersistence()
        {
            var project = ValidProject();
            project.TerraformGitRepoUrl = "https://attacker.net/repository.git";

            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.InsertProject(project));

            StringAssert.Contains(refusal.Message, "attacker.net");
            _contextFactory.DidNotReceive().GetContext();
        }

        private static ProjectApiModel ValidProject() => new()
        {
            ProjectId = 1,
            ProjectName = "TestProject",
            ArtefactsUrl = $"https://{ArtefactHost}/drops",
            TerraformGitRepoUrl = $"https://{TerraformHost}/repository.git"
        };
    }
}
