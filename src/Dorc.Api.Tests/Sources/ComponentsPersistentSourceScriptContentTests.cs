using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    [TestClass]
    public class ComponentsPersistentSourceScriptContentTests
    {
        private const string StoredBaseline =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [TestMethod]
        public void DeploymentMappingPreservesTheStoredBaselineWhenDiskContentDiffers()
        {
            var storedScript = new Script
            {
                Id = 11,
                Name = "Deploy.ps1",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = StoredBaseline
            };
            var components = new List<Component>
            {
                new()
                {
                    Id = 7,
                    Name = "Deploy",
                    Script = storedScript,
                    ScriptId = storedScript.Id
                }
            };

            var context = Substitute.For<IDeploymentContext>();
            var componentSet = DbContextMock.GetQueryableMockDbSet(components);
            context.Components.Returns(componentSet);
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(context);
            var source = new ComponentsPersistentSource(contextFactory);

            var diskPath = Path.Join(
                AppContext.BaseDirectory,
                $"dorc-script-content-{Guid.NewGuid():N}.ps1");

            try
            {
                File.WriteAllText(diskPath, "Write-Host 'different content'");

                var deploymentScript = source.GetScripts(7);
                var onDisk = ScriptContentHash.Of(File.ReadAllBytes(diskPath));

                // The mapping carries the recorded baseline through untouched. It must never be
                // recomputed from whatever is on disk at the time, because the disk content is
                // exactly what the baseline exists to be checked against.
                Assert.AreEqual(StoredBaseline, deploymentScript.ContentHash);
                Assert.IsFalse(ScriptContentHash.Matches(deploymentScript.ContentHash, onDisk));
                Assert.AreEqual(StoredBaseline, storedScript.ContentHash);
            }
            finally
            {
                if (File.Exists(diskPath))
                {
                    File.Delete(diskPath);
                }
            }
        }
    }
}
