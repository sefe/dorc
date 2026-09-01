using Dorc.Core.Tests.Graph;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class PrincipalDirectoryTests
    {
        // P-1: user search by name returns a populated DirectoryPrincipalApiModel.
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
            Assert.AreEqual("11111111-1111-1111-1111-111111111111", results[0].PrincipalId);
#pragma warning disable CS0618 // intentionally exercises legacy Sid field for dual-ID parity
            Assert.AreEqual("S-1-5-21-100-200-300-1001", results[0].OnPremisesSid);
#pragma warning restore CS0618
            Assert.IsFalse(results[0].IsGroup);
        }

        // P-2: group search by name returns a populated DirectoryPrincipalApiModel with IsGroup=true.
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
            Assert.AreEqual("22222222-2222-2222-2222-222222222222", results[0].PrincipalId);
#pragma warning disable CS0618
            Assert.AreEqual("S-1-5-21-100-200-300-2001", results[0].OnPremisesSid);
#pragma warning restore CS0618
        }

        // P-3: FindById resolves an Entra object id via direct /users/{id} lookup.
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
            var user = searcher.FindById(entraId);

            Assert.AreEqual("Bob Jones", user.DisplayName);
            Assert.AreEqual(entraId, user.PrincipalId);
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
            var user = searcher.FindById(sid);

            Assert.AreEqual("Carol Lee", user.DisplayName);
            Assert.AreEqual("44444444-4444-4444-4444-444444444444", user.PrincipalId);
#pragma warning disable CS0618
            Assert.AreEqual(sid, user.OnPremisesSid, "Sid round-trips so legacy AccessControl rows keep matching");
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
            var result = searcher.FindById(sid);

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
            var groupId = searcher.FindGroupIfMember("alice", "Admins", "contoso.com");

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
            searcher.FindGroupIfMember("CONTOSO\\alice", "Admins", "contoso.com");

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
            var result = searcher.FindGroupIfMember("ghost", "Admins", "contoso.com");

            Assert.AreEqual(string.Empty, result, "must be empty string, not null — preserves CachedUserGroupReader cache semantics");
        }

        // P-7: GetIdentifiersForUser emits BOTH the Entra group id (Pid match) and the
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
            var sids = searcher.GetIdentifiersForUser(userId);

            CollectionAssert.Contains(sids, userId, "self id is first");
            CollectionAssert.Contains(sids, "S-1-5-21-100-200-300-USER", "self on-prem SID is appended");
            CollectionAssert.Contains(sids, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "group Entra id is included");
            CollectionAssert.Contains(sids, "S-1-5-21-100-200-300-G1", "group on-prem SID is included");
            CollectionAssert.Contains(sids, "cccccccc-cccc-cccc-cccc-cccccccccccc", "cloud-only group Entra id is still included");
        }

        // P-8: FindById skips a disabled user but still falls through to group lookup.
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
            var result = searcher.FindById(entraId);

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
            Assert.ThrowsExactly<ArgumentException>(() => searcher.FindById(entraId), "must throw ArgumentException, not AggregateException — implies .GetAwaiter().GetResult() unwrapping");
        }

        // SC-10 acceptance: simulate an existing customer install whose AccessControl.Sid
        // rows hold on-prem AD SIDs. FindById with the SID resolves via
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
            var resolved = searcher.FindById(legacyAdSid);

            Assert.AreEqual("ffffffff-ffff-ffff-ffff-ffffffffffff", resolved.PrincipalId);
#pragma warning disable CS0618
            Assert.AreEqual(legacyAdSid, resolved.OnPremisesSid);
