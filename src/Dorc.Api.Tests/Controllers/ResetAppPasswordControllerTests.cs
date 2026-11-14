using System.Runtime.Versioning;
using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Api.Tests.Controllers
{
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class ResetAppPasswordControllerTests
    {
        [TestMethod]
        public void Put_RefusesCleanlyThroughCredentialSourceWhenNonProductionCredentialIsUnavailable()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-APP", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 11,
                    EnvironmentName = "DEV-APP",
                    EnvironmentIsProd = false
                });

            var appDb = new DatabaseApiModel { Id = 42, Name = "AppDb", ServerName = "sql01.corp.local" };
            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-APP").Returns(appDb);
            databases.GetDatabasesForEnvId(11).Returns(new[] { appDb });
            databases.GetDatabasesForServerName("sql01.corp.local").Returns(new[] { appDb });
            databases.GetDatabasesForServerAndLogin("sql01.corp.local", "app_user").Returns(new[] { appDb });
            databases.GetEnvironmentNamesForDatabaseId(42).Returns(new[] { "DEV-APP" });

            var sqlReset = Substitute.For<ISqlUserPasswordReset>();
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-APP").Returns(true);

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns("CORP");

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Resolve(DeploymentTier.NonProduction).Returns((DeploymentCredential?)null);

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                configValues: configValues,
                privileges: privileges,
                settings: settings,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-APP", "app_user");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType(okResult.Value, typeof(ApiBoolResult));
            var body = (ApiBoolResult)okResult.Value;
            Assert.IsFalse(body.Result);
            StringAssert.Contains(body.Message, "Unable to retrieve DOrc Login details");

            credentialSource.Received(1).Resolve(DeploymentTier.NonProduction);
            configValues.DidNotReceiveWithAnyArgs().GetConfigValue(default!);
            sqlReset.DidNotReceiveWithAnyArgs().ResetSqlUserPassword(default!, default!);
        }

        [TestMethod]
        public void Put_ReturnsNotFoundWhenEnvironmentDoesNotExist()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("MISSING-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns((EnvironmentApiModel)null!);

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var controller = CreateController(environments: environments, credentialSource: credentialSource);

            var result = controller.Put("App", "MISSING-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            var notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWithoutResolvingCredentialWhenEnvironmentIsProduction()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("PROD-EU", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 99,
                    EnvironmentName = "PROD-EU",
                    EnvironmentIsProd = true
                });

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "PROD-EU").Returns(true);

            var databases = Substitute.For<IDatabasesPersistentSource>();
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var sqlReset = Substitute.For<ISqlUserPasswordReset>();

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "PROD-EU", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            databases.DidNotReceiveWithAnyArgs().GetApplicationDatabaseForEnvFilter(default!, default!, default!);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
            sqlReset.DidNotReceiveWithAnyArgs().ResetSqlUserPassword(default!, default!);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWithoutResolvingCredentialWhenCallerIsNotOwnerOrAdmin()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(false);

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var controller = CreateController(
                environments: environments,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWhenTargetDatabaseIsNotAttachedToEnvironment()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);

            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-ENV")
                .Returns(new DatabaseApiModel { Id = 77, Name = "UnmappedDb", ServerName = "sql-other.corp.local" });
            databases.GetDatabasesForEnvId(12).Returns(Array.Empty<DatabaseApiModel>());

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var controller = CreateController(
                databases: databases,
                environments: environments,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWhenTargetDatabaseIsSharedWithProductionEnvironment()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });
            environments.EnvironmentIsProd("PROD-SHARED").Returns(true);

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);

            var sharedDb = new DatabaseApiModel { Id = 88, Name = "SharedDb", ServerName = "sql-shared.corp.local" };
            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-ENV").Returns(sharedDb);
            databases.GetDatabasesForEnvId(12).Returns(new[] { sharedDb });
            databases.GetDatabasesForServerName("sql-shared.corp.local").Returns(new[] { sharedDb });
            databases.GetEnvironmentNamesForDatabaseId(88).Returns(new[] { "DEV-ENV", "PROD-SHARED" });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var controller = CreateController(
                databases: databases,
                environments: environments,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
        }

        [TestMethod]
        [DataRow("sql-mixed.corp.local", "SQL-MIXED.CORP.LOCAL")]
        [DataRow("sql-mixed.corp.local", "  sql-mixed  ")]
        [DataRow("(local)", "localhost")]
        [DataRow(@"sql-mixed.corp.local\MSSQLSERVER", "sql-mixed.corp.local,1433")]
        public void Put_ReturnsForbiddenWhenSqlServerInstanceAlsoHostsDistinctDatabaseMappedToProduction(
            string nonProdServerName,
            string prodSiblingServerName)
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });
            environments.EnvironmentIsProd("DEV-ENV").Returns(false);
            environments.EnvironmentIsProd("PROD-SIBLING").Returns(true);

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);

            var nonProdDb = new DatabaseApiModel { Id = 42, Name = "DevAppDb", ServerName = nonProdServerName };
            var prodSiblingDb = new DatabaseApiModel { Id = 99, Name = "ProdAppDb", ServerName = prodSiblingServerName };

            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-ENV").Returns(nonProdDb);
            databases.GetDatabasesForEnvId(12).Returns(new[] { nonProdDb });
            databases.GetDatabasesForServerName(nonProdServerName).Returns(new[] { nonProdDb, prodSiblingDb });
            databases.GetDatabasesForServerAndLogin(nonProdServerName, "app_user").Returns(new[] { nonProdDb });
            databases.GetEnvironmentNamesForDatabaseId(42).Returns(new[] { "DEV-ENV" });
            databases.GetEnvironmentNamesForDatabaseId(99).Returns(new[] { "PROD-SIBLING" });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var sqlReset = Substitute.For<ISqlUserPasswordReset>();

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
            sqlReset.DidNotReceiveWithAnyArgs().ResetSqlUserPassword(default!, default!);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWhenTargetDatabaseIdIsZero()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);

            var zeroIdDb = new DatabaseApiModel { Id = 0, Name = "TransientDb", ServerName = "sql-dev.corp.local" };
            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-ENV").Returns(zeroIdDb);
            databases.GetDatabasesForEnvId(12).Returns(new[] { zeroIdDb });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var sqlReset = Substitute.For<ISqlUserPasswordReset>();

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "app_user");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
            sqlReset.DidNotReceiveWithAnyArgs().ResetSqlUserPassword(default!, default!);
        }

        [TestMethod]
        public void Put_ReturnsForbiddenWhenSharedSqlLoginMapsToAnotherEnvironmentCallerCannotManage()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-ENV", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 12,
                    EnvironmentName = "DEV-ENV",
                    EnvironmentIsProd = false
                });
            environments.EnvironmentIsProd(Arg.Any<string>()).Returns(false);

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-ENV").Returns(true);
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "UAT-OTHER-TEAM").Returns(false);

            var devDb = new DatabaseApiModel { Id = 42, Name = "DevAppDb", ServerName = "sql-nonprod.corp.local" };
            var uatDbSharingLogin = new DatabaseApiModel { Id = 55, Name = "UatAppDb", ServerName = "sql-nonprod.corp.local" };

            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("shared_login", "App", "DEV-ENV").Returns(devDb);
            databases.GetDatabasesForEnvId(12).Returns(new[] { devDb });
            databases.GetDatabasesForServerName("sql-nonprod.corp.local").Returns(new[] { devDb, uatDbSharingLogin });
            databases.GetDatabasesForServerAndLogin("sql-nonprod.corp.local", "shared_login")
                .Returns(new[] { devDb, uatDbSharingLogin });
            databases.GetEnvironmentNamesForDatabaseId(42).Returns(new[] { "DEV-ENV" });
            databases.GetEnvironmentNamesForDatabaseId(55).Returns(new[] { "UAT-OTHER-TEAM" });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            var sqlReset = Substitute.For<ISqlUserPasswordReset>();

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                privileges: privileges,
                credentialSource: credentialSource);

            var result = controller.Put("App", "DEV-ENV", "shared_login");

            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            credentialSource.DidNotReceiveWithAnyArgs().Resolve(default);
            sqlReset.DidNotReceiveWithAnyArgs().ResetSqlUserPassword(default!, default!);
        }

        [TestMethod]
        public void Put_AllowsResetAndDisposesCredentialWhenSqlServerInstanceIsPurelyNonProduction()
        {
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment("DEV-APP", Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 11,
                    EnvironmentName = "DEV-APP",
                    EnvironmentIsProd = false
                });
            environments.EnvironmentIsProd("DEV-APP").Returns(false);
            environments.EnvironmentIsProd("UAT-APP").Returns(false);

            var appDb = new DatabaseApiModel { Id = 42, Name = "AppDb", ServerName = "sql01-nonprod.corp.local" };
            var siblingNonProdDb = new DatabaseApiModel { Id = 43, Name = "SiblingUatDb", ServerName = "sql01-nonprod.corp.local" };

            var databases = Substitute.For<IDatabasesPersistentSource>();
            databases.GetApplicationDatabaseForEnvFilter("app_user", "App", "DEV-APP").Returns(appDb);
            databases.GetDatabasesForEnvId(11).Returns(new[] { appDb });
            databases.GetDatabasesForServerName("sql01-nonprod.corp.local").Returns(new[] { appDb, siblingNonProdDb });
            databases.GetDatabasesForServerAndLogin("sql01-nonprod.corp.local", "app_user").Returns(new[] { appDb });
            databases.GetEnvironmentNamesForDatabaseId(42).Returns(new[] { "DEV-APP" });
            databases.GetEnvironmentNamesForDatabaseId(43).Returns(new[] { "UAT-APP" });

            var sqlReset = Substitute.For<ISqlUserPasswordReset>();
            sqlReset.ResetSqlUserPassword("sql01-nonprod.corp.local", "app_user")
                .Returns(new ApiBoolResult { Result = true });

            var privileges = Substitute.For<ISecurityPrivilegesChecker>();
            privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DEV-APP").Returns(true);

            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns("CORP");

            var resolvedCredential = DeploymentCredential.FromPlainText("svc-nonprod", "nonprod-pass");
            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Resolve(DeploymentTier.NonProduction).Returns(resolvedCredential);

            var controller = CreateController(
                databases: databases,
                environments: environments,
                sqlReset: sqlReset,
                privileges: privileges,
                settings: settings,
                credentialSource: credentialSource,
                bypassWindowsLogonForTest: true);

            var result = controller.Put("App", "DEV-APP", "app_user");

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType(okResult.Value, typeof(ApiBoolResult));
            var body = (ApiBoolResult)okResult.Value;
            Assert.IsTrue(body.Result);

            credentialSource.Received(1).Resolve(DeploymentTier.NonProduction);
            sqlReset.Received(1).ResetSqlUserPassword("sql01-nonprod.corp.local", "app_user");
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = resolvedCredential.Password.Length);
        }

        [TestMethod]
        [DataRow("sql01.corp.local", "SQL01.CORP.LOCAL", true)]
        [DataRow("  tcp:sql01.corp.local,1433  ", @"sql01.corp.local\MSSQLSERVER", true)]
        [DataRow("sql01", "sql01.corp.local", true)]
        [DataRow("(local)", "localhost", true)]
        [DataRow(@".\INST01", @"(local)\inst01", true)]
        [DataRow(@"sql01\DEV,1444", @"sql01\PROD,1444", true)]
        [DataRow("sql01,1433", @"sql01\PROD,1433", true)]
        [DataRow("sql01", "sql01,1433", true)]
        [DataRow("sql01", @"sql01\PROD,1433", true)]
        [DataRow(@"sql01\DEV,1444", @"sql01\DEV,1445", false)]
        [DataRow(@"sql01\DEV", @"sql01\PROD", false)]
        [DataRow(@"sql01.corp.local\INST_DEV", @"sql01.corp.local\INST_PROD", false)]
        [DataRow("sql01.corp.local", @"sql01.corp.local\INST_PROD", false)]
        [DataRow("sql01.dev.local", "sql01.prod.local", false)]
        public void SqlServerInstancesMatch_NormalizesHostProtocolPortAndInstanceScope(
            string left,
            string right,
            bool expectedMatch)
        {
            Assert.AreEqual(expectedMatch, DatabasesPersistentSource.SqlServerInstancesMatch(left, right));
        }

        [TestMethod]
        public void DatabasesPersistentSource_MatchesEquivalentExplicitTcpEndpointsForServerAndLoginLookups()
        {
            var devEnv = new Environment { Id = 10, Name = "DEV-ENV", IsProd = false };
            var prodEnv = new Environment { Id = 20, Name = "PROD-ENV", IsProd = true };

            var devPortDb = new Database
            {
                Id = 101,
                Name = "DevDb",
                ServerName = @"sql01\DEV,1444",
                Environments = new List<Environment> { devEnv },
                TagLinks = new List<DatabaseTag>()
            };
            var prodSamePortDb = new Database
            {
                Id = 102,
                Name = "ProdDbSameTcpEndpoint",
                ServerName = @"sql01\PROD,1444",
                Environments = new List<Environment> { prodEnv },
                TagLinks = new List<DatabaseTag>()
            };
            var defaultPortDb = new Database
            {
                Id = 103,
                Name = "DefaultPortDb",
                ServerName = "sql01,1433",
                Environments = new List<Environment> { devEnv },
                TagLinks = new List<DatabaseTag>()
            };
            var ignoredLabelDefaultPortDb = new Database
            {
                Id = 104,
                Name = "IgnoredInstanceLabelOn1433",
                ServerName = @"sql01\PROD,1433",
                Environments = new List<Environment> { prodEnv },
                TagLinks = new List<DatabaseTag>()
            };
            var differentPortDb = new Database
            {
                Id = 105,
                Name = "DifferentPortDb",
                ServerName = @"sql01\DEV,1445",
                Environments = new List<Environment> { devEnv },
                TagLinks = new List<DatabaseTag>()
            };

            var sharedLogin = new User { Id = 7, LoginId = "app_shared_login" };
            var environmentUsers = new List<EnvironmentUser>
            {
                new() { DbId = 101, UserId = 7, Database = devPortDb, User = sharedLogin },
                new() { DbId = 102, UserId = 7, Database = prodSamePortDb, User = sharedLogin },
                new() { DbId = 104, UserId = 7, Database = ignoredLabelDefaultPortDb, User = sharedLogin },
                new() { DbId = 105, UserId = 7, Database = differentPortDb, User = sharedLogin }
            };

            var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Database>
            {
                devPortDb,
                prodSamePortDb,
                defaultPortDb,
                ignoredLabelDefaultPortDb,
                differentPortDb
            });
            var envUsersSet = DbContextMock.GetQueryableMockDbSet(environmentUsers);

            var context = Substitute.For<IDeploymentContext>();
            context.Databases.Returns(dbSet);
            context.EnvironmentUsers.Returns(envUsersSet);

            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(_ => context);

            var source = new DatabasesPersistentSource(
                contextFactory,
                Substitute.For<IRolePrivilegesChecker>(),
                Substitute.For<IClaimsPrincipalReader>());

            var port1444Matches = source.GetDatabasesForServerName(@"sql01\DEV,1444").Select(d => d.Id).OrderBy(id => id).ToArray();
            CollectionAssert.AreEqual(new[] { 101, 102 }, port1444Matches);

            var port1433Matches = source.GetDatabasesForServerName("sql01,1433").Select(d => d.Id).OrderBy(id => id).ToArray();
            CollectionAssert.AreEqual(new[] { 103, 104 }, port1433Matches);

            var loginPort1444Matches = source.GetDatabasesForServerAndLogin(@"sql01\DEV,1444", "APP_SHARED_LOGIN")
                .Select(d => d.Id)
                .OrderBy(id => id)
                .ToArray();
            CollectionAssert.AreEqual(new[] { 101, 102 }, loginPort1444Matches);

            var loginPort1433Matches = source.GetDatabasesForServerAndLogin("sql01,1433", "app_shared_login")
                .Select(d => d.Id)
                .OrderBy(id => id)
                .ToArray();
            CollectionAssert.AreEqual(new[] { 104 }, loginPort1433Matches);

            Assert.AreEqual(0, source.GetDatabasesForServerName(@"sql99\NONE,1444").Count());
        }

        private static ResetAppPasswordController CreateController(
            IDatabasesPersistentSource? databases = null,
            IEnvironmentsPersistentSource? environments = null,
            ISqlUserPasswordReset? sqlReset = null,
            IConfigValuesPersistentSource? configValues = null,
            ISecurityPrivilegesChecker? privileges = null,
            IConfigurationSettings? settings = null,
            IDeploymentCredentialSource? credentialSource = null,
            bool bypassWindowsLogonForTest = false)
        {
            ResetAppPasswordController controller = bypassWindowsLogonForTest
                ? new TestableResetAppPasswordController(
                    databases ?? Substitute.For<IDatabasesPersistentSource>(),
                    environments ?? Substitute.For<IEnvironmentsPersistentSource>(),
                    sqlReset ?? Substitute.For<ISqlUserPasswordReset>(),
                    configValues ?? Substitute.For<IConfigValuesPersistentSource>(),
                    Substitute.For<ILogger<ResetAppPasswordController>>(),
                    privileges ?? Substitute.For<ISecurityPrivilegesChecker>(),
                    settings ?? Substitute.For<IConfigurationSettings>(),
                    Substitute.For<IClaimsPrincipalReader>(),
                    credentialSource ?? Substitute.For<IDeploymentCredentialSource>())
                : new ResetAppPasswordController(
                    databases ?? Substitute.For<IDatabasesPersistentSource>(),
                    environments ?? Substitute.For<IEnvironmentsPersistentSource>(),
                    sqlReset ?? Substitute.For<ISqlUserPasswordReset>(),
                    configValues ?? Substitute.For<IConfigValuesPersistentSource>(),
                    Substitute.For<ILogger<ResetAppPasswordController>>(),
                    privileges ?? Substitute.For<ISecurityPrivilegesChecker>(),
                    settings ?? Substitute.For<IConfigurationSettings>(),
                    Substitute.For<IClaimsPrincipalReader>(),
                    credentialSource ?? Substitute.For<IDeploymentCredentialSource>());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        private sealed class TestableResetAppPasswordController : ResetAppPasswordController
        {
            public TestableResetAppPasswordController(
                IDatabasesPersistentSource databasesPersistentSource,
                IEnvironmentsPersistentSource environmentsPersistentSource,
                ISqlUserPasswordReset sqlUserPasswordReset,
                IConfigValuesPersistentSource configValuesPersistentSource,
                ILogger<ResetAppPasswordController> logger,
                ISecurityPrivilegesChecker securityPrivilegesChecker,
                IConfigurationSettings configurationSettingsEngine,
                IClaimsPrincipalReader claimsPrincipalReader,
                IDeploymentCredentialSource credentialSource)
                : base(
                    databasesPersistentSource,
                    environmentsPersistentSource,
                    sqlUserPasswordReset,
                    configValuesPersistentSource,
                    logger,
                    securityPrivilegesChecker,
                    configurationSettingsEngine,
                    claimsPrincipalReader,
                    credentialSource)
            {
            }

            protected override ApiBoolResult ExecuteImpersonated(
                DeploymentCredential credential,
                string domainName,
                Func<ApiBoolResult> action)
            {
                Assert.IsTrue(credential.IsComplete);
                return action();
            }
        }
    }
}
