using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// docs/database-tags IS S-001 characterization, updated by S-003: the declared
    /// flip candidates (multi-tag misses) now assert tag-membership semantics; the
    /// U-1 duplicate/shared-tag throws and site 3's omitted-means-no-filter
    /// behaviour survive unchanged from the freeze.
    /// </summary>
    [TestClass]
    public class DatabaseTypeSourceCharacterizationTests
    {
        private static IDeploymentContextFactory FactoryFor(IDeploymentContext context)
        {
            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(context);
            return factory;
        }

        private static DatabasesPersistentSource DatabasesSource(IDeploymentContext context) =>
            new(FactoryFor(context),
                Substitute.For<IRolePrivilegesChecker>(),
                Substitute.For<IClaimsPrincipalReader>());

        // ---- Sites 1 & 4: the two GetDatabaseByType overloads ----

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
        public void GetDatabaseByType_EnvironmentOverload_UsesTagMembership()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", Type = "Endur", ServerName = "s1" },
                new Database { Id = 2, Name = "M1", Type = "Endur;Reporting", ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            // FLIPPED by S-003: "Endur" now matches the multi-tag row too, so the
            // shared tag throws (U-1); the unique tag resolves the multi-tag row.
            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByType(env, "Endur"));
            Assert.AreEqual("M1", source.GetDatabaseByType(env, "Reporting").Name);
        }

        [TestMethod]
        public void GetDatabaseByType_EnvNameOverload_UsesTagMembership()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", Type = "Endur", ServerName = "s1" },
                new Database { Id = 2, Name = "M1", Type = "Endur;Reporting", ServerName = "s2" });
            var source = DatabasesSource(context);

            // FLIPPED by S-003: same tag-membership semantics as the EF overload.
            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByType("Endur DV 10", "Endur"));
            Assert.AreEqual("M1", source.GetDatabaseByType("Endur DV 10", "Reporting")!.Name);
        }

        [TestMethod]
        public void GetDatabaseByType_BothOverloads_ThrowOnDuplicateType()
        {
            // U-1 kept behaviour — S-003 must NOT flip these.
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "A", Type = "Endur", ServerName = "s1" },
                new Database { Id = 2, Name = "B", Type = "Endur", ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByType(env, "Endur"));
            Assert.Throws<InvalidOperationException>(() => source.GetDatabaseByType("Endur DV 10", "Endur"));
        }

        // ---- Site 2: Endur users join ----

        [TestMethod]
        public void GetEnvironmentUsers_MatchesEndurByTagMembership()
        {
            var context = Substitute.For<IDeploymentContext>();
            var env = new Environment { Id = 42, Name = "Endur DV 10" };
            var endurDb = new Database { Id = 10, Name = "D1", Type = "Endur" };
            var multiTagDb = new Database { Id = 11, Name = "D2", Type = "Endur;Ops" };
            endurDb.Environments = new List<Environment> { env };
            multiTagDb.Environments = new List<Environment> { env };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Database> { endurDb, multiTagDb });
            var userSet = DbContextMock.GetQueryableMockDbSet(new List<User>
            {
                new() { Id = 1, LanId = "endur.user", LoginId = "endur.user" },
                new() { Id = 2, LanId = "multi.user", LoginId = "multi.user" }
            });
            var envUserSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentUser>
            {
                new() { DbId = 10, UserId = 1, PermissionId = 5 },
                new() { DbId = 11, UserId = 2, PermissionId = 5 }
            });
            context.Environments.Returns(envSet);
            context.Databases.Returns(dbSet);
            context.Users.Returns(userSet);
            context.EnvironmentUsers.Returns(envUserSet);
            var source = new UsersPersistentSource(FactoryFor(context), Substitute.For<IClaimsPrincipalReader>());

            var users = source.GetEnvironmentUsers(42, UserAccountType.Endur).ToList();

            // FLIPPED by S-003: the multi-tag database now carries the "Endur" tag,
            // so its user is visible alongside the single-value one.
            Assert.AreEqual(2, users.Count);
            CollectionAssert.AreEquivalent(new[] { "endur.user", "multi.user" },
                users.Select(u => u.LanId).ToArray());
        }

        // ---- Site 3: permissions dbType filter ----

        private static UserPermsPersistentSource PermsSourceWithOneMultiTagDb(out IDeploymentContext context)
        {
            context = Substitute.For<IDeploymentContext>();
            var db = new Database { Id = 10, Name = "db1", ServerName = "s1", Type = "Endur;Ops" };
            var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Database> { db });
            var userSet = DbContextMock.GetQueryableMockDbSet(new List<User>
            {
                new() { Id = 1, LoginId = "u1", LoginType = "Endur" }
            });
            var envUserSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentUser>
            {
                new() { DbId = 10, UserId = 1, PermissionId = 5 }
            });
            var permSet = DbContextMock.GetQueryableMockDbSet(new List<Permission>
            {
                new() { Id = 5, Name = "dbo", DisplayName = "Owner" }
            });
            context.Databases.Returns(dbSet);
            context.Users.Returns(userSet);
            context.EnvironmentUsers.Returns(envUserSet);
            context.Permissions.Returns(permSet);
            return new UserPermsPersistentSource(FactoryFor(context));
        }

        [TestMethod]
        public void GetUserDbPermissions_OmittedDbType_AppliesNoFilter()
        {
            // Kept behaviour: an omitted optional dbType means "no filter" (IS S-004
            // reconciliation — S-003/S-004 must NOT change this).
            var source = PermsSourceWithOneMultiTagDb(out _);
            Assert.AreEqual(1, source.GetUserDbPermissions("s1", "db1").Count);
            Assert.AreEqual(1, source.GetUserDbPermissions("s1", "db1", null).Count);
        }

        [TestMethod]
        public void GetUserDbPermissions_FilterUsesTagMembership()
        {
            var source = PermsSourceWithOneMultiTagDb(out _);
            // FLIPPED by S-003: the filter is per-tag membership now.
            Assert.AreEqual(1, source.GetUserDbPermissions("s1", "db1", "Endur").Count);
            Assert.AreEqual(1, source.GetUserDbPermissions("s1", "db1", "Ops").Count);
            Assert.AreEqual(0, source.GetUserDbPermissions("s1", "db1", "Endu").Count);
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

            var hit = PropertiesSourceFor(new Database { Id = 1, Name = "END_DV10_DB", Type = "Endur" })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(hit, "ENDUR_END_DV10.cfg");

            // FLIPPED by S-003: a multi-tag row carrying "Endur" resolves the path.
            var multiTag = PropertiesSourceFor(new Database { Id = 1, Name = "END_DV10_DB", Type = "Endur;Ops" })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(multiTag, "ENDUR_END_DV10.cfg");
        }

        // ---- Null-Type rows are excluded everywhere (gate F-2) ----

        [TestMethod]
        public void NullTypeDatabases_AreExcludedFromTagLookups()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "N1", Type = null, ServerName = "s1" },
                new Database { Id = 2, Name = "E1", Type = "Endur", ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            // Sites 1 & 4: the null-Type row neither matches nor causes a duplicate.
            Assert.AreEqual("E1", source.GetDatabaseByType(env, "Endur").Name);
            Assert.AreEqual("E1", source.GetDatabaseByType("Endur DV 10", "Endur")!.Name);
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
                    new Database { Id = 1, Name = "X1", Type = null },
                    new Database { Id = 2, Name = "END_DV10_DB", Type = "Endur" })
                .GetConfigurationFilePath(environment);
            StringAssert.Contains(hit, "ENDUR_END_DV10.cfg");

            // Site 5, U-1 collision-throw fixture (gate F-1): two databases carrying
            // the Endur tag still throw.
            Assert.Throws<InvalidOperationException>(() =>
                PropertiesSourceFor(
                        new Database { Id = 1, Name = "A_DB", Type = "Endur;Ops" },
                        new Database { Id = 2, Name = "B_DB", Type = "Endur" })
                    .GetConfigurationFilePath(environment));
        }

        [TestMethod]
        public void NullTypeDatabases_AreInvisibleToUsersJoinAndPermsFilter()
        {
            // Sites 2 & 3 (gate F-2): a null-Type database matches no tag.
            var context = Substitute.For<IDeploymentContext>();
            var env = new Environment { Id = 42, Name = "Endur DV 10" };
            var nullDb = new Database { Id = 12, Name = "D3", ServerName = "s1", Type = null };
            nullDb.Environments = new List<Environment> { env };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Database> { nullDb });
            var userSet = DbContextMock.GetQueryableMockDbSet(new List<User>
            {
                new() { Id = 3, LanId = "null.user", LoginId = "null.user" }
            });
            var envUserSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentUser>
            {
                new() { DbId = 12, UserId = 3, PermissionId = 5 }
            });
            var permSet = DbContextMock.GetQueryableMockDbSet(new List<Permission>
            {
                new() { Id = 5, Name = "dbo", DisplayName = "Owner" }
            });
            context.Environments.Returns(envSet);
            context.Databases.Returns(dbSet);
            context.Users.Returns(userSet);
            context.EnvironmentUsers.Returns(envUserSet);
            context.Permissions.Returns(permSet);

            var usersSource = new UsersPersistentSource(FactoryFor(context), Substitute.For<IClaimsPrincipalReader>());
            Assert.AreEqual(0, usersSource.GetEnvironmentUsers(42, UserAccountType.Endur).Count());

            var permsSource = new UserPermsPersistentSource(FactoryFor(context));
            Assert.AreEqual(0, permsSource.GetUserDbPermissions("s1", "D3", "Endur").Count);
            Assert.AreEqual(1, permsSource.GetUserDbPermissions("s1", "D3").Count);
        }

        // ---- Padded legacy rows: the EF pattern vs in-memory tokenization ----

        [TestMethod]
        public void PaddedEntries_MissAtTheEfPattern_ButMatchInMemory()
        {
            // The delimiter-wrap pattern is exact about whitespace: a legacy padded
            // entry ("Endur ;Ops") misses at EF sites until the one-time U-2
            // normalization runs. In-memory sites trim during tokenization and are
            // immune. Both behaviours are by design (HLPS §3, R-4).
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "P1", Type = "Endur ;Ops", ServerName = "s1" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            Assert.IsNull(source.GetDatabaseByType(env, "Endur"));            // EF pattern: miss
            Assert.AreEqual("P1", source.GetDatabaseByType(env, "Ops").Name); // unpadded entry: hit
            Assert.AreEqual("P1", source.GetDatabaseByType("Endur DV 10", "Endur")!.Name); // in-memory: trimmed hit
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
            { Name = "N1", ServerName = "s9", Type = " Endur ; Ops ;; Endur " });

            Assert.AreEqual("Endur;Ops", addedDatabases.Single().Type);
        }
    }
}
