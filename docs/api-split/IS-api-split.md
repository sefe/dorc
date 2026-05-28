---
status: APPROVED
author: Agent
date: 2026-05-28
issue: sefe/dorc#423
folder: docs/api-split/
hlps: HLPS-api-split.md (APPROVED)
codebase_anchor: aab79d14 (main, 2026-05-28)
---

# IS: API Split — Implementation Sequence

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED (Round 2 verification 2026-05-28 — S-005 NS-1 contradiction fixed; all Round 1 findings closed) |
| **Author**  | Agent                                                   |
| **Date**    | 2026-05-28                                              |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)       |
| **Folder**  | docs/api-split/                                         |
| **Issue**   | sefe/dorc#423                                           |
| **Codebase anchor** | `aab79d14` (`main`, 2026-05-28)                 |

---

## Step Index

| ID    | Title                                                             | Addresses (HLPS)               | Depends On    |
|-------|-------------------------------------------------------------------|---------------------------------|---------------|
| S-001 | Graph migration — promote `AzureEntraSearcher`, close parity gaps, delete AD code | D-2, Scope A, SC-1, SC-8a, SC-9, SC-10 | —             |
| S-002 | `Dorc.Api.WindowsWorker` project scaffold + loopback host + shared-secret auth scheme | D-1, D-3, Scope B, SC-2 | —     |
| S-003 | `IWindowsWorkerClient` contract + null-impl for Linux + worker-absence detection (U-11) | D-3, Scope C, SC-4 | S-002         |
| S-004 | Move `RefDataServersController` registry/remote-server probing to worker (proof-of-pattern) | Scope D, SC-3 | S-001, S-003 |
| S-005 | Move WMI service-status probe path (`DaemonStatusProbe`, `WmiUtil`) to worker | Scope D, SC-3        | S-001, S-003  |
| S-006 | Move `ResetAppPasswordController` impersonation to worker (worker-move only; security fix is S-009) | Scope D, SC-3        | S-001, S-003  |
| S-007 | Remove Windows authentication scheme from primary (Negotiate, `WinAuth*`) | Scope E, SC-1        | S-001         |
| S-008 | MSI installer wiring — worker component, shared-secret + service-account provisioning | Scope B, SC-7, C-7   | S-002         |
| S-009 | Log-injection SPEC carve-outs for `ResetAppPasswordController`, `BundledRequestsController`, `MakeLikeProdController`, `Deployment/Requests.cs` | SC-8b | — |
| S-010 | Documentation + Linux container smoke test                        | Scope F, SC-1, C-4   | S-001, S-002, S-003, S-007 |

### Ordering note
This work spans **multiple PRs**, not one. The natural cut points are S-001, the worker family (S-002..S-006 + S-008), Windows-auth removal (S-007), and the cross-cutting carve-outs (S-009, S-010). Each PR completes one or more atomic steps; no PR ships a partial step. The `Depends On` column is *prerequisite for merge*, not for authoring — steps with no shared prerequisites can be authored in parallel.

