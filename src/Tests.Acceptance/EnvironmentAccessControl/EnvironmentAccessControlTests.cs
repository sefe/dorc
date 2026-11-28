using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.EnvironmentAccessControl
{
    [Binding]
    public class EnvironmentAccessControlTests
    {
        private bool _ownerIntegrityExecuted;
        private bool _privilegeLifecycleExecuted;

        [Given("the live parent environment has an access control list with an assigned owner")]
        public void GivenTheLiveParentEnvironmentHasAnAclWithAssignedOwner()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user searches Active Directory users and groups and attempts to strip all Owner privileges from the environment ACL")]
        public void WhenTheUserSearchesActiveDirectoryAndAttemptsToStripAllOwnerPrivileges()
        {
            GetRefDataAndAccessControl_OAuthActiveDirectoryAndEnvironmentAcl_EnforcesOwnerIntegrity();
            _ownerIntegrityExecuted = true;
        }

        [Then("DirectorySearch returns matching users and groups with 200 OK")]
        [Then("PUT AccessControl rejects removing the last environment owner with 400 BadRequest")]
        public void ThenDirectorySearchAndOwnerIntegritySucceed()
        {
            Assert.IsTrue(_ownerIntegrityExecuted);
        }

        [Given("registered users, roles, groups, and a temporary environment are available for access control testing")]
        public void GivenRegisteredUsersRolesGroupsAndTemporaryEnvironmentAreAvailable()
        {
            AssertLiveEnvironmentEnabled();
        }

        [When("the user validates account existence, grants and revokes environment ACL privileges, and assigns and deletes custom database permissions")]
        public void WhenTheUserValidatesAccountsAndMutatesPrivileges()
        {
            SearchUsersAndMutateEnvironmentAccessControl_AccountExistenceRefDataUsersAndPrivilegeLifecycle_VerifiesDirectoryLookupAndPrivilegeGrantRevoke();
            _privilegeLifecycleExecuted = true;
        }

        [Then("Account existence checks validate Windows users and groups and reject invalid account types with 400 BadRequest")]
        [Then("environment ACL privileges and custom database permissions are persisted, verified, and revoked cleanly")]
        public void ThenAccountExistenceAndPrivilegeLifecycleSucceed()
        {
            Assert.IsTrue(_privilegeLifecycleExecuted);
        }

        public void GetRefDataAndAccessControl_OAuthActiveDirectoryAndEnvironmentAcl_EnforcesOwnerIntegrity()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

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

#pragma warning disable CS0618
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
                        Sid = p.Sid,
                        Allow = p.Allow & ~4,
                        Deny = p.Deny
                    })
                    .ToList()
            };
