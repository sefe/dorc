using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// docs/database-tags IS S-001: characterization freeze of the resolver's
    /// database-Type consumers (survey sites 6-7) on today's whole-string semantics.
    /// The multi-tag and null-Type assertions are the two declared flip candidates for
    /// S-003; the duplicate-Type throw is U-1 kept behaviour and must survive.
    /// </summary>
    [TestClass]
    public class DatabaseTypeResolverCharacterizationTests
    {
        private static (VariableScopeOptionsResolver resolver, IVariableResolver variableResolver,
            List<string> calls, Dictionary<string, VariableValue?> values)
            CreateResolver(params DatabaseApiModel[] databases)
        {
            var properties = Substitute.For<IPropertiesPersistentSource>();
            var servers = Substitute.For<IServersPersistentSource>();
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            var databasesSource = Substitute.For<IDatabasesPersistentSource>();
            var userPerms = Substitute.For<IUserPermsPersistentSource>();
            servers.GetServersForEnvId(42).Returns(Array.Empty<ServerApiModel>());
            databasesSource.GetDatabasesForEnvironmentName(Arg.Any<string>()).Returns(databases);

            var resolver = new VariableScopeOptionsResolver(properties, servers, daemons, databasesSource, userPerms);
            var calls = new List<string>();
            var values = new Dictionary<string, VariableValue?>();
            var variableResolver = Substitute.For<IVariableResolver>();
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<VariableValue?>()))
                .Do(ci =>
                {
                    calls.Add(ci.ArgAt<string>(0));
                    values[ci.ArgAt<string>(0)] = ci.ArgAt<VariableValue?>(1);
                });
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<string>()))
                .Do(ci => calls.Add(ci.ArgAt<string>(0)));
            return (resolver, variableResolver, calls, values);
        }

        private static EnvironmentApiModel Environment42() => new()
        {
            EnvironmentId = 42,
            EnvironmentName = "Endur DV 10",
            Details = new EnvironmentDetailsApiModel { FileShare = @"\\share" }
        };

        [TestMethod]
        public void SingleValueTypes_EmitBothPerTypeFamilies_WithScalarAndArrayShapes()
        {
            var (resolver, variableResolver, calls, values) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "R1", Type = "Endur Reporting", ServerName = "s1" },
                new DatabaseApiModel { Id = 2, Name = "X1", Type = "Endur External", ServerName = "s2" },
                new DatabaseApiModel { Id = 3, Name = "W1", Type = "Warehouse", ServerName = "s3" },
                new DatabaseApiModel { Id = 4, Name = "W2", Type = "Warehouse", ServerName = "s4" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            // Site 6: fixed lookups hit on exact whole-string equality.
            CollectionAssert.Contains(calls, "ReportingDatabaseName");
            CollectionAssert.Contains(calls, "ExternalDatabaseName");

            // Site 7: per-type variables in BOTH families; spaces become underscores.
            CollectionAssert.Contains(calls, "DbServer_Endur_Reporting");
            CollectionAssert.Contains(calls, "DbName_Endur_Reporting");
            CollectionAssert.Contains(calls, "DbServer_Warehouse");
            CollectionAssert.Contains(calls, "DbName_Warehouse");

            // Scalar when exactly one database carries the type, array when several.
            Assert.IsInstanceOfType(values["DbServer_Endur_Reporting"]!.Value, typeof(string));
            Assert.IsInstanceOfType(values["DbServer_Warehouse"]!.Value, typeof(string[]));
            CollectionAssert.AreEqual(new[] { "W1", "W2" }, (string[])values["DbName_Warehouse"]!.Value);
        }

        [TestMethod]
        public void MultiTagType_SatisfiesEveryTag_AndEmitsPerTagVariables()
        {
            // FLIPPED by S-003 (declared flip candidate): a semicolon value is now a
            // tag list — each tag matches, and no joined-name variable is emitted.
            var (resolver, variableResolver, calls, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "R1", Type = "Endur;Endur Reporting", ServerName = "s1" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            CollectionAssert.Contains(calls, "ReportingDatabaseName");
            CollectionAssert.Contains(calls, "DbServer_Endur");
            CollectionAssert.Contains(calls, "DbServer_Endur_Reporting");
            CollectionAssert.Contains(calls, "DbName_Endur");
            CollectionAssert.DoesNotContain(calls, "DbServer_Endur;Endur_Reporting");
        }

        [TestMethod]
        public void NullType_ContributesNothing_AndNoLongerThrows()
        {
            // FLIPPED by S-003 (declared flip candidate): null means "no tags" — the
            // database is skipped instead of crashing variable resolution.
            var (resolver, variableResolver, calls, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "N1", Type = null, ServerName = "s1" },
                new DatabaseApiModel { Id = 2, Name = "W1", Type = "Warehouse", ServerName = "s2" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            CollectionAssert.Contains(calls, "DbServer_Warehouse");
            Assert.IsFalse(calls.Any(c => c.StartsWith("DbServer_N1") || c == "DbServer_"));
        }

        [TestMethod]
        public void SharedTagAcrossDatabases_ThrowsAtFixedLookups_AndEmitsArrayInLoop()
        {
            // U-1 with tag semantics: a shared *resolution* tag still throws at the
            // fixed lookups; the per-tag loop handles sharing with the array shape.
            var (resolver, variableResolver, _, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "R1", Type = "Endur Reporting;Extra", ServerName = "s1" },
                new DatabaseApiModel { Id = 2, Name = "R2", Type = "Endur Reporting", ServerName = "s2" });

            Assert.Throws<InvalidOperationException>(
                () => resolver.SetPropertyValues(variableResolver, Environment42()));

            // The same sharing on a non-resolution tag emits the array shape.
            var (resolver2, variableResolver2, calls2, values2) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "W1", Type = "Warehouse;Extra", ServerName = "s1" },
                new DatabaseApiModel { Id = 2, Name = "W2", Type = "Warehouse", ServerName = "s2" });
            resolver2.SetPropertyValues(variableResolver2, Environment42());
            CollectionAssert.Contains(calls2, "DbServer_Warehouse");
            Assert.IsInstanceOfType(values2["DbServer_Warehouse"]!.Value, typeof(string[]));
            Assert.IsInstanceOfType(values2["DbServer_Extra"]!.Value, typeof(string));
        }

        [TestMethod]
        public void CaseDifferingTags_SurviveOrdinalDedup_AndEmitSeparately()
        {
            // U-5: tokenization is Ordinal — "Endur" and "endur" are distinct tags,
            // matching today's behaviour for two whole-Type strings differing by case.
            var (resolver, variableResolver, calls, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "D1", Type = "Warehouse;warehouse", ServerName = "s1" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            CollectionAssert.Contains(calls, "DbServer_Warehouse");
            CollectionAssert.Contains(calls, "DbServer_warehouse");
        }

        [TestMethod]
        public void PaddedEntries_AreTrimmedByTokenization_InMemory()
        {
            var (resolver, variableResolver, calls, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "D1", Type = " Warehouse ; Extra ", ServerName = "s1" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            CollectionAssert.Contains(calls, "DbServer_Warehouse");
            CollectionAssert.Contains(calls, "DbServer_Extra");
        }

        [TestMethod]
        public void DatabasePermissions_CarryTheRawJoinedTypeVerbatim()
        {
            // HLPS §3 position: DatabaseDefinition.Type passes through unmodified.
            var (resolver, variableResolver, _, values) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "D1", Type = "Endur;Extra", ServerName = "s1" });

            resolver.SetPropertyValues(variableResolver, Environment42());

            var perms = (VariableValueDbPerm[])values["DatabasePermissions"]!.Value;
            Assert.AreEqual("Endur;Extra", perms.Single().Database.Type);
        }

        [TestMethod]
        public void DuplicateWholeType_AtFixedLookups_Throws()
        {
            // U-1 kept behaviour: SingleOrDefault throws when two databases in one
            // environment share the looked-up type. S-003 must NOT flip this.
            var (resolver, variableResolver, _, _) = CreateResolver(
                new DatabaseApiModel { Id = 1, Name = "R1", Type = "Endur Reporting", ServerName = "s1" },
                new DatabaseApiModel { Id = 2, Name = "R2", Type = "Endur Reporting", ServerName = "s2" });

            Assert.Throws<InvalidOperationException>(
                () => resolver.SetPropertyValues(variableResolver, Environment42()));
        }
    }
}
