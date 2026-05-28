---
status: IN REVIEW
author: Agent
date: 2026-05-28
issue: sefe/dorc#423
folder: docs/api-split/
supersedes_pr: sefe/dorc#424
---

# HLPS: Split `Dorc.Api` into Linux-compatible API + Windows-only worker

| Field      | Value                       |
|------------|-----------------------------|
| **Status** | IN REVIEW                   |
| **Author** | Agent                       |
| **Date**   | 2026-05-28                  |
| **Issue**  | sefe/dorc#423               |
| **Folder** | docs/api-split/             |
| **Supersedes** | sefe/dorc#424 (Copilot agent PR, closed unmerged) |

---

## 1. Problem Statement

`Dorc.Api` is a single ASP.NET Core process that cannot run on Linux because portions of it depend on Windows-only stacks: `System.DirectoryServices` (Active Directory), `WindowsIdentity` / Negotiate / Kerberos, `System.Management` (WMI), and the Windows registry. Issue #423 requires the API to be split so that:

1. A primary API contains only code that runs on Linux (and Windows).
2. A secondary API contains the bare minimum Windows-only code.
3. The primary API can call into the secondary when required.

The motivating outcome is the ability to deploy the bulk of DORC on Linux hosts, treating Windows-only functionality (AD lookup, WMI service probing, registry reads, NTLM/Kerberos auth, password reset via impersonation) as an out-of-process Windows worker.

---

## 2. Observed Constraints from Today's Codebase

The research captured in [`research/WINDOWS_DEPENDENCIES_ANALYSIS.md`](research/WINDOWS_DEPENDENCIES_ANALYSIS.md) inventories the Windows surface. Summary:

| Surface                  | Files (approx.) | Linux alternative available?                     |
|--------------------------|-----------------|--------------------------------------------------|
| `System.DirectoryServices` (AD) | ~9        | Partially — `AzureEntraSearcher.cs` exists; full coverage requires either Graph or `Novell.Directory.Ldap.NETStandard` |
| Windows Authentication (NTLM/Kerberos/impersonation) | ~9 | OAuth2/JWT already supported; full removal of Negotiate requires deployment-side decision |
| WMI (`System.Management`) | ~9             | None drop-in. Remote service probing needs SSH/PowerShell-remoting/REST design |
| Registry (`Microsoft.Win32`) | ~14         | `RuntimeInformation` covers local; remote registry has no portable equivalent |

The PR #424 attempt mixed three concerns:
1. Structural split (the stated goal).
2. A bulk *banned-words* class rename (`PropertiesService→Properties`, `RequestService→Requests`, …) that misread CLAUDE.md's *naming principle* as a *naming blacklist* and produced grab-bag names (`Properties`, `Requests`, `Operations`) that are arguably worse than the originals.
3. Speculative refactors and orchestrator extraction.

Bundling (2) and (3) into the split caused a 5629-line diff with 113 files touched, persistent build-error cycles, and ~340 silently semantic-conflicted files once the parallel daemons-modernisation work landed on `main`. This HLPS deliberately scopes (1) only.

---

## 3. Resolved Decisions

Resolved with architecture owner on 2026-05-28. Recorded here as the design anchors the IS and SPECs will build on.

