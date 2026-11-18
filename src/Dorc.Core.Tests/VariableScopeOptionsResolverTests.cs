using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class VariableScopeOptionsResolverTests
    {
        [TestMethod]
        [DataRow("IAR DV 07", "DV07")]
        [DataRow("IAR QA 12", "QA12")]
        [DataRow("IAR QA 01", "QA01")]
        [DataRow("Endur DV 10", "DV10")]
        [DataRow("IAR DV 11", "DV11")]
        [DataRow("DOrc DV 03", "DV03")]
        [DataRow("TEVO DV 11", "DV11")]
        [DataRow("IDF DV 01", "DV01")]
        [DataRow("Endur v18 PR", "v18PR")]
        [DataRow("Endur Sandbox Testing", "SandboxTesting")]
        [DataRow("Endur-QA-27", "QA27")]
        [DataRow("DV 07", "DV07")]
        [DataRow("SingleWord", "SingleWord")]
        [DataRow("local", "local")]
        public void GetShortNameFromEnvironmentName_ReturnsExpectedShortName(string environmentName, string expected)
        {
            var result = VariableScopeOptionsResolver.GetShortNameFromEnvironmentName(environmentName);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void SetGlobalPropertyValues_EmitsFixedOptionsAndServerAndDatabaseTagTokens()
        {
            var propsSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IPropertiesPersistentSource>();
            var serversSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IServersPersistentSource>();
            var daemonsSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IDaemonsPersistentSource>();
            var dbsSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IDatabasesPersistentSource>();
            var permsSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IUserPermsPersistentSource>();
            var containersSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IContainersPersistentSource>();
            var cloudSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.ICloudResourcesPersistentSource>();
            var apiSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IApiRegistrationsPersistentSource>();
            var variableResolver = NSubstitute.Substitute.For<Dorc.Core.VariableResolution.IVariableResolver>();
            var user = new System.Security.Claims.ClaimsPrincipal();

            var stored = new Dictionary<string, Dorc.ApiModel.MonitorRunnerApi.VariableValue>(StringComparer.OrdinalIgnoreCase);
            variableResolver
                .When(r => r.SetRequestSuppliedPropertyValue(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<string>()))
                .Do(ci => stored[ci.ArgAt<string>(0)] = new Dorc.ApiModel.MonitorRunnerApi.VariableValue { Value = ci.ArgAt<string>(1), Type = typeof(string) });
            variableResolver
                .When(r => r.SetPropertyValue(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<string>()))
                .Do(ci => stored[ci.ArgAt<string>(0)] = new Dorc.ApiModel.MonitorRunnerApi.VariableValue { Value = ci.ArgAt<string>(1), Type = typeof(string) });
            variableResolver
                .When(r => r.SetPropertyValue(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<Dorc.ApiModel.MonitorRunnerApi.VariableValue>()))
                .Do(ci => stored[ci.ArgAt<string>(0)] = ci.ArgAt<Dorc.ApiModel.MonitorRunnerApi.VariableValue>(1));

            serversSource.GetServers(user).Returns(new[]
            {
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-STRAT-01", Tags = new[] { "STP Strategies" } },
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-WEB-01", Tags = new[] { "Web Tier" } },
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-WEB-02", Tags = new[] { "Web Tier" } },
                new Dorc.ApiModel.ServerApiModel { Name = "", Tags = new[] { "Web Tier" } },
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-UNTAGGED", Tags = null }
            });

            dbsSource.GetDatabases().Returns(new[]
            {
                new Dorc.ApiModel.DatabaseApiModel { Name = "DB_ENDUR_01", ServerName = "SQL-01", Tags = new[] { "Endur Core" } },
                new Dorc.ApiModel.DatabaseApiModel { Name = "DB_REP_01", ServerName = "SQL-REP-01", Tags = new[] { "Reporting" } },
                new Dorc.ApiModel.DatabaseApiModel { Name = "DB_REP_02", ServerName = "SQL-REP-02", Tags = new[] { "Reporting" } },
                new Dorc.ApiModel.DatabaseApiModel { Name = "", ServerName = "", Tags = new[] { "Reporting" } },
                new Dorc.ApiModel.DatabaseApiModel { Name = "DB_UNTAGGED", ServerName = "SQL-03", Tags = null }
            });

            var resolver = new VariableScopeOptionsResolver(
                propsSource, serversSource, daemonsSource, dbsSource,
                permsSource, containersSource, cloudSource, apiSource);

            resolver.SetGlobalPropertyValues(variableResolver, user);

            Assert.AreEqual("<EnvironmentName>", stored["EnvironmentName"].Value);
            Assert.AreEqual("SRV-STRAT-01", stored["ServerNames_STP_Strategies"].Value);
            CollectionAssert.AreEqual(new[] { "SRV-WEB-01", "SRV-WEB-02" }, (string[])stored["ServerNames_Web_Tier"].Value);
            Assert.AreEqual("SQL-01", stored["DbServer_Endur_Core"].Value);
            Assert.AreEqual("DB_ENDUR_01", stored["DbName_Endur_Core"].Value);
            CollectionAssert.AreEqual(new[] { "SQL-REP-01", "SQL-REP-02" }, (string[])stored["DbServer_Reporting"].Value);
            CollectionAssert.AreEqual(new[] { "DB_REP_01", "DB_REP_02" }, (string[])stored["DbName_Reporting"].Value);

            // Null return branch from sources
            serversSource.GetServers(user).Returns((IEnumerable<Dorc.ApiModel.ServerApiModel>)null!);
            dbsSource.GetDatabases().Returns((IEnumerable<Dorc.ApiModel.DatabaseApiModel>)null!);
            resolver.SetGlobalPropertyValues(variableResolver, user);

            // Duplicate server name for same tag (deduplication branch) and collision branch ("A B" vs "A_B")
            serversSource.GetServers(user).Returns(new[]
            {
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-DUP", Tags = new[] { "Web Tier", "web tier" } }
            });
            resolver.SetGlobalPropertyValues(variableResolver, user);

            stored.Clear();
            serversSource.GetServers(user).Returns(new[]
            {
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-1", Tags = new[] { "A B" } },
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-2", Tags = new[] { "A_B" } },
                new Dorc.ApiModel.ServerApiModel { Name = "SRV-3", Tags = new[] { "A B", "SafeTag" } }
            });
            resolver.SetGlobalPropertyValues(variableResolver, user);
            Assert.IsFalse(stored.ContainsKey("ServerNames_A_B"));
            Assert.AreEqual("SRV-3", stored["ServerNames_SafeTag"].Value);
            Assert.AreEqual("<EnvironmentName>", stored["EnvironmentName"].Value);
        }
    }
}
