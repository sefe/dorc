---
status: IN REVIEW
author: Agent
date: 2026-05-28
revised: 2026-08-17 (Round 4)
issue: sefe/dorc#423
folder: docs/api-split/
supersedes_pr: sefe/dorc#424
codebase_anchor: aebd286 (main, 2026-08-10) — re-anchored in Round 4; was aab79d14, 214 commits behind
revision_round: 4
---

# HLPS: Replace AD with Microsoft Graph + Split `Dorc.Api` into a Linux-compatible API and a Windows-only worker

| Field      | Value                       |
|------------|-----------------------------|
| **Status** | IN REVIEW (Round 4). Was APPROVED at Round 3; **re-opened 2026-08-17** because implementation of S-001/S-002/S-003/S-004/S-007 falsified four success criteria and surfaced five undocumented behavioural changes. See Appendix B. |
| **Author** | Agent                       |
| **Date**   | 2026-05-28                  |
| **Issue**  | sefe/dorc#423               |
| **Folder** | docs/api-split/             |
| **Supersedes** | sefe/dorc#424 (Copilot agent PR, closed unmerged) |
| **Codebase anchor** | `aebd286` (`main`, 2026-08-10). Re-anchored in Round 4 — the Round 3 anchor `aab79d14` was 214 commits stale, predating the Kafka substrate, the daemons modernisation follow-ups, and an LDAP-injection fix on `ActiveDirectorySearcher` (see Appendix B, R4-6). |

---

## 1. Problem Statement

`Dorc.Api` is a single ASP.NET Core process that cannot run on Linux because portions of it (and its dependencies `Dorc.Core`, `Dorc.PersistentData`) depend on Windows-only stacks: `System.DirectoryServices` (Active Directory), `WindowsIdentity` / Negotiate / Kerberos, `System.Management` (WMI), and the Windows registry. Issue #423 requires the API to be split so that:

1. A primary API contains only code that runs on Linux (and Windows).
2. A secondary API contains the bare minimum Windows-only code.
3. The primary API can call into the secondary when required.

To meet (1) end-to-end, the entire compile graph of the primary API — not just `Dorc.Api`, but `Dorc.Core` and `Dorc.PersistentData` it pulls in — must be free of Windows-only references. The Active Directory surface is the largest such reference and is replaced with Microsoft Graph rather than moved to the worker (see D-2). The motivating outcome is the ability to deploy the bulk of DORC on Linux hosts, treating WMI, remote registry, and password-reset impersonation as an out-of-process Windows worker.

---

## 2. Observed Constraints from Today's Codebase

All file references in this section are pinned to `aab79d14` (see frontmatter). Research carried over from PR #424 lives in [`research/`](research/) and may have drifted; treat it as background, not the source of truth.