#pragma warning restore CS0618
        }

        // ---------------------------------------------------------------------
        // Request-level assertions. Response-shape tests alone let a completely
        // wrong $filter pass; these assert what the code actually SENT to Graph.
        // ---------------------------------------------------------------------

        // P-1 (request side): the user search must constrain to enabled accounts and must
        // probe onPremisesSamAccountName, or on-prem-synced users become unsearchable.
        [TestMethod]
        public void P1_Search_EmitsEnabledOnlyFilterAcrossExpectedProperties()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """{ "value": [] }""")
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            NewSearcher(handler).Search("alice");

            var filter = handler.LastFilter("/users");
            Assert.IsNotNull(filter, "no $filter was sent for the user search");
            StringAssert.Contains(filter, "accountEnabled eq true",
                "dropping this lets disabled leavers appear in the people picker");
            StringAssert.Contains(filter, "startsWith(onPremisesSamAccountName,'alice')");
            StringAssert.Contains(filter, "startsWith(displayName,'alice')");
            StringAssert.Contains(filter, "startsWith(userPrincipalName,'alice')");
        }

        // P-2 (request side): group search must probe displayName and onPremisesSamAccountName.
        [TestMethod]
        public void P2_Search_EmitsGroupFilterAcrossExpectedProperties()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """{ "value": [] }""")
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            NewSearcher(handler).Search("admins");

            var filter = handler.LastFilter("/groups");
            Assert.IsNotNull(filter);
            StringAssert.Contains(filter, "startsWith(displayName,'admins')");
            StringAssert.Contains(filter, "startsWith(onPremisesSamAccountName, 'admins')");
        }

        // R-6: a single quote in the search term must be doubled, not passed through, or the
        // term can break out of the OData string literal.
        [TestMethod]
        public void Search_EscapesSingleQuoteInODataStringLiteral()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """{ "value": [] }""")
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            NewSearcher(handler).Search("O'Brien");

            var filter = handler.LastFilter("/users");
            Assert.IsNotNull(filter);
            StringAssert.Contains(filter, "O''Brien", "single quote must be doubled for OData");
            Assert.IsFalse(filter!.Contains("'O'Brien'", StringComparison.Ordinal),
                "unescaped quote would terminate the string literal early");
        }

        // The Search guard must actually be invoked — a malformed term must never reach Graph.
        [TestMethod]
        public void Search_RejectsMalformedTermWithoutCallingGraph()
        {
            var handler = new MockHttpHandler { ThrowOnUnmatched = true };

            var results = NewSearcher(handler).Search("a\\b");

            Assert.AreEqual(0, results.Count);
            Assert.AreEqual(0, handler.Captured.Count, "no Graph call should be made for a rejected term");
        }

        // P-6 (negative): the load-bearing case. If checkMemberGroups reports no membership,
        // the method must return empty — not the group id it happened to resolve.
        [TestMethod]
        public void P6_GetGroupSidIfUserIsMemberRecursive_NonMember_ReturnsEmptyString()
        {
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", """
                { "value": [{ "id": "66666666-6666-6666-6666-666666666666" }] }
                """)
                .MapPath(HttpMethod.Get, "/groups", """
                { "value": [{ "id": "77777777-7777-7777-7777-777777777777" }] }
                """)
                // Resolvable user, resolvable group, but NOT a member.
                .MapPath(HttpMethod.Post, "/checkMemberGroups", """{ "value": [] }""");

            var result = NewSearcher(handler).FindGroupIfMember("alice", "Admins", "contoso.com");

            Assert.AreEqual(string.Empty, result,
                "a non-member must not receive the group id — this is the privilege-escalation guard");
        }

        // P-9: FindByName populates display name and email, including the mail fallback.
        [TestMethod]
        public void P9_GetUserData_PopulatesDisplayNameAndEmail()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """
                {
                    "value": [{
                        "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        "displayName": "Alice Smith",
                        "userPrincipalName": "alice@contoso.com",
                        "mail": "alice.smith@contoso.com",
                        "accountEnabled": true,
                        "onPremisesSecurityIdentifier": "S-1-5-21-100-200-300-1001"
                    }]
                }
                """);

            var user = NewSearcher(handler).FindByName("alice");

            Assert.AreEqual("Alice Smith", user.DisplayName);
            Assert.AreEqual("alice.smith@contoso.com", user.Email);
            Assert.AreEqual("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", user.PrincipalId);
        }

        // P-9: when Entra has no `mail`, Email falls back to the UPN rather than going null —
        // notification paths depend on this.
        [TestMethod]
        public void P9_GetUserData_FallsBackToUpnWhenMailUnset()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users", """
                {
                    "value": [{
                        "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                        "displayName": "Bob Jones",
                        "userPrincipalName": "bob@contoso.com",
                        "accountEnabled": true
                    }]
                }
                """);

            var user = NewSearcher(handler).FindByName("bob");

            Assert.AreEqual("bob@contoso.com", user.Email);
        }

        // GetIdentifiersForUser is handed a bare sAMAccountName by WinAuthClaimsPrincipalReader.
        // It must resolve that to an object id before addressing Graph — a bare name as a
        // path segment yields 400 and takes down every authorization check.
        [TestMethod]
        public void GetSidsForUser_ResolvesBareSamAccountNameBeforeAddressingGraph()
        {
            const string objectId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", $$"""
                { "value": [{ "id": "{{objectId}}" }] }
                """)
                .MapPath(HttpMethod.Get, $"/users/{objectId}/transitiveMemberOf", """
                { "value": [{ "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
                              "onPremisesSecurityIdentifier": "S-1-5-21-9-9-9-500" }] }
                """)
                .MapPath(HttpMethod.Get, $"/users/{objectId}", """
                { "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                  "onPremisesSecurityIdentifier": "S-1-5-21-9-9-9-100" }
                """);

            var sids = NewSearcher(handler).GetIdentifiersForUser("jsmith");

            CollectionAssert.Contains(sids, "dddddddd-dddd-dddd-dddd-dddddddddddd");
            CollectionAssert.Contains(sids, "S-1-5-21-9-9-9-500");

            // P-7: the membership query must $select the on-prem SID. Without it, legacy
            // AccessControl.Sid rows stop matching after the migration — and because the
            // fake does not honour $select, only asserting the emitted query catches this.
            var select = handler.LastSelect("/transitiveMemberOf");
            Assert.IsNotNull(select, "no $select was sent for transitiveMemberOf");
            StringAssert.Contains(select, "onPremisesSecurityIdentifier");
            Assert.IsFalse(handler.Captured.Any(c => c.Path.Contains("/users/jsmith", StringComparison.OrdinalIgnoreCase)),
                "a bare sAMAccountName must never be used as a Graph path segment");
        }

        // transitiveMemberOf is a PAGED collection. Every page must be drained or users in
        // many groups silently lose ACL grants.
        [TestMethod]
        public void GetSidsForUser_DrainsAllPagesOfTransitiveMemberOf()
        {
            const string objectId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
            var handler = new MockHttpHandler();
            // Page 2 first: rules match first-wins, so the skiptoken rule must precede
            // the generic transitiveMemberOf rule, or page 1 would answer itself forever.
            handler.Map(req => req.RequestUri != null && req.RequestUri.Query.Contains("PAGE2"), """
                { "value": [{ "id": "20000000-0000-0000-0000-000000000002" }] }
                """);
            handler.MapPath(HttpMethod.Get, $"/users/{objectId}/transitiveMemberOf", """
                {
                    "value": [{ "id": "10000000-0000-0000-0000-000000000001" }],
                    "@odata.nextLink": "https://graph.microsoft.com/v1.0/users/cccccccc-cccc-cccc-cccc-cccccccccccc/transitiveMemberOf/microsoft.graph.group?$skiptoken=PAGE2"
                }
                """);
            handler.MapPath(HttpMethod.Get, $"/users/{objectId}", """
                { "id": "cccccccc-cccc-cccc-cccc-cccccccccccc" }
                """);

            var sids = NewSearcher(handler).GetIdentifiersForUser(objectId);

            CollectionAssert.Contains(sids, "10000000-0000-0000-0000-000000000001");
            CollectionAssert.Contains(sids, "20000000-0000-0000-0000-000000000002",
                "second page of transitiveMemberOf was not drained");
        }

        // An ambiguous name (same sAMAccountName synced from two on-prem domains) must not
        // silently bind the caller to whichever principal Graph returned first.
        [TestMethod]
        public void GetGroupSidIfUserIsMemberRecursive_AmbiguousName_RefusesToGuess()
        {
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", """
                {
                    "value": [
                        { "id": "11111111-0000-0000-0000-000000000001" },
                        { "id": "22222222-0000-0000-0000-000000000002" }
                    ]
                }
                """)
                .MapPath(HttpMethod.Get, "/groups", """
                { "value": [{ "id": "77777777-7777-7777-7777-777777777777" }] }
                """)
                .MapPath(HttpMethod.Post, "/checkMemberGroups", """
                { "value": [ "77777777-7777-7777-7777-777777777777" ] }
                """);

            var result = NewSearcher(handler).FindGroupIfMember("alice", "Admins", "contoso.com");

            Assert.AreEqual(string.Empty, result,
                "an ambiguous sAMAccountName must not resolve to an arbitrary principal");
        }

        // 401/403 from Graph must surface as UnauthorizedAccessException, not a raw ApiException.
        [TestMethod]
        public void Search_MapsForbiddenToUnauthorizedAccessException()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/users",
                    """{ "error": { "code": "Authorization_RequestDenied", "message": "denied" } }""",
                    System.Net.HttpStatusCode.Forbidden);

            Assert.ThrowsExactly<UnauthorizedAccessException>(() => NewSearcher(handler).Search("alice"));
        }

        // P-4 / SC-10 (request side). MapFilter only requires the property NAME to appear,
        // so a hardcoded wrong SID routed identically and the suite stayed green. These assert
        // the SID VALUE the caller passed actually reaches the filter.
        [TestMethod]
        public void P4_GetUserDataById_SendsTheCallersSidInTheFilter()
        {
            const string sid = "S-1-5-21-100-200-300-1234";
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSecurityIdentifier", $$"""
                {
                    "value": [{
                        "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                        "displayName": "Alice",
                        "userPrincipalName": "alice@contoso.com",
                        "accountEnabled": true,
                        "onPremisesSecurityIdentifier": "{{sid}}"
                    }]
                }
                """);

            NewSearcher(handler).FindById(sid);

            var filter = handler.LastFilter("/users");
            Assert.IsNotNull(filter, "no $filter was sent for the SID lookup");
            StringAssert.Contains(filter, $"onPremisesSecurityIdentifier eq '{sid}'",
                "the caller's SID must reach the filter — otherwise every legacy AccessControl.Sid row resolves to the wrong principal");
        }

        [TestMethod]
        public void P4_GetUserDataById_SendsTheCallersSidInTheGroupFilter()
        {
            const string sid = "S-1-5-21-100-200-300-5678";
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSecurityIdentifier", """{ "value": [] }""")
                .MapFilter(HttpMethod.Get, "/groups", "onPremisesSecurityIdentifier", $$"""
                {
                    "value": [{
                        "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
                        "displayName": "Admins",
                        "mailNickname": "admins",
                        "onPremisesSecurityIdentifier": "{{sid}}"
                    }]
                }
                """);

            NewSearcher(handler).FindById(sid);

            var filter = handler.LastFilter("/groups");
            Assert.IsNotNull(filter);
            StringAssert.Contains(filter, $"onPremisesSecurityIdentifier eq '{sid}'");
        }

        // P-5 (request side): the membership path must exclude disabled accounts and must
        // request two matches so an ambiguous name is detectable rather than silently taken.
        [TestMethod]
        public void P5_ResolveUserIdFromName_ExcludesDisabledAndRequestsTwoMatches()
        {
            var handler = new MockHttpHandler()
                .MapFilter(HttpMethod.Get, "/users", "onPremisesSamAccountName", """
                { "value": [{ "id": "66666666-6666-6666-6666-666666666666" }] }
                """)
                .MapPath(HttpMethod.Get, "/groups", """
                { "value": [{ "id": "77777777-7777-7777-7777-777777777777" }] }
                """)
                .MapPath(HttpMethod.Post, "/checkMemberGroups", """
                { "value": [ "77777777-7777-7777-7777-777777777777" ] }
                """);

            NewSearcher(handler).FindGroupIfMember("alice", "Admins", "contoso.com");

            // /users/{id}/checkMemberGroups also contains "/users", so select the GET that
            // actually carried a $filter.
            var req = handler.Captured.LastOrDefault(c =>
                c.Method == "GET" && c.Path.Contains("/users") && c.Filter != null);
            Assert.IsNotNull(req, "no filtered GET /users request was sent");
            StringAssert.Contains(req!.Filter, "accountEnabled eq true",
                "a disabled leaver must not resolve and receive group claims");
            StringAssert.Contains(req.Filter, "onPremisesSamAccountName eq 'alice'");
            StringAssert.Contains(req.Query, "top=2",
                "$top=2 is what makes an ambiguous name detectable rather than silently collapsed");
        }

        // M2M: service principals must appear in the picker again, keyed on appId because
        // that is what OAuthClaimsPrincipalReader matches against AccessControl.Pid.
        [TestMethod]
        public void Search_IncludesServicePrincipalsKeyedOnAppId()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/servicePrincipals", """
                {
                    "value": [{
                        "id": "aaaa1111-0000-0000-0000-000000000001",
                        "appId": "bbbb2222-0000-0000-0000-000000000002",
                        "displayName": "dorc-ci-client",
                        "accountEnabled": true
                    }]
                }
                """)
                .MapPath(HttpMethod.Get, "/users", """{ "value": [] }""")
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            var results = NewSearcher(handler).Search("dorc");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("dorc-ci-client", results[0].DisplayName);
            Assert.AreEqual("bbbb2222-0000-0000-0000-000000000002", results[0].PrincipalId,
                "Pid must be the appId — that is the value AccessControl.Pid holds for M2M callers");
        }

        // Application.Read.All is a permission existing tenants have not consented to. A 403
        // on that one query must not take user and group search down with it.
        [TestMethod]
        public void Search_ServicePrincipalsForbidden_StillReturnsUsersAndGroups()
        {
            var handler = new MockHttpHandler()
                .MapPath(HttpMethod.Get, "/servicePrincipals",
                    """{ "error": { "code": "Authorization_RequestDenied", "message": "denied" } }""",
                    System.Net.HttpStatusCode.Forbidden)
                .MapPath(HttpMethod.Get, "/users", """
                {
                    "value": [{
                        "id": "11111111-1111-1111-1111-111111111111",
                        "displayName": "Alice Smith",
                        "userPrincipalName": "alice@contoso.com",
                        "accountEnabled": true
                    }]
                }
                """)
                .MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");

            var results = NewSearcher(handler).Search("alice");

            Assert.AreEqual(1, results.Count, "a missing Application.Read.All consent must degrade, not fail the search");
            Assert.AreEqual("Alice Smith", results[0].DisplayName);
        }

        // Search paginates: the AD path returned ~1000, a single Graph page is 100.
        [TestMethod]
        public void Search_DrainsAllUserPages()
        {
            var handler = new MockHttpHandler();
            handler.Map(req => req.RequestUri != null && req.RequestUri.Query.Contains("USERPAGE2"), """
                { "value": [{ "id": "22222222-2222-2222-2222-222222222222", "displayName": "Bob Two", "accountEnabled": true }] }
                """);
            handler.MapPath(HttpMethod.Get, "/users", """
                {
                    "value": [{ "id": "11111111-1111-1111-1111-111111111111", "displayName": "Bob One", "accountEnabled": true }],
                    "@odata.nextLink": "https://graph.microsoft.com/v1.0/users?$skiptoken=USERPAGE2"
                }
                """);
            handler.MapPath(HttpMethod.Get, "/groups", """{ "value": [] }""");
            handler.MapPath(HttpMethod.Get, "/servicePrincipals", """{ "value": [] }""");

            var results = NewSearcher(handler).Search("bob");

            CollectionAssert.AreEquivalent(
                new[] { "Bob One", "Bob Two" },
                results.Select(r => r.DisplayName).ToArray(),
                "second page of user results was not drained");
        }

        private static PrincipalDirectory NewSearcher(MockHttpHandler handler)
        {
            return new PrincipalDirectory(
                () => GraphTestClient.Create(handler),
                NullLogger<PrincipalDirectory>.Instance);
        }
    }
}
