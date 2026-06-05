# HLPS: Security & RBAC Hardening — Priority-1 Cluster

| Field       | Value                              |
|-------------|------------------------------------|
| **Status**  | DRAFT                              |
| **Author**  | Agent                              |
| **Date**    | 2026-06-05                         |
| **Folder**  | docs/security-rbac-hardening/      |

---

## 1. Problem Statement

A codebase review on 2026-06-05 surfaced a cluster of authorization, secret-handling,
and cryptographic-hygiene concerns. Several are concrete, confirmed defects with a clear
security impact; others require a triage/audit pass before a fix can be scoped. They are
grouped here because they share a single theme — **trust boundaries that are weaker than
the product's own documentation and RBAC model claim** — and because addressing them as
one initiative lets the team apply a consistent authorization pattern and a single
adversarial review across the set.

The cluster maps to open issues #665, #438, #605, and to the bot-authored "quantum
encryption" change that is now the live at-rest secret encryptor on `main`.

This is a defensive-security initiative on the team's own repository. No offensive or
evasion work is in scope.

---

## 2. Observed Findings

### SF-1 — `DELETE /BundledRequests` has no authorization check (issue #665)

**Confirmed.** `BundledRequestsController.Delete` performs no permission check beyond
`[Authorize]`. Any authenticated user can delete any bundle step on any project's bundle.
The sibling `POST` and `PUT` actions on the same controller both gate on
`CanModifyProject(User, model.ProjectId)`; `DELETE` does not. The method body carries a
self-acknowledged comment stating the delete permission check was deliberately left out.

**Impact.** Bundles encode multi-step refresh-from-prod workflows. An accidental or
malicious delete by any signed-in user is a real operational risk, and it contradicts the
wiki, which states that editing or deleting a bundle step requires Project Write or Admin.

**Severity.** HIGH (authorization defect — privilege boundary missing on a destructive
operation).

### SF-2 — Secret retrieval is over-permissioned (issue #438)

Secret (secure property) values are decrypted and returned to any caller for whom
`CanReadSecrets(user, environmentName)` returns true. The business requirement in #438 is
narrower: **only service accounts that hold the per-environment "read secrets" grant**
should be able to retrieve plaintext secrets — *not* DOrc admins, power users, or other
broadly-privileged roles by virtue of their role alone.

The finding is therefore: the effective set of principals who can read plaintext secrets
is wider than policy intends. Whether admins/power users actually bypass the per-environment
grant today depends on the internal logic of `CanReadSecrets` and the role model, which
must be established by audit before a fix is designed (see U-1).

**Severity.** HIGH if a broad role currently bypasses the per-environment grant; MEDIUM if
the gap is only partial. Confirmed by U-1.

### SF-3 — Aikido SAST/SCA findings across the codebase (issue #605)

> **Updated 2026-06-05 (U-5 resolved).** The Aikido export was provided and triaged in
> `AUDIT-S-005-aikido-triage.md`. Issue #605 framed this as "CLI tools and PowerShell module,"
> but the actual surface is broader and the two most genuinely exploitable findings sit
> **outside** that framing. SF-3 is therefore restated as "the Aikido SAST/SCA surface."

Triage outcome (full detail and per-finding rationale in `AUDIT-S-005-aikido-triage.md`):

- **T-1 — LDAP query injection** in `Dorc.Core/ActiveDirectorySearcher.cs` (unescaped
  `sAMAccountName` in an LDAP filter). Highest-scored finding; true positive; **HIGH** real risk
  because SIDs resolved here drive authorization. *Outside #605's stated scope.*
- **T-2 — XSS** via `innerHTML` of directory-search display names in `dorc-web`
  (`addUserOrGroupTemplateHelper.ts`). True positive; **MED–HIGH**. *Outside #605's scope.*
- **T-3 / T-9 / T-10 — dependency CVEs** (`System.Security.Cryptography.Xml` 8.0.2→8.0.3 HIGH;
  `Microsoft.Identity.Client`→4.81.0 and `System.Text.Json`→patched 8.x, both LOW). True
  positives, zero-code-risk version bumps.