| Surface                  | Where it lives (`aab79d14`) | Linux alternative |
|--------------------------|-------------------------------|--------------------|
| `System.DirectoryServices` (AD) — package refs | `Dorc.Core.csproj`, `Dorc.PersistentData.csproj` | Microsoft Graph (see D-2) |
| AD code (production) | `Dorc.Core/ActiveDirectorySearcher.cs`, `Dorc.Core/CompositeDirectorySearcher.cs` (class `CompositeActiveDirectorySearcher`), `Dorc.Core/AzureEntraSearcher.cs` (already Graph-backed, currently annotated `[SupportedOSPlatform("windows")]`), `Dorc.Core/IdentityServer/IdentityServerSearcher.cs`, `Dorc.Core/Interfaces/IActiveDirectorySearcher.cs`, `Dorc.Core/Interfaces/IDirectorySearcherFactory.cs`, `Dorc.Api/Services/DirectorySearcherFactory.cs`, `Dorc.Api/Services/CachedUserGroupReader.cs`, `Dorc.Api/Services/UserGroupReaderFactory.cs`, `Dorc.Api/Services/ApiRegistry.cs` (DI wiring), `Dorc.Api/Services/ActiveDirectorySearchService.cs`, `Dorc.Api/Controllers/AccessControlController.cs`, `Dorc.Api/Controllers/RefDataEnvironmentsUsersController.cs`, `Dorc.Api/Controllers/RefDataProjectsController.cs`, `Dorc.Api/Controllers/RequestController.cs`, `Dorc.Api/Controllers/PropertyValuesController.cs`. AD tests: `Dorc.Api.Tests/Controllers/AccessControlControllerTests.cs`, `Dorc.Api.Tests/Controllers/RefDataProjectsControllerDeleteTests.cs` |
| Windows Auth (Negotiate, NTLM, Kerberos) | `Dorc.Api/Security/WinAuthClaimsPrincipalReader.cs`, `Dorc.Api/Security/WinAuthLoggingMiddleware.cs`, Negotiate scheme registration in `Dorc.Api/Program.cs`, `WindowsIdentity` impersonation in `Dorc.Api/Controllers/ResetAppPasswordController.cs` | OAuth2/JWT (already supported); Negotiate scheme removed from primary; impersonation moves to worker |
| WMI (`System.Management`) | `Dorc.Core/DaemonStatusProbe.cs` (Windows-only probe path post-#649 rename), `Dorc.Api/Services/WmiUtil.cs` | None drop-in — moves to worker |
| Registry (`Microsoft.Win32`) | `Dorc.Api/Controllers/RefDataServersController.cs` (remote OS-version detection) | `RuntimeInformation` for local; remote registry moves to worker |

PR #424's research docs cite "~9 / ~14" file counts as of their writing; the table above supersedes those numbers and reflects current `main`.

### Why this HLPS (and not just a redo of #424)

PR #424 mixed three concerns: the structural split (the stated goal), a bulk *banned-words* class rename that misread CLAUDE.md's naming *principle* as a *blacklist* (producing grab-bag names: `Properties`, `Requests`, `Operations`), and speculative orchestrator extraction. Bundling caused a 5629-line diff with 113 files touched, persistent build-error cycles, and ~340 silently semantic-conflicted files once #649 landed on `main`. This HLPS scopes only the structural and dependency work; the rename is explicitly out of scope.

---

## 3. Resolved Decisions

Resolved with architecture owner on 2026-05-28. Recorded here as the design anchors the IS and SPECs will build on.

### D-1 — Worker process topology: HTTP loopback (was U-1)
The Windows worker is a **separate ASP.NET Core process** (working name `Dorc.Api.WindowsWorker.exe` — see U-4) bound to `127.0.0.1` only. The primary holds an `IWindowsWorkerClient` whose implementation is an `HttpClient`. Per-request loopback cost is acceptable given the existing claims cache and the fact that — per D-2 — the authz hot path no longer touches the worker at all. Rationale: in-proc plugin defeats the Linux-deployment goal (a Linux primary process cannot load a Windows assembly); queue sidecar is the wrong shape for synchronous request-path operations.

The worker is a **permanent architectural component**, not transitional. Future replacement of WMI/registry with SSH/PowerShell-remoting (separate HLPS if ever taken on) would change the worker's *internals*, not its existence.

### D-2 — AD replaced with Microsoft Graph codebase-wide (was U-2; expanded per review Round 1)
`System.DirectoryServices` and `System.DirectoryServices.AccountManagement` are **removed from every project in the primary API's compile graph** (`Dorc.Api`, `Dorc.Core`, `Dorc.PersistentData`) and replaced with Microsoft Graph by promoting and extending the existing `Dorc.Core/AzureEntraSearcher.cs`. AD code does **not** move to the worker — it is deleted. Consequence: the worker's surface shrinks to genuinely non-portable Windows operations (WMI, remote registry, password reset).

Specifically:
- `Dorc.Core/AzureEntraSearcher.cs` becomes the production `IActiveDirectorySearcher`; its `[SupportedOSPlatform("windows")]` attribute is removed (the attribute is incorrect — Graph is cross-platform).
- `Dorc.Core/ActiveDirectorySearcher.cs`, `Dorc.Core/CompositeDirectorySearcher.cs` (class `CompositeActiveDirectorySearcher`), `Dorc.Core/IdentityServer/IdentityServerSearcher.cs`, `Dorc.Api/Services/DirectorySearcherFactory.cs`, `Dorc.Api/Services/UserGroupReaderFactory.cs`, `Dorc.Api/Services/ActiveDirectorySearchService.cs` are deleted (or — for the interfaces — reduced to the Graph contract).
- `Dorc.Api/Services/ApiRegistry.cs` DI registrations switch from the composite/factory pattern to direct registration of the Graph-backed searcher.
- Consumers (`ClaimsTransformer`, `CachedUserGroupReader`, `DirectorySearchController`, `AccessControlController`, `RefDataEnvironmentsUsersController`, `RefDataProjectsController`, `PropertyValuesController`, `RequestController`, AD tests) keep their interface dependency (`IActiveDirectorySearcher`) and gain no behavioural change beyond Graph's documented semantic gaps (see C-5 + parity matrix in §4).
- `Dorc.PersistentData`'s AD code path is removed; the `System.DirectoryServices` package ref drops from its csproj.

Customer implication: every DORC install now requires an Entra ID tenant + app registration + Graph permissions, **and** (for any install with existing `AccessControl.Sid` rows or AD-rooted `RBAC` mappings) requires **Entra Connect (or Cloud Sync)** so on-prem SIDs are mirrored to Entra `onPremisesSecurityIdentifier`. Without Entra Connect, P-4 / P-7 in the parity matrix cannot resolve existing ACLs. Pure on-prem AD-only installs (no Entra tenant) **cannot upgrade** to this version. See U-10 (Product-owner decision).

### D-3 — Inter-API auth: loopback-only + shared secret header (was U-3)
The worker binds to `127.0.0.1` and rejects any request without a shared-secret header `X-Worker-Key`. Authorization decisions are made entirely in the primary (using Graph-backed claims) *before* the worker is called; the worker trusts that any call reaching it has already been authz'd. For password reset, the worker uses **its own service account** (an AD-delegated reset-password identity) — the caller's identity is forwarded in the request body for audit only, not for impersonation.

**Threat model:** the in-scope adversary is an unprivileged off-host attacker who has reached the API host's network but not the host itself. Loopback binding eliminates that adversary by construction; the shared secret is a second-layer defence against a co-located non-DORC process (defence in depth, not a primary control).

**Secret protection class:** the shared secret is stored in `appsettings.json` (or its environment-specific overlay) with the same protection class as DORC's existing connection-string secrets — i.e., file-system ACLs restrict read to the service account, no application-level encryption today. Hardening to DPAPI/Azure Key Vault is a separate concern tracked outside this HLPS.

**Rotation policy:** the shared secret is configured at install time. Rotation requires updating both processes' config and restarting both. There is no online rotation; secret mismatch on the worker returns a `401` with body `{"error":"worker_key_invalid"}` so the cause is diagnosable. The IS step that wires the worker host will codify this in the SPEC.

Rationale: JWT/mTLS add machinery without material security benefit on loopback; loopback-only binding + shared secret matches the documented threat model.

---

## 4. Scope

### In Scope

**A. Graph migration (codebase-wide, per D-2):**
- Promote `Dorc.Core/AzureEntraSearcher.cs` to the production implementation, removing its (incorrect) `[SupportedOSPlatform("windows")]` attribute.
- Close the parity-matrix gaps flagged below as ❌:
  - **P-4 (legacy AD SID lookup):** extend `GetUserDataById` so that when the input matches the SID shape (`S-1-5-...`) and the direct `Users[id]` lookup 404s, it falls back to `/users?$filter=onPremisesSecurityIdentifier eq '<sid>'` and the equivalent for groups.
  - **P-5 (sAMAccountName resolution):** extend `GetGroupSidIfUserIsMemberRecursive` to resolve the `userName` argument via `/users?$filter=onPremisesSamAccountName eq '<name>' or userPrincipalName eq '<name>'` before calling `checkMemberGroups`.
  - **P-7 (claims expansion emits both Pid and Sid):** rework whatever path expands a user's group memberships into claims so that it surfaces both the Entra `id` (→ `Pid`) and the `onPremisesSecurityIdentifier` (→ `Sid`), supporting the existing `ac.Pid ?? ac.Sid` resolution pattern.
- Achieve parity with the deleted AD code for the load-bearing behaviours in the parity matrix. Behaviours not in the matrix are explicitly *not* parity-guaranteed and any consumer that depends on them must be identified and re-designed.
- Delete the AD code listed in §2 / D-2.
- Drop `System.DirectoryServices` and `System.DirectoryServices.AccountManagement` package refs from `Dorc.Core.csproj` and `Dorc.PersistentData.csproj`.
- Update `Dorc.Api/Services/ApiRegistry.cs` DI to register the Graph-backed searcher directly.
- Tests (SC-6 / SC-9): **every row in the parity matrix gets at least one integration-level test against a Graph SDK fake (e.g. `Microsoft.Graph` request-adapter mock or a recorded HTTP harness) in addition to any mocked-interface unit tests.** Mocks at the `IActiveDirectorySearcher` boundary alone do *not* satisfy SC-9 because they don't exercise the Graph payload shape. Update `Dorc.Api.Tests/Controllers/AccessControlControllerTests.cs` and `Dorc.Api.Tests/Controllers/RefDataProjectsControllerDeleteTests.cs` to the Graph-backed pattern.

**Parity matrix (load-bearing behaviours that must work post-Graph):**

> **Round 4:** the authoritative matrix now lives at [`parity-matrix.md`](parity-matrix.md) per
> SC-9. The table below is retained as the record of what was known at drafting time; where the
> two differ, the living artefact wins. Three rows the table marked ✅ at Round 3 turned out to
> have no test behind them (P-6's negative case, P-9 entirely) or a test that could not detect a
> wrong query (P-1/P-2/P-4). See Appendix B.

The matrix below is derived from the `IActiveDirectorySearcher` contract *and* its call sites (notably `AccessControlPersistentSource`, `EnvironmentsPersistentSource`, `ClaimsTransformer`). Every row in this table represents behaviour an existing customer install relies on; the Graph implementation in `aab79d14` already covers some rows but has gaps in others — those gaps are flagged below and are in-scope for S-001.

| # | Behaviour | Today (DirectoryServices) | Graph equivalent / strategy | Current Graph impl status at `aab79d14` |
|---|---|---|---|---|
| P-1 | User search by name | LDAP filter | `/users?$filter=startswith(displayName,...) or startswith(userPrincipalName,...) or startswith(onPremisesSamAccountName,...)` | ✅ Already implemented (`Search`, line ~74) |
| P-2 | Group search by name | LDAP filter | `/groups?$filter=startswith(displayName,...) or startswith(mailNickname,...) or startswith(onPremisesSamAccountName,...)` | ✅ Already implemented (line ~116) |
| P-3 | Resolve identity by Entra object ID | n/a | `/users/{id}` then `/groups/{id}` fallback | ✅ Already implemented (`GetUserDataById`, line ~163) |
| P-4 | **Resolve identity by legacy AD SID** (existing `AccessControl.Sid` rows) | SID lookup | `/users?$filter=onPremisesSecurityIdentifier eq '<sid>'` then `/groups?$filter=onPremisesSecurityIdentifier eq '<sid>'` fallback | ❌ **Gap — must be added in S-001.** Without this, every existing `AccessControl.Sid` row 404s post-migration (see `AccessControlPersistentSource.cs:77/180/196`, `EnvironmentsPersistentSource` lines 165/166/203/373/422/728/932 which use `ac.Sid` in EF queries). |
| P-5 | **Resolve user by sAMAccountName** (for recursive membership) | LDAP `sAMAccountName=` | `/users?$filter=onPremisesSamAccountName eq '<name>'` then fall back to `/users/<upn>` | ❌ **Gap — must be added in S-001.** Current impl (`GetGroupSidIfUserIsMemberRecursive`, line ~325) treats the `userName` parameter as an Entra object ID / UPN and calls `graphClient.Users[userName]`, so sAMAccountName-only callers silently return empty. |
| P-6 | Recursive group membership ("is user X in group Y, transitively?") | `IsMemberOf` + walk | After P-5 resolves user, `/users/{id}/checkMemberGroups` (transitive) | ✅ Logic already present; depends on P-5 |
| P-7 | All group SIDs for a user (used for claims expansion) | Walk groups | `/users/{id}/transitiveMemberOf?$select=id,onPremisesSecurityIdentifier` returning both `id` and `onPremisesSecurityIdentifier` so consumers can match against either `Pid` or `Sid` columns | ❌ **Gap — must be added in S-001.** `EnvironmentsPersistentSource` line 894 (`ac.Pid ?? ac.Sid`) shows the codebase already accommodates dual ID worlds; the claims-expansion path must emit both values. |
| P-8 | Disabled account detection | `userAccountControl` bit | `accountEnabled` | ✅ Already used (`GetUserDataById` checks `AccountEnabled == true`) |
| P-9 | Display name + email | LDAP attribute | Graph `displayName` / `mail` / `userPrincipalName` | ✅ Already implemented |

**Out of parity (explicitly):**
- Local-machine SIDs (DORC didn't use these meaningfully).
- Foreign Security Principals (cross-forest trusts) — must be flagged for any consumer that depends on this; none identified at `aab79d14`.
- Well-known SIDs (`BUILTIN\Administrators` etc.) — DORC's RBAC uses domain groups, not well-known SIDs.

**Data-migration implication:** P-4 and P-7 depend on the customer's Entra tenant having `onPremisesSecurityIdentifier` populated, which requires **Entra Connect (or Cloud Sync) to be present and to have synced the on-prem AD users/groups whose SIDs are persisted in `AccessControl.Sid`**. Without that, existing ACL rows cannot be resolved. This is folded into U-10 (customer migration prerequisite).

**B. Worker process (per D-1):**
- New project (working name `Dorc.Api.WindowsWorker` — see U-4).
- Worker host binds to `127.0.0.1` only; rejects calls without the `X-Worker-Key` header (per D-3).
- MSI component for Windows-only deployments (`Setup.Dorc/Web/RequestApi/ApiWindows.wxs` is a reference template from PR #424).
- Service-account configuration for password reset; documented Graph-permissions setup for the primary.

**C. Inter-API contract (per D-3):**
- `IWindowsWorkerClient` interface in `Dorc.Api`; HTTP-based implementation behind a `DelegatingHandler` that injects the secret.
- Null-pattern / `503`-returning implementation for Linux installs where the worker is absent.

**D. Move Windows-only code from primary to worker:**
- WMI service-status probe path in `Dorc.Core/DaemonStatusProbe.cs` and `Dorc.Api/Services/WmiUtil.cs`.
- Remote registry reads in `Dorc.Api/Controllers/RefDataServersController.cs`.
- `WindowsIdentity` impersonation in `Dorc.Api/Controllers/ResetAppPasswordController.cs` (controller stays in primary as a thin pass-through; impersonation logic moves).

**E. Remove Windows authentication scheme from primary:**
- Delete `Dorc.Api/Security/WinAuthClaimsPrincipalReader.cs` and `Dorc.Api/Security/WinAuthLoggingMiddleware.cs`.
- Remove Negotiate authentication scheme registration from `Dorc.Api/Program.cs`.
- Any remaining authentication flows continue to work via OAuth2/JWT (already supported).

**F. Documentation:**
- Architecture note: runtime topology, primary/worker relationship, configuration knobs.
- Customer-facing: Entra tenant setup, required Graph permissions, AD-to-Entra migration prerequisites.

### Out of Scope (explicitly)
- **Bulk class renaming** to remove the words *Service / Helper / Manager / Util*. CLAUDE.md's rule is principle-first; class-by-class renames belong in their own scoped PRs only when the new name is *more* specific than the old.
- Supporting pure on-prem AD installs without an Entra tenant. Per D-2, an Entra tenant is now a hard prerequisite.
- Replacing WMI with SSH / PowerShell-remoting / REST agents (separate HLPS if/when desired). Worker is permanent (D-1).
- Folder reorganisation by "function" (`Identity/`, `Build/`, `Orchestration/`) inside the existing API.
- Changing the public Swagger/REST surface of `Dorc.Api` in shape. (Behaviour envelope on Linux installs is covered by C-1 and SC-4.)
- Hardening the shared-secret storage to DPAPI / Azure Key Vault. Separate concern (M-1/D-3 acknowledged).
- Log-injection findings in `BundledRequestsController`, `MakeLikeProdController`, `ResetAppPasswordController`, `Deployment/Requests.cs` — see SC-8b.

**Added in Round 4 — consequences of S-001 that were not stated at Round 3.** These are recorded
as scope decisions rather than left as silent behaviour changes; each needs an explicit accept or
reject from the panel:

- **M2M / IdentityServer client principals leave the identity picker.** Deleting
  `IdentityServerSearcher` removes the only searcher returning IdentityServer clients from
  `AccessControlController.SearchUsers`. Existing M2M grants continue to work; new ones cannot be
  created through the UI. `IdentityServerClient.cs` and `GetIdentityServerClientId()` remain in
  the tree but unreachable. **Proposed disposition: accept, and remove the dead client code in
  S-010.** If rejected, S-001 must keep a Graph service-principal search path.
- **Graph result truncation and loss of composite fault tolerance** — tracked as U-14 and U-13
  rather than silently accepted.
- **`AppSettings:ActiveDirectoryRoles` becomes dead configuration** — tracked as U-12.
- **`AppSettings:IsUseAdSidsForAccessControl` becomes a no-op** — tracked as U-17.

---

## 5. Constraints

- **C-1 No client-side compile-time change.** Existing API consumers (`dorc-web`, `Dorc.Api.Client`, CLI tools) compile and ship unchanged. Behavioural changes are scoped: on Windows installs they are nil (SC-3); on Linux installs the WMI / registry / password-reset endpoints return a documented `503` (SC-4). Customer-facing release notes must call out this behavioural envelope.
- **C-2 No bundled refactor.** Naming, folder layout, and DI cleanup do not ride along on this HLPS.
- **C-3 Single host on Windows.** On Windows installs, the worker process ships and runs alongside the primary as a separate MSI component, bound to loopback only.
- **C-4 Customer infrastructure.** Every DORC install requires an Entra ID tenant + app registration with Graph permissions. Document required permissions (U-9) and the AD-to-Entra migration path (U-10) before release.
- **C-5 Bounded functional change.** Endpoints behave identically pre- and post-split for the parity matrix in §4. Known semantic gaps outside the matrix (foreign security principals, well-known SIDs, local-machine SIDs — none currently relied on) are documented and any future use is gated on a follow-up HLPS.
- **C-6 Follow the HLPS → IS → SPEC process.** Each batch of file moves and the Graph migration go through an IS step with their own SPEC and adversarial review.
- **C-7 Installer-side secret handling requires security review.** The MSI component that provisions the shared secret at install time must pass an explicit security review pass before release (the secret-provisioning surface is a new attack vector).

---

## 6. Success Criteria

- **SC-1** `Dorc.Api`, `Dorc.Core`, and `Dorc.PersistentData` build on Linux with no Windows-only dependencies, enforced by CI. **Phased, because the two halves land in different steps and stating them as one criterion made SC-1 unsatisfiable from S-001 until S-005:**
  - **SC-1a (S-001):** no `System.DirectoryServices*` package refs; no `<RuntimeIdentifier>win-*</RuntimeIdentifier>`; no *new* Windows-only API usage. Enforced by the Linux build gate.
  - **SC-1b (S-005):** no `System.Management` package refs, and the gate's accepted-backlog list is empty.
  - The gate must enforce this at **three** layers, not one: a `csproj` guard (catches the dependency being *declared*), CA1416-as-error (catches the API being *used* — a Windows-only API compiles clean on Linux unless the warning is promoted, so a package-ref grep alone is not a gate), and a Linux test run. Added in Round 4 after the original single-layer gate was shown to pass with full `System.DirectoryServices.AccountManagement` usage reintroduced.
- **SC-2** `Dorc.Api.WindowsWorker` builds, runs on Windows only, binds to `127.0.0.1`, and rejects calls missing `X-Worker-Key` with a documented `401` body.
- **SC-3** On Windows installs with the worker present: WMI, registry, and password-reset endpoints behave identically to today (verified by parity tests against pre-split fixtures).
- **SC-4** On Linux installs (no worker): WMI / registry / password-reset endpoints return `503 Service Unavailable` with body `{"error":"windows_worker_unavailable", "endpoint":"<name>"}`. This is the documented behavioural envelope referenced in C-1.
- **SC-5** Existing client apps (`dorc-web`, `Dorc.Api.Client`) require no code changes to compile and ship. Behavioural-envelope changes (SC-4) are handled by surfaced error messages, not by client logic changes.
- **SC-6** All existing unit and integration tests pass at parity with pre-split coverage; new contract tests cover the primary↔worker HTTP surface; new tests cover the Graph-backed AD code path against the parity matrix in §4.
- **SC-7** MSI installer adds the worker as a Windows-only component without breaking existing upgrade paths.
- **SC-8a** LDAP-injection findings on PR #424 (`DirectorySearchController` ×2) are eliminated by the Graph migration removing the LDAP code path. Verified by re-running the security scan post-merge.
- **SC-8b** Log-injection findings (`BundledRequestsController`, `MakeLikeProdController`, `ResetAppPasswordController`, `Deployment/Requests.cs`) are addressed in dedicated SPECs carved out from the relevant IS steps (**S-006** for `ResetAppPasswordController` — corrected in Round 4, this said S-005, which is the WMI probe step and never touches that file; the others get their own SPEC under the step that touches them). Not deferred outside this HLPS.
- **SC-9** The parity matrix lives at [`docs/api-split/parity-matrix.md`](parity-matrix.md) — created in Round 4; SC-9 had mandated it since Round 1 but it was never written, and the matrix lived only inside §4 and SPEC-S-001, both of which go stale when their step merges. Every row has at least one **integration-level** test exercising the Graph-backed path against a Graph SDK fake or recorded HTTP harness. Interface-level mocks at the `IActiveDirectorySearcher` boundary alone do not satisfy this criterion.
  - **Added in Round 4:** for rows whose risk is a *wrong query* rather than a wrong response, the test must assert the emitted `$filter` / `$select`. A response-only test cannot detect a wrong filter, because the fake answers regardless of what was asked. A row is not ✅ until its test **fails when the behaviour is removed**.
- **SC-10** Existing customer installs with `AccessControl.Sid` rows backed by on-prem AD SIDs continue to resolve correctly after migration, provided their Entra tenant has Entra Connect (or Cloud Sync) populating `onPremisesSecurityIdentifier`. Verified by an integration test against a Graph fake that exposes `onPremisesSecurityIdentifier` for sample users/groups.

---

## 7. Unknowns Register

### Blocking

Per CLAUDE.md, **blocking unknowns halt progress**. Original U-1, U-2, U-3 were resolved as D-1,
D-2, D-3. Round 4 promotes one existing unknown and adds two, all of which gate *release*
rather than further implementation — S-001 through S-007 can continue, but none of this may
ship to a customer until they are answered.

- **U-10 (promoted from non-blocking in Round 4) — migration path for Cohort B.** Customers on
  pure on-prem AD with no Entra tenant lose all ACL resolution at upgrade. This was filed as
  non-blocking with a default of "hard break, no shim, published prerequisites", but a default
  chosen by the authors is not a product decision, and by Round 3 the HLPS was APPROVED with a
  customer-breaking change resting on an unanswered question. **Owner: Product.** Required
  before release: confirm the hard break, or fund a back-compat shim.
- **U-12 (new) — role-claim prerequisite.** S-007 deletes `ClaimsTransformer`, which was the only
  reader of `AppSettings:ActiveDirectoryRoles`. `User.IsInRole("Admin")` / `("PowerUser")` remain
  load-bearing across `RefDataController`, `RefDataPermissionController`, `RefDataSqlPortsController`,
  `DeploymentsHub`, `RolePrivilegesChecker` and `SecurityObjectFilter`, and now resolve **solely**
  from the JWT `role` claim. The installer still writes the AD group settings, so an operator who
  upgrades and configures them as always gets 403 on every admin endpoint if their Entra app
  registration does not emit `role`. **Owner: architecture + docs.** Required before release:
  either the app-registration guidance makes `role` emission a documented prerequisite, or the
  dead config and its WiX writes are removed so the knob stops lying about being effective.
- **U-16 (new) — worker provisioning ordering.** S-004 routes a live endpoint through the worker
  while `WindowsWorker:Enabled` ships `false` and nothing sets it `true`, because provisioning is
  S-008's job and S-008 has no PR. As sequenced, merging S-004 turns a working endpoint into a
  503 on every existing Windows install. **Owner: architecture.** Required before S-004 merges:
  land S-008 first, or flip the shipped default. The same ordering trap applies to S-007, whose
  startup guard rejects the `AuthenticationScheme` value the MSI itself writes.

### Non-blocking (added in Round 4)

- **U-13** Graph fault tolerance. Removing `CompositeActiveDirectorySearcher` means a Graph 429/503
  is now a 500 rather than a degraded result, and no retry handler is configured on the Kiota
  adapter. Decide whether to configure Graph SDK retry/backoff or accept the change.
- **U-14** Result-set truncation. `Search` reads one Graph page (100 users + 100 groups) with no
  signal to the caller; the AD path returned up to ~1000. Pre-existing on the OAuth path, but
  S-001 makes it the only path, including for `DirectorySearchController`.
- **U-15** Multi-domain tenants. `onPremisesSamAccountName` is unique per domain, not per tenant.
  Current behaviour refuses to resolve an ambiguous name (fails closed). Confirm that refusing is
  preferred over a domain-scoped filter.
- **U-17** `IsUseAdSidsForAccessControl` is a no-op after S-007 — `ClaimsPrincipalReaderFactory`,
  its only reader, is left unregistered when `ConfigureBoth` is deleted. Decide: delete the flag
  and the class, or re-register it. Note that re-registering does not restore the behaviour, since
  its AD branch resolves names that Graph cannot match.

### Non-blocking (from Rounds 1-3; resolved during IS; some require named owner)
- **U-4** Naming of the new project: `Dorc.Api.Windows` vs. `Dorc.Api.WindowsWorker`. Prefer the latter for specificity (per CLAUDE.md naming principle); confirm in the IS step that creates the project.
- **U-5** Worker config: shared `appsettings.json` with the primary vs. its own. Affects secret-handling at install time.
- **U-6** Worker URL discovery: config-file (likely) vs. service discovery.
- **U-7** Contract-test strategy for the inter-API surface (consumer-driven vs. shared-DTO vs. OpenAPI-spec-driven). Recommendation pending in the IS step that wires the contract: shared-DTO via the existing `Dorc.ApiModel` pattern is the lowest-friction choice.
- **U-8** `Setup.Acceptance` handling: install the worker MSI for the test environment, or stub the worker endpoints. Gates SC-6 (contract tests on a real worker vs. mock).
- **U-9** Graph permission set required (delegated vs application; specific permission names). Affects customer setup guidance (C-4) and the IS step that does the Graph migration. **Recommendation pending architecture confirmation:** application-only permissions (`User.Read.All`, `Group.Read.All`, `GroupMember.Read.All`) with admin consent, since the worker runs as a service account and never acts on behalf of an end user.
- **U-10** *(promoted to Blocking in Round 4 — detail retained here.)* Migration path for existing customers from AD to Entra. **Owner: Product.** Two customer cohorts and two outcomes:
  - **Cohort A — has Entra tenant + Entra Connect populating `onPremisesSecurityIdentifier`.** Upgrade is transparent: P-4 / P-7 in the parity matrix resolve existing `AccessControl.Sid` rows via Entra. SC-10 covers this.
  - **Cohort B — pure on-prem AD, no Entra tenant.** Hard break. Existing ACLs cannot be resolved post-migration.
  Product decision required before release: do we publish migration guidance only, or do we ship a back-compat shim (e.g. an on-prem LDAP fallback for the SID-resolution path)? Default position absent product input: hard break for Cohort B; published prerequisites; no shim.
- **U-11** Worker-absence detection on Linux: how does the primary know the worker is not available? Options: explicit config flag (`WindowsWorker:Enabled=false`), startup health-probe, null-impl registration based on `OperatingSystem.IsLinux()`. Decision in S-003's SPEC; recommendation pending: config flag (explicit, environment-agnostic, debuggable).

---

## 8. References

- Issue: [sefe/dorc#423](https://github.com/sefe/dorc/issues/423)
- Superseded PR: [sefe/dorc#424](https://github.com/sefe/dorc/pull/424) (closed unmerged — see PR description for closure rationale)
- This HLPS's PR: [sefe/dorc#706](https://github.com/sefe/dorc/pull/706)
- Research carried over from PR #424 (informational; superseded by §2 for current file paths):
  - [`research/WINDOWS_DEPENDENCIES_ANALYSIS.md`](research/WINDOWS_DEPENDENCIES_ANALYSIS.md)
  - [`research/ARCHITECTURE_API_SPLIT.md`](research/ARCHITECTURE_API_SPLIT.md)
  - [`research/REGISTRY_UPGRADE_EXAMPLE.md`](research/REGISTRY_UPGRADE_EXAMPLE.md)
  - [`research/FILES_TO_MOVE_ANALYSIS.md`](research/FILES_TO_MOVE_ANALYSIS.md)
  - [`research/DEPLOYMENT_DEPENDENCY_ANALYSIS.md`](research/DEPLOYMENT_DEPENDENCY_ANALYSIS.md)
- CLAUDE.md naming principle: cohesive naming over banned-words blacklist. Memory: `feedback_naming_principle.md`.

---

## 9. Next Steps (indicative — IS document is binding)

The step list below is **indicative**, not binding — the binding ordering lives in `IS-api-split.md` once drafted. Listed here so the HLPS's shape and the IS's shape are obviously aligned.

1. **Adversarial review of this HLPS — Round 2** (CLAUDE.md checkpoint). After this revision lands, the document returns to `IN REVIEW` for a second panel pass.
2. On approval, draft `IS-api-split.md`. Indicative ordering with the riskiest step first (per Round-1 finding M-7):
   - **S-001 — Graph migration (the spike).** Promote `AzureEntraSearcher`, remove its `[SupportedOSPlatform]` attribute, close the parity-matrix ❌ gaps (P-4 SID lookup, P-5 sAMAccountName resolution, P-7 dual-ID claims emission), switch consumers, delete AD code, drop `System.DirectoryServices*` refs from `Dorc.Core` and `Dorc.PersistentData`, **ship the Linux build CI gate** that enforces SC-1 going forward. If parity proves harder than D-2 assumes, we learn it here, not after four worker steps.
   - **S-002** — Create `Dorc.Api.WindowsWorker` project, worker host, shared-secret auth scheme, DI scaffolding, MSI skeleton.
   - **S-003** — Define `IWindowsWorkerClient` HTTP contract; null/notSupported impl for Linux installs.
   - **S-004** — Move smallest Windows-only endpoint family (registry/remote-server probing) as proof-of-pattern.
   - **S-005** — Move WMI service-status probe path.
   - **S-006** — Move `ResetAppPasswordController` impersonation (worker uses its own service account); includes SC-8b carve-out for that controller's log-injection fix.
   - **S-007** — Remove Windows-auth scheme from primary (Negotiate scheme + `WinAuth*` files).
   - **S-008** — Installer wiring: ship worker MSI component, provision shared secret + service account at install time (subject to C-7 security review).
   - **S-009** — Remaining log-injection SPEC carve-outs (per SC-8b) for `BundledRequestsController`, `MakeLikeProdController`, `Deployment/Requests.cs`.
   - **S-010** — Documentation: Entra tenant setup, Graph permissions, AD-to-Entra migration prerequisites, deployment topology.
3. SPECs are drafted just-in-time per S-step, each adversarially reviewed before execution.

---

## Appendix A — Round-1 review findings disposition

For audit. Disposition of findings from the 2026-05-28 adversarial panel (Round 1).

| ID | Severity | Finding (one-line) | Disposition |
|---|---|---|---|
| H-1 | HIGH | `AzureEntraSearcher.cs` path wrong (it's in `Dorc.Core`) | Accept — fixed in §2 and D-2 |
| H-2 | HIGH | `[SupportedOSPlatform("windows")]` on `AzureEntraSearcher` contradicts SC-1 | Accept — removal added to D-2 / Scope A |
| H-3 | HIGH | AD-deletion scope broader than `Dorc.Api` (covers `Dorc.Core` + `Dorc.PersistentData`) | Accept — Scope A and D-2 expanded codebase-wide |
| H-4 | HIGH | SC-4 "work normally via Graph" unmeasurable without parity matrix | Accept — parity matrix added to §4; SC-9 added |
| H-5 | HIGH | C-1 "no client-side change" vs. SC-4 503 envelope | Accept — C-1 reworded; SC-4 clarified as documented envelope |
| R2-1 | HIGH | U-8 (Setup.Acceptance) gates SC-6 contract tests | Acknowledged — U-8 reframed with explicit gating; remains non-blocking per user direction |
| R2-4 | HIGH | U-9 (Graph permissions) should be blocking | Acknowledged with recommendation in U-9; remains non-blocking per user direction |
| R2-5 | HIGH | Windows auth (Negotiate, WinAuth*) missing from In-Scope | Accept — Scope E added; S-007 added |
| M-1 | MED | D-3 rotation/restart policy missing | Accept — added to D-3 |
| M-2 | MED | D-3 threat model not stated | Accept — added to D-3 |
| M-3 | MED | WMI long-term home unclear | Accept — D-1 clarified worker is permanent |
| M-4 / R2-9 | MED | U-10 needs decision owner | Accept — U-10 owner = Product; default position recorded |
| M-5 | MED | SC-8 conflates LDAP vs log injection | Accept — split into SC-8a / SC-8b |
| M-6 | MED | SC-6 contract source unspecified | Accept — recommendation added to U-7 |
| R2-7 | MED | S-001 should be Graph migration (riskiest spike first) | Accept — Next Steps reordered |
| R2-8 / C-5 | MED | C-5 get-out clause for Graph parity | Accept — parity matrix in §4 closes the loophole |
| L-1 | LOW | Next Steps pre-empts IS | Downgrade — labelled "indicative — IS document is binding" |
| L-2 | LOW | Worker name in D-1 | Defer — U-4 tracks |
| L-3 | LOW | File counts not pinned | Accept — frontmatter `codebase_anchor` field added |
| L-4 | LOW | Secret protection class hand-wavy | Accept — named in D-3 |
| R2-11 | LOW | Installer secret-handling security-review pass | Accept — C-7 added |
| R2-12 | LOW | `ApiRegistry.cs` not listed | Accept — listed in §2 and D-2 |
| R2-10 | LOW | Status-frontmatter timing nit | Reject — cosmetic |

### Round 2 findings disposition

| ID | Severity | Finding (one-line) | Disposition |
|---|---|---|---|
| NH-1 | HIGH | Parity matrix missed legacy AD SID lookup (every existing `AccessControl.Sid` row would 404 post-migration) | Accept — added as P-4 in §4 parity matrix with explicit Graph strategy (`$filter=onPremisesSecurityIdentifier eq '...'`); P-7 added to ensure claims-expansion path emits both `Pid` and `Sid`; SC-10 added; U-10 reframed with Cohort A / Cohort B distinction; Scope A bullet added requiring `GetUserDataById` SID-fallback in S-001 |
| NH-2 | HIGH | `GetGroupSidIfUserIsMemberRecursive` silently broken for sAMAccountName callers (Graph impl treats userName as Entra ID) | Accept — added as P-5 in §4 parity matrix; Scope A bullet added requiring `GetGroupSidIfUserIsMemberRecursive` to resolve `userName` via `onPremisesSamAccountName` filter before calling `checkMemberGroups` |
| NM-1 | MED | D-2 misdiagnosed `[SupportedOSPlatform("windows")]` attribute as "inherited" | Accept — D-2 reworded: "the attribute is incorrect — Graph is cross-platform" |
| NM-2 | MED | Scope A "tests via mocks at the boundary" insufficient for SC-9 | Accept — Scope A tightened: every parity-matrix row requires an integration-level test against a Graph SDK fake; SC-9 reinforced with the same requirement |
| NM-3 | MED | SC-4 `503` envelope doesn't say how primary detects worker absence | Accept — added as new non-blocking U-11 with config-flag recommendation; decision in S-003 SPEC |
| NL-1 | LOW | CI gate not visible in Next Steps | Accept — S-001 step description updated to call out the Linux build CI gate explicitly |
| NL-2 | LOW | Appendix A maps R2-8 to MED but Round 1 had it as HIGH | Accept (cosmetic) — original Round 1 review listed R2-8 in its MEDIUM section ("'No silent functional change… except where Graph semantics differ' is a get-out clause"), so the mapping is correct; this row clarifies the trace |

---

## Appendix B — Round 4 revision: why an APPROVED HLPS was re-opened

Round 3 approved this document on 2026-05-28 against codebase anchor `aab79d14`. Between then and
2026-08-17, steps S-001, S-002, S-003, S-004 and S-007 were implemented and adversarially reviewed.
That work falsified four success criteria and surfaced five behavioural changes the HLPS never
stated. Approval at Round 3 was therefore premature — not wrong in its decisions (D-1/D-2/D-3 all
held up under implementation), but wrong in claiming its criteria were sound and its scope complete.

The pattern worth recording: **every one of these was invisible to document review and only
became visible when something executed.** A plan review cannot tell you that a CI gate does not
fail, or that a test passes against a mutated implementation.

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| R4-1 | HIGH | SC-9 mandated `docs/api-split/parity-matrix.md` since Round 1. The file was never created; the matrix lived only in §4 and SPEC-S-001, both of which go stale when their step merges. | Accept — artefact created; SC-9 reworded to reference it and to require request-level assertions. |
| R4-2 | HIGH | Three matrix rows marked ✅ had no evidence: P-6's negative case and P-9 had no test at all, and P-1/P-2/P-4's tests asserted only the response, so a wrong `$filter` passed. Mutation testing showed six of eight injected defects — including an authorization bypass and removal of the OData escaping — passing the suite. | Accept — SC-9 now requires a row's test to fail when the behaviour is removed; matrix records mutation evidence per row. |
| R4-3 | HIGH | SC-1 combined `System.DirectoryServices*` (removed at S-001) with `System.Management` (removed at S-005) into one criterion, making it unsatisfiable for the entire span between those steps and giving no way to state partial compliance. | Accept — split into SC-1a / SC-1b. |
| R4-4 | HIGH | SC-1's CI gate was specified as "a Linux build job that fails if Windows-only refs reappear". A build job does not fail: CA1416 is a warning unless promoted, so full `System.DirectoryServices.AccountManagement` usage compiles clean on Linux. The criterion described an enforcement mechanism that cannot enforce. | Accept — SC-1 now specifies three layers and names CA1416-as-error explicitly. |
| R4-5 | MEDIUM | SC-8b assigned the `ResetAppPasswordController` log-injection fix to S-005, which is the WMI probe step and never touches that file. The IS assigns that controller to S-006. | Accept — corrected to S-006. |
| R4-6 | MEDIUM | The Round 3 anchor was 214 commits stale. In that window `main` gained the Kafka substrate and an LDAP-injection fix to `ActiveDirectorySearcher` — a file S-001 deletes. The identical vulnerable regex in `AzureEntraSearcher` was never covered by that fix, so migrating as planned would have reverted a security fix in effect, with no git conflict to signal it. | Accept — re-anchored to `aebd286`; carry-forward performed in S-001. Recorded because an HLPS pinned to a stale anchor cannot see this class of interaction. |
| R4-7 | MEDIUM | U-10 (Cohort B hard break) sat in the non-blocking list with an authors' default standing in for a Product decision, while the HLPS was APPROVED and implementation proceeded. | Accept — promoted to Blocking. |
| R4-8 | MEDIUM | Five consequences of S-001/S-007 were absent from Scope: M2M principals leaving the identity picker, `ActiveDirectoryRoles` becoming dead config with a new IdP prerequisite, `IsUseAdSidsForAccessControl` becoming a no-op, loss of composite fault tolerance, and Graph result truncation. | Accept — added to Scope / Out of Scope and tracked as U-12 through U-17. |
| R4-9 | MEDIUM | The step sequence lets S-004 and S-007 merge before S-008 provisions them, turning working endpoints into 503s and a failed startup on existing installs. The HLPS records step *dependencies* but has no notion of a step being un-shippable without a later one. | Accept — U-16 added as blocking; IS dependency column should distinguish "builds on" from "cannot ship without". |

### What Round 4 does not resolve

- U-10, U-12 and U-16 are blocking and need named owners, not author defaults.
- SC-3 ("verified by parity tests against pre-split fixtures") still has no fixtures. S-004 shipped
  with no test asserting the worker's registry endpoint rejects an unauthenticated request, and the
  worker projects are `net8.0-windows`, so they cannot be verified in Linux CI at all. SC-2 and SC-3
  need a stated verification host before S-005 and S-006 add more worker surface.
- SC-6 depends on U-8 (`Setup.Acceptance` strategy), still unanswered.
