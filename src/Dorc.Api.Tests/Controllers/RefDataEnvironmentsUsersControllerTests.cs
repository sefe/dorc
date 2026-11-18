using System.Security.Claims;
using Dorc.Api.Controllers;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
public class RefDataEnvironmentsUsersControllerTests
{
    [TestMethod]
    public void SupportsOwnerManagementAndActiveDirectorySearch()
    {
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        var environments = Substitute.For<IEnvironmentsPersistentSource>();
        var ad = Substitute.For<IActiveDirectorySearcher>();
        var controller = new RefDataEnvironmentsUsersController(security, environments, ad)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        environments.GetEnvironmentOwnerId(10).Returns("S-1-5-21-1");
        ad.GetUserDataById("S-1-5-21-1").Returns(new UserElementApiModel { DisplayName = "Alice" });
        var ownerResult = (ObjectResult)controller.GetOwner(10);
        Assert.AreEqual(200, ownerResult.StatusCode);
        Assert.AreEqual("Alice", ((EnvironmentOwnerApiModel)ownerResult.Value!).DisplayName);

        Assert.AreEqual(403, ((ObjectResult)controller.UpdateOwner(99, new() { DisplayName = "Bob" })).StatusCode);
        environments.GetEnvironment(10, Arg.Any<ClaimsPrincipal>()).Returns(new EnvironmentApiModel { EnvironmentName = "DV02" });
        security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DV02").Returns(false);
        Assert.AreEqual(403, ((ObjectResult)controller.UpdateOwner(10, new() { DisplayName = "Bob" })).StatusCode);

        security.IsEnvironmentOwnerOrAdmin(Arg.Any<ClaimsPrincipal>(), "DV02").Returns(true);
        var bob = new UserElementApiModel { DisplayName = "Bob", Sid = "S-1-5-21-2" };
        ad.GetUserData("Bob").Returns(bob);
        environments.SetEnvironmentOwner(Arg.Any<ClaimsPrincipal>(), 10, bob).Returns(true);
        Assert.AreEqual(200, ((ObjectResult)controller.UpdateOwner(10, new() { DisplayName = "Bob" })).StatusCode);

        ad.Search("bob").Returns([bob]);
        Assert.AreEqual(200, ((ObjectResult)controller.SearchUsers("bob")).StatusCode);
    }
}
