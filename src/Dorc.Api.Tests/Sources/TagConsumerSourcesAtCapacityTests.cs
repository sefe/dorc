using Dorc.Api.Tests.Mocks;
using Dorc.PersistentData.Contexts;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Consumer re-verification at scale, source-level half: the persistent-source
    /// tag consumers are exercised with large tag sets, not just the string helpers.
    /// The old delimited column capped the joined value at 4,000 characters; rows
    /// have no equivalent ceiling, so these fixtures deliberately exceed it.
    /// </summary>
    [TestClass]
    public class TagConsumerSourcesAtCapacityTests
    {
        private static string[] ManyTags(string mustInclude = "")
        {
            var tags = Enumerable.Range(0, 190).Select(i => $"tag-{i:D4}-abcdefghijk").ToList();
            if (mustInclude.Length > 0)
                tags.Add(mustInclude);

            // Past what the delimited column could have held, which is the point.
            Assert.IsTrue(string.Join(";", tags).Length > TagLimits.MaxTagStringLength - 100);
            return tags.ToArray();
        }

        private static List<ServerTag> ServerTagsFor(int serverId, string[] tags) =>
            tags.Select(t => new ServerTag { ServerId = serverId, Tag = t }).ToList();

        [TestMethod]
        public void DaemonsSource_GetServersForDaemon_ProjectsEveryTagRow()
        {
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            var context = Substitute.For<IDeploymentContext>();
            contextFactory.GetContext().Returns(context);

            var tags = ManyTags();
            var daemonList = new List<Daemon>
            {
                new()
                {
                    Id = 7,
                    Name = "svc",
                    Server = new List<Server>
                    {
                        new()
                        {
                            Id = 1, Name = "web01", OsName = "w",
                            TagLinks = tags.Select(t => new ServerTag { ServerId = 1, Tag = t }).ToList()
                        }
                    }
                }
            };
            var daemonSet = DbContextMock.GetQueryableMockDbSet(daemonList);
            context.Daemons.Returns(daemonSet);

            var source = new DaemonsPersistentSource(
                contextFactory, Substitute.For<IDaemonObservationPersistentSource>());

            var servers = source.GetServersForDaemon(7).ToList();

            Assert.AreEqual(1, servers.Count);
            CollectionAssert.AreEqual(tags, servers[0].Tags);
        }

        [TestMethod]
        public void ServersSource_GetAppServerDetails_FiltersOnAppservWithinNearLimitTags()
        {
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            var context = Substitute.For<IDeploymentContext>();
            contextFactory.GetContext().Returns(context);

            var matching = new Server
            { Id = 1, Name = "app01", TagLinks = ServerTagsFor(1, ManyTags("appserver-node")) };
            var nonMatching = new Server
            { Id = 2, Name = "web01", TagLinks = ServerTagsFor(2, ManyTags()) };
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

            // The Contains("appserv") substring filter matches the "appserver-node"
            // tag — the documented semantics — within a large tag set.
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("app01", result[0].Name);
        }
    }
}
