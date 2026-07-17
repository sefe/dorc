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
    /// docs/database-tags IS S-001: characterization freeze of the persistent-source
    /// database-Type consumers (survey sites 1-5) on today's whole-string semantics.
    /// Multi-tag misses are the declared flip candidates for S-003; duplicate-Type
    /// throws (U-1) and omitted-filter behaviour (site 3) must survive the rewrite.
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
        public void GetDatabaseByType_EnvironmentOverload_MatchesWholeStringOnly()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", Type = "Endur", ServerName = "s1" },
                new Database { Id = 2, Name = "M1", Type = "Endur;Reporting", ServerName = "s2" });
            var source = DatabasesSource(context);
            var env = new EnvironmentApiModel { EnvironmentId = 42, EnvironmentName = "Endur DV 10" };

            Assert.AreEqual("END_DB_DV10", source.GetDatabaseByType(env, "Endur").Name);
            // Flip candidate (S-003): the multi-tag row misses today.
            Assert.IsNull(source.GetDatabaseByType(env, "Reporting"));
        }

        [TestMethod]
        public void GetDatabaseByType_EnvNameOverload_MatchesWholeStringOnly()
        {
            var context = ContextWithEnvironmentDatabases(
                new Database { Id = 1, Name = "END_DB_DV10", Type = "Endur", ServerName = "s1" },
                new Database { Id = 2, Name = "M1", Type = "Endur;Reporting", ServerName = "s2" });
            var source = DatabasesSource(context);

            Assert.AreEqual("END_DB_DV10", source.GetDatabaseByType("Endur DV 10", "Endur")!.Name);
            // Flip candidate (S-003): the multi-tag row misses today.
            Assert.IsNull(source.GetDatabaseByType("Endur DV 10", "Reporting"));
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
        public void GetEnvironmentUsers_MatchesEndurTypeAsWholeStringOnly()
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

            // Flip candidate (S-003): the user mapped via the multi-tag database is
            // invisible today because "Endur;Ops" != "Endur".
            Assert.AreEqual(1, users.Count);
            Assert.AreEqual("endur.user", users[0].LanId);
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
        public void GetUserDbPermissions_FilterMatchesJoinedStringExactly()
        {
            var source = PermsSourceWithOneMultiTagDb(out _);
            // Flip candidates (S-003): per-tag membership will match "Endur" and stop
            // treating the joined string as the only match key.
            Assert.AreEqual(0, source.GetUserDbPermissions("s1", "db1", "Endur").Count);
            Assert.AreEqual(1, source.GetUserDbPermissions("s1", "db1", "Endur;Ops").Count);
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
        public void GetConfigurationFilePath_ResolvesFromWholeStringEndurTypeOnly()
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

            // Flip candidate (S-003): a multi-tag row misses and the path is lost.
            var miss = PropertiesSourceFor(new Database { Id = 1, Name = "END_DV10_DB", Type = "Endur;Ops" })
                .GetConfigurationFilePath(environment);
            Assert.IsNull(miss);
        }
    }
}
