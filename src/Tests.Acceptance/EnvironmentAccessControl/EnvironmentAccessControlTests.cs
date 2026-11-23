using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.LiveEnvironmentFixture;

namespace Tests.Acceptance.EnvironmentAccessControl
{
    [TestClass]
    [TestCategory("EnvironmentAccessControl")]
    public class EnvironmentAccessControlTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void GetRefDataAndAccessControl_OAuthActiveDirectoryAndEnvironmentAcl_EnforcesOwnerIntegrity()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var cfgResp = caller.ExecuteRaw("ApiConfig", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, cfgResp.StatusCode, cfgResp.Content);
            StringAssert.Contains(cfgResp.Content ?? string.Empty, "AuthenticationScheme");

            var metaResp = caller.ExecuteRaw("Metadata", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, metaResp.StatusCode, metaResp.Content);
            Assert.IsFalse(string.IsNullOrWhiteSpace(metaResp.Content));

            var usersResp = caller.ExecuteRaw("DirectorySearch/users", Method.Get, new Dictionary<string, string>
            {
                ["userSearchCriteria"] = "apolezhaev"
            });
            Assert.AreEqual(HttpStatusCode.OK, usersResp.StatusCode, usersResp.Content);

            var groupsResp = caller.ExecuteRaw("DirectorySearch/groups", Method.Get, new Dictionary<string, string>
            {
                ["groupSearchCriteria"] = "DOrc"
            });
            Assert.AreEqual(HttpStatusCode.OK, groupsResp.StatusCode, groupsResp.Content);

            var parentEnv = GetEnvironmentByName(caller, LiveParentEnvName);
            Assert.IsNotNull(parentEnv);

            var aclResp = caller.ExecuteRaw("AccessControl", Method.Get, new Dictionary<string, string>
            {
                ["accessControlType"] = AccessControlType.Environment.ToString(),
                ["accessControlName"] = LiveParentEnvName
            });
            Assert.AreEqual(HttpStatusCode.OK, aclResp.StatusCode, aclResp.Content);
            var acl = DorcApiClient.Deserialize<AccessSecureApiModel>(aclResp.Content);
            Assert.IsNotNull(acl);

            var stripped = new AccessSecureApiModel
            {
                Name = acl.Name,
                ObjectId = acl.ObjectId,
                Type = acl.Type,
                UserEditable = acl.UserEditable,
                Privileges = (acl.Privileges ?? Array.Empty<AccessControlApiModel>())
                    .Select(p => new AccessControlApiModel
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Pid = p.Pid,
                        Allow = p.Allow & ~4,
                        Deny = p.Deny
                    })
                    .ToList()
            };
            var badAclResp = caller.ExecuteRaw("AccessControl", Method.Put, body: stripped);
            Assert.AreEqual(HttpStatusCode.BadRequest, badAclResp.StatusCode, "Removing the last owner must return 400 BadRequest.");
        }
    }
}