#pragma warning restore CS0618
            var badAclResp = caller.ExecuteRaw("AccessControl", Method.Put, body: stripped);
            Assert.AreEqual(HttpStatusCode.BadRequest, badAclResp.StatusCode, "Removing the last owner must return 400 BadRequest.");
        }

        public void SearchUsersAndMutateEnvironmentAccessControl_AccountExistenceRefDataUsersAndPrivilegeLifecycle_VerifiesDirectoryLookupAndPrivilegeGrantRevoke()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var allUsersResp = caller.ExecuteRaw("RefDataUsers", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, allUsersResp.StatusCode, allUsersResp.Content);
            var allUsers = DorcApiClient.Deserialize<List<UserApiModel>>(allUsersResp.Content) ?? new List<UserApiModel>();
            Assert.IsNotEmpty(allUsers, "Expected GET /RefDataUsers to return registered users.");

            var granularityResp = caller.ExecuteRaw("RefDataUsers/ByGranularity", Method.Get, new Dictionary<string, string>
            {
                ["granularity"] = "1"
            });
            Assert.AreEqual(HttpStatusCode.OK, granularityResp.StatusCode, granularityResp.Content);
            var granularityUsers = DorcApiClient.Deserialize<List<UserApiModel>>(granularityResp.Content) ?? new List<UserApiModel>();
            Assert.IsNotEmpty(granularityUsers);

            var sampleUser = allUsers.First(u => !string.IsNullOrWhiteSpace(u.LanId));
            var byNameResp = caller.ExecuteRaw("RefDataUsers/ByName", Method.Get, new Dictionary<string, string>
            {
                ["userName"] = sampleUser.LanId
            });
            Assert.AreEqual(HttpStatusCode.OK, byNameResp.StatusCode, byNameResp.Content);
            var foundByName = DorcApiClient.Deserialize<UserApiModel>(byNameResp.Content);
            Assert.IsNotNull(foundByName);
            Assert.AreEqual(sampleUser.LanId, foundByName.LanId);

            var parentEnv = GetEnvironmentByName(caller, LiveParentEnvName);
            Assert.IsNotNull(parentEnv);

            var byEnvResp = caller.ExecuteRaw("RefDataUsers/ByEnvironmentId", Method.Get, new Dictionary<string, string>
            {
                ["environmentId"] = parentEnv.EnvironmentId.ToString(),
                ["userAccountType"] = "Endur"
            });
            Assert.AreEqual(HttpStatusCode.OK, byEnvResp.StatusCode, byEnvResp.Content);

            var nonExistentUserResp = caller.ExecuteRaw("Account/userExists", Method.Get, new Dictionary<string, string>
            {
                ["userLanId"] = "NonExistentUser_DOrcReg_99999",
                ["accountType"] = "Windows"
            });
            Assert.AreEqual(HttpStatusCode.OK, nonExistentUserResp.StatusCode, nonExistentUserResp.Content);
            Assert.IsFalse(DorcApiClient.Deserialize<bool>(nonExistentUserResp.Content), "Non-existent Windows user must return false.");

            var nonExistentGroupResp = caller.ExecuteRaw("Account/groupExists", Method.Get, new Dictionary<string, string>
            {
                ["groupLanId"] = "NonExistentGroup_DOrcReg_99999",
                ["accountType"] = "Windows"
            });
            Assert.AreEqual(HttpStatusCode.OK, nonExistentGroupResp.StatusCode, nonExistentGroupResp.Content);
            Assert.IsFalse(DorcApiClient.Deserialize<bool>(nonExistentGroupResp.Content), "Non-existent Windows group must return false.");

            var invalidUserAccountTypeResp = caller.ExecuteRaw("Account/userExists", Method.Get, new Dictionary<string, string>
            {
                ["userLanId"] = "SVC-LIVE-TFSTEST",
                ["accountType"] = "InvalidAccountType"
            });
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidUserAccountTypeResp.StatusCode, "Invalid accountType on Account/userExists must return 400 BadRequest.");

            var invalidGroupAccountTypeResp = caller.ExecuteRaw("Account/groupExists", Method.Get, new Dictionary<string, string>
            {
                ["groupLanId"] = "DOrc",
                ["accountType"] = "InvalidAccountType"
            });
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidGroupAccountTypeResp.StatusCode, "Invalid accountType on Account/groupExists must return 400 BadRequest.");

            var rolesResp = caller.ExecuteRaw("RefDataRoles", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, rolesResp.StatusCode, $"GET /RefDataRoles failed: {rolesResp.Content}");
            var roles = DorcApiClient.Deserialize<List<string>>(rolesResp.Content);
            Assert.IsNotNull(roles, "Expected non-null role list from GET /RefDataRoles.");

            var groupsResp = caller.ExecuteRaw("RefDataGroups", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, groupsResp.StatusCode, $"GET /RefDataGroups failed: {groupsResp.Content}");
            var groups = DorcApiClient.Deserialize<List<GroupApiModel>>(groupsResp.Content) ?? new List<GroupApiModel>();
            if (groups.Count > 0 && !string.IsNullOrWhiteSpace(groups[0].GroupName))
            {
                var groupByNameResp = caller.ExecuteRaw($"RefDataGroups/{Uri.EscapeDataString(groups[0].GroupName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, groupByNameResp.StatusCode, $"GET /RefDataGroups/{groups[0].GroupName} failed: {groupByNameResp.Content}");
                var singleGroup = DorcApiClient.Deserialize<GroupApiModel>(groupByNameResp.Content);
                Assert.IsNotNull(singleGroup);
                Assert.AreEqual(groups[0].GroupName, singleGroup.GroupName);
            }
            var unknownGroupResp = caller.ExecuteRaw("RefDataGroups/NonExistentGroup_DOrcReg_99999", Method.Get);
            Assert.AreEqual(HttpStatusCode.NotFound, unknownGroupResp.StatusCode, "GET /RefDataGroups/{unknown} must return 404 NotFound.");

            var searchUsersResp = caller.ExecuteRaw("AccessControl/SearchUsers", Method.Get, new Dictionary<string, string>
            {
                ["search"] = "drepin"
            });
            Assert.AreEqual(HttpStatusCode.OK, searchUsersResp.StatusCode, searchUsersResp.Content);
            var searchResults = DorcApiClient.Deserialize<List<UserElementApiModel>>(searchUsersResp.Content) ?? new List<UserElementApiModel>();
            Assert.IsNotEmpty(searchResults, "Expected GET /AccessControl/SearchUsers?search=drepin to return directory matches.");
            var targetPrincipal = searchResults.First(u => !string.IsNullOrWhiteSpace(u.Pid) && !string.IsNullOrWhiteSpace(u.Username));
            Assert.IsFalse(string.IsNullOrWhiteSpace(targetPrincipal.DisplayName));

            var existingUserResp = caller.ExecuteRaw("Account/userExists", Method.Get, new Dictionary<string, string>
            {
                ["userLanId"] = "SVC-LIVE-TFSTEST",
                ["accountType"] = "Windows"
            });
            Assert.AreEqual(HttpStatusCode.OK, existingUserResp.StatusCode, existingUserResp.Content);
            Assert.IsTrue(DorcApiClient.Deserialize<bool>(existingUserResp.Content), "Expected Account/userExists to return true for 'SVC-LIVE-TFSTEST'.");

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var tempEnvName = $"DOrc_Live_AclEnv_{suffix}";
            int? tempEnvId = null;
            int? customPermId = null;

            try
            {
                var createEnvResp = caller.ExecuteRaw("RefDataEnvironments", Method.Post, body: new EnvironmentApiModel
                {
                    EnvironmentName = tempEnvName,
                    EnvironmentSecure = false,
                    EnvironmentIsProd = false,
                    Details = new EnvironmentDetailsApiModel
                    {
                        EnvironmentOwner = parentEnv.Details?.EnvironmentOwner ?? Environment.UserName,
                        Description = "Temporary environment for AccessControl grant/revoke test",
                        FileShare = string.Empty,
                        ThinClient = string.Empty,
                        RestoredFromSourceDb = string.Empty,
                        Notes = "Automated live test"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, createEnvResp.StatusCode, createEnvResp.Content);
                var createdEnv = DorcApiClient.Deserialize<EnvironmentApiModel>(createEnvResp.Content);
                Assert.IsNotNull(createdEnv);
                tempEnvId = createdEnv.EnvironmentId;

                var getAclResp = caller.ExecuteRaw("AccessControl", Method.Get, new Dictionary<string, string>
                {
                    ["accessControlType"] = AccessControlType.Environment.ToString(),
                    ["accessControlName"] = tempEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, getAclResp.StatusCode, getAclResp.Content);
                var acl = DorcApiClient.Deserialize<AccessSecureApiModel>(getAclResp.Content);
                Assert.IsNotNull(acl);

                var privilegesWithGrant = (acl.Privileges ?? Array.Empty<AccessControlApiModel>())
                    .Where(p => !string.Equals(p.Pid, targetPrincipal.Pid, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var grantName = targetPrincipal.IsGroup
                    ? targetPrincipal.DisplayName
                    : $"EUROPE\\{targetPrincipal.Username}";
                privilegesWithGrant.Add(new AccessControlApiModel
                {
                    Id = 0,
                    Name = grantName,
                    Pid = targetPrincipal.Pid,
                    Allow = 1,
                    Deny = 0
                });

                var putGrantResp = caller.ExecuteRaw("AccessControl", Method.Put, body: new AccessSecureApiModel
                {
                    Name = acl.Name,
                    ObjectId = acl.ObjectId,
                    Type = acl.Type,
                    UserEditable = acl.UserEditable,
                    Privileges = privilegesWithGrant
                });
                Assert.AreEqual(HttpStatusCode.OK, putGrantResp.StatusCode, $"PUT /AccessControl (grant) failed: {putGrantResp.Content}");
                var grantedAcl = DorcApiClient.Deserialize<AccessSecureApiModel>(putGrantResp.Content);
                Assert.IsNotNull(grantedAcl);
                Assert.IsNotNull(grantedAcl.Privileges);
                var grantedPrivileges = grantedAcl.Privileges;
                var grantedEntry = grantedPrivileges.FirstOrDefault(p =>
                    string.Equals(p.Pid, targetPrincipal.Pid, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(grantedEntry, $"Expected PUT /AccessControl to persist privilege for '{targetPrincipal.Pid}'.");
                Assert.AreEqual(1, grantedEntry.Allow);

                var privilegesAfterRevoke = grantedPrivileges
                    .Where(p => !string.Equals(p.Pid, targetPrincipal.Pid, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var putRevokeResp = caller.ExecuteRaw("AccessControl", Method.Put, body: new AccessSecureApiModel
                {
                    Name = grantedAcl.Name,
                    ObjectId = grantedAcl.ObjectId,
                    Type = grantedAcl.Type,
                    UserEditable = grantedAcl.UserEditable,
                    Privileges = privilegesAfterRevoke
                });
                Assert.AreEqual(HttpStatusCode.OK, putRevokeResp.StatusCode, $"PUT /AccessControl (revoke) failed: {putRevokeResp.Content}");
                var revokedAcl = DorcApiClient.Deserialize<AccessSecureApiModel>(putRevokeResp.Content);
                Assert.IsNotNull(revokedAcl);
                Assert.IsNotNull(revokedAcl.Privileges);
                var revokedPrivileges = revokedAcl.Privileges;
                Assert.IsFalse(
                    revokedPrivileges.Any(p =>
                        string.Equals(p.Pid, targetPrincipal.Pid, StringComparison.OrdinalIgnoreCase)),
                    $"Expected PUT /AccessControl to remove revoked privilege for '{targetPrincipal.Pid}'.");

                // RefDataPermission full CRUD lifecycle + duplicate rejection (Postman endpoints)
                var permName = $"Live_Perm_{suffix}";
                var postPermResp = caller.ExecuteRaw("RefDataPermission", Method.Post, body: new PermissionDto
                {
                    PermissionName = permName,
                    DisplayName = $"Live Permission {suffix}"
                });
                Assert.AreEqual(HttpStatusCode.OK, postPermResp.StatusCode, $"POST /RefDataPermission failed: {postPermResp.Content}");

                var dupPermResp = caller.ExecuteRaw("RefDataPermission", Method.Post, body: new PermissionDto
                {
                    PermissionName = $"{permName}_Dup",
                    DisplayName = $"Live Permission {suffix}"
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, dupPermResp.StatusCode, "Duplicate POST /RefDataPermission must return 400 BadRequest.");

                var getPermsResp = caller.ExecuteRaw("RefDataPermission", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getPermsResp.StatusCode, getPermsResp.Content);
                var perms = DorcApiClient.Deserialize<List<PermissionDto>>(getPermsResp.Content) ?? new List<PermissionDto>();
                var createdPerm = perms.FirstOrDefault(p => string.Equals(p.PermissionName, permName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(createdPerm, $"Expected GET /RefDataPermission to include '{permName}'.");
                customPermId = createdPerm.Id;

                var putPermResp = caller.ExecuteRaw(
                    "RefDataPermission",
                    Method.Put,
                    new Dictionary<string, string> { ["id"] = customPermId.Value.ToString() },
                    body: new PermissionDto
                    {
                        Id = customPermId.Value,
                        PermissionName = permName,
                        DisplayName = $"Updated Permission {suffix}"
                    });
                Assert.AreEqual(HttpStatusCode.OK, putPermResp.StatusCode, $"PUT /RefDataPermission failed: {putPermResp.Content}");

                var getPermsAfterPutResp = caller.ExecuteRaw("RefDataPermission", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getPermsAfterPutResp.StatusCode, getPermsAfterPutResp.Content);
                var permsAfterPut = DorcApiClient.Deserialize<List<PermissionDto>>(getPermsAfterPutResp.Content) ?? new List<PermissionDto>();
                Assert.IsTrue(customPermId.HasValue, "Expected customPermId to have a value.");
                var customPermIdValue = customPermId.Value;
                var updatedPerm = permsAfterPut.FirstOrDefault(p => p.Id == customPermIdValue);
                Assert.IsNotNull(updatedPerm);
                Assert.AreEqual($"Updated Permission {suffix}", updatedPerm.DisplayName);

                // Verify RefDataUserPermissions lifecycle (GET all user perms, 400 for unattached DB, attach DB, PUT/GET/DELETE user DB permission)
                var allUserPermsResp = caller.ExecuteRaw("RefDataUserPermissions", Method.Get, new Dictionary<string, string>
                {
                    ["userId"] = sampleUser.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, allUserPermsResp.StatusCode, $"GET /RefDataUserPermissions?userId={sampleUser.Id} failed: {allUserPermsResp.Content}");

                var unattachedUserPermResp = caller.ExecuteRaw("RefDataUserPermissions", Method.Get, new Dictionary<string, string>
                {
                    ["userId"] = sampleUser.Id.ToString(),
                    ["databaseId"] = "99999999",
                    ["envId"] = tempEnvId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.BadRequest, unattachedUserPermResp.StatusCode, "GET /RefDataUserPermissions for unattached databaseId must return 400 BadRequest.");

                var permDbName = $"DOrc_Live_PermDB_{suffix}";
                var createPermDbResp = caller.ExecuteRaw("RefDataDatabases", Method.Post, body: new DatabaseApiModel
                {
                    Name = permDbName,
                    ServerName = $"SQLPERM_{suffix}",
                    DatabaseType = DatabaseType.SqlServer,
                    AdGroup = "everyone"
                });
                Assert.AreEqual(HttpStatusCode.OK, createPermDbResp.StatusCode, createPermDbResp.Content);
                var createdPermDb = DorcApiClient.Deserialize<DatabaseApiModel>(createPermDbResp.Content);
                Assert.IsNotNull(createdPermDb);
                Assert.IsGreaterThan(0, createdPermDb.Id);
                var permDbId = createdPermDb.Id;

                try
                {
                    var attachPermDbResp = caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = tempEnvId.Value.ToString(),
                        ["componentId"] = permDbId.ToString(),
                        ["action"] = "attach",
                        ["component"] = "database"
                    });
                    Assert.AreEqual(HttpStatusCode.OK, attachPermDbResp.StatusCode, attachPermDbResp.Content);

                    var putUserPermResp = caller.ExecuteRaw("RefDataUserPermissions", Method.Put, new Dictionary<string, string>
                    {
                        ["userId"] = sampleUser.Id.ToString(),
                        ["permissionId"] = customPermIdValue.ToString(),
                        ["dbId"] = permDbId.ToString(),
                        ["envId"] = tempEnvId.Value.ToString()
                    });
                    Assert.AreEqual(HttpStatusCode.OK, putUserPermResp.StatusCode, $"PUT /RefDataUserPermissions failed: {putUserPermResp.Content}");

                    var getDbUserPermsResp = caller.ExecuteRaw("RefDataUserPermissions", Method.Get, new Dictionary<string, string>
                    {
                        ["userId"] = sampleUser.Id.ToString(),
                        ["databaseId"] = permDbId.ToString(),
                        ["envId"] = tempEnvId.Value.ToString()
                    });
                    Assert.AreEqual(HttpStatusCode.OK, getDbUserPermsResp.StatusCode, getDbUserPermsResp.Content);
                    var dbUserPerms = DorcApiClient.Deserialize<List<UserPermDto>>(getDbUserPermsResp.Content) ?? new List<UserPermDto>();
                    Assert.IsTrue(dbUserPerms.Any(up => up.Id == customPermIdValue), "Expected GET /RefDataUserPermissions to include the newly granted database permission.");

                    var delUserPermResp = caller.ExecuteRaw("RefDataUserPermissions", Method.Delete, new Dictionary<string, string>
                    {
                        ["userId"] = sampleUser.Id.ToString(),
                        ["permissionId"] = customPermIdValue.ToString(),
                        ["dbId"] = permDbId.ToString(),
                        ["envId"] = tempEnvId.Value.ToString()
                    });
                    Assert.AreEqual(HttpStatusCode.OK, delUserPermResp.StatusCode, $"DELETE /RefDataUserPermissions failed: {delUserPermResp.Content}");
                }
                finally
                {
                    caller.ExecuteRaw("RefDataEnvironmentsDetails", Method.Put, new Dictionary<string, string>
                    {
                        ["envId"] = tempEnvId.Value.ToString(),
                        ["componentId"] = permDbId.ToString(),
                        ["action"] = "detach",
                        ["component"] = "database"
                    });
                    caller.ExecuteRaw("RefDataDatabases", Method.Delete, new Dictionary<string, string>
                    {
                        ["databaseId"] = permDbId.ToString()
                    });
                }

                var deletePermResp = caller.ExecuteRaw("RefDataPermission", Method.Delete, new Dictionary<string, string>
                {
                    ["id"] = customPermIdValue.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, deletePermResp.StatusCode, $"DELETE /RefDataPermission failed: {deletePermResp.Content}");
                customPermId = null;

                // Verify GET & PUT /RefDataEnvironmentsUsers/owner/{id} (including AD fallback searcher resolution)
                var getOwnerResp = caller.ExecuteRaw($"RefDataEnvironmentsUsers/owner/{tempEnvId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getOwnerResp.StatusCode, $"GET /RefDataEnvironmentsUsers/owner/{tempEnvId.Value} failed: {getOwnerResp.Content}");

                var putOwnerResp = caller.ExecuteRaw($"RefDataEnvironmentsUsers/owner/{tempEnvId.Value}", Method.Put, body: new EnvironmentOwnerApiModel
                {
                    DisplayName = targetPrincipal.DisplayName,
                    Username = targetPrincipal.Username
                });
                Assert.AreEqual(HttpStatusCode.OK, putOwnerResp.StatusCode, $"PUT /RefDataEnvironmentsUsers/owner/{tempEnvId.Value} failed: {putOwnerResp.Content}");

                // Verify self-service PUT /ResetAppPassword (returns 200 OK with Result=false when no application DB is attached)
                var selfResetPwdResp = caller.ExecuteRaw("ResetAppPassword", Method.Put, new Dictionary<string, string>
                {
                    ["envFilter"] = "Endur",
                    ["envName"] = tempEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, selfResetPwdResp.StatusCode, $"Self-service PUT /ResetAppPassword failed: {selfResetPwdResp.Content}");
                var selfResetResult = DorcApiClient.Deserialize<ApiBoolResult>(selfResetPwdResp.Content);
                Assert.IsNotNull(selfResetResult);
                Assert.IsFalse(selfResetResult.Result, "Expected self-service PUT /ResetAppPassword on environment without attached Endur DB to return Result=false.");
            }
            finally
            {
                var allPermsResp = caller.ExecuteRaw("RefDataPermission", Method.Get);
                if (allPermsResp.StatusCode == HttpStatusCode.OK)
                {
                    var allPerms = DorcApiClient.Deserialize<List<PermissionDto>>(allPermsResp.Content) ?? new List<PermissionDto>();
                    foreach (var p in allPerms.Where(p => p.PermissionName != null && p.PermissionName.StartsWith("Live_Perm_", StringComparison.OrdinalIgnoreCase)))
                    {
                        caller.ExecuteRaw("RefDataPermission", Method.Delete, new Dictionary<string, string>
                        {
                            ["id"] = p.Id.ToString()
                        });
                    }
                }
                if (tempEnvId.HasValue)
                {
                    caller.ExecuteRaw("RefDataEnvironments", Method.Delete, body: new EnvironmentApiModel
                    {
                        EnvironmentId = tempEnvId.Value,
                        EnvironmentName = tempEnvName
                    });
                }
            }
        }
    }
}
