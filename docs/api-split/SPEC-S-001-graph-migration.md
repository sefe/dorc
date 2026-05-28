---
name: SPEC-S-001 — Graph migration spike (AD removal codebase-wide)
description: JIT Specification for S-001 — promote AzureEntraSearcher to sole IActiveDirectorySearcher, close parity-matrix gaps P-4/P-5/P-7, delete AD code, drop System.DirectoryServices* refs, add Linux build CI gate. Single-PR scope.
type: spec
status: DRAFT
---

# SPEC-S-001 — Graph migration spike (AD removal codebase-wide)

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | DRAFT                                                   |
| **Step**    | S-001                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-05-28                                              |
| **IS**      | [IS-api-split.md](IS-api-split.md) (APPROVED)           |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)       |
| **Folder**  | docs/api-split/                                         |
| **Codebase anchor** | `aab79d14` (`main`, 2026-05-28)                 |

---

## 1. Context

### What this step addresses
S-001 is the Graph-migration spike — the riskiest part of the IS, sequenced first so that any unworkable assumption in HLPS D-2 surfaces here rather than after four worker steps. After S-001 lands:

- `Dorc.Core/AzureEntraSearcher` is the only production `IActiveDirectorySearcher`.
- The three parity-matrix gaps (P-4 legacy AD SID lookup, P-5 sAMAccountName resolution, P-7 dual-ID claims emission) are closed in `AzureEntraSearcher`.
- The AD-code set listed in HLPS §2 / D-2 is deleted.
- `System.DirectoryServices*` package refs are removed from `Dorc.Core.csproj` and `Dorc.PersistentData.csproj`.
- A Linux build CI job enforces SC-1 going forward.

