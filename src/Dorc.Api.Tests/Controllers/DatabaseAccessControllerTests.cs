using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Security;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.Core.DatabaseAccess;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
public class DatabaseAccessControllerTests
{
    private static (DatabaseAccessController Controller, IDeploymentContextFactory Factory) Create(
        bool enabled, bool canModify = true, bool sharedAllowed = true,
        Dictionary<string, string?>? obsoleteSettings = null, bool owner = true, bool sharedOwner = true)
    {
        var mapper = Substitute.For<IEnvironmentMapper>();
        mapper.GetEnvironmentByDatabase(10, 1, Arg.Any<ClaimsPrincipal>())
            .Returns(new EnvironmentApiModel { EnvironmentName = "Env" });
        var privileges = Substitute.For<ISecurityPrivilegesChecker>();
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "Env").Returns(canModify);
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "Shared").Returns(sharedAllowed);
        privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "Env").Returns(owner);
        privileges.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "Shared").Returns(sharedOwner);
        var factory = Substitute.For<IDeploymentContextFactory>();
        var context = Substitute.For<IDeploymentContext>();
        var databases = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.Database>
        {
            new() { Id = 1, Environments = new List<Dorc.PersistentData.Model.Environment>
                { new() { Name = "Env" }, new() { Name = "Shared" } } }
        });
        context.Databases.Returns(databases);
        factory.GetContext().Returns(context);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseAccess:Enabled"] = enabled.ToString()
        }).AddInMemoryCollection(obsoleteSettings ?? []).Build();
        var service = new DatabaseAccessService(null!, null!, null!, null!, NullLogger<DatabaseAccessService>.Instance);
        var controller = new DatabaseAccessController(service, null!, new DatabaseAccessProviders([new SqlServerDatabaseAccessProvider()]), configuration, mapper, privileges, factory,
            NullLogger<DatabaseAccessController>.Instance, new OAuthClaimsPrincipalReader(Substitute.For<IUserGroupReader>()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, factory);
    }

    [TestMethod]
    public void DisabledFeatureDoesNotAccessNewSchema()
    {
        var (controller, factory) = Create(false);
        var result = controller.Get(10, 1);
        Assert.IsInstanceOfType<NotFoundObjectResult>(result.Result);
        factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    public void GlobalFlagDisablesAllOperationsBeforeAccessingNewSchema()
    {
        var (controller, factory) = Create(false, obsoleteSettings: new()
        {
            ["DatabaseAccess:ExecutionEnabled"] = "true",
            ["DatabaseAccess:DatabaseIds:0"] = "1"
        });
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.Save(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.Preview(10, 1).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.Apply(10, 1, new() { Token = "confirmed" }).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.PreviewImport(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.Import(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.SearchDirectory(10, 1, "user").Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.ResolveDirectory(10, 1, "sid").Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.LookupDirectory(10, 1, "DOMAIN\\account").Result);
        Assert.IsInstanceOfType<NotFoundObjectResult>(controller.Audit(10, 1).Result);
        factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GlobalFlagEnablesAccessAndApplyWithoutDatabaseOrExecutionConfiguration(bool obsoleteSettings)
    {
        var (controller, _) = Create(true, obsoleteSettings: obsoleteSettings ? new()
        {
            ["DatabaseAccess:ExecutionEnabled"] = "false",
            ["DatabaseAccess:DatabaseIds:0"] = "999"
        } : null);
        var status = (DatabaseAccessStatus)((OkObjectResult)controller.Status(10, 1).Result!).Value!;
        Assert.IsTrue(status.Enabled);
        Assert.IsTrue(status.ExecutionEnabled);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true")], "OAuth"));

        var result = controller.Apply(10, 1, new() { Token = "confirmed" });
        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("The authenticated identity has no audit identifier.", problem.Detail);
    }

    [TestMethod]
    public void SharedDatabaseRequiresEveryEnvironmentPermission()
    {
        var (controller, _) = Create(true, sharedAllowed: false);
        Assert.IsInstanceOfType<ForbidResult>(controller.Save(10, 1, new()).Result);
        Assert.IsInstanceOfType<ForbidResult>(controller.Apply(10, 1, new()).Result);
    }

    [TestMethod]
    public void MissingAssociationAndUnauthorizedReadAreDenied()
    {
        var (controller, _) = Create(true, canModify: false);
        Assert.IsInstanceOfType<ForbidResult>(controller.Get(10, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(controller.Apply(10, 1, new()).Result);
        Assert.IsInstanceOfType<NotFoundResult>(controller.Get(999, 1).Result);
        Assert.IsInstanceOfType<ForbidResult>(controller.LookupDirectory(10, 1, "DOMAIN\\account").Result);
        Assert.IsInstanceOfType<NotFoundResult>(controller.LookupDirectory(999, 1, "DOMAIN\\account").Result);
    }

    [TestMethod]
    public void StatusReportsDisabledWithoutLoadingNewSchema()
    {
        var (controller, factory) = Create(false);
        var result = (DatabaseAccessStatus)((OkObjectResult)controller.Status(10, 1).Result!).Value!;
        Assert.IsFalse(result.Enabled);
        Assert.IsFalse(result.ExecutionEnabled);
        factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void DbOwnerApprovalRequiresOwnershipOfEveryAttachedEnvironment(bool owner, bool sharedOwner)
    {
        var (controller, _) = Create(true, owner: owner, sharedOwner: sharedOwner);
        var status = (DatabaseAccessStatus)((OkObjectResult)controller.Status(10, 1).Result!).Value!;
        Assert.AreEqual(owner && sharedOwner, status.CanApproveDbOwner);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true")], "OAuth"));
        var request = new DatabaseAccessImportRequest { ApprovedDbOwnerLegacyUserIds = [42] };
        var results = new[] { controller.PreviewImport(10, 1, request).Result, controller.Import(10, 1, request).Result };
        foreach (var result in results)
        {
            if (owner && sharedOwner)
                Assert.AreEqual(400, ((ProblemDetails)((ObjectResult)result!).Value!).Status);
            else Assert.IsInstanceOfType<ForbidResult>(result);
        }
        var ordinary = controller.PreviewImport(10, 1, new());
        Assert.AreEqual(400, ((ProblemDetails)((ObjectResult)ordinary.Result!).Value!).Status,
            "Ordinary imports still require Modify, not privileged approval rights.");
    }

    [TestMethod]
    public void ClientCredentialsIdentityReachesSaveValidationWithoutAUserSubject()
    {
        var (controller, _) = Create(true);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true"), new Claim("client_id", "dorc-cli")], "OAuth"));

        var result = controller.Save(10, 1, new DatabaseAccessState { DatabaseId = 2 });

        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("Database ID does not match the route.", problem.Detail);
    }

    [TestMethod]
    public void ClientCredentialsIdentityWithoutClientIdIsRejectedBeforeSave()
    {
        var (controller, _) = Create(true);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("m2m", "true")], "OAuth"));

        var result = controller.Save(10, 1, new DatabaseAccessState { DatabaseId = 2 });

        var problem = (ProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.AreEqual(400, problem.Status);
        Assert.AreEqual("The authenticated identity has no audit identifier.", problem.Detail);
    }
}