- **T-4 — unpinned GitHub Action** in `release.yml`. True positive; supply-chain hardening.
- **T-5 — further `innerHTML` sinks** across several Lit components. True-positive pattern, mostly
  LOW (controlled/enum data) but each site needs an is-it-user-data check.
- **T-6 / T-7 — path-traversal patterns** in script-group pipe file I/O and Terraform code-source
  copy. True positives but on internally-generated / admin-configured (same-trust) inputs →
  defence-in-depth, LOW / LOW–MED.
- **T-8 / T-11 / T-12 — false positives / accepted risk**: operator-supplied CLI file path; a
  truncated example JWT in `OAUTH_400_ERROR_DIAGNOSIS.md` (cleared for free by S-002); build-time
  file reads in `vite.config.js`.

**Severity.** Mixed — one HIGH true positive (T-1), one MED–HIGH (T-2), several easy true-positive
dependency/CI fixes, the remainder defence-in-depth or false positive.

### SF-4 — Bot-merged at-rest secret encryptor: review and hygiene

`QuantumResistantPropertyEncryptor` (authored by an automated agent and merged to `main`)
is now the live `IPropertyEncryptor` implementation, registered in `CoreRegistry` and in
the Monitor host. It encrypts all newly written secure property values in a new `v2:`
(AES-256-GCM) format and retains backward-compatible decryption of legacy formats. The
implementation is functional and is wired consistently across processes (all consumers
resolve `IPropertyEncryptor` via DI; no split-brain was found). However, the change
carries several concerns that warrant a focused review of the **shipped diff**:

1. **Misleading naming.** AES-256-GCM is a symmetric cipher believed to remain secure
   against quantum attack; it is **not** post-quantum cryptography. The class name and the
   accompanying documentation overstate the security property, and the name also conflicts
   with the CLAUDE.md cohesive-naming standard.
2. **Key-derivation consistency.** The constructor derives the AES key by either truncating
   the first 32 bytes of a base64-decoded key or SHA-256-hashing it, depending on input
   shape. Whether this produces a stable, well-defined key for every existing key
   configuration must be confirmed (see U-3) — a key-derivation regression would render
   existing `v2:` values undecryptable.
3. **GCM nonce strategy.** A random 96-bit nonce under a single long-lived key has a
   birthday-bound collision risk. At DOrc's secret volume this is very likely acceptable,
   but it should be explicitly assessed and documented rather than assumed.
4. **Repository hygiene.** Two agent-authored documents — `QUANTUM_ENCRYPTION_UPGRADE.md`
   and `OAUTH_400_ERROR_DIAGNOSIS.md` — sit at the repository root, violating the CLAUDE.md
   rule that artifacts live under `docs/{feature}/`.

**Severity.** Naming/hygiene = LOW. Key-derivation correctness = HIGH **if** U-3 finds a
configuration where derivation is unstable; otherwise informational.

---

## 3. Scope

**In scope:**
- `Dorc.Api` controllers and services in the authorization path for the affected operations
  (`BundledRequestsController`, the secret-read path in `PropertyValuesService`).
- `ISecurityPrivilegesChecker` / `IRolePrivilegesChecker` and their implementations, only as
  needed to correct the trust boundaries above.
- The CLI tools (`src/Tools.*`) and the PowerShell module (`Dorc.PowerShell`,
  `Dorc.NetFramework.PowerShell`) **for audit** under SF-3; concrete code fixes scoped after
  triage.
- The shipped `QuantumResistantPropertyEncryptor` diff and its root-level documentation
  artifacts (review + hygiene).
- Additive tests for every behaviour change.

**Out of scope:**
- A full RBAC redesign or a new permission model. Changes apply the **existing** pattern
  (`CanModifyProject`, per-environment grants) to the gaps; they do not invent new roles.
- Re-architecting the encryptor or changing the on-disk secret format. SF-4 is a review and
  hygiene pass, not a re-encryption project. Any decision to change format or rotate keys is
  a separate initiative.
