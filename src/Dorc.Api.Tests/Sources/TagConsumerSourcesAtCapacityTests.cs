using Dorc.Api.Tests.Mocks;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// HLPS §3.3 consumer re-verification, source-level half (tag-capacity IS S-003;
    /// gate round-1 findings 1-2): the surveyed persistent-source consumers are
    /// exercised with near-limit (~3,989-char) tag strings, not just string helpers.
    /// </summary>
    [TestClass]
    public class TagConsumerSourcesAtCapacityTests
    {
        private static string NearLimitTags(string mustInclude = "")
        {
            var count = mustInclude.Length > 0 ? 188 : 190;
            var tags = Enumerable.Range(0, count).Select(i => $"tag-{i:D4}-abcdefghijk");
            var joined = string.Join(";", tags) + (mustInclude.Length > 0 ? ";" + mustInclude : "");
            Assert.IsTrue(joined.Length > 3900 && joined.Length <= 4000);
            return joined;
        }

        [TestMethod]
        public void DaemonsSource_GetServersForDaemon_ProjectsNearLimitTagsUnmodified()
        {
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            var context = Substitute.For<IDeploymentContext>();
            contextFactory.GetContext().Returns(context);

            var joined = NearLimitTags();
            var daemonList = new List<Daemon>
            {
                new()
                {
                    Id = 7,
                    Name = "svc",
                    Server = new List<Server>
                    {
                        new() { Id = 1, Name = "web01", OsName = "w", ApplicationTags = joined }
                    }
                }
            };
            var daemonSet = DbContextMock.GetQueryableMockDbSet(daemonList);
            context.Daemons.Returns(daemonSet);

            var source = new DaemonsPersistentSource(
                contextFactory, Substitute.For<IDaemonObservationPersistentSource>());

            var servers = source.GetServersForDaemon(7).ToList();

            Assert.AreEqual(1, servers.Count);
            Assert.AreEqual(joined, servers[0].ApplicationTags);
        }

        [TestMethod]
        public void ServersSource_GetAppServerDetails_FiltersOnAppservWithinNearLimitTags()
        {
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            var context = Substitute.For<IDeploymentContext>();
            contextFactory.GetContext().Returns(context);

            var matching = new Server
            { Id = 1, Name = "app01", ApplicationTags = NearLimitTags("appserver-node") };
            var nonMatching = new Server
            { Id = 2, Name = "web01", ApplicationTags = NearLimitTags() };
            var envList = new List<Environment>
            {
                new() { Id = 5, Name = "DV 01", Servers = new List<Server> { matching, nonMatching } }
            };
            var envSet = DbContextMock.GetQueryableMockDbSet(envList);
            context.Environments.Returns(envSet);

            var source = new ServersPersistentSource(
                contextFactory,
                Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>(),
                Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>());

            var result = source.GetAppServerDetails("DV 01").ToList();

            // The Contains("appserv") substring filter matches the embedded
            // "appserver-node" tag — the documented U-5 semantics — at near-limit length.
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("app01", result[0].Name);
        }
    }
}
