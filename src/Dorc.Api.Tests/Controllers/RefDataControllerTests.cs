using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
public class RefDataControllerTests
{
    private readonly IManageProjectsPersistentSource components = Substitute.For<IManageProjectsPersistentSource>();
    private readonly IProjectsPersistentSource projects = Substitute.For<IProjectsPersistentSource>();
    private readonly ISecurityPrivilegesChecker security = Substitute.For<ISecurityPrivilegesChecker>();
    private readonly IClaimsPrincipalReader claims = Substitute.For<IClaimsPrincipalReader>();
    private readonly IDeploymentDefinitions definitions = Substitute.For<IDeploymentDefinitions>();

    private RefDataController Controller(bool admin = false)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(admin ? new[] { new Claim(ClaimTypes.Role, "Admin") } : []));
        return new RefDataController(components, projects, security, claims, definitions)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };
    }

    private static RefDataApiModel Document() => new()
    {
        Project = new ProjectApiModel { ProjectId = 7, ProjectName = "project" },
        Components = [], Revision = "expected"
    };

    [TestMethod]
    public void Get_ReturnsRevisionAndAuthenticatedCapability()
    {
        definitions.Get(7).Returns(Document());
        security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 7).Returns(true);
        var result = (ObjectResult)Controller().Get("7");
        var model = (RefDataApiModel)result.Value!;
        Assert.AreEqual("expected", model.Revision);
        Assert.IsTrue(model.CanEdit);
        Assert.IsFalse(model.CanDeploy);
    }

    [TestMethod]
    public void Put_WithoutRights_DoesNotEnterMutation()
    {
        Assert.AreEqual(403, ((ObjectResult)Controller().Put(Document())).StatusCode);
        definitions.DidNotReceiveWithAnyArgs().Save(default!, default, default!, default!, default!, default);
    }

    [TestMethod]
    [DataRow(true, 428)]
    [DataRow(false, 409)]
    public void Put_RevisionFailure_ReturnsActionableStatus(bool missing, int status)
    {
        security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 7).Returns(true);
        definitions.Save(Arg.Any<RefDataApiModel>(), false, Arg.Any<string>(), Arg.Any<Func<int, bool>>(), Arg.Any<Action>(), false)
            .Returns(_ => throw new DefinitionPreconditionException(missing));
        Assert.AreEqual(status, ((ObjectResult)Controller().Put(Document())).StatusCode);
        projects.DidNotReceiveWithAnyArgs().UpdateProject(default!);
        components.DidNotReceiveWithAnyArgs().InsertRefDataAudit(default!, default, default!);
    }

    [TestMethod]
    public void Put_UsesSingleBoundaryAndReturnsFreshRevision()
    {
        var controller = Controller();
        security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 7).Returns(true);
        claims.GetUserFullDomainName(controller.User).Returns("domain\\actor");
        definitions.Save(Arg.Any<RefDataApiModel>(), false, "domain\\actor",
            Arg.Any<Func<int, bool>>(), Arg.Any<Action>(), false).Returns(call =>
            {
                call.Arg<Action>()();
                var saved = Document(); saved.Revision = "fresh"; return saved;
            });
        var result = (RefDataApiModel)((ObjectResult)controller.Put(Document())).Value!;
        Assert.AreEqual("fresh", result.Revision);
        Assert.IsTrue(result.CanEdit);
        projects.Received(1).ValidateProject(Arg.Any<ProjectApiModel>(), HttpRequestType.Put);
        projects.DidNotReceiveWithAnyArgs().UpdateProject(default!);
        components.DidNotReceiveWithAnyArgs().InsertRefDataAudit(default!, default, default!);
    }

    [TestMethod]
    public void Post_RequiresAdminAndUsesCreateBoundary()
    {
        Assert.Throws<Exception>(() => Controller().Post(Document()));
        definitions.Save(Arg.Any<RefDataApiModel>(), true, Arg.Any<string>(),
            Arg.Any<Func<int, bool>>(), Arg.Any<Action>(), false).Returns(Document());
        Assert.AreEqual(200, ((ObjectResult)Controller(true).Post(Document())).StatusCode);
    }
}