- The Kafka/RabbitMQ substrate question (issue #614) — unrelated.
- Functional (non-security) bugs surfaced in the same review (e.g. #552, #428).
- New endpoints beyond what an authorization fix strictly requires (e.g. the daemon-side
  inverse endpoint noted in the daemons FOLLOW-UPS is not pulled in here).

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|-------------------|
| SC-01 | `DELETE /BundledRequests` rejects callers without Project Write/Admin on the owning project with 403, and continues to permit authorized callers. Behaviour matches the existing `POST`/`PUT` authorization on the same controller. (SF-1) |
| SC-02 | The set of principals able to retrieve plaintext secrets matches the policy in #438 — restricted to principals holding the per-environment "read secrets" grant, with no broad-role bypass. The exact target rule is fixed by U-1 before implementation. (SF-2) |
| SC-03 | Every reported Aikido finding (whole surface, not only CLI/PowerShell) has an in-repo triage record (true/false positive, severity, remediation recommendation) — **met by `AUDIT-S-005-aikido-triage.md`**. True-positive HIGH/CRITICAL findings are either fixed within this initiative or have an explicit, owner-assigned deferral. (SF-3) |
| SC-04 | The shipped encryptor diff has a documented review outcome covering naming, key-derivation stability, and nonce strategy. Key-derivation is confirmed stable for all existing key configurations, or the risk is fixed. The two root-level documents are relocated under `docs/` or removed. (SF-4) |
| SC-05 | Every behaviour change is covered by automated tests asserting both the allow and the deny paths at the layer where the decision is made. No regression to authorized flows. |
| SC-06 | No change to the database schema, the external API request/response contract (beyond status codes for newly-denied operations), or the on-disk secret format. |

---

## 5. Constraints

- C-01: Authorization fixes must reuse the existing `ISecurityPrivilegesChecker` pattern and
  must not introduce a parallel authorization mechanism.
- C-02: A denied operation must fail closed (403 / no plaintext), never fail open.
- C-03: SF-4 must not alter the on-disk format or break decryption of any existing value.
  Backward compatibility with `v1:` / unversioned / `v2:` values is mandatory.
- C-04: Renaming the live encryptor type (if pursued under SF-4) must be a pure refactor
  with no behavioural change, and must preserve DI registration in every host (API, Monitor,
  CLIs) — verified by U-4.
- C-05: Naming of any new or renamed type must satisfy the CLAUDE.md cohesive-naming rule.
- C-06: SF-3 triage must not publish exploit detail for any true-positive finding beyond what
  is needed to scope and verify the fix.

---

## 6. Proposed Solution Directions

Conceptual only; detailed design lives in the Implementation Sequence and JIT Specs.

### SD-1: Gate `DELETE /BundledRequests` on project-modify rights (SF-1)

Resolve the bundle's owning project from its id, then apply the same
`CanModifyProject`-based 403 gate used by `POST`/`PUT`, returning `NotFound` when the bundle
id does not exist. Remove the stale "permission check not changed here" comment. A read path
from bundle id to project id must exist on the persistent source; if absent, it is a small
additive lookup.

### SD-2: Constrain secret retrieval to the intended principals (SF-2)

Once U-1 establishes the current effective rule, adjust the secret-read decision so that
plaintext is returned only to principals holding the per-environment "read secrets" grant,
per #438. The change is localised to the authorization decision feeding the secret-read
path; the masking/empty-value behaviour for non-holders is preserved. No new role is added.

### SD-3: Triage then remediate scanner findings (SF-3)

**Triage complete** (`AUDIT-S-005-aikido-triage.md`). Remediation now proceeds by the grouping
the triage produced, ordered by real risk × ease: zero-code-risk dependency bumps and Action
pinning first (clears the most alerts), then the LDAP-injection fix (highest real risk), then
safe-rendering for user/directory data sinks, then defence-in-depth path containment. False
positives and accepted risks are recorded with justification rather than changed.

### SD-4: Review the encryptor diff and correct hygiene (SF-4)

Conduct a focused review of the shipped encryptor: confirm key-derivation stability across
existing key configurations (U-3), assess and document the random-nonce strategy, and decide
on a cohesive-naming-compliant type name (pure refactor, U-4). Relocate or remove the two
root-level documents so artifacts live under `docs/`. No format change.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | What is the **current** effective rule for `CanReadSecrets`, and what is the **target** rule #438 wants? | User / team | **Blocking** for SD-2 — **DEFERRED** | **Current rule traced (2026-06-05):** plaintext secrets are returned if the user is in the **`Admin`** role (unconditional global bypass in `SecurityObjectFilter.HasPrivilege`), **or** holds the **`ReadSecrets`** ACL grant on the environment, **or** holds the **`Owner`** grant (`CanReadSecrets` checks `ReadSecrets | Owner`). #438 wants **only** the `ReadSecrets` grant → two gaps: admin bypass and owner access. **Target rule PARKED pending team discussion/validation** — it is a deliberate reduction of admin/owner power (operational impact: support/debug workflows may rely on admin secret reads) and needs three decisions: (1) exclude admins? (2) exclude owners? (3) confirm no workflow depends on admin/owner secret reads. Design note: the admin bypass is global to `HasPrivilege`, so the fix must decouple the secret-read decision from it without disturbing other authz; the separate deployment-time decryption path (Monitor/Runner, via the encryptor directly) must remain untouched. |
| U-2 | Does a bundle-id → project-id lookup already exist on `IBundledRequestsPersistentSource`, or must it be added? | Agent | Non-blocking | Open. Cheap to determine during S-001 spec; additive if missing. |
| U-3 | For every key configuration currently in use, does `QuantumResistantPropertyEncryptor` key-derivation produce a stable, correct 32-byte key (no truncation/hash ambiguity that would break decryption of already-written `v2:` values)? | Agent / User | **Blocking** for any SF-4 code change beyond hygiene | **RESOLVED (REVIEW-S-007).** Derivation is deterministic per stored key, so all existing `v2:` values remain decryptable; no migration/data-loss risk and no format change required. The truncate-or-hash branch is a smell, not a defect; a defined KDF would only matter if paired with a future re-encryption migration (out of scope). |
| U-4 | Can the encryptor type be renamed without breaking DI registration in every host (API/Monitor/CLIs) and without behavioural change? | Agent | **Blocking** for the rename only | **RESOLVED (REVIEW-S-007).** Yes — pure refactor. Touch-list: the type, three registration sites (`CoreRegistry`, `Monitor/Program.cs`, `EncryptionMigrationCLI/Program.cs`), its tests, and the doc title. No interface / DI-shape / on-disk-format change. |
| U-5 | What exactly does Aikido flag for the CLI tools and PowerShell module (finding list, severities)? | User | **Blocking** for SD-3 remediation steps | **RESOLVED 2026-06-05.** Aikido export provided and triaged in `AUDIT-S-005-aikido-triage.md` (43 findings, 12 categories T-1…T-12). Surface is broader than #605's framing; highest real risk is T-1 (LDAP injection, `Dorc.Core`) and T-2 (XSS, `dorc-web`). Remediation grouped into S-006a…f. No blocking unknowns remain for SF-3. |
| U-6 | Is relocating/removing the two root-level docs acceptable, or are they referenced by external links/onboarding that must be preserved (e.g. via a redirect/stub)? | User | Non-blocking | Open. Default: move under `docs/`; confirm no external dependency. |

**Blocking unknowns halt the dependent steps only** — SF-1 (S-001) has no blocking unknown
and may proceed first.

---

## 8. Out-of-Scope Risks

- **Broad RBAC drift.** This initiative fixes specific, identified trust-boundary gaps. Other
  controllers may carry similar gaps; a systematic authorization audit across all destructive
  endpoints is a worthwhile follow-up but is not scoped here.
- **Secret-format migration.** If U-3 reveals a key-derivation problem severe enough to
  warrant a format or key change, that becomes a separate, larger initiative with its own
  HLPS — it is explicitly out of scope here.
- **Agent-authored change velocity.** The encryptor finding is one instance of a broader
  pattern (large agent-authored features merging at a pace that may outrun the adversarial
  review gate). That governance question is noted for the user but is not a code deliverable
  of this initiative.
