---
status: LIVING
issue: sefe/dorc#423
owner: HLPS-api-split.md SC-9
codebase_anchor: aebd286 (main, 2026-08-10)
last_verified: 2026-08-17
---

# AD → Microsoft Graph parity matrix

The living artefact SC-9 requires. It was previously embedded in `HLPS-api-split.md` §4 and
duplicated in `SPEC-S-001-graph-migration.md`; both are point-in-time documents that go stale
once their step merges, which is precisely why SC-9 asked for a standalone file.

**Rules of use**

- Every row must have at least one **integration-level** test driving the Graph payload through
  the real Kiota deserialization path (the `MockHttpHandler` harness in
  `Dorc.Core.Tests/Graph/`). Mocks at the `IActiveDirectorySearcher` boundary do **not** satisfy
  SC-9 — they prove nothing about the request or the payload shape.
- A test that only asserts the *response* is insufficient for rows whose risk is a wrong query.
  Those rows must assert the emitted `$filter` / `$select`. This is recorded per row below.
- A row is not ✅ until its test exists **and** fails when the behaviour is removed. Mutation
  results are recorded in the Evidence column where they have been run.

## Matrix

| # | Behaviour | Graph strategy | Status | Evidence |
|---|---|---|---|---|
| P-1 | User search by name | `/users?$filter=accountEnabled eq true and (startsWith(displayName,…) or startsWith(givenName,…) or startsWith(onPremisesSamAccountName,…) or startsWith(surname,…) or startsWith(mail,…) or startsWith(userPrincipalName,…))` | ✅ | `P1_Search_FindsUserByDisplayName` + `P1_Search_EmitsEnabledOnlyFilterAcrossExpectedProperties` (asserts `$filter`). Mutation: replacing the filter fails the suite. |
| P-2 | Group search by name | `/groups?$filter=startsWith(displayName,…) or startsWith(mailNickname,…) or startsWith(onPremisesSamAccountName,…)` | ✅ | `P2_Search_FindsGroupByDisplayName` + `P2_Search_EmitsGroupFilterAcrossExpectedProperties` (asserts `$filter`). |
| P-3 | Resolve identity by Entra object id | `/users/{id}` then `/groups/{id}` | ✅ | `P3_GetUserDataById_ResolvesByEntraId`. |
| P-4 | Resolve identity by legacy AD SID | `/users?$filter=onPremisesSecurityIdentifier eq '<sid>'`, then groups | ✅ | `P4_GetUserDataById_ResolvesByAdSidViaOnPremisesFilter`, `P4_…_ResolvesGroupByAdSidWhenUserMisses`. Filter matching is now parsed from `$filter` specifically — matching the whole query string was unsound because `$select` contains the same property name. |
| P-5 | Resolve user by sAMAccountName (incl. `DOMAIN\name`, `(External)`) | `/users?$filter=accountEnabled eq true and (onPremisesSamAccountName eq '<name>' or userPrincipalName eq '<name>')`, `$top=2` | ✅ | `P5_…_ResolvesUserBySamAccountName`, `P5_…_StripsDomainPrefix`, `P5_…_NoUserMatch_ReturnsEmptyString`, `GetGroupSidIfUserIsMemberRecursive_AmbiguousName_RefusesToGuess`. |
| P-6 | Recursive group membership | `/users/{id}/checkMemberGroups` | ✅ | `P6_…_NonMember_ReturnsEmptyString` — the **negative** case is the load-bearing one here. Previously untested: a mutation making the method ignore the `checkMemberGroups` result and always return the group id passed the entire suite (an authorization bypass). |
| P-7 | All group ids + SIDs for a user | `/users/{id}/transitiveMemberOf/microsoft.graph.group?$select=id,onPremisesSecurityIdentifier&$top=999`, draining `@odata.nextLink` | ✅ | `P7_GetSidsForUser_EmitsBothPidAndSid`, `GetSidsForUser_DrainsAllPagesOfTransitiveMemberOf`, `GetSidsForUser_ResolvesBareSamAccountNameBeforeAddressingGraph`. The `$select` is asserted directly — the fake does not honour `$select`, so a response-only test cannot catch its removal. |
| P-8 | Disabled account detection | `accountEnabled` | ✅ | `P8_GetUserDataById_DisabledUserButGroupHit_ReturnsGroup`, `P8_…_DisabledUserNoGroup_Throws`. |
| P-9 | Display name + email | `displayName` / `mail` / `userPrincipalName` fallback | ✅ | `P9_GetUserData_PopulatesDisplayNameAndEmail`, `P9_GetUserData_FallsBackToUpnWhenMailUnset`. Previously claimed covered with **no test calling `GetUserData` at all**. |
| SC-10 | Legacy `AccessControl.Sid` rows resolve post-migration | `onPremisesSecurityIdentifier` filter | ✅ | `SC10_AccessControlSidRow_ResolvesViaOnPremisesIdentifier`. See caveat below. |

## Explicitly out of parity

- Local-machine SIDs — not meaningfully used by DORC.
- Foreign Security Principals (cross-forest trusts) — no consumer identified.
- Well-known SIDs (`BUILTIN\Administrators` etc.) — DORC's RBAC uses domain groups.
- **IdentityServer / M2M client principals in the identity picker.** Deleting
  `IdentityServerSearcher` removed the only searcher that returned IdentityServer clients from
  `AccessControlController.SearchUsers`. Existing M2M grants keep working (`GetSidsForUser`
  short-circuits for M2M), but new grants cannot be made through the UI. Added in Round 4 — this
  was an undocumented consequence of S-001, not a decision.

## Known gaps not covered by any row

These are real behavioural differences from the AD implementation. They are recorded here rather
than silently omitted, and are tracked in the HLPS Unknowns Register.

| Gap | Difference | Tracking |
|---|---|---|
| Result-set truncation | `DirectorySearcher.FindAll()` returned up to the server limit (~1000). `Search` reads a single Graph page (100 users + 100 groups) with no truncation signal to the caller. | U-14 |
| Fault tolerance | `CompositeActiveDirectorySearcher` caught per-searcher failures and returned partial results. With one searcher, a Graph 429/503 surfaces as a 500. No retry policy is configured on the Kiota adapter. | U-13 |
| Tenant-wide name resolution | `onPremisesSamAccountName` is unique per *domain*, not per *tenant*. In a multi-domain tenant the same name can resolve to more than one principal. Current behaviour: refuse and return empty (fail closed) rather than bind to an arbitrary identity. | U-15 |
| Cloud-only accounts | Any row depending on `onPremisesSecurityIdentifier` (P-4, P-7, SC-10) yields nothing for accounts that never existed on-prem. Correct, but means SC-10 covers Cohort A only. | U-10 |
