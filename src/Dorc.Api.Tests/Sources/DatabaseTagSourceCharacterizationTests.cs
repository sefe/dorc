using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// Characterization of the tag consumers in the persistent sources. Values that
    /// used to miss because they carried several tags now assert tag-membership
    /// semantics; the shared-tag throw is kept behaviour and must survive.
    /// </summary>
    [TestClass]
    public class DatabaseTagSourceCharacterizationTests
    {

        /// <summary>Tag rows for a Database fixture — tags are stored as rows now.</summary>
        private static List<DatabaseTag> DbTags(params string[] tags) =>
            tags.Select(t => new DatabaseTag { Tag = t }).ToList();
        private static IDeploymentContextFactory FactoryFor(IDeploymentContext context)
        {
            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(context);
            return factory;
        }

        private static DatabasesPersistentSource DatabasesSource(
            IDeploymentContext context,
            IDatabaseAccessReader? databaseAccessReader = null) =>
            new(FactoryFor(context),
                Substitute.For<IRolePrivilegesChecker>(),
                Substitute.For<IClaimsPrincipalReader>(),
                databaseAccessReader ?? Substitute.For<IDatabaseAccessReader>());

        // ---- Sites 1 & 4: the two GetDatabaseByTag overloads ----

        private static IDeploymentContext ContextWithEnvironmentDatabases(params Database[] databases)
        {
            var context = Substitute.For<IDeploymentContext>();
            var env = new Environment { Id = 42, Name = "Endur DV 10", Databases = databases.ToList() };
            foreach (var db in databases)
                db.Environments = new List<Environment> { env };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            var dbSet = DbContextMock.GetQueryableMockDbSet(databases.ToList());
            context.Environments.Returns(envSet);
            context.Databases.Returns(dbSet);
            return context;
        }

        [TestMethod]
        public void UpdateDatabase_OmittedOrNullType_PreservesStoredEngine()
        {
            foreach (var payload in new[]
            {
                """{"Id":1,"Name":"analytics","ServerName":"project/service"}""",
                """{"Id":1,"Name":"analytics","ServerName":"project/service","DatabaseType":null}"""
            })
            {
                var existing = new Database
                {
                    Id = 1, Name = "analytics", ServerName = "project/service",
                    DatabaseType = DatabaseType.ClickHouse
                };
                var context = ContextWithEnvironmentDatabases(existing);
                var groups = DbContextMock.GetQueryableMockDbSet(new List<AdGroup>());
                context.AdGroups.Returns(groups);
                var input = System.Text.Json.JsonSerializer.Deserialize<DatabaseApiModel>(payload)!;

                var result = DatabasesSource(context).UpdateDatabase(1, input, new System.Security.Claims.ClaimsPrincipal());

                Assert.AreEqual(DatabaseType.ClickHouse, existing.DatabaseType);
                Assert.AreEqual(DatabaseType.ClickHouse, result!.DatabaseType);
                context.Received(1).SaveChanges();
            }
        }

        [TestMethod]
        public void UpdateDatabase_ExplicitSqlServer_ReplacesStoredEngine()
        {
            var existing = new Database
            {
                Id = 1, Name = "analytics", ServerName = "project/service",
                DatabaseType = DatabaseType.ClickHouse
            };
            var context = ContextWithEnvironmentDatabases(existing);
            var groups = DbContextMock.GetQueryableMockDbSet(new List<AdGroup>());
            context.AdGroups.Returns(groups);

            DatabasesSource(context).UpdateDatabase(1, new DatabaseApiModel
            {
                Id = 1, Name = existing.Name, ServerName = existing.ServerName,
                DatabaseType = DatabaseType.SqlServer
            }, new System.Security.Claims.ClaimsPrincipal());

            Assert.AreEqual(DatabaseType.SqlServer, existing.DatabaseType);
        }

        [TestMethod]
        public void AddDatabase_DefaultsToSqlServer_AndPersistsExplicitEngine()
        {
            foreach (var engine in new DatabaseType?[] { null, DatabaseType.CosmosDb })
            {
                var context = ContextWithEnvironmentDatabases();
                var added = new List<Database>();
                context.Databases.When(d => d.Add(Arg.Any<Database>())).Do(ci => added.Add(ci.Arg<Database>()));
                var groups = DbContextMock.GetQueryableMockDbSet(new List<AdGroup>());
                context.AdGroups.Returns(groups);

                DatabasesSource(context).AddDatabase(new DatabaseApiModel
                {
                    Name = "db", ServerName = "server", DatabaseType = engine
                });

                Assert.AreEqual(engine ?? DatabaseType.SqlServer, added.Single().DatabaseType);
            }
        }

        [TestMethod]
        public void GetDatabaseByTag_EnvironmentOverload_UsesTagMembership()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", TagLinks = DbTags("Endur"), ServerName = "s1" },
                new Database { Id = 2, Name = "M1", TagLinks = DbTags("Endur", "Reporting"), ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            // FLIPPED by S-003: "Endur" now matches the multi-tag row too, so the
            // A shared tag throws; a unique tag resolves the multi-tag row.
            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByTag(env, "Endur"));
            Assert.AreEqual("M1", source.GetDatabaseByTag(env, "Reporting").Name);
        }

        [TestMethod]
        public void GetDatabaseByTag_EnvNameOverload_UsesTagMembership()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", TagLinks = DbTags("Endur"), ServerName = "s1" },
                new Database { Id = 2, Name = "M1", TagLinks = DbTags("Endur", "Reporting"), ServerName = "s2" });
            var source = DatabasesSource(context);

            // FLIPPED by S-003: same tag-membership semantics as the EF overload.
            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByTag("Endur DV 10", "Endur"));
            Assert.AreEqual("M1", source.GetDatabaseByTag("Endur DV 10", "Reporting")!.Name);
        }

        [TestMethod]
        public void GetDatabaseByTag_BothOverloads_ThrowOnSharedTag()
        {
            // Kept behaviour — tag membership must NOT flip these, and the error
            // message must identify the environment, duplicate tag, and databases.
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "A", TagLinks = DbTags("Endur"), ServerName = "s1" },
                new Database { Id = 2, Name = "B", TagLinks = DbTags("Endur"), ServerName = null });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            var envEx = Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByTag(env, "Endur"));
            StringAssert.Contains(envEx.Message, "Endur DV 10");
            StringAssert.Contains(envEx.Message, "'Endur'");
            StringAssert.Contains(envEx.Message, "'A' on 's1'");
            StringAssert.Contains(envEx.Message, "'B'");

            var nameEx = Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByTag("Endur DV 10", "Endur"));
            StringAssert.Contains(nameEx.Message, "Endur DV 10");
            StringAssert.Contains(nameEx.Message, "'Endur'");
            StringAssert.Contains(nameEx.Message, "'A' on 's1'");
            StringAssert.Contains(nameEx.Message, "'B'");
        }

        // ---- Site 5: configuration file path ----

        private static PropertiesPersistentSource PropertiesSourceFor(params Database[] databases)
        {
            var context = Substitute.For<IDeploymentContext>();
            var env = new Environment { Id = 42, Name = "Endur DV 10", Databases = databases.ToList() };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            context.Environments.Returns(envSet);
            return new PropertiesPersistentSource(FactoryFor(context),
                Substitute.For<ILogger<PropertiesPersistentSource>>());
        }

        [TestMethod]
        public void GetConfigurationFilePath_ResolvesFromEndurTagMembership()
        {
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = "Endur DV 10",
                Details = new EnvironmentDetailsApiModel { FileShare = @"\\share" }
            };

            var hit = PropertiesSourceFor(new Database { Id = 1, Name = "END_DV10_DB", TagLinks = DbTags("Endur") })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(hit, "ENDUR_END_DV10.cfg");

            // FLIPPED by S-003: a multi-tag row carrying "Endur" resolves the path.
            var multiTag = PropertiesSourceFor(new Database { Id = 1, Name = "END_DV10_DB", TagLinks = DbTags("Endur", "Ops") })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(multiTag, "ENDUR_END_DV10.cfg");
        }

        // ---- Null-Type rows are excluded everywhere (gate F-2) ----

        [TestMethod]
        public void NullTypeDatabases_AreExcludedFromTagLookups()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "N1", TagLinks = DbTags(), ServerName = "s1" },
                new Database { Id = 2, Name = "E1", TagLinks = DbTags("Endur"), ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            // Sites 1 & 4: the null-Type row neither matches nor causes a duplicate.
            Assert.AreEqual("E1", source.GetDatabaseByTag(env, "Endur").Name);
            Assert.AreEqual("E1", source.GetDatabaseByTag("Endur DV 10", "Endur")!.Name);
        }

        [TestMethod]
        public void GetConfigurationFilePath_NullTypeExcluded_AndSharedTagThrows()
        {
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 42,
                EnvironmentName = "Endur DV 10",
                Details = new EnvironmentDetailsApiModel { FileShare = @"\\share" }
            };

            // Site 5, null-Type row excluded (gate F-2).
            var hit = PropertiesSourceFor(
                    new Database { Id = 1, Name = "X1", Tags = null },
                    new Database { Id = 2, Name = "END_DV10_DB", TagLinks = DbTags("Endur") })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(hit, "ENDUR_END_DV10.cfg");

            // Collision-throw fixture: two databases carrying
            // the Endur tag still throw with an actionable message naming the environment,
            // tag, conflicting databases, and EndurConfigurationFile.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PropertiesSourceFor(
                        new Database { Id = 1, Name = "A_DB", ServerName = "srv1", TagLinks = DbTags("Endur", "Ops") },
                        new Database { Id = 2, Name = "B_DB", ServerName = null, TagLinks = DbTags("Endur") })
                    .GetConfigurationFilePath(environment));
            StringAssert.Contains(ex.Message, "Endur DV 10");
            StringAssert.Contains(ex.Message, "'Endur'");
            StringAssert.Contains(ex.Message, "'A_DB' on 'srv1'");
            StringAssert.Contains(ex.Message, "'B_DB'");
            StringAssert.Contains(ex.Message, "EndurConfigurationFile");
        }

        // ---- Padding can no longer make a row unmatchable ----

        [TestMethod]
        public void PaddedInput_IsTrimmedOnWrite_SoBothLookupsAgree()
        {
            // The old delimited column had an asymmetry worth a test: a padded entry
            // ("Endur ;Ops") missed at the EF sites, because the delimiter-wrap LIKE
            // was exact about whitespace, while in-memory sites trimmed and matched.
            // Rows remove the asymmetry at the source - every entry point trims, so a
            // padded tag never reaches storage and both lookups see the same thing.
            var context = ContextWithEnvironmentDatabases();
            var added = new List<Database>();
            context.Databases.When(d => d.Add(Arg.Any<Database>())).Do(ci => added.Add(ci.Arg<Database>()));
            var adGroupSet = DbContextMock.GetQueryableMockDbSet(new List<AdGroup>());
            context.AdGroups.Returns(adGroupSet);

            DatabasesSource(context).AddDatabase(new DatabaseApiModel
            {
                Name = "P1", ServerName = "s1", Tags = new[] { "  Endur  ", "Ops" }
            });

            CollectionAssert.AreEqual(new[] { "Endur", "Ops" },
                added.Single().TagLinks.Select(t => t.Tag).ToArray());
        }

        [TestMethod]
        public void WriteNormalization_TrimsDedupsAndPreservesOrder()
        {
            var context = ContextWithEnvironmentDatabases();
            var addedDatabases = new List<Database>();
            context.Databases.When(x => x.Add(Arg.Any<Database>()))
                .Do(call => addedDatabases.Add(call.Arg<Database>()));
            var adGroupSet = DbContextMock.GetQueryableMockDbSet(new List<AdGroup>());
            context.AdGroups.Returns(adGroupSet);
            var source = DatabasesSource(context);

            source.AddDatabase(new DatabaseApiModel
            { Name = "N1", ServerName = "s9", Tags = new[] { "Endur", "Ops", "", "Endur" } });

            // Rows are the source of truth; the deprecated column is dual-written
            // until the follow-up release drops it, so both must agree.
            CollectionAssert.AreEqual(new[] { "Endur", "Ops" },
                addedDatabases.Single().TagLinks.Select(t => t.Tag).ToArray());
            Assert.AreEqual("Endur;Ops", addedDatabases.Single().Tags);
        }
    }
}
