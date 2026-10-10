using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.Api.Interfaces;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.Core.DatabaseAccess;
using Dorc.PersistentData.Contexts;
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
        bool enabled, bool execution, bool canModify = true, bool sharedAllowed = true)
    {
        var mapper = Substitute.For<IEnvironmentMapper>();
        mapper.GetEnvironmentByDatabase(10, 1, Arg.Any<ClaimsPrincipal>())
            .Returns(new EnvironmentApiModel { EnvironmentName = "Env" });
        var privileges = Substitute.For<ISecurityPrivilegesChecker>();
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "Env").Returns(canModify);
        privileges.CanModifyEnvironment(Arg.Any<ClaimsPrincipal>(), "Shared").Returns(sharedAllowed);
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
            ["DatabaseAccess:Enabled"] = enabled.ToString(),
            ["DatabaseAccess:ExecutionEnabled"] = execution.ToString(),
            ["DatabaseAccess:DatabaseIds:0"] = "1"
        }).Build();
        var controller = new DatabaseAccessController(null!, null!, new DatabaseAccessProviders([new SqlServerDatabaseAccessProvider()]), configuration, mapper, privileges, factory,
            NullLogger<DatabaseAccessController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, factory);
    }

    [TestMethod]
    public void DisabledFeatureDoesNotAccessNewSchema()
    {
        var (controller, factory) = Create(false, false);
        var result = controller.Get(10, 1);
        Assert.IsInstanceOfType<NotFoundObjectResult>(result.Result);
        factory.DidNotReceive().GetContext();
    }

    [TestMethod]
    public void TargetExecutionHasIndependentGate()
    {
        var (controller, _) = Create(true, false);
        var result = controller.Apply(10, 1, new() { Token = "confirmed" });
        Assert.AreEqual(403, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public void SharedDatabaseRequiresEveryEnvironmentPermission()
    {
        var (controller, _) = Create(true, true, sharedAllowed: false);
        Assert.IsInstanceOfType<ForbidResult>(controller.Save(10, 1, new()).Result);
    }

    [TestMethod]
    public void MissingAssociationAndUnauthorizedReadAreDenied()
    {
        var (controller, _) = Create(true, true, canModify: false);
        Assert.IsInstanceOfType<ForbidResult>(controller.Get(10, 1).Result);
        Assert.IsInstanceOfType<NotFoundResult>(controller.Get(999, 1).Result);
    }

    [TestMethod]
    public void StatusReportsDisabledWithoutLoadingNewSchema()
    {
        var (controller, factory) = Create(false, true);
        var result = (DatabaseAccessStatus)((OkObjectResult)controller.Status(10, 1).Result!).Value!;
        Assert.IsFalse(result.Enabled);
        Assert.IsFalse(result.ExecutionEnabled);
        factory.DidNotReceive().GetContext();
    }
}