### D-1 — Worker process topology: HTTP loopback (was U-1)
The Windows worker is a **separate ASP.NET Core process** (working name `Dorc.Api.WindowsWorker.exe`) bound to `127.0.0.1` only. The primary holds an `IWindowsWorkerClient` whose implementation is an `HttpClient`. Per-request loopback cost is acceptable given the existing claims cache (see D-2 — most authz paths no longer touch the worker at all). Rationale: in-proc plugin defeats the Linux-deployment goal (Linux primary can't load a Windows assembly); queue sidecar is the wrong shape for synchronous request-path operations.

### D-2 — AD replaced with Microsoft Graph in the primary (was U-2)
`System.DirectoryServices` and `System.DirectoryServices.AccountManagement` are **removed from `Dorc.Api` and replaced with Microsoft Graph** by extending the existing `AzureEntraSearcher.cs`. AD code does **not** move to the worker — it is deleted from the codebase. Consequence: the worker's surface shrinks to the genuinely non-portable Windows operations (WMI, remote registry, password reset). The authorization hot path (`ClaimsTransformer` / `CachedUserGroupReader`) runs entirely in the Linux primary and never hits the worker.

Implication for customers: every DORC install now requires an Entra ID tenant with an app registration and Graph permissions (delegated + application as appropriate). This is a deliberate forward-looking choice; pure on-prem AD installs are out of scope for the new architecture.

### D-3 — Inter-API auth: loopback-only + shared secret header (was U-3)
The worker binds to `127.0.0.1` and rejects any request without a shared-secret header `X-Worker-Key`. The secret lives in `appsettings.json` alongside other DORC secrets (same protection class). Authorization decisions are made entirely in the primary (using Graph-backed claims) *before* the worker is called; the worker trusts that any call reaching it has already been authz'd. For password reset, the worker uses **its own service account** (an AD-delegated reset-password identity) — the caller's identity is forwarded in the request body for audit only, not for impersonation. Rationale: JWT/mTLS add machinery without material security benefit on loopback; loopback-only binding + shared secret matches the threat model.

---

## 4. Scope

### In Scope
- New project `Dorc.Api.WindowsWorker` (separate ASP.NET Core process, see D-1).
- Inter-API HTTP contract: `IWindowsWorkerClient` interface in `Dorc.Api`, REST surface in the worker.
- Shared-secret auth scheme on the worker (per D-3) + matching `DelegatingHandler` on the primary.
- **Replacing AD usage in `Dorc.Api` with Microsoft Graph** (per D-2):
  - Extend `AzureEntraSearcher.cs` to full functional parity with `ActiveDirectorySearcher.cs` (user/group search, recursive group membership, SID-equivalent resolution via Entra object IDs, claims expansion).
  - Wire `ClaimsTransformer`, `DirectorySearchController`, `CachedUserGroupReader`, and any other AD consumers to the Graph-backed path.
  - Delete `ActiveDirectorySearcher.cs` and remove `System.DirectoryServices` / `System.DirectoryServices.AccountManagement` package refs from `Dorc.Api` and any project that no longer needs them.
- Moving the genuinely Windows-only code to `Dorc.Api.WindowsWorker`:
  - WMI service-status code (`Dorc.Core/ServiceStatus.cs` / `DaemonStatusProbe.cs` per the post-#649 rename — the Windows-only probe path only).
  - Remote registry reads in `RefDataServersController` (Windows server OS-version detection).
  - Password-reset impersonation (`ResetAppPasswordController` → worker endpoint that runs as the worker's service account).
- Installer work: new MSI component for the worker (Windows-only deployments), shared-secret provisioning at install time, service account configuration.
- Documentation: architecture note explaining the runtime topology, Graph-tenant setup requirements for customers, and the configuration knobs.

### Out of Scope (explicitly)
- **Bulk class renaming** to remove the words *Service / Helper / Manager / Util*. CLAUDE.md's rule is principle-first; class-by-class renames belong in their own scoped PRs only when the new name is *more* specific than the old.
- Supporting pure on-prem AD installs without an Entra tenant. Per D-2, an Entra tenant is now a hard prerequisite.
- Replacing WMI with SSH/PowerShell-remoting (separate HLPS if/when desired).
- Folder reorganisation by "function" (`Identity/`, `Build/`, `Orchestration/`) inside the existing API — orthogonal to the split.
- Changing the public Swagger/REST surface of `Dorc.Api`. Existing clients (`Dorc.Api.Client`, `dorc-web`, CLIs) must continue to work unchanged.

---

## 5. Constraints

- **C-1 Backwards compatibility.** Existing API consumers (`dorc-web`, `Dorc.Api.Client`, CLI tools, MSI deployment) keep working with no client-side change.
- **C-2 No bundled refactor.** Naming, folder layout, and DI cleanup do not ride along on this PR.
- **C-3 Single host on Windows.** On Windows installs, the worker process ships and runs alongside the primary as a separate MSI component, bound to loopback only.
- **C-4 Customer infrastructure.** Every DORC install requires an Entra ID tenant + app registration with Graph permissions (per D-2). Document required permissions and provide setup guidance.
- **C-5 No silent functional change.** Every endpoint behaves identically pre- and post-split, except where Graph semantics differ from `System.DirectoryServices` (those differences must be enumerated and documented in the IS step that does the Graph swap).
- **C-6 Follow the HLPS → IS → SPEC process.** Each batch of file moves and the Graph migration each go through an IS step with their own SPEC and adversarial review.

---

## 6. Success Criteria

- **SC-1** `Dorc.Api` builds and runs on a Linux container (no `<RuntimeIdentifier>win-*</RuntimeIdentifier>`, no `System.DirectoryServices*` package refs, no `System.Management` refs).
- **SC-2** `Dorc.Api.WindowsWorker` builds and runs on Windows only; loopback-bound; rejects calls missing the shared-secret header.
- **SC-3** On Windows installs with the worker present: WMI, registry, and password-reset endpoints behave identically to today.
- **SC-4** On Linux installs (no worker): WMI / registry / password-reset endpoints return a documented `503` with a clear "this operation requires the Windows worker" body. AD-derived endpoints (search, claims expansion) work normally via Graph.
- **SC-5** Existing client apps (`dorc-web`, `Dorc.Api.Client`) make no code changes.
- **SC-6** Unit and integration tests pass at parity with pre-split coverage; new contract tests cover the primary↔worker HTTP surface; new tests cover the Graph-backed AD code path.
- **SC-7** MSI installer adds the worker as a Windows-only component without breaking existing upgrade paths.
- **SC-8** Security findings on PR #424 (LDAP injection in `DirectorySearchController`, log injection in 4 controllers) are addressed *as part of the migration*, not deferred. Note: LDAP-injection class disappears entirely once Graph replaces `System.DirectoryServices`.

---

## 7. Unknowns Register

### Blocking
*None remaining.* Original U-1, U-2, U-3 resolved as D-1, D-2, D-3 above.

### Non-blocking (resolved during IS)
- **U-4** Naming of the new project: `Dorc.Api.Windows` vs. `Dorc.Api.WindowsWorker`. Prefer the latter for specificity (per CLAUDE.md naming principle); confirm in S-001.
- **U-5** Worker config: shared `appsettings.json` with the primary vs. its own. Affects secret-handling at install time.
- **U-6** Worker URL discovery: config-file (likely) vs. service discovery.
- **U-7** Contract-test strategy for the inter-API surface.
- **U-8** `Setup.Acceptance` handling: install the worker for the test environment, or stub it.
- **U-9 [NEW]** Graph permission set required (delegated vs application). Affects customer setup guidance. To be resolved in the IS step that does the Graph migration.
- **U-10 [NEW]** Migration path for existing customers from AD to Entra. If a customer has on-prem AD only (no Entra Connect, no Entra tenant), the upgrade is a hard break — document upgrade prerequisites clearly.

---

## 8. References

- Issue: [sefe/dorc#423](https://github.com/sefe/dorc/issues/423)
- Superseded PR: [sefe/dorc#424](https://github.com/sefe/dorc/pull/424) (closed unmerged — see PR description for closure rationale)
- Research carried over from PR #424:
  - [`research/WINDOWS_DEPENDENCIES_ANALYSIS.md`](research/WINDOWS_DEPENDENCIES_ANALYSIS.md) — file-level inventory of Windows dependencies + cross-platform alternatives with historic-Windows compatibility matrices.
  - [`research/ARCHITECTURE_API_SPLIT.md`](research/ARCHITECTURE_API_SPLIT.md) — PR #424's proposed architecture (HTTP-based split). Informational; the topology decision is U-1.
  - [`research/REGISTRY_UPGRADE_EXAMPLE.md`](research/REGISTRY_UPGRADE_EXAMPLE.md) — concrete before/after for the registry dependency in `RefDataServersController`.
  - [`research/FILES_TO_MOVE_ANALYSIS.md`](research/FILES_TO_MOVE_ANALYSIS.md) — PR #424's file-by-file move evaluation.
  - [`research/DEPLOYMENT_DEPENDENCY_ANALYSIS.md`](research/DEPLOYMENT_DEPENDENCY_ANALYSIS.md) — analysis of hidden Windows deps in the Deployment folder.
- CLAUDE.md naming principle: cohesive naming over banned-words blacklist. Memory: `feedback_naming_principle.md`.

---

## 9. Next Steps

1. **Adversarial review of this HLPS** (CLAUDE.md checkpoint) — IN REVIEW until panel approval.
2. On approval, draft `IS-api-split.md`. With D-1/D-2/D-3 anchored, the IS shape is:
   - **S-001** — Create empty `Dorc.Api.WindowsWorker` project + worker host + shared-secret auth scheme + DI scaffolding + MSI component skeleton.
   - **S-002** — Define `IWindowsWorkerClient` HTTP contract surface in `Dorc.Api`; null/notSupported implementation for Linux installs.
   - **S-003** — Move the smallest Windows-only endpoint family end-to-end as the proof-of-pattern. Candidate: registry/remote-server probing (smallest, lowest blast radius).
   - **S-004** — Move WMI service-status code (the Windows-only probe path in `DaemonStatusProbe.cs`) to the worker.
   - **S-005** — Move `ResetAppPasswordController` impersonation to the worker; primary-side becomes thin pass-through with caller-identity-in-body for audit.
   - **S-006** — Graph migration: extend `AzureEntraSearcher.cs` to full parity, switch `ClaimsTransformer` / `CachedUserGroupReader` / `DirectorySearchController` to Graph. Resolves SC-8's LDAP-injection findings by removing the LDAP code path.
   - **S-007** — Remove `System.DirectoryServices*` and `System.Management` package refs from `Dorc.Api`; verify Linux container build (SC-1).
   - **S-008** — Installer wiring: ship the worker MSI component on Windows installs; provision shared secret + service account at install time.
   - **S-009** — Documentation: Entra tenant setup, Graph permissions, deployment topology.
3. SPECs are drafted just-in-time per S-step, each adversarially reviewed before execution.