### Parallelism
- **S-001 and S-002 are fully independent** — Graph migration and worker scaffold touch disjoint files. Two contributors can pick them up simultaneously.
- **S-009 is independent of everything** — log-injection fixes can ship at any time and may go in earlier as standalone PRs if reviewer bandwidth allows.
- **S-004 / S-005 / S-006 are mutually independent at code level** once S-003 lands — but all three also depend on S-001 (S-001 reshapes `Dorc.Core`'s csproj and DI; the WMI/registry/password-reset moves touch the same files). Authors must not ship these ahead of S-001.
- **S-007 depends only on S-001** (the JWT/OAuth2 path becomes the sole auth scheme once AD-backed Negotiate is replaceable).
- **S-008** can begin skeleton-only after S-002 lands and finish as worker endpoints fill in.
- **S-010** is the closeout — depends on the substantive steps but is documentation-only.

### Risk shape
- **S-001 carries the most risk** (parity gaps P-4/P-5/P-7, customer-impact via Cohort A/B, Entra Connect prerequisite). It is sequenced first as the spike: if the parity work is harder than HLPS D-2 assumes, we learn it before the worker steps are stranded.
- **S-006 carries the second-most risk** — password reset is the only impersonation path; mistakes here are user-visible security regressions. Treat its SPEC as security-sensitive.
- **S-008 is third** — installer changes have to survive every customer upgrade path. Pair with the C-7 security review pass.
- All other steps are mechanical moves behind a well-defined interface seam.

---

## S-001 — Graph migration: promote `AzureEntraSearcher`, close parity gaps, delete AD code

### What changes
`Dorc.Core/AzureEntraSearcher.cs` becomes the sole production `IActiveDirectorySearcher` implementation. Its incorrect `[SupportedOSPlatform("windows")]` attribute is removed. The three parity-matrix gaps from HLPS §4 are closed:

- **P-4 (legacy AD SID lookup):** the user-by-id path falls back to `$filter=onPremisesSecurityIdentifier eq '<sid>'` against users then groups when a direct `Users[id]`/`Groups[id]` lookup returns 404 and the input matches the SID shape (`S-1-5-...`).
- **P-5 (sAMAccountName resolution):** the recursive-membership path resolves the `userName` argument via `$filter=onPremisesSamAccountName eq '<name>' or userPrincipalName eq '<name>'` before calling `checkMemberGroups`. The SPEC must handle the `DOMAIN\name` form by stripping the prefix prior to the filter.
- **P-7 (dual-ID claims emission):** the path that expands a user's group memberships emits both the Entra `id` and `onPremisesSecurityIdentifier` so consumer code can match against either `Pid` or `Sid` columns (current pattern: `ac.Pid ?? ac.Sid`).

The deleted-from-codebase AD set: `Dorc.Core/ActiveDirectorySearcher.cs`, `Dorc.Core/CompositeDirectorySearcher.cs` (class `CompositeActiveDirectorySearcher`), `Dorc.Core/IdentityServer/IdentityServerSearcher.cs`, `Dorc.Api/Services/DirectorySearcherFactory.cs`, `Dorc.Api/Services/UserGroupReaderFactory.cs`, `Dorc.Api/Services/ActiveDirectorySearchService.cs`. Interfaces (`IActiveDirectorySearcher`, `IDirectorySearcherFactory`) are reduced to the Graph contract or removed if no longer referenced.

`Dorc.Api/Services/ApiRegistry.cs` DI switches from the composite/factory pattern to direct registration of the Graph-backed searcher. AD-using consumers (`ClaimsTransformer`, `CachedUserGroupReader`, `DirectorySearchController`, `AccessControlController`, `RefDataEnvironmentsUsersController`, `RefDataProjectsController`, `PropertyValuesController`, `RequestController`, and their tests) keep their `IActiveDirectorySearcher` dependency and gain no behavioural change beyond the documented parity matrix.

The `System.DirectoryServices` and `System.DirectoryServices.AccountManagement` package refs are removed from `Dorc.Core.csproj` and `Dorc.PersistentData.csproj`.

A CI gate is added: a Linux build job for `Dorc.Api` (and its transitive deps `Dorc.Core`, `Dorc.PersistentData`) that fails the pipeline if Windows-only refs reappear. This protects SC-1 going forward.

### Why it changes
Addresses HLPS D-2, satisfies SC-1 (no Windows-only refs in primary's compile graph), satisfies SC-8a (LDAP-injection class disappears with the LDAP code), satisfies SC-9 (parity matrix has test coverage), satisfies SC-10 (existing `AccessControl.Sid` rows resolve via `onPremisesSecurityIdentifier`). Sequenced first because parity work is the highest-risk part of the HLPS; if D-2's assumptions prove wrong, no worker plumbing has been wasted.

### Dependencies
None. Touches `Dorc.Core`, `Dorc.PersistentData`, `Dorc.Api` (DI + AD consumers), and CI configuration.

### Verification intent
- `Dorc.Api.csproj`, `Dorc.Core.csproj`, `Dorc.PersistentData.csproj` build with no `System.DirectoryServices*` package refs.
- **`Dorc.Api.Client.csproj` still builds unchanged** against the updated `Dorc.ApiModel` (covers SC-5 for this step).
- A Linux Docker build of `Dorc.Api` succeeds and the resulting container starts to the point of binding its HTTP listener. (Full Linux endpoint functionality is verified in S-010's smoke test.)
- Every row of the HLPS §4 parity matrix has at least one integration test that exercises the Graph-backed path against a Graph SDK fake or a recorded HTTP harness; the tests assert the observable behaviour, not the Graph payload shape.
- Re-running the PR #424 SAST scan against this branch shows zero LDAP-injection findings (SC-8a).
- A scripted check (`git grep`) for `System.DirectoryServices` returns hits only in `docs/api-split/research/` (informational research) and nowhere in `src/`.

---

## S-002 — `Dorc.Api.WindowsWorker` project + worker host + shared-secret auth scheme

### What changes
New ASP.NET Core project `Dorc.Api.WindowsWorker` (final name confirms HLPS U-4) added to `src/Dorc.sln`. Kestrel binds to `127.0.0.1` only (configurable port). A worker authentication scheme is registered that reads the `X-Worker-Key` header and validates against a configured value; missing or mismatched headers return `401` with body `{"error":"worker_key_invalid"}` (matching HLPS D-3's diagnostic). All controllers added to the worker carry `[Authorize(Policy = "FromPrimary")]` so the scheme is mandatory.

The worker has its own `Program.cs`, `appsettings.json` (resolving HLPS U-5 to favour a worker-local config file for clean secret-store separation), and a minimal health endpoint `GET /health` returning `200 OK` to support S-003's worker-absence detection.

The project is initially endpoint-empty — no Windows operations yet. Subsequent steps (S-004..S-006) add controllers.

### Why it changes
Addresses HLPS D-1 (loopback worker process), D-3 (shared-secret auth), Scope B (worker exists). Satisfies SC-2 at the binding/auth-scheme level (endpoint behaviour proven by S-004 onward).

### Dependencies
None. Brand-new project; no cross-project edits required beyond `Dorc.sln`.

### Verification intent
- Project builds on Windows. (Project is intentionally Windows-only — `<TargetFramework>net8.0-windows</TargetFramework>` per HLPS Scope B.)
- Worker starts, binds only to `127.0.0.1` (verified by netstat / a connection from another interface failing).
- A test client hitting `/health` with no `X-Worker-Key` header gets `401` + the documented body.
- A test client with the correct header gets `200 OK`.
- The shared-secret value is read from `appsettings.json` (or its environment overlay); a startup check fails fast if the value is missing or empty.

---

## S-003 — `IWindowsWorkerClient` contract + null-impl for Linux + worker-absence detection

### What changes
`Dorc.Api` gains an `IWindowsWorkerClient` interface defining the Windows-only operations the primary will delegate (initially empty; controllers added in S-004..S-006 will extend it). A `DelegatingHandler` injects the `X-Worker-Key` header into outbound calls from the typed `HttpClient`.

A second implementation (`WorkerUnavailableClient` or similar) is registered when worker support is disabled. Selection is per HLPS U-11: a `WindowsWorker:Enabled` boolean in `appsettings.json` controls registration. When disabled, every `IWindowsWorkerClient` method throws a typed exception that translates (via existing middleware or a new short ExceptionFilter) to `503 Service Unavailable` with body `{"error":"windows_worker_unavailable", "endpoint":"<name>"}` per HLPS SC-4.

**Open SPEC-level decision** (to resolve in S-003 SPEC, named here so it isn't silently picked): the typed exception may be thrown either *synchronously at method call* by `WorkerUnavailableClient`, or *raised at startup* via a missing-registration probe. Synchronous-at-call gives a per-endpoint diagnostic; startup-probe fails fast. Recommendation pending: synchronous-at-call, so that an endpoint-specific `503` includes the endpoint name in the body (matching the body shape promised in SC-4).

Worker URL is configured under `WindowsWorker:Url` in `appsettings.json` (resolving HLPS U-6 to favour explicit config over discovery, matching DORC's existing pattern). When `WindowsWorker:Enabled=true` and the URL is missing or unreachable at startup, the primary logs a clear error and uses the null implementation.

Contract tests (resolving HLPS U-7 to shared-DTO via `Dorc.ApiModel`) live under `Dorc.Api.Tests` and exercise both the enabled (against a stub worker) and disabled paths.

### Why it changes
Addresses HLPS D-3, Scope C, SC-4. Establishes the seam every subsequent worker-move step uses. Resolves non-blocking unknowns U-6, U-7, U-11.

### Dependencies
S-002 (the worker exists to call).

### Verification intent
- Unit tests cover the `DelegatingHandler`'s header injection.
- Integration tests with `WindowsWorker:Enabled=false` confirm the `503` body shape on every documented endpoint.
- Integration tests with `WindowsWorker:Enabled=true` and a stub worker confirm the round-trip works.
- Startup tests confirm the typed-exception → `503` translation is wired.

---

## S-004 — Move `RefDataServersController` registry/remote-server probing to worker (proof-of-pattern)

### What changes
The Windows-registry read used to detect remote-server OS versions (currently in `Dorc.Api/Controllers/RefDataServersController.cs`) moves to a new worker controller. The primary's controller becomes a thin pass-through that calls `IWindowsWorkerClient.GetRemoteServerInfoAsync(...)`. A shared-DTO model (in `Dorc.ApiModel`) carries the response across the hop.

The `Microsoft.Win32.Registry` usage is removed from `Dorc.Api`. If no other Windows-only registry usage remains, the `Microsoft.Win32.Registry` package ref (if explicit) drops from `Dorc.Api.csproj`.

**Fixture capture sequencing** (also applies to S-005 and S-006): the pre-split parity-test fixtures (captured by running the current endpoint against representative inputs and saving the response) must be captured against `main` *before* this PR branches. The SPEC for this step establishes the fixture format and check-in location; subsequent move-PRs reuse it.

### Why it changes
Smallest worker-move surface; proves the S-002/S-003 pattern works end-to-end before the larger moves (S-005, S-006). Addresses Scope D, contributes to SC-3.

### Dependencies
- S-001 (Graph migration must be in so AD code isn't simultaneously moving).
- S-003 (the `IWindowsWorkerClient` contract exists).

### Verification intent
- On Windows with the worker running, calling the primary's `RefDataServers` OS-detection endpoint produces the same response as before the move (parity test against a pre-split fixture).
- On Linux (worker disabled), the endpoint returns the documented `503`.
- The new worker controller has its own integration tests against a fake registry where possible.

---

## S-005 — Move WMI service-status probe path to worker

### What changes
The Windows-only probe path in `Dorc.Core/DaemonStatusProbe.cs` (the post-#649 rename of `ServiceStatus.cs`) and the supporting `Dorc.Api/Services/WmiUtil.cs` move to the worker. The interface seam in `Dorc.Core` (likely an `IDaemonStatusProbe` or equivalent) stays in `Dorc.Core` so consumers don't change shape; the implementation behind it splits — the worker has the WMI implementation, the primary's implementation calls the worker via `IWindowsWorkerClient`.

`System.Management` package ref drops from any project where it was added solely for WMI.

### Why it changes
Addresses Scope D, contributes to SC-3, contributes to SC-1 (removes `System.Management`).

### Dependencies
S-001 and S-003. S-001 reshapes `Dorc.Core`'s csproj and DI; moving `DaemonStatusProbe.cs` out of `Dorc.Core` before S-001 settles would produce csproj merge conflicts. The WMI code itself does not touch AD, but the shared host project does.

### Verification intent
- On Windows with worker running, `DaemonStatusController` returns identical results to pre-split for a representative set of services on a representative set of servers (parity test).
- On Linux (worker disabled), `DaemonStatusController` returns documented `503`s.
- `git grep "System.Management"` returns no hits outside the worker project (and `docs/`).

---

## S-006 — Move `ResetAppPasswordController` impersonation to worker (worker-move only)

### What changes
The `WindowsIdentity` impersonation logic currently in `Dorc.Api/Controllers/ResetAppPasswordController.cs` moves to a worker endpoint. The primary's controller stays in place as a thin pass-through that authz's the caller (using Graph claims), forwards the request to the worker via `IWindowsWorkerClient.ResetPasswordAsync(...)`, and returns the response. The worker uses its own service account (an AD-delegated reset-password identity, configured at install time per S-008) — it does **not** impersonate the original caller. The caller's identity is included in the request body for audit logging only, per HLPS D-3.

The log-injection finding on this controller is **out of scope for this step** and is fixed in S-009 alongside the other log-injection items. Splitting the security fix from the worker-move keeps the security-sensitive diff on its own reviewable surface (per IS Round-1 finding H-1).

### Why it changes
Addresses Scope D, contributes to SC-3. Security-sensitive even without the SC-8b fix (impersonation logic is moving processes) — the SPEC for this step gets a dedicated adversarial review.

### Dependencies
- S-001 (caller authz now runs through Graph-backed claims, not Negotiate; HLPS D-3 assumes this for "primary authz's the caller").
- S-003 (the contract exists).

### Verification intent
- On Windows with worker running, password reset succeeds for the same input that succeeded pre-split, against a test AD or a Graph-fake setup (parity test against the pre-split fixture established in S-004).
- On Linux (worker disabled), the endpoint returns the documented `503`.
- Audit log entries contain the caller's identity (forwarded by the primary) without containing the worker's service-account identity (which should not appear in user-facing audit).
- The SAST scan against this controller is run, but the log-injection finding is allowed to remain pending S-009 (linked in the PR description).

---

## S-007 — Remove Windows authentication scheme from primary

### What changes
The Negotiate authentication scheme registration is removed from `Dorc.Api/Program.cs`. `Dorc.Api/Security/WinAuthClaimsPrincipalReader.cs` and `Dorc.Api/Security/WinAuthLoggingMiddleware.cs` are deleted. Any remaining references in DI or middleware pipelines are removed.

All authenticated traffic now flows through the existing OAuth2/JWT path, with claims expanded via the Graph-backed `IActiveDirectorySearcher` (per S-001). No new endpoints; no client-side change.

### Why it changes
Addresses Scope E, contributes to SC-1 (Negotiate is Windows-only via Kerberos/NTLM stacks). With Graph providing claims, Negotiate no longer earns its keep.

### Dependencies
S-001. (Graph claims path must be production-ready before Negotiate is removed.)

### Verification intent
- Authenticated requests against the primary work end-to-end with JWT only.
- **SID-based ACL parity:** a user who was previously authorised against `AccessControl.Sid` rows via Negotiate-derived SID claims continues to be authorised after this step, because Graph-claims expansion (per S-001's P-7 implementation) emits the same `onPremisesSecurityIdentifier` value the ACL row contains. Verified by an integration test using a Graph fake that exposes `onPremisesSecurityIdentifier` for the test user.
- **`Dorc.Api.Client.csproj` still builds unchanged** (covers SC-5 for this step).
- A Linux container build (CI gate from S-001) confirms no Negotiate-related package or assembly reference remains.
- Existing tests that asserted Negotiate-claims behaviour are either removed (no longer applicable) or rewritten to assert the equivalent Graph-claims behaviour.

---

## S-008 — MSI installer wiring for the worker

### What changes
A new MSI component is added to `Setup.Dorc/` (reference template: `Setup.Dorc/Web/RequestApi/ApiWindows.wxs` from PR #424, but rewritten against current main). The component installs `Dorc.Api.WindowsWorker`, registers it as a hosted process (see U-12 below), and provisions:

- The shared secret (`X-Worker-Key` value) — generated at install time, written to both the primary's and the worker's config in a single transaction.
- The worker's service-account credentials (for password-reset impersonation, used by S-006).
- The worker's `WindowsWorker:Url` value injected into the primary's config.

Upgrade paths are tested against representative prior-version installs to ensure no breakage.

C-7 (HLPS) applies: the install-time secret-handling surface gets an explicit security review pass before release.

### Why it changes
Addresses Scope B (installer), satisfies SC-7. Without this, the worker can't be deployed via the normal DORC install path.

### Dependencies
S-002 (the worker exists to install). Skeleton can begin immediately after S-002; final wiring (`WindowsWorker:Url` in primary config, secret generation) needs the contract details from S-003.

### Verification intent
- Fresh-install MSI on a Windows host produces a running primary + worker with the shared secret configured on both ends.
- Upgrade from the prior release version preserves existing primary settings and adds the worker.
- The shared secret is unique per install (no hard-coded default).
- The C-7 security review pass is signed off before release.

---

## S-009 — Log-injection SPEC carve-outs for remaining controllers

### What changes
The four log-injection findings — on `ResetAppPasswordController` (moved by S-006 but its log-injection fix lives here), `BundledRequestsController`, `MakeLikeProdController`, and `Deployment/Requests.cs` — are fixed using a common pattern: user-controlled values that flow into log entries are either parameterised (structured logging via `{}` placeholders, not string concatenation) or sanitised (newline/control-character stripping for cases where the value must appear inline).

A small sanitisation helper is factored once and reused across the four call sites. The pattern is documented in this step's SPEC so future log-injection findings have a single reference.

Each fix is a small change; they can ship as one PR (one SPEC, four file edits) or split per-controller — author's choice. Independent of the worker work — can ship at any time.

### Why it changes
Satisfies SC-8b. Surface area is small; bundling per-controller fixes into one IS step keeps the scope visible without forcing serial execution.

### Dependencies
None. Can be authored in parallel with everything else.

### Verification intent
- Re-running the PR #424 SAST scan against each controller after its fix returns zero log-injection findings for that file.
- Existing logging behaviour (what shows up in DORC's logs for a given request) is unchanged for non-malicious input.

---

## S-010 — Documentation + Linux container smoke test

### What changes
Two artefacts:

- **Architecture documentation** in `docs/api-split/`: runtime topology (primary + optional Windows worker), configuration surface (`WindowsWorker:Enabled`, `Url`, secret), inter-API contract overview, Linux deployment guide.
- **Customer-facing documentation**: Entra ID tenant setup, app registration, required Graph permissions (per HLPS U-9 recommendation: application permissions `User.Read.All`, `Group.Read.All`, `GroupMember.Read.All` with admin consent; finalised in this step's SPEC by checking the actual API surface required to satisfy parity matrix P-1..P-9), AD-to-Entra migration prerequisites including Entra Connect requirement for existing-`AccessControl.Sid` installs (HLPS U-10 Cohort A vs B). Default position for Cohort B per HLPS: hard break documented.

- **Linux container smoke test** in CI: a workflow that builds the `Dorc.Api` Linux container, runs it with a stub Graph backend and `WindowsWorker:Enabled=false`, hits a representative set of endpoints, and asserts the cross-platform path works end-to-end. This is the *positive* counterpart to the SC-1 CI gate (S-001's gate is a negative check — no Windows refs; this is a positive check — actually runs on Linux).

### Why it changes
Closes out Scope F and the C-4 customer-documentation constraint. The smoke test guards SC-1 dynamically (CI gate is static).

### Dependencies
S-001 (Graph migration in), S-002 (worker exists for the architecture diagram to be accurate), S-003 (`WindowsWorker:Enabled=false` behaviour is in), S-007 (no Negotiate scheme to confuse the diagram).

### Verification intent
- Documentation lands and is linked from `docs/api-split/`'s README (created in this step).
- Linux smoke-test workflow is added to `.github/workflows/` and passes against the merged branch.
- A reviewer following the customer-facing setup doc end-to-end can stand up an Entra app registration, configure DORC against it, and exercise an authenticated request.

---

## Cross-cutting acceptance for the IS as a whole

At the end of S-010, the HLPS Success Criteria SC-1..SC-10 are all satisfied. Specifically:

| SC    | Step(s) that satisfy it           |
|-------|-----------------------------------|
| SC-1  | S-001 (negative — no Windows refs), S-007 (Negotiate removal), S-010 (positive — Linux container runs) |
| SC-2  | S-002 (worker binding + auth scheme), S-008 (installer) |
| SC-3  | S-004, S-005, S-006 (each verifies parity)             |
| SC-4  | S-003 (worker-absence behaviour)                       |
| SC-5  | S-001 and S-007 verification intents explicitly assert `Dorc.Api.Client` builds unchanged; S-010 smoke test exercises the client surface end-to-end |
| SC-6  | S-003 (contract tests), S-001 (Graph parity tests)     |
| SC-7  | S-008                                                   |
| SC-8a | S-001 (LDAP-injection class eliminated)                |
| SC-8b | S-009 (all four controllers, including `ResetAppPasswordController`) |
| SC-9  | S-001 (parity-matrix tests against Graph fake)         |
| SC-10 | S-001 (`onPremisesSecurityIdentifier` lookup + test against fake exposing it) |

---

## IS-level Lookahead Unknowns

Carried forward from the HLPS plus those surfaced during IS drafting / Round-1 review.

- **U-12 [IS-level, IS Round-1] — Worker hosting model on Windows: Windows Service vs IIS-hosted process.** Each has security and upgrade implications. Decision must be made before S-008's SPEC drafts (it shapes the MSI component and the install-time secret-provisioning surface). If the decision forces a non-trivial change to the worker host code (e.g., IIS's `IHostBuilder` integration), revise S-002 retroactively. Recommendation pending: Windows Service (simpler, no IIS dependency, matches the existing DORC Monitor service model).
- Other Lookahead unknowns inherited from HLPS §7 (U-4..U-11) — resolved in the SPEC step that touches them, as noted in the per-step bodies.

---

## Notes for SPEC authors

Each S-step gets a JIT SPEC (`SPEC-S-00X-<title>.md`) before execution. Per CLAUDE.md the SPEC contains pseudocode / plain-language, not copy-pasteable code, and goes through adversarial review before the step is executed.

The recurring patterns SPECs will need to lock down:
- **Graph SDK fakes / recorded HTTP harness** for the parity-matrix tests (S-001 SPEC establishes the pattern).
- **Worker-endpoint contract conventions** — DTO location (`Dorc.ApiModel`), versioning policy if any, error-shape conventions (S-003 SPEC establishes the pattern; S-004 onwards reuses).
- **Log-injection sanitisation helper** — common pattern factored once in S-009 across all four affected controllers.
- **Parity test harness** — pre-split fixture capture for the moved endpoints (S-004 SPEC establishes the pattern; S-005 and S-006 reuse).
