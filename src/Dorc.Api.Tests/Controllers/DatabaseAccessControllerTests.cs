using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
public class DatabaseAccessControllerTests
{
    private sealed record Harness(
        DatabaseAccessController Controller,
        IDeploymentContextFactory Factory,
        ISecurityPrivilegesChecker Privileges);

    private static Harness Create(
        bool? enabled,
        bool canModify = true,
        bool sharedAllowed = true,
        Dictionary<string, string?>? obsoleteSettings = null,
        bool owner = true,
        bool sharedOwner = true,
        bool unattachedDatabase = false,
        DatabaseAccessReconciler? customReconciler = null,
        DatabaseAccessDirectory? customDirectory = null)
    {
        var mapper = Substitute.For<IEnvironmentMapper>();
        mapper.GetEnvironmentByDatabase(10, 1, Arg.Any<ClaimsPrincipal>())
            .Returns(new EnvironmentApiModel { EnvironmentName = "Env" });
        var privileges = Substitute.For<ISecurityPrivilegesChecker>();
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "Env").Returns(canModify);
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "OtherEnv").Returns(sharedAllowed);
        privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "Env").Returns(owner && canModify);
        privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OtherEnv").Returns(sharedOwner && sharedAllowed);

        var factory = Substitute.For<IDeploymentContextFactory>();
        var context = Substitute.For<IDeploymentContext>();
        var attached = unattachedDatabase
            ? new List<Dorc.PersistentData.Model.Environment>()
            : new List<Dorc.PersistentData.Model.Environment> { new() { Name = "Env" }, new() { Name = "OtherEnv" } };
        var dbEntities = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.Database>
        {
            new() { Id = 1, Name = "Db1", ServerName = "sql01", Environments = attached }
        });
        context.Databases.Returns(dbEntities);
        factory.GetContext().Returns(context);

        var settings = obsoleteSettings ?? [];
        if (enabled.HasValue) settings["DatabaseAccess:Enabled"] = enabled.Value.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var store = new DatabaseAccessStore(factory);
        var directory = customDirectory ?? new DatabaseAccessDirectory(Substitute.For<IActiveDirectorySearcher>());
        var providers = new DatabaseAccessProviders([new SqlServerDatabaseAccessProvider()]);
        var reconciler = customReconciler ?? new DatabaseAccessReconciler(store, factory, providers, directory, NullLogger<DatabaseAccessReconciler>.Instance);

        var controller = new DatabaseAccessController(
            reconciler, directory, providers, configuration, mapper, privileges, factory,
            NullLogger<DatabaseAccessController>.Instance,
            new OAuthClaimsPrincipalReader(Substitute.For<IUserGroupReader>()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return new Harness(controller, factory, privileges);
    }

    [TestMethod]
    public void DisabledFeatureDoesNotAccessNewSchema()
    {
        var h = Create(false);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.Get(10, 1).Result);
        h.Factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    public void FeatureDefaultsToEnabledWhenSettingIsMissing()
    {
        var h = Create(null);
        var status = (DatabaseAccessStatus)((OkObjectResult)h.Controller.Status(10, 1).Result!).Value!;
        Assert.IsTrue(status.Enabled);
        Assert.IsTrue(status.ExecutionEnabled);
        Assert.IsTrue(status.CanApproveDbOwner);
        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("m2m", "true")], "OAuth"));
        var result = h.Controller.Apply(10, 1, new() { Token = "confirmed" });
        Assert.AreEqual(400, ((ProblemDetails)((ObjectResult)result.Result!).Value!).Status);
    }

    [TestMethod]
    public void GlobalFlagDisablesAllOperationsBeforeAccessingNewSchema()
    {
        var h = Create(false, obsoleteSettings: new()
        {
            ["DatabaseAccess:ExecutionEnabled"] = "true",
            ["DatabaseAccess:DatabaseIds:0"] = "1"
        });
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.Save(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.Preview(10, 1).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.Apply(10, 1, new() { Token = "confirmed" }).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.SearchDirectory(10, 1, "user").Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.ResolveDirectory(10, 1, "sid").Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.SqlLogins(10, 1).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(h.Controller.Audit(10, 1).Result);
        h.Factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GlobalFlagEnablesAccessAndApplyWithoutDatabaseOrExecutionConfiguration(bool obsoleteSettings)
    {
        var h = Create(true, obsoleteSettings: obsoleteSettings ? new()
        {
            ["DatabaseAccess:ExecutionEnabled"] = "false",
            ["DatabaseAccess:DatabaseIds:0"] = "999"
        } : null);
        var status = (DatabaseAccessStatus)((OkObjectResult)h.Controller.Status(10, 1).Result!).Value!;
        Assert.IsTrue(status.Enabled);
        Assert.IsTrue(status.ExecutionEnabled);
        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("m2m", "true")], "OAuth"));

        var result = h.Controller.Apply(10, 1, new() { Token = "confirmed" });
        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("The authenticated identity has no audit identifier.", problem.Detail);
    }

    [TestMethod]
    public void MultiEnvironmentDatabaseRequiresEveryEnvironmentPermission()
    {
        var h = Create(true, sharedAllowed: false);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Save(10, 1, new()).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Apply(10, 1, new()).Result);
    }

    [TestMethod]
    public void MissingAssociationAndUnauthorizedReadAreDenied()
    {
        var h = Create(true, canModify: false);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Get(10, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Apply(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundResult>(h.Controller.Get(999, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.SqlLogins(10, 1).Result);
        Assert.IsInstanceOfType<NotFoundResult>(h.Controller.SqlLogins(999, 1).Result);
    }

    [TestMethod]
    public void WritersWhoAreNotOwnersOrAdminsAreDeniedEveryOperation()
    {
        var h = Create(true, canModify: true, owner: false);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Get(10, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Save(10, 1, new()).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Preview(10, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.Apply(10, 1, new()).Result);
        Assert.IsInstanceOfType<ForbidResult>(h.Controller.SqlLogins(10, 1).Result);
    }

    [TestMethod]
    public void StatusReportsDisabledWithoutLoadingNewSchema()
    {
        var h = Create(false);
        var result = (DatabaseAccessStatus)((OkObjectResult)h.Controller.Status(10, 1).Result!).Value!;
        Assert.IsFalse(result.Enabled);
        Assert.IsFalse(result.ExecutionEnabled);
        Assert.IsFalse(result.CanApproveDbOwner);
        h.Factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void DbOwnerApprovalRequiresOwnershipOfEveryAttachedEnvironment(bool owner, bool sharedOwner)
    {
        var h = Create(true, owner: owner, sharedOwner: sharedOwner);
        var allowed = owner && sharedOwner;
        if (owner)
        {
            var status = (DatabaseAccessStatus)((OkObjectResult)h.Controller.Status(10, 1).Result!).Value!;
            Assert.AreEqual(allowed, status.CanApproveDbOwner);
        }
        else
        {
            Assert.IsInstanceOfType<ForbidResult>(h.Controller.Status(10, 1).Result);
        }

        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("m2m", "true")], "OAuth"));
        var saveResult = h.Controller.Save(10, 1, new()).Result;
        if (allowed)
            Assert.AreEqual(400, ((ProblemDetails)((ObjectResult)saveResult!).Value!).Status);
        else
            Assert.IsInstanceOfType<ForbidResult>(saveResult);
    }

    [TestMethod]
    public void ErrorHandlingMapsDomainConcurrencyUnsupportedAndInfrastructureExceptions()
    {
        var adSearcher = Substitute.For<IActiveDirectorySearcher>();
        adSearcher.Search("valid_term").Returns([new UserElementApiModel { Sid = "S-1-5-18", DisplayName = "LocalSystem" }]);
        adSearcher.GetUserDataById("sid_503").Returns(_ => throw new System.DirectoryServices.DirectoryServicesCOMException("LDAP down"));
        var h = Create(true, customDirectory: new DatabaseAccessDirectory(adSearcher));

        // 200 OK directory search
        var found = (List<DatabaseAccessDirectoryIdentity>)((OkObjectResult)h.Controller.SearchDirectory(10, 1, "valid_term").Result!).Value!;
        Assert.HasCount(1, found);

        // 400 ArgumentException
        var badSearch = (ProblemDetails)((ObjectResult)h.Controller.SearchDirectory(10, 1, "a").Result!).Value!;
        Assert.AreEqual(400, badSearch.Status);

        // 503 DirectoryServicesCOMException
        var ldapFail = (ProblemDetails)((ObjectResult)h.Controller.ResolveDirectory(10, 1, "sid_503").Result!).Value!;
        Assert.AreEqual(503, ldapFail.Status);

        // 422 NotSupportedException via unsupported provider on Save
        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true"), new Claim("client_id", "dorc-cli")], "OAuth"));
        var unsupported = (ProblemDetails)((ObjectResult)h.Controller.Save(10, 1, new DatabaseAccessState
        {
            DatabaseId = 1,
            Provider = "postgresql"
        }).Result!).Value!;
        Assert.AreEqual(422, unsupported.Status);

        // 409 DbUpdateConcurrencyException and DatabaseAccessConflictException
        adSearcher.GetUserDataById("sid_409").Returns(_ => throw new DbUpdateConcurrencyException("Stale"));
        Assert.AreEqual(409, ((ProblemDetails)((ObjectResult)h.Controller.ResolveDirectory(10, 1, "sid_409").Result!).Value!).Status);
        adSearcher.GetUserDataById("sid_conflict").Returns(_ => throw new DatabaseAccessConflictException("Conflict"));
        Assert.AreEqual(409, ((ProblemDetails)((ObjectResult)h.Controller.ResolveDirectory(10, 1, "sid_conflict").Result!).Value!).Status);

        // 503 DbUpdateException
        adSearcher.GetUserDataById("sid_dbupdate").Returns(_ => throw new DbUpdateException("Write failed"));
        Assert.AreEqual(503, ((ProblemDetails)((ObjectResult)h.Controller.ResolveDirectory(10, 1, "sid_dbupdate").Result!).Value!).Status);

        // Unattached database: Status sets CanApproveDbOwner=false and Save forbids
        var unattached = Create(true, unattachedDatabase: true);
        var unattachedStatus = (DatabaseAccessStatus)((OkObjectResult)unattached.Controller.Status(10, 1).Result!).Value!;
        Assert.IsFalse(unattachedStatus.CanApproveDbOwner);
        Assert.IsInstanceOfType<ForbidResult>(unattached.Controller.Save(10, 1, new DatabaseAccessState { DatabaseId = 1 }).Result);
    }

    [TestMethod]
    public void ClientCredentialsIdentityReachesSaveValidationWithoutAUserSubject()
    {
        var h = Create(true);
        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true"), new Claim("client_id", "dorc-cli")], "OAuth"));

        var result = h.Controller.Save(10, 1, new DatabaseAccessState { DatabaseId = 2 });

        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("Database ID does not match the route.", problem.Detail);
    }

    [TestMethod]
    public void ClientCredentialsIdentityWithoutClientIdIsRejectedBeforeSave()
    {
        var h = Create(true);
        h.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("m2m", "true")], "OAuth"));

        var result = h.Controller.Save(10, 1, new DatabaseAccessState { DatabaseId = 2 });

        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("The authenticated identity has no audit identifier.", problem.Detail);
    }

    [TestMethod]
    public void EndToEndSavePreviewSqlLoginsAndAuditAllowOwnerApprovedDbOwnerTransitions()
    {
        var enabledCfg = new ConfigurationBuilder().Build();
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DOrcConnectionString");
        if (string.IsNullOrWhiteSpace(connection) || !OperatingSystem.IsWindows())
            return;

        var dbName = "dorc_ctrl_e2e_" + Guid.NewGuid().ToString("N");
        var masterCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
        using var masterSql = new Microsoft.Data.SqlClient.SqlConnection(masterCs);
        masterSql.Open();
        using (var createCmd = new Microsoft.Data.SqlClient.SqlCommand($"CREATE DATABASE [{dbName}];", masterSql))
            createCmd.ExecuteNonQuery();
        try
        {
            var dbCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection) { InitialCatalog = dbName }.ConnectionString;
            var accessOpts = new DbContextOptionsBuilder<DatabaseAccessContext>().UseSqlServer(dbCs).Options;
            using var accessCtx = new DatabaseAccessContext(accessOpts);
            using (var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(dbCs))
            {
                sqlConn.Open();
                using (var schemaCmd = new Microsoft.Data.SqlClient.SqlCommand("CREATE SCHEMA [deploy];", sqlConn))
                    schemaCmd.ExecuteNonQuery();
                using (var tableCmd = new Microsoft.Data.SqlClient.SqlCommand("CREATE TABLE [deploy].[Database] ([Id] INT PRIMARY KEY); INSERT [deploy].[Database] VALUES (1);", sqlConn))
                    tableCmd.ExecuteNonQuery();
            }
            var creator = (Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator)
                ((Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure<IServiceProvider>)accessCtx.Database)
                .Instance.GetRequiredService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
            creator.CreateTables();

            var depFactory = new FuncDeploymentContextFactory(() =>
            {
                var dep = Substitute.For<IDeploymentContext>();
                var inner = new DatabaseAccessContext(accessOpts);
                dep.Database.Returns(inner.Database);
                dep.When(x => x.Dispose()).Do(_ => inner.Dispose());
                var dbSet = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.Database>
                {
                    new()
                    {
                        Id = 1, Name = "Db1", ServerName = "sql01", DatabaseType = DatabaseType.SqlServer,
                        Environments = new List<Dorc.PersistentData.Model.Environment>
                        {
                            new() { Name = "Env" }, new() { Name = "OtherEnv" }
                        }
                    }
                });
                dep.Databases.Returns(dbSet);
                return dep;
            });

            var fakeProvider = Substitute.For<IDatabaseAccessProvider>();
            fakeProvider.Name.Returns("sql-server");
            fakeProvider.Preview(Arg.Any<DatabaseAccessTarget>(), Arg.Any<DatabaseAccessState>())
                .Returns(new DatabaseAccessPreview { Token = "tok" });
            fakeProvider.SqlLogins(Arg.Any<DatabaseAccessTarget>()).Returns(["sa_app"]);
            var realStore = new DatabaseAccessStore(depFactory);
            var realDirectory = new DatabaseAccessDirectory(Substitute.For<IActiveDirectorySearcher>());
            var realProviders = new DatabaseAccessProviders([fakeProvider]);
            var realReconciler = new DatabaseAccessReconciler(realStore, depFactory, realProviders, realDirectory, NullLogger<DatabaseAccessReconciler>.Instance);

            var privs = Substitute.For<ISecurityPrivilegesChecker>();
            privs.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "Env").Returns(true);
            privs.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "OtherEnv").Returns(true);
            var mapper = Substitute.For<IEnvironmentMapper>();
            mapper.GetEnvironmentByDatabase(10, 1, Arg.Any<ClaimsPrincipal>())
                .Returns(new EnvironmentApiModel { EnvironmentName = "Env" });

            var fullOwnerCtrl = new DatabaseAccessController(
                realReconciler, realDirectory, realProviders, enabledCfg, mapper, privs, depFactory,
                NullLogger<DatabaseAccessController>.Instance,
                new OAuthClaimsPrincipalReader(Substitute.For<IUserGroupReader>()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("m2m", "true"), new Claim("client_id", "dorc-cli")], "OAuth"))
                    }
                }
            };

            var gotState = (DatabaseAccessState)((OkObjectResult)fullOwnerCtrl.Get(10, 1).Result!).Value!;
            Assert.AreEqual(1, gotState.DatabaseId);

            // Owner of all attached environments can save db_owner directly in desired state
            var savedState = (DatabaseAccessState)((OkObjectResult)fullOwnerCtrl.Save(10, 1, new DatabaseAccessState
            {
                DatabaseId = 1,
                Revision = gotState.Revision,
                Provider = "sql-server",
                Principals = [new() { Name = "svc_owner", Kind = "Native", LoginName = "svc_owner", Present = true }],
                Roles = [new() { Name = "db_owner", Managed = false, Present = true }],
                Memberships = [new() { Principal = "svc_owner", Role = "db_owner", Present = true }]
            }).Result!).Value!;
            Assert.AreEqual(1, savedState.Revision);

            Assert.IsInstanceOfType<OkObjectResult>(fullOwnerCtrl.Preview(10, 1).Result);
            Assert.IsInstanceOfType<OkObjectResult>(fullOwnerCtrl.SqlLogins(10, 1).Result);
            var audits = (List<DatabaseAccessAuditApiModel>)((OkObjectResult)fullOwnerCtrl.Audit(10, 1).Result!).Value!;
            Assert.IsNotEmpty(audits);
        }
        finally
        {
            using var dropCmd = new Microsoft.Data.SqlClient.SqlCommand(
                $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];", masterSql);
            dropCmd.ExecuteNonQuery();
        }
    }

    private sealed class FuncDeploymentContextFactory(Func<IDeploymentContext> factory) : IDeploymentContextFactory
    {
        public IDeploymentContext GetContext() => factory();
    }
}