S-001 contains **no worker code** (that's S-002+), **no Windows-auth-scheme removal** (that's S-007 — though `ClaimsTransformer` becomes effectively dead and S-007 will delete it), **no installer changes**, and **no documentation work** (that's S-010).

### Scope of file changes
**Modified:**
- `src/Dorc.Core/AzureEntraSearcher.cs` — remove `[SupportedOSPlatform("windows")]`; close gaps P-4 / P-5 / P-7 per §2.
- `src/Dorc.Core/Interfaces/IActiveDirectorySearcher.cs` — no shape change expected; if the gap-fix work requires a method-signature change (e.g. P-7's claims-expansion returning a richer DTO), the interface change ships here and consumers update with it.
- `src/Dorc.Core.csproj` — drop `System.DirectoryServices` + `System.DirectoryServices.AccountManagement` PackageReferences.
- `src/Dorc.PersistentData/Dorc.PersistentData.csproj` — drop the same two PackageReferences.
- `src/Dorc.Api/Services/ApiRegistry.cs` — DI registration switches from `CompositeActiveDirectorySearcher` (with `ActiveDirectorySearcher` + `AzureEntraSearcher` + `IdentityServerSearcher`) to direct registration of `AzureEntraSearcher` as the single `IActiveDirectorySearcher`.
- `src/Dorc.Api.Tests/Controllers/AccessControlControllerTests.cs`, `src/Dorc.Api.Tests/Controllers/RefDataProjectsControllerDeleteTests.cs` — switch fixture wiring from AD-backed mocks to Graph-backed test doubles (see §3).

**Deleted:**
- `src/Dorc.Core/ActiveDirectorySearcher.cs`
- `src/Dorc.Core/CompositeDirectorySearcher.cs` (class `CompositeActiveDirectorySearcher`)
- `src/Dorc.Core/IdentityServer/IdentityServerSearcher.cs` (and folder if empty)
- `src/Dorc.Api/Services/DirectorySearcherFactory.cs`
- `src/Dorc.Api/Services/UserGroupReaderFactory.cs`
- `src/Dorc.Api/Services/ActiveDirectorySearchService.cs`
- `src/Dorc.Core/Interfaces/IDirectorySearcherFactory.cs` (interface no longer needed)
- `src/Dorc.Api.Tests/Fakes/System.DirectoryServices.fakes` — already deleted on `main` per `aab79d14`'s diff; sanity-check no straggler.

**Added:**
- `.github/workflows/linux-build.yml` (or extend an existing workflow) — Linux-build CI gate per §4.
- `src/Dorc.Core.Tests/AzureEntraSearcherTests.cs` (or expand existing) — Graph-fake-backed parity-matrix tests per §3.
- A small test helper `src/Dorc.Core.Tests/Graph/GraphRequestAdapterFake.cs` (location TBD in §3) for stubbing `IRequestAdapter`.

### Governing constraints
- **HLPS D-2**: AD code is *deleted*, not moved to the worker.
- **HLPS SC-1**: no `System.DirectoryServices*` refs in `Dorc.Api`, `Dorc.Core`, `Dorc.PersistentData`; Linux container builds.
- **HLPS SC-5**: `Dorc.Api.Client` builds unchanged.
- **HLPS SC-6**: parity-matrix integration tests against a Graph SDK fake (not boundary mocks).
- **HLPS SC-8a**: re-run SAST scan post-merge; zero LDAP-injection findings.
- **HLPS SC-9**: parity matrix has test coverage per row.
- **HLPS SC-10**: existing `AccessControl.Sid` rows resolve via `onPremisesSecurityIdentifier`.
- **HLPS C-2**: no bundled renames or layout changes.
- **CLAUDE.md naming**: no class renames in this SPEC unless required by interface evolution.

---

## 2. Production Code Change

### 2.1 Remove `[SupportedOSPlatform("windows")]` on `AzureEntraSearcher`

**Target**: `src/Dorc.Core/AzureEntraSearcher.cs` line 15.

**Change**: delete the attribute. Graph is cross-platform; the attribute was incorrect (HLPS D-2 / Round-1 H-2 / Round-2 NM-1).

**Why it's safe**: every consumer of `IActiveDirectorySearcher` will be reachable from Linux once `ClaimsTransformer`'s `[SupportedOSPlatform("windows")]` is removed (S-007). For S-001, removing the attribute on `AzureEntraSearcher` itself produces a CA1416 cascade in `ClaimsTransformer` *only if* `ClaimsTransformer` is built with platform-attribute analyzers on; in practice the project's analyzer config silences this for `[SupportedOSPlatform("windows")]`-annotated classes consuming non-annotated services. The S-007 step removes `ClaimsTransformer` outright.

### 2.2 Close P-4 — legacy AD SID lookup in `GetUserDataById`

**Target**: `AzureEntraSearcher.GetUserDataById(string pid)` (currently at line ~163).

**Behaviour change**: when the input matches an AD SID shape (regex `^S-1-(5|12)-\d+(-\d+)*$`), perform the following ordered lookups and return the first hit:

1. `graphClient.Users[pid].GetAsync(...)` — handles the case where the caller mistakenly passed an Entra `id` shaped like an SID (unlikely but cheap).
2. **New:** `graphClient.Users.GetAsync(req => req.Filter = $"onPremisesSecurityIdentifier eq '{pid}'", req.Select = ["id","displayName","userPrincipalName","mail","accountEnabled","onPremisesSecurityIdentifier"], req.Headers["ConsistencyLevel"]="eventual")`. Take first hit with `AccountEnabled == true`.
3. **New:** equivalent query against `graphClient.Groups` with `Select = ["id","displayName","mailNickname","mail","onPremisesSecurityIdentifier"]`.

If the input does not match the SID shape, behaviour is unchanged (direct `Users[id]` then `Groups[id]` fallback as today).

The returned `UserElementApiModel` is populated identically to today, with the addition that **`Sid` is populated from `onPremisesSecurityIdentifier`** when the lookup went via filter (paths 2/3) — so callers that key off `Sid` (which exists per `EnvironmentsPersistentSource` line 894 et al.) match.

OData injection safety: the `pid` value flows into a Graph filter string. Apply the same `EscapeODataString` helper already used elsewhere in the file (see line 249 in `GetUserData`).

### 2.3 Close P-5 — sAMAccountName resolution in `GetGroupSidIfUserIsMemberRecursive`

**Target**: `AzureEntraSearcher.GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)` (currently at line ~325).

**Behaviour change**: replace the current `graphClient.Users[userName]` call with a resolution step that:

1. Strips a `DOMAIN\` prefix if present (split on `\\`, take the part after; if more than one `\`, log a warning and use the last segment — matches AD's domain-qualified `sAMAccountName` convention).
2. Strips the `(External)` suffix if present (some callers in DORC may carry this for guest accounts; matches the `GetUserData` regex on line 242).
3. Calls `graphClient.Users.GetAsync(req => req.Filter = $"onPremisesSamAccountName eq '{name}' or userPrincipalName eq '{name}'", req.Select = ["id"], req.Headers["ConsistencyLevel"]="eventual")`.
4. Takes the first hit. If none, return `string.Empty` (matches the current null-on-miss contract).

After resolution, the existing `checkMemberGroups` logic runs unchanged.

The `domainName` parameter is **currently ignored** by the Graph implementation. Keep this behaviour — DORC's Entra setup is single-tenant per install; multi-domain forests are not in DORC's deployment scope (parity-matrix out-of-parity item: "Foreign Security Principals (cross-forest trusts)"). Document this in an XML comment on the method.

OData injection safety: same `EscapeODataString` treatment for `name` and `groupName`.

### 2.4 Close P-7 — dual-ID claims emission in `GetSidsForUser`

**Target**: `AzureEntraSearcher.GetSidsForUser(string userId)` (currently at line ~292).

**Behaviour change**: today the method calls `GetMemberGroups.PostAsGetMemberGroupsPostResponseAsync(...)` which returns Entra group `id`s only. Switch to:

1. Call `graphClient.Users[userId].TransitiveMemberOf.GraphGroup.GetAsync(req => req.Select = ["id","onPremisesSecurityIdentifier"])` (the `.graph.group` cast endpoint, available on `Users[].TransitiveMemberOf`).
2. For each returned group, append **both** the `id` *and* the `onPremisesSecurityIdentifier` (when non-null) to the result list.
3. The first entry remains the user's own identifier as today; if the input `userId` is itself an Entra `id` and the user's `onPremisesSecurityIdentifier` is known (look up via a single `graphClient.Users[userId].GetAsync(req => req.Select = ["id","onPremisesSecurityIdentifier"])` call), append that too.

Result: `GetSidsForUser` returns a list whose entries collectively cover both `AccessControl.Pid` (Entra `id`) and `AccessControl.Sid` (on-prem SID) match opportunities. Downstream EF queries like `EnvironmentsPersistentSource` line 932 (`EF.Constant(userSids).Contains(joined.ac.Sid) || joined.ac.Pid != null && EF.Constant(userSids).Contains(joined.ac.Pid)`) keep working unchanged.

### 2.5 DI registration switch in `ApiRegistry.cs`

**Target**: `src/Dorc.Api/Services/ApiRegistry.cs` — the DI registration that currently constructs `CompositeActiveDirectorySearcher` from `ActiveDirectorySearcher` + `AzureEntraSearcher` + `IdentityServerSearcher`.

**Change**: register `AzureEntraSearcher` directly as the singleton `IActiveDirectorySearcher`. Remove the composite construction. Remove the registrations of `ActiveDirectorySearcher` and `IdentityServerSearcher` (those classes are deleted).

Consumers (`CachedUserGroupReader`, `DirectorySearchController`, etc.) inject `IActiveDirectorySearcher` and get the Graph-backed instance.

### 2.6 Delete AD code

**Files deleted** (whole files, no in-place edits):
- `src/Dorc.Core/ActiveDirectorySearcher.cs`
- `src/Dorc.Core/CompositeDirectorySearcher.cs`
- `src/Dorc.Core/IdentityServer/IdentityServerSearcher.cs`
- `src/Dorc.Core/Interfaces/IDirectorySearcherFactory.cs` (no longer referenced after `DirectorySearcherFactory` is deleted)
- `src/Dorc.Api/Services/DirectorySearcherFactory.cs`
- `src/Dorc.Api/Services/UserGroupReaderFactory.cs` (the factory abstraction collapses to a single registration)
- `src/Dorc.Api/Services/ActiveDirectorySearchService.cs`

If the `IdentityServer` folder is left empty by the deletion of `IdentityServerSearcher.cs`, delete the folder. **Do not rename or relocate any other files** in this SPEC — that's out of scope per HLPS C-2.

`UserGroupsReaderFactory` is currently consumed by `ClaimsTransformer` line 24 (`adUserGroupReaderFactory.GetWinAuthUserGroupsReader()`). After this file is deleted, `ClaimsTransformer` will not compile. **Resolution**: change the `ClaimsTransformer` constructor to inject `IUserGroupReader` directly. (This is the minimal change required for the codebase to compile after the factory deletion; it does *not* count as removing the Windows-auth scheme — that's S-007.)

### 2.7 Drop `System.DirectoryServices*` package refs

**Target**: `src/Dorc.Core/Dorc.Core.csproj` and `src/Dorc.PersistentData/Dorc.PersistentData.csproj`.

**Change**: delete the `<PackageReference Include="System.DirectoryServices" Version="..." />` and `<PackageReference Include="System.DirectoryServices.AccountManagement" Version="..." />` entries from both csproj files.

Verify via `git grep "System.DirectoryServices"` after the deletion — expected hits are limited to `docs/api-split/research/` (informational) and `src/Dorc.Api.Tests/Fakes/` (already-empty area). Any remaining hit in production code is a missed reference and must be resolved before this SPEC's PR opens.

### 2.8 Interface shape decision

`IActiveDirectorySearcher` shape stays the same in this SPEC: the four-method contract (`Search`, `GetUserData`, `GetUserDataById`, `GetSidsForUser`, `GetGroupSidIfUserIsMemberRecursive`) is unchanged. P-7's enrichment happens inside `GetSidsForUser` — same signature, richer return list. P-4 / P-5 happen inside their existing methods — no new methods exposed.

If the SPEC author discovers a need to add a method (e.g. an explicit `GetUserDataBySid` rather than overloading `GetUserDataById`), they may add it — but call out the change in the PR description and update `IActiveDirectorySearcher`'s consumers in the same PR.

---

## 3. Test plan

### 3.1 Graph fake harness

**Choice**: in-memory mock of `IRequestAdapter` using NSubstitute (already in the test stack — `Dorc.Core.Tests.csproj` line 16, `Dorc.Api.Tests.csproj` line 17).

**Pattern**:
- A small helper `GraphRequestAdapterFake` (in `src/Dorc.Core.Tests/Graph/`) configures an `IRequestAdapter` substitute to return canned responses for matching `RequestInformation`. Matching is by URL path + method.
- Tests instantiate `AzureEntraSearcher` with a `GraphServiceClient` constructed against the fake adapter. Today the searcher constructs its own `GraphServiceClient` from `ClientSecretCredential`; this is brittle for testing. **Refactor for testability** (in-scope for this SPEC): the searcher accepts an optional `GraphServiceClient` via constructor (defaulting to today's construction), or — cleaner — via a `Func<GraphServiceClient>` factory parameter. The test substitutes a Graph-fake-backed client.
- Recorded HTTP harness (WireMock.Net) was considered and rejected: heavier dependency, slower tests, fixture format is more verbose than NSubstitute return values.

### 3.2 Tests per parity-matrix row

Each row of HLPS §4 gets at least one integration-style test exercising `AzureEntraSearcher` against the fake adapter. Test method names mirror the matrix IDs:

| Test | Asserts |
|------|---------|
| `P1_Search_FindsUserByDisplayName` | `Search("alice")` returns the user when fake responds to `/users?$filter=startsWith(displayName,'alice')...` |
| `P2_Search_FindsGroupByDisplayName` | `Search("admins")` returns the group when fake responds to `/groups?...` |
| `P3_GetUserDataById_ResolvesByEntraId` | `GetUserDataById("11111111-2222-...")` returns the user when fake responds to `/users/<id>` |
| `P4_GetUserDataById_ResolvesByAdSidViaOnPremisesFilter` | `GetUserDataById("S-1-5-21-...")` returns the user when fake responds to `/users?$filter=onPremisesSecurityIdentifier eq 'S-1-5-...'`. Asserts `Sid` field on returned model equals the input SID. |
| `P4_GetUserDataById_ResolvesGroupByAdSidWhenUserMisses` | Same as above but with `/users?...` returning empty and `/groups?...` returning the group. |
| `P5_GetGroupSidIfUserIsMemberRecursive_ResolvesUserBySamAccountName` | Pass `"alice"` (bare sAMAccountName); fake responds to `/users?$filter=onPremisesSamAccountName eq 'alice' or userPrincipalName eq 'alice'`. Assert `checkMemberGroups` call uses the resolved Entra `id`. |
| `P5_GetGroupSidIfUserIsMemberRecursive_StripsDomainPrefix` | Pass `"DOMAIN\\alice"`; expect filter on `'alice'` (after prefix strip). |
| `P6_GetGroupSidIfUserIsMemberRecursive_ReturnsGroupIdOnTransitiveMatch` | Same as P5 but assert the return value is the target group's Entra `id` when `checkMemberGroups` returns a hit. |
| `P7_GetSidsForUser_EmitsBothPidAndSid` | Fake `/users/{id}/transitiveMemberOf/microsoft.graph.group` returns two groups, one with `onPremisesSecurityIdentifier`, one without. Assert returned list contains both groups' Entra IDs + the one's SID. |
| `P8_GetUserDataById_OmitsDisabledAccounts` | Fake returns user with `accountEnabled = false`; `GetUserDataById` throws `ArgumentException` (current contract). |
| `P9_GetUserData_PopulatesDisplayNameAndEmail` | Asserts the returned `UserElementApiModel` has `DisplayName` and `Email` populated from Graph's `displayName` / `mail` (falling back to `userPrincipalName` per existing logic). |

Negative tests:
- `GetUserDataById_NonSidNonGuidInput_ThrowsArgumentException` — current contract preserved.
- `GetGroupSidIfUserIsMemberRecursive_NoUserMatch_ReturnsEmpty` — current contract.

### 3.3 Test refactor of existing AD-backed test files

- `AccessControlControllerTests.cs` and `RefDataProjectsControllerDeleteTests.cs` currently mock `IActiveDirectorySearcher` at the interface boundary. **Keep those boundary mocks** — they were designed for controller-level coverage and they don't depend on AD specifically. Their only required change is updating fixture-setup code that previously instantiated `ActiveDirectorySearcher` (which is deleted) to instead use NSubstitute against `IActiveDirectorySearcher`.

### 3.4 SC-10 (existing `AccessControl.Sid` rows resolve) acceptance test

A focused test seeds a fake Graph with users whose `onPremisesSecurityIdentifier` matches representative SIDs that would exist in real customer `AccessControl.Sid` rows. Calls `AzureEntraSearcher.GetUserDataById` with each SID and asserts a successful resolution. This is the test that proves SC-10 holds.

### 3.5 SAST re-scan (SC-8a)

After the PR is opened, re-run the PR-#424-flagged scanners against the diff. Zero LDAP-injection findings expected (the LDAP code path is gone).

---

## 4. CI gate (SC-1 enforcement)

**Add** a Linux build job to the CI workflow (likely `.github/workflows/dorc-build.yml` or equivalent — confirm location during execution).

**Job shape**:
- Runs on `ubuntu-latest`.
- Executes `dotnet build src/Dorc.Api/Dorc.Api.csproj -c Release` (and the same for `Dorc.Core` and `Dorc.PersistentData` if not transitively covered).
- Fails on any build error, in particular CA1416 platform-compatibility errors or unresolved `System.DirectoryServices*` types.
- Also runs `grep -r "System\.DirectoryServices" src/ --include="*.cs" --include="*.csproj"` and fails if any hit appears outside `docs/`.

This is the **negative** SC-1 gate (prevents Windows-only refs reappearing). The **positive** gate (Linux container actually runs DORC end-to-end) is S-010's smoke test.

---

## 5. Verification checklist

Done means:

- [ ] `Dorc.Core.csproj` and `Dorc.PersistentData.csproj` contain no `System.DirectoryServices*` `<PackageReference>`.
- [ ] `git grep "System.DirectoryServices" src/` returns no hits.
- [ ] All deleted files (§2.6) are gone from the working tree and the index.
- [ ] `Dorc.Api`, `Dorc.Core`, `Dorc.PersistentData`, `Dorc.Api.Client`, `Dorc.Api.Tests`, `Dorc.Core.Tests` all build on Windows in Release.
- [ ] The Linux CI job (§4) passes on the PR.
- [ ] All §3.2 parity-matrix tests pass.
- [ ] The §3.4 SC-10 acceptance test passes.
- [ ] The existing `AccessControlControllerTests` and `RefDataProjectsControllerDeleteTests` pass with the boundary-mock substitution.
- [ ] The PR description links to the PR-#424 SAST re-scan result confirming zero LDAP-injection findings.
- [ ] A scripted check shows `ClaimsTransformer` compiles after the `IUserGroupsReaderFactory` deletion (the constructor was switched to take `IUserGroupReader` directly per §2.6).

---

## 6. Risks and watchpoints

- **R-1 Graph SDK return shape for `TransitiveMemberOf.GraphGroup`**. The .NET Graph SDK exposes this via a cast endpoint that returns `GroupCollectionResponse`. Confirm via SDK docs / a small spike that `Select = ["id","onPremisesSecurityIdentifier"]` is honoured on this endpoint and that `onPremisesSecurityIdentifier` is non-null for synced groups. If the cast endpoint does not honour `$select` on `onPremisesSecurityIdentifier`, fall back to per-group `Groups[id].GetAsync(req => req.Select = [...])` calls — slower but correct.
- **R-2 `DOMAIN\` prefix handling in production calls.** P-5's prefix-strip is correct for `DOMAIN\alice`, but if a caller passes `alice@DOMAIN` (the UPN form) the filter `userPrincipalName eq 'alice@DOMAIN'` should match. The OR in the filter covers this. Add a test for the UPN form.
- **R-3 `ClaimsTransformer` analyzer warnings**. Removing `[SupportedOSPlatform("windows")]` on `AzureEntraSearcher` while `ClaimsTransformer` still carries the attribute (until S-007 deletes the class) may surface a CA1416 warning. Treat as a transient — S-007 resolves it. If the project enforces "warnings as errors" for CA1416, suppress with `#pragma` and a `// remove with S-007` comment.
- **R-4 Entra Connect dependency.** SC-10 only holds for customers running Entra Connect. The test fakes this; documenting the prerequisite is S-010's job. If S-001's reviewers feel SC-10's acceptance test is insufficient evidence, escalate; otherwise document and proceed.
- **R-5 `IActiveDirectorySearcher` interface XML doc drift.** The interface XML comments will be stale (they describe AD semantics). Update them inline as part of this SPEC; do not split into a follow-up.
- **R-6 OData injection in new filter strings.** Every filter built from caller input flows through `EscapeODataString`. Add a test that asserts a single-quote in the input is properly escaped (parity test pattern; current code already does this for `GetUserData` per line 249).

---

## 7. Open SPEC-level decisions

- **D-S1.1** Whether to add a `GraphServiceClient` factory parameter to `AzureEntraSearcher` (test-seam refactor, §3.1) or use a less-invasive approach (extracting a virtual method on the searcher). Recommendation: factory parameter — cleaner, smaller call sites, no virtuals.
- **D-S1.2** CI job naming and placement (single workflow vs new). Recommendation: extend the existing `.github/workflows/` Linux job if one exists; otherwise add a focused `linux-build-gate.yml`.

---

## 8. Out of scope (within S-001)

- Worker project creation (S-002).
- `IWindowsWorkerClient` contract (S-003).
- Negotiate scheme / `WinAuth*` removal (S-007).
- Installer changes (S-008).
- Log-injection fixes (S-009).
- Documentation (S-010).
- Class renames or folder reorganisation (HLPS C-2).
- Performance tuning of Graph calls beyond what's needed to satisfy parity.
- Hardening secrets storage (HLPS Out-of-Scope item).
