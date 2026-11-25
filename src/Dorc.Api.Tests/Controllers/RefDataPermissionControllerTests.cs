using System.Net;
using System.Net.Http.Json;
using Dorc.ApiModel;
using NSubstitute;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class RefDataPermissionControllerTests
{
    private static DorcApiFixture _fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _) => _fixture = new DorcApiFixture("dorc_api_perm");

    [ClassCleanup]
    public static void Cleanup() => _fixture?.Dispose();

    [TestMethod]
    public async Task AccessControlAndPermissions_EnforceOwnerInvariants_CrossObjectIdorProtection_AndRoleChecks()
    {
        using var adminClient = _fixture.CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");
        using var regularUserClient = _fixture.CreateClientForUser("regular-oid", "regular.user@sefe.eu", "User");

        var envAName = "AclEnvA_" + Guid.NewGuid().ToString("N")[..8];
        var envBName = "AclEnvB_" + Guid.NewGuid().ToString("N")[..8];

        var envAResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envAName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);
        var envA = await envAResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);

        var envBResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = envBName,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);
        var envB = await envBResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);

        Assert.IsNotNull(envA);
        Assert.IsNotNull(envB);

        var aclA = await adminClient.GetFromJsonAsync<AccessSecureApiModel>(
            $"/AccessControl?accessControlType=Environment&accessControlName={envAName}", _fixture.JsonOptions);
        var aclB = await adminClient.GetFromJsonAsync<AccessSecureApiModel>(
            $"/AccessControl?accessControlType=Environment&accessControlName={envBName}", _fixture.JsonOptions);
        Assert.IsNotNull(aclA);
        Assert.IsNotNull(aclB);

        // 1. Removing all owners from an environment is rejected with 400 BadRequest
        var removeAllOwnersPayload = new AccessSecureApiModel
        {
            Name = envAName,
            ObjectId = aclA.ObjectId,
            Type = AccessControlType.Environment,
            Privileges = Array.Empty<AccessControlApiModel>()
        };
        var noOwnersResp = await adminClient.PutAsJsonAsync("/AccessControl", removeAllOwnersPayload, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, noOwnersResp.StatusCode);

        // 2. Cross-object ACL entry ID tampering is rejected with 400 BadRequest
        var envBOwnerEntry = aclB.Privileges.First();
        var crossObjectPayload = new AccessSecureApiModel
        {
            Name = envAName,
            ObjectId = aclB.ObjectId,
            Type = AccessControlType.Environment,
            Privileges = new[]
            {
                aclA.Privileges.First(),
                new AccessControlApiModel
                {
                    Id = envBOwnerEntry.Id,
                    Name = envBOwnerEntry.Name,
                    Sid = envBOwnerEntry.Sid,
                    Pid = envBOwnerEntry.Pid,
                    Allow = 4,
                    Deny = 0
                }
            }
        };
        var crossObjectResp = await adminClient.PutAsJsonAsync("/AccessControl", crossObjectPayload, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, crossObjectResp.StatusCode);

        // 3. Regular user without write/owner rights cannot edit ACLs (403 Forbidden)
        var regularUserPutResp = await regularUserClient.PutAsJsonAsync("/AccessControl", aclA, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, regularUserPutResp.StatusCode);

        // 4. Non-Admin cannot create Production environment (403 Forbidden)
        using var powerUserClient = _fixture.CreateClientForUser("power-oid", "power.user@sefe.eu", "PowerUser");
        var prodCreateResp = await powerUserClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = "ProdEnv_" + Guid.NewGuid().ToString("N")[..6],
            EnvironmentIsProd = true,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "power.user@sefe.eu" }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, prodCreateResp.StatusCode);

        // 5. RefDataPermission CRUD role checks
        var permName = "CustomPerm_" + Guid.NewGuid().ToString("N")[..6];
        var regularPermResp = await regularUserClient.PostAsJsonAsync("/RefDataPermission", new PermissionDto
        {
            DisplayName = permName,
            PermissionName = permName
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.Forbidden, regularPermResp.StatusCode);

        var adminPermResp = await adminClient.PostAsJsonAsync("/RefDataPermission", new PermissionDto
        {
            DisplayName = permName,
            PermissionName = permName
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, adminPermResp.StatusCode);
    }

    [TestMethod]
    public void RefDataPermissionAndSqlPortsControllers_EnforceInjectedRolePrivilegesChecker()
    {
        var permSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.IPermissionsPersistentSource>();
        var roleChecker = NSubstitute.Substitute.For<Dorc.PersistentData.IRolePrivilegesChecker>();
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("TestAuth"));

        var permController = new Dorc.Api.Controllers.RefDataPermissionController(permSource, roleChecker)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user }
            }
        };

        var dto = new PermissionDto { Id = 1, DisplayName = "Disp", PermissionName = "Perm" };
        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(user), false);
        Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Post(dto)).StatusCode);
        Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Put(1, dto)).StatusCode);
        Assert.AreEqual(403, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Delete(1)).StatusCode);

        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(user), true);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Post(dto)).StatusCode);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Put(1, dto)).StatusCode);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Delete(1)).StatusCode);

        permSource.When(s => s.CreatePermission(dto)).Do(_ => throw new ArgumentException("Duplicate"));
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Post(dto)).StatusCode);

        permSource.When(s => s.UpdatePermission(1, dto)).Do(_ => throw new ArgumentException("Duplicate"));
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)permController.Put(1, dto)).StatusCode);

        var sqlPortsSource = NSubstitute.Substitute.For<Dorc.PersistentData.Sources.Interfaces.ISqlPortsPersistentSource>();
        var sqlController = new Dorc.Api.Controllers.RefDataSqlPortsController(sqlPortsSource, roleChecker)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user }
            }
        };
        var portDto = new SqlPortApiModel { InstanceName = "INST1", SqlPort = "1433" };
        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(user), false);
        Assert.Throws<UnauthorizedAccessException>(() => sqlController.Post(portDto));
        Assert.Throws<UnauthorizedAccessException>(() => sqlController.Delete(portDto));

        NSubstitute.SubstituteExtensions.Returns(roleChecker.IsAdmin(user), true);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Post(portDto)).StatusCode);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Delete(portDto)).StatusCode);

        // Null/empty/oversized InstanceName (>250) or SqlPort (>10) returns 400 BadRequest before persistence
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Post(null!)).StatusCode);
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Post(new SqlPortApiModel { InstanceName = "", SqlPort = "1433" })).StatusCode);
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Post(new SqlPortApiModel { InstanceName = new string('A', 251), SqlPort = "1433" })).StatusCode);
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)sqlController.Post(new SqlPortApiModel { InstanceName = "INST1", SqlPort = "12345678901" })).StatusCode);

        var realSqlPortsSource = new Dorc.PersistentData.Sources.SqlPortsPersistentSource(
            NSubstitute.Substitute.For<Dorc.PersistentData.Contexts.IDeploymentContextFactory>());
        Assert.Throws<ArgumentException>(() => realSqlPortsSource.CreateSqlPort(null!));
        Assert.Throws<ArgumentException>(() => realSqlPortsSource.CreateSqlPort(new SqlPortApiModel { InstanceName = " ", SqlPort = "1433" }));
        Assert.Throws<ArgumentException>(() => realSqlPortsSource.CreateSqlPort(new SqlPortApiModel { InstanceName = new string('X', 251), SqlPort = "1433" }));
        Assert.Throws<ArgumentException>(() => realSqlPortsSource.CreateSqlPort(new SqlPortApiModel { InstanceName = "INST1", SqlPort = new string('9', 11) }));
    }
}
