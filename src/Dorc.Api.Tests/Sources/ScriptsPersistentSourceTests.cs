using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    [TestClass]
    public class ScriptsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory = null!;
        private ScriptsPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _source = new ScriptsPersistentSource(
                _contextFactory,
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IScriptsAuditPersistentSource>());
        }

        [TestMethod]
        [DataRow(@"\\attacker\share\payload.ps1")]
        [DataRow(@"..\..\payload.ps1")]
        [DataRow("""{"ScriptPath":"C:\\Windows\\Temp\\payload.ps1"}""")]
        public void UpdateScript_RejectsPathThatCanEscapeScriptRoot(string path)
        {
            var script = new ScriptApiModel
            {
                Id = 1,
                Name = "Deploy",
                Path = path
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _source.UpdateScript(script, Substitute.For<IPrincipal>()));
            _contextFactory.DidNotReceive().GetContext();
        }
    }
}
