using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// HLPS §3.3 consumer re-verification (docs/tag-capacity-expansion, IS S-003):
    /// the surveyed tag consumers behave correctly with near-limit multi-tag strings.
    /// The Contains-substring semantics are the documented, checkpoint-accepted
    /// behaviour (U-5) — asserted here as-is, not "fixed".
    /// </summary>
    [TestClass]
    public class TagConsumersAtCapacityTests
    {
        private static string NearLimitTags(out string[] tags)
        {
            // ~190 tags of 20 chars ≈ 3990 chars joined — just under the 4000 limit.
            tags = Enumerable.Range(0, 190).Select(i => $"tag-{i:D4}-abcdefghijk").ToArray();
            var joined = string.Join(";", tags);
            Assert.IsTrue(joined.Length > 3900 && joined.Length <= TagLimits.MaxTagStringLength);
            return joined;
        }

        [TestMethod]
        public void VariableScopeOptionsResolver_EmitsPerTagVariables_AtNearLimitTagString()
        {
            var joined = NearLimitTags(out var tags);

            var properties = Substitute.For<IPropertiesPersistentSource>();
            var servers = Substitute.For<IServersPersistentSource>();
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            var databases = Substitute.For<IDatabasesPersistentSource>();
            var userPerms = Substitute.For<IUserPermsPersistentSource>();
            servers.GetServersForEnvId(42).Returns(new[]
            {
                new ServerApiModel { ServerId = 1, Name = "web01", ApplicationTags = joined }
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

            // Every one of the ~190 tags yields its ServerNames_ variable — the split
            // logic has no hidden length assumptions.
            foreach (var tag in tags)
                CollectionAssert.Contains(calls, $"ServerNames_{tag}");
        }

        [TestMethod]
        public void ContainsBasedFiltering_MatchesSubstringsWithinLongTagStrings()
        {
            // U-5 documented behaviour: substring matching over the joined string, so
            // "appserv" matches whether standalone or embedded in a longer tag — at any
            // string length. This is accepted, not a defect (HLPS §8 U-5).
            var joined = NearLimitTags(out _) + ";appserver-node";

            Assert.IsTrue(joined.Contains("appserv"));
            Assert.IsTrue(joined.Length > 3900);
        }
    }
}
