using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Consumer re-verification at scale: the tag consumers behave correctly with
    /// large tag sets. Tags are rows now, so there is no joined-length ceiling — the
    /// fixtures deliberately exceed what the old delimited column could have held.
    /// The Contains-substring semantics of the app-server filter are documented,
    /// accepted behaviour — asserted here as-is, not "fixed".
    /// </summary>
    [TestClass]
    public class TagConsumersAtCapacityTests
    {
        private static string[] ManyTags()
        {
            // ~190 tags: past what the old delimited column could hold once joined.
            var tags = Enumerable.Range(0, 190).Select(i => $"tag-{i:D4}-abcdefghijk").ToArray();
            Assert.IsTrue(string.Join(";", tags).Length > TagLimits.MaxTagStringLength - 100);
            return tags;
        }

        [TestMethod]
        public void VariableScopeOptionsResolver_EmitsPerTagVariables_AtNearLimitTagString()
        {
            var tags = ManyTags();

            var properties = Substitute.For<IPropertiesPersistentSource>();
            var servers = Substitute.For<IServersPersistentSource>();
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            var databases = Substitute.For<IDatabasesPersistentSource>();
            var userPerms = Substitute.For<IUserPermsPersistentSource>();
            servers.GetServersForEnvId(42).Returns(new[]
            {
                new ServerApiModel { ServerId = 1, Name = "web01", Tags = tags }
            });
            daemons.GetDaemonsForServer(1).Returns(Array.Empty<DaemonApiModel>());
            databases.GetDatabasesForEnvironmentName(Arg.Any<string>())
                .Returns(Array.Empty<DatabaseApiModel>());

            var resolver = new VariableScopeOptionsResolver(properties, servers, daemons, databases, userPerms);
            var calls = new List<string>();
            var variableResolver = Substitute.For<IVariableResolver>();
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<VariableValue?>()))
                .Do(ci => calls.Add(ci.ArgAt<string>(0)));
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<string>()))
                .Do(ci => calls.Add(ci.ArgAt<string>(0)));

            resolver.SetPropertyValues(variableResolver, new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = "IAR DV 07",
                Details = new EnvironmentDetailsApiModel { FileShare = @"\\share" }
            });

            // Every one of the ~190 tags yields its ServerNames_ variable — the
            // grouping has no hidden length assumptions.
            foreach (var tag in tags)
                CollectionAssert.Contains(calls, $"ServerNames_{tag}");
        }

        [TestMethod]
        public void HasTagContaining_MatchesAFragmentInsideALongerTag()
        {
            // The app-server filter and the Endur post-restore refresh both mean
            // SUBSTRING, not set membership: "appserv" has to match the tag
            // "appserver-node". This is the semantic that silently inverted when tags
            // became an array — tags.Contains(x) went from string.Contains to exact
            // equality with no compiler error — so it is pinned here against the named
            // production rule rather than against a local expression.
            var tags = ManyTags().Append("appserver-node").ToArray();

            Assert.IsTrue(TagString.HasTagContaining(tags, "appserv"));
            Assert.IsTrue(TagString.HasTagContaining(new[] { "endurappsvc_prod_1" }, "endurappsvc_prod"));
        }

        [TestMethod]
        public void HasTag_RequiresTheWholeTag_UnlikeHasTagContaining()
        {
            // The distinction the two methods exist to make explicit: exact membership
            // does NOT match a fragment, so a caller that picks the wrong one changes
            // which servers a deployment resolves to.
            var tags = new[] { "appserver-node" };

            Assert.IsFalse(TagString.HasTag(tags, "appserv"));
            Assert.IsTrue(TagString.HasTagContaining(tags, "appserv"));
        }
    }
}
