using System.Net;
using System.Net.Http.Json;
using Dorc.ApiModel;

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
}
