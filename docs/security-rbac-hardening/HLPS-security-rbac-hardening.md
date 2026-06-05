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

### SF-3 — Unaudited "critical" findings in CLI tools and PowerShell module (issue #605)

An external scanner (Aikido) reports critical security issues in the CLI tools and the
PowerShell module. The specific findings, their exploitability, and whether they are true
positives are **not yet established in-repo**. This finding is a triage workstream, not a
single fix: the deliverable of the first step is a categorised, in-repo record of each
reported issue (true positive / false positive / accepted risk) with a remediation
recommendation, from which concrete fix steps are scoped.

**Severity.** UNKNOWN until triaged — treated as potentially HIGH/CRITICAL pending audit.

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
| SC-03 | Every reported Aikido finding for the CLI tools and PowerShell module has an in-repo triage record (true/false positive, severity, remediation recommendation). True-positive HIGH/CRITICAL findings are either fixed within this initiative or have an explicit, owner-assigned deferral. (SF-3) |
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

Produce an in-repo audit record categorising each Aikido finding for the CLI tools and
PowerShell module. From that record, scope concrete fix steps for confirmed HIGH/CRITICAL
true positives; record false positives and accepted risks with justification. This direction
is deliberately audit-first because the findings are not yet characterised in-repo.

### SD-4: Review the encryptor diff and correct hygiene (SF-4)

Conduct a focused review of the shipped encryptor: confirm key-derivation stability across
existing key configurations (U-3), assess and document the random-nonce strategy, and decide
on a cohesive-naming-compliant type name (pure refactor, U-4). Relocate or remove the two
root-level documents so artifacts live under `docs/`. No format change.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | What is the **current** effective rule for `CanReadSecrets` — does any broad role (Admin / power user) grant plaintext-secret access independently of the per-environment "read secrets" grant? What is the exact **target** rule #438 wants? | User / audit | **Blocking** for SD-2 | Open. Audit `CanReadSecrets` + role model; confirm target rule with issue owner before designing the fix. |
| U-2 | Does a bundle-id → project-id lookup already exist on `IBundledRequestsPersistentSource`, or must it be added? | Agent | Non-blocking | Open. Cheap to determine during S-001 spec; additive if missing. |
| U-3 | For every key configuration currently in use, does `QuantumResistantPropertyEncryptor` key-derivation produce a stable, correct 32-byte key (no truncation/hash ambiguity that would break decryption of already-written `v2:` values)? | Agent / User | **Blocking** for any SF-4 code change beyond hygiene | Open. Establish via audit against real key shapes before touching the type. |
| U-4 | Can the encryptor type be renamed without breaking DI registration in every host (API/Monitor/CLIs) and without behavioural change? | Agent | **Blocking** for the rename only | Open. Enumerate all registration/instantiation sites; rename is pure-refactor or it is not done. |
| U-5 | What exactly does Aikido flag for the CLI tools and PowerShell module (finding list, severities)? | User | **Blocking** for SD-3 remediation steps | Open. The triage step (S-00x) produces this; remediation steps cannot be ordered until it lands. |
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
