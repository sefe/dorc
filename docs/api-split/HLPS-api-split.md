---
status: DRAFT
author: Agent
date: 2026-05-28
issue: sefe/dorc#423
folder: docs/api-split/
supersedes_pr: sefe/dorc#424
---

# HLPS: Split `Dorc.Api` into Linux-compatible and Windows-only APIs

| Field      | Value                       |
|------------|-----------------------------|
| **Status** | DRAFT                       |
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

## 3. Scope

### In Scope
- Establishing a new project layout (working name `Dorc.Api.WindowsWorker`) for Windows-only endpoints currently inside `Dorc.Api`.
- Defining the inter-API contract (HTTP client interface in `Dorc.Api` → `Dorc.Api.WindowsWorker`).
- Identifying, moving, and minimally-adapting the smallest set of files that **must** be Windows-only:
  - `DirectorySearchController` + `ActiveDirectorySearcher` paths (until a Linux LDAP/Graph alternative is selected — see U-2).
  - `ResetAppPasswordController` (uses `WindowsIdentity` impersonation).
  - `BundledRequestsController`, `MakeLikeProdController` (only their Windows-specific code paths — most logic is portable).
  - WMI-using service-status code currently in `Dorc.Core/ServiceStatus.cs` / `DaemonStatusProbe.cs` (post-#649 rename).
  - Remote registry reads in `RefDataServersController`.
- Installer/wxs work to ship the new component (one MSI feature, one optional install on Windows-only deployments).
- Documentation: ADR or architecture note explaining the runtime topology and how the worker is selected/configured.

### Out of Scope (explicitly)
- **Bulk class renaming** to remove the words *Service / Helper / Manager / Util*. CLAUDE.md's rule is principle-first (avoid grab-bag dumping-ground classes); class-by-class renames belong in their own scoped PRs only when the new name is *more* specific than the old.
- Replacing `System.DirectoryServices` with `Novell.Directory.Ldap` or Graph (worthwhile, but separate HLPS — see U-2).
- Replacing WMI with SSH/PowerShell-remoting (separate HLPS).
- Folder reorganisation by "function" (`Identity/`, `Build/`, `Orchestration/`) inside the existing API — orthogonal to the split.
- Changing the public Swagger/REST surface of `Dorc.Api`. Existing clients (`Dorc.Api.Client`, `dorc-web`, CLIs) must continue to work unchanged.

---

## 4. Constraints

- **C-1 Backwards compatibility.** Existing API consumers (`dorc-web`, `Dorc.Api.Client`, CLI tools, MSI deployment) keep working with no client-side change.
- **C-2 No bundled refactor.** Naming, folder layout, and DI cleanup do not ride along on this PR.
- **C-3 Single deployable on Windows.** On Windows installs, the worker process must ship and run alongside the primary API as a separate MSI component (mirroring `Setup.Dorc/Web/RequestApi/ApiWindows.wxs` from the PR #424 attempt).
- **C-4 Auth across the hop.** The primary API → worker call must propagate the calling user's identity in a way the worker can re-authenticate the original user when it needs to (AD lookups, impersonation for password reset).
- **C-5 No silent functional change.** Every endpoint behaves identically pre- and post-split. The split is mechanical, not semantic.
- **C-6 Follow the HLPS → IS → SPEC process.** Each batch of file moves goes through an IS step with its own SPEC and adversarial review.

---

## 5. Success Criteria

- **SC-1** `Dorc.Api` builds and runs on a Linux container (no `<RuntimeIdentifier>win-*</RuntimeIdentifier>` required, no Windows-only NuGet refs in the csproj).
- **SC-2** All Windows-only endpoints respond correctly on a Windows install where the worker is present, and return a documented `503` (or equivalent) on installs where the worker is absent.
- **SC-3** Existing client apps (`dorc-web`, `Dorc.Api.Client`) make no code changes.
- **SC-4** Unit and integration tests pass at parity with pre-split coverage.
- **SC-5** MSI installer adds the worker as an optional/required component without breaking existing upgrade paths.
- **SC-6** Security findings on PR #424 (LDAP injection in `DirectorySearchController`, log injection in 4 controllers) are addressed *in the moved code* as part of the move, not deferred.

---

## 6. Unknowns Register

Per CLAUDE.md — blocking unknowns halt progress until resolved.

### Blocking (must answer before IS)

- **U-1 [BLOCKING]** Does the project want the worker to be a separate ASP.NET Core HTTP process, an in-proc plugin (`AssemblyLoadContext` with a Windows-only assembly conditionally loaded), or a queue-driven sidecar? PR #424 assumed HTTP. HTTP is simplest but the loopback cost matters for AD lookups on the hot path of authorization. **Decision required from architecture owner.**
- **U-2 [BLOCKING]** What is the future of `System.DirectoryServices`? If a Linux LDAP alternative (Graph / Novell) will be adopted in the next 1–2 quarters, the AD code shouldn't be moved into the Windows worker at all — it should be replaced in-place. The scope of "what must be Windows-only" depends entirely on this decision.
- **U-3 [BLOCKING]** Auth propagation across the inter-API hop — the worker needs to either (a) trust a signed token from the primary API and re-impersonate, or (b) receive the original client's Kerberos ticket via constrained delegation. Both have deployment implications that need stakeholder sign-off before code is written.

### Non-blocking (can be resolved during IS)

- **U-4** Naming of the new project: `Dorc.Api.Windows` (PR #424's choice) vs. `Dorc.Api.WindowsWorker` (more specific). Defer to first IS step.
- **U-5** Should the worker share `appsettings.json` with the primary API or have its own? Affects config-secret handling.
- **U-6** How does the primary API discover the worker URL? Config-file vs. service discovery. Likely config-file given current DORC topology.
- **U-7** Test strategy for the inter-API contract: contract tests, mock worker for the primary's tests, etc.
- **U-8** Whether `Setup.Acceptance` needs to install the worker for the test environment, or if a stub is sufficient.

---

## 7. References

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

## 8. Next Steps

1. Resolve U-1, U-2, U-3 with the architecture owner (checkpoint).
2. On approval of this HLPS, draft `IS-api-split.md` ordering the work into atomic steps. Likely shape (subject to U-1/U-2/U-3):
   - S-001 — Create empty `Dorc.Api.WindowsWorker` project + DI scaffolding + MSI component.
   - S-002 — Define `IWindowsWorkerClient` HTTP contract surface.
   - S-003 — Move first endpoint family (probable candidate: registry/remote-server probing) end-to-end as the proof-of-pattern.
   - S-004+ — Move remaining Windows-only families in turn (DirectorySearch, ResetAppPassword, WMI/service-status).
   - S-N — Remove Windows-only NuGet refs from `Dorc.Api.csproj` and add a Linux container build gate.
3. SPECs are drafted just-in-time per S-step, each adversarially reviewed before execution.
