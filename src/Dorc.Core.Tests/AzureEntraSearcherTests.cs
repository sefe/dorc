using Dorc.Core.Tests.Graph;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class AzureEntraSearcherTests
    {
        // P-1: user search by name returns a populated UserElementApiModel.
        [TestMethod]
        public void P1_Search_FindsUserByDisplayName()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """
                {
                    "value": [{
                        "id": "11111111-1111-1111-1111-111111111111",
                        "displayName": "Alice Smith",
                        "userPrincipalName": "alice@contoso.com",
                        "mail": "alice@contoso.com",
                        "accountEnabled": true,
                        "onPremisesSecurityIdentifier": "S-1-5-21-100-200-300-1001",
                        "onPremisesSamAccountName": "alice"
                    }]
                }
                """)
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            var searcher = NewSearcher(handler);
            var results = searcher.Search("alice");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Alice Smith", results[0].DisplayName);
            Assert.AreEqual("11111111-1111-1111-1111-111111111111", results[0].Pid);
#pragma warning disable CS0618 // intentionally exercises legacy Sid field for dual-ID parity
            Assert.AreEqual("S-1-5-21-100-200-300-1001", results[0].Sid);
#pragma warning restore CS0618
            Assert.IsFalse(results[0].IsGroup);
        }

        // P-2: group search by name returns a populated UserElementApiModel with IsGroup=true.
        [TestMethod]
        public void P2_Search_FindsGroupByDisplayName()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """{ "value": [] }""")
                .MapPath(HttpMethod.Get, "/groups", """
                {
                    "value": [{
                        "id": "22222222-2222-2222-2222-222222222222",
                        "displayName": "Admins",
                        "mailNickname": "admins",
                        "mail": "admins@contoso.com",
                        "onPremisesSecurityIdentifier": "S-1-5-21-100-200-300-2001"
                    }]
                }
                """);

            var searcher = NewSearcher(handler);
            var results = searcher.Search("admins");

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].IsGroup);
            Assert.AreEqual("22222222-2222-2222-2222-222222222222", results[0].Pid);
#pragma warning disable CS0618
            Assert.AreEqual("S-1-5-21-100-200-300-2001", results[0].Sid);
