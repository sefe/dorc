using System.Security.Claims;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using NSubstitute;

namespace Dorc.Core.Tests.Security;

[TestClass]
public class SharedResourceAccessPolicyTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    public void CreationAndUnattachedEditsRequireRole_NotEnvironmentAccess(bool powerUser, bool admin, bool allowed)
    {
        var user = new ClaimsPrincipal();
        var roles = Substitute.For<IRolePrivilegesChecker>();
        roles.IsPowerUser(user).Returns(powerUser);
        roles.IsAdmin(user).Returns(admin);
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        security.CanModifyEnvironment(user, Arg.Any<string>()).Returns(true);
        var policy = new SharedResourceAccessPolicy(roles, security);

        Assert.AreEqual(allowed, policy.CanCreateDefinition(user));
        Assert.AreEqual(allowed, policy.DefinitionAccess(user, []).IsAllowed);
        security.DidNotReceiveWithAnyArgs().CanModifyEnvironment(default!, default!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MappedEditRequiresEveryEnvironment_EvenForPrivilegedRoles(bool privileged)
    {
        var user = new ClaimsPrincipal();
        var roles = Substitute.For<IRolePrivilegesChecker>();
        roles.IsPowerUser(user).Returns(privileged);
        roles.IsAdmin(user).Returns(privileged);
        var security = Substitute.For<ISecurityPrivilegesChecker>();
        security.CanModifyEnvironment(user, "allowed").Returns(true);
        var policy = new SharedResourceAccessPolicy(roles, security);

        var denied = policy.DefinitionAccess(user, ["allowed", "denied"]);
        Assert.IsFalse(denied.IsAllowed);
        Assert.AreEqual("denied", denied.DeniedEnvironment);
        Assert.IsTrue(policy.DefinitionAccess(user, ["allowed"]).IsAllowed);
        Assert.IsTrue(policy.CanManageMembership(user, "allowed"));
        Assert.IsFalse(policy.CanManageMembership(user, "denied"));
        roles.DidNotReceiveWithAnyArgs().IsAdmin(default!);
        roles.DidNotReceiveWithAnyArgs().IsPowerUser(default!);
    }
}