#pragma warning restore CS0618
        }

        // P-3: GetUserDataById resolves an Entra object id via direct /users/{id} lookup.
        [TestMethod]
        public void P3_GetUserDataById_ResolvesByEntraId()
        {
            const string entraId = "33333333-3333-3333-3333-333333333333";
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, $"/users/{entraId}", $$"""
                {
                    "id": "{{entraId}}",
                    "displayName": "Bob Jones",
                    "userPrincipalName": "bob@contoso.com",
                    "mail": "bob@contoso.com",
                    "accountEnabled": true
                }
                """);

            var searcher = NewSearcher(handler);
            var user = searcher.GetUserDataById(entraId);

            Assert.AreEqual("Bob Jones", user.DisplayName);
            Assert.AreEqual(entraId, user.Pid);
            Assert.IsFalse(user.IsGroup);
        }

        // P-4: SID-shaped input — direct lookup 404s, filter fallback hits.
        // SC-10 acceptance: existing AccessControl.Sid rows continue to resolve via
        // onPremisesSecurityIdentifier when Entra Connect populates it.
        [TestMethod]
        public void P4_GetUserDataById_ResolvesByAdSidViaOnPremisesFilter()
        {
            const string sid = "S-1-5-21-100-200-300-1234";
            var handler = new MockHttpHandler()
                // direct /users/{sid} 404s (sid is not a GUID)
                .MapPath(HttpMethod.Get, $"/users/{sid}", "{}", System.Net.HttpStatusCode.NotFound)
                // filter fallback succeeds
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSecurityIdentifier", $$"""
                {
                    "value": [{
                        "id": "44444444-4444-4444-4444-444444444444",
                        "displayName": "Carol Lee",
                        "userPrincipalName": "carol@contoso.com",
                        "mail": "carol@contoso.com",
                        "accountEnabled": true,
                        "onPremisesSecurityIdentifier": "{{sid}}"
                    }]
                }
                """);

            var searcher = NewSearcher(handler);
            var user = searcher.GetUserDataById(sid);

            Assert.AreEqual("Carol Lee", user.DisplayName);
            Assert.AreEqual("44444444-4444-4444-4444-444444444444", user.Pid);
#pragma warning disable CS0618
            Assert.AreEqual(sid, user.Sid, "Sid round-trips so legacy AccessControl rows keep matching");
#pragma warning restore CS0618
        }

        // P-4 (group variant): user filter misses, group filter hits.
        [TestMethod]
        public void P4_GetUserDataById_ResolvesGroupByAdSidWhenUserMisses()
        {
            const string sid = "S-1-5-21-100-200-300-9999";
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, $"/users/{sid}", "{}", System.Net.HttpStatusCode.NotFound)
                .MapPath(HttpMethod.Get, $"/groups/{sid}", "{}", System.Net.HttpStatusCode.NotFound)
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSecurityIdentifier", """{ "value": [] }""")
                .MapFilter(HttpMethod.Get, "/groups", "onPremisesSecurityIdentifier", $$"""
                {
                    "value": [{
                        "id": "55555555-5555-5555-5555-555555555555",
                        "displayName": "Domain Admins",
                        "mailNickname": "domainadmins",
                        "mail": null,
                        "onPremisesSecurityIdentifier": "{{sid}}"
                    }]
                }
                """);

            var searcher = NewSearcher(handler);
            var result = searcher.GetUserDataById(sid);

            Assert.IsTrue(result.IsGroup);
            Assert.AreEqual("Domain Admins", result.DisplayName);
        }

        // P-5: sAMAccountName resolution before checkMemberGroups.
        [TestMethod]
        public void P5_GetGroupSidIfUserIsMemberRecursive_ResolvesUserBySamAccountName()
        {
            var handler = new MockHttpHandler()
                // First call: filter by onPremisesSamAccountName/userPrincipalName
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", """
                {
                    "value": [{ "id": "66666666-6666-6666-6666-666666666666" }]
                }
                """)
                // Second call: group lookup by displayName
                .MapPath(HttpMethod.Get, "/groups", """
                {
                    "value": [{ "id": "77777777-7777-7777-7777-777777777777" }]
                }
                """)
                // Third call: checkMemberGroups for the resolved user
                .MapPath(HttpMethod.Post, "/checkMemberGroups", """
                {
                    "value": [ "77777777-7777-7777-7777-777777777777" ]
                }
                """);

            var searcher = NewSearcher(handler);
            var groupId = searcher.GetGroupSidIfUserIsMemberRecursive("alice", "Admins", "contoso.com");

            Assert.AreEqual("77777777-7777-7777-7777-777777777777", groupId);
        }

        // P-5: DOMAIN\\ prefix is stripped before the filter call.
        [TestMethod]
        public void P5_GetGroupSidIfUserIsMemberRecursive_StripsDomainPrefix()
        {
            string? capturedFilter = null;
            var handler = new MockHttpHandler();
            handler.Map(req =>
                {
                    if (req.Method == HttpMethod.Get
                        && req.RequestUri != null
                        && req.RequestUri.AbsolutePath.Contains("/users", StringComparison.OrdinalIgnoreCase)
                        && (req.RequestUri.Query?.Contains("onPremisesSamAccountName", StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        capturedFilter = req.RequestUri.Query;
                        return true;
                    }
                    return false;
                }, """{ "value": [{ "id": "88888888-8888-8888-8888-888888888888" }] }""");
            handler.MapPath(HttpMethod.Get, "/groups", """{ "value": [{ "id": "99999999-9999-9999-9999-999999999999" }] }""");
            handler.MapPath(HttpMethod.Post, "/checkMemberGroups", """{ "value": [ "99999999-9999-9999-9999-999999999999" ] }""");

            var searcher = NewSearcher(handler);
            searcher.GetGroupSidIfUserIsMemberRecursive("CONTOSO\\alice", "Admins", "contoso.com");

            Assert.IsNotNull(capturedFilter);
            // Filter is URL-encoded: 'alice' → %27alice%27
            StringAssert.Contains(capturedFilter, "%27alice%27", "filter should be on the bare sAMAccountName after stripping DOMAIN\\");
            Assert.IsFalse(capturedFilter!.Contains("CONTOSO", StringComparison.OrdinalIgnoreCase),
                "the DOMAIN\\ prefix must not survive into the filter query");
        }

        // P-5: missed user returns string.Empty (NOT null) so CachedUserGroupReader's
        // sid != null cache check behaves identically to today.
        [TestMethod]
        public void P5_GetGroupSidIfUserIsMemberRecursive_NoUserMatch_ReturnsEmptyString()
        {
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", """{ "value": [] }""");

            var searcher = NewSearcher(handler);
            var result = searcher.GetGroupSidIfUserIsMemberRecursive("ghost", "Admins", "contoso.com");

            Assert.AreEqual(string.Empty, result, "must be empty string, not null — preserves CachedUserGroupReader cache semantics");
        }

        // P-7: GetSidsForUser emits BOTH the Entra group id (Pid match) and the
        // onPremisesSecurityIdentifier (Sid match) so EnvironmentsPersistentSource's
        // `ac.Pid ?? ac.Sid` resolution pattern keeps working post-migration.
        [TestMethod]
        public void P7_GetSidsForUser_EmitsBothPidAndSid()
        {
            const string userId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            // Order matters: most specific paths first so /users/{id}/transitiveMemberOf/...
            // doesn't get absorbed by the /users/{id} self-lookup rule.
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/transitiveMemberOf", """
                {
                    "value": [
                        { "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "onPremisesSecurityIdentifier": "S-1-5-21-100-200-300-G1" },
                        { "id": "cccccccc-cccc-cccc-cccc-cccccccccccc", "onPremisesSecurityIdentifier": null }
                    ]
                }
                """)
                .MapPath(HttpMethod.Get, $"/users/{userId}", $$"""
                {
                    "id": "{{userId}}",
                    "onPremisesSecurityIdentifier": "S-1-5-21-100-200-300-USER"
                }
                """);

            var searcher = NewSearcher(handler);
            var sids = searcher.GetSidsForUser(userId);

            CollectionAssert.Contains(sids, userId, "self id is first");
            CollectionAssert.Contains(sids, "S-1-5-21-100-200-300-USER", "self on-prem SID is appended");
            CollectionAssert.Contains(sids, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "group Entra id is included");
            CollectionAssert.Contains(sids, "S-1-5-21-100-200-300-G1", "group on-prem SID is included");
            CollectionAssert.Contains(sids, "cccccccc-cccc-cccc-cccc-cccccccccccc", "cloud-only group Entra id is still included");
        }

        // P-8: GetUserDataById skips a disabled user but still falls through to group lookup.
        [TestMethod]
        public void P8_GetUserDataById_DisabledUserButGroupHit_ReturnsGroup()
        {
            const string entraId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, $"/users/{entraId}", $$"""
                {
                    "id": "{{entraId}}",
                    "displayName": "Disabled User",
                    "accountEnabled": false
                }
                """)
                .MapPath(HttpMethod.Get, $"/groups/{entraId}", $$"""
                {
                    "id": "{{entraId}}",
                    "displayName": "Some Group",
                    "mailNickname": "somegroup",
                    "mail": null
                }
                """);

            var searcher = NewSearcher(handler);
            var result = searcher.GetUserDataById(entraId);

            Assert.IsTrue(result.IsGroup);
            Assert.AreEqual("Some Group", result.DisplayName);
        }

        // P-8: both user and group miss → ArgumentException.
        [TestMethod]
        public void P8_GetUserDataById_DisabledUserNoGroup_Throws()
        {
            const string entraId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, $"/users/{entraId}", $$"""
                {
                    "id": "{{entraId}}",
                    "accountEnabled": false
                }
                """)
                .MapPath(HttpMethod.Get, $"/groups/{entraId}", "{}", System.Net.HttpStatusCode.NotFound);

            var searcher = NewSearcher(handler);
            Assert.ThrowsExactly<ArgumentException>(() => searcher.GetUserDataById(entraId), "must throw ArgumentException, not AggregateException — implies .GetAwaiter().GetResult() unwrapping");
        }

        // SC-10 acceptance: simulate an existing customer install whose AccessControl.Sid
        // rows hold on-prem AD SIDs. GetUserDataById with the SID resolves via
        // onPremisesSecurityIdentifier and the returned model carries the SID back so
        // EnvironmentsPersistentSource's ac.Sid matching still works.
        [TestMethod]
        public void SC10_AccessControlSidRow_ResolvesViaOnPremisesIdentifier()
        {
            const string legacyAdSid = "S-1-5-21-3623811015-3361044348-30300820-1013";
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, $"/users/{legacyAdSid}", "{}", System.Net.HttpStatusCode.NotFound)
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSecurityIdentifier", $$"""
                {
                    "value": [{
                        "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
                        "displayName": "Legacy Customer User",
                        "userPrincipalName": "legacy@customer.example",
                        "mail": "legacy@customer.example",
                        "accountEnabled": true,
                        "onPremisesSecurityIdentifier": "{{legacyAdSid}}"
                    }]
                }
                """);

            var searcher = NewSearcher(handler);
            var resolved = searcher.GetUserDataById(legacyAdSid);

            Assert.AreEqual("ffffffff-ffff-ffff-ffff-ffffffffffff", resolved.Pid);
#pragma warning disable CS0618
            Assert.AreEqual(legacyAdSid, resolved.Sid);
#pragma warning restore CS0618
        }

        private static AzureEntraSearcher NewSearcher(MockHttpHandler handler)
        {
            return new AzureEntraSearcher(
                () => GraphTestClient.Create(handler),
                NullLogger<AzureEntraSearcher>.Instance);
        }
    }
}
