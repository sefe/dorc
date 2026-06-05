# IS: Security & RBAC Hardening — Implementation Sequence

| Field       | Value                                          |
|-------------|------------------------------------------------|
| **Status**  | DRAFT                                          |
| **Author**  | Agent                                          |
| **Date**    | 2026-06-05                                     |
| **HLPS**    | HLPS-security-rbac-hardening.md (DRAFT)        |
| **Folder**  | docs/security-rbac-hardening/                  |

---

## Step Index

| ID    | Title                                                        | Addresses          | Depends On |
|-------|--------------------------------------------------------------|--------------------|------------|
| S-001 | Authorize `DELETE /BundledRequests`                          | SF-1, SC-01, SC-05 | —          |
| S-002 | Relocate root-level security docs under `docs/`              | SF-4, SC-04        | —          |
| S-003 | Audit `CanReadSecrets` effective vs. target rule             | SF-2, U-1          | —          |
| S-004 | Constrain secret retrieval to intended principals            | SF-2, SC-02, SC-05 | S-003      |
| S-005 | Triage Aikido findings for CLI tools + PowerShell module     | SF-3, SC-03, U-5   | —          |
| S-006 | Remediate confirmed CLI / PowerShell findings                | SF-3, SC-03, SC-05 | S-005      |
| S-007 | Review encryptor diff (key-derivation, nonce, naming)        | SF-4, SC-04, U-3, U-4 | —       |

**Ordering rationale.** S-001 and S-002 are independent, low-risk, and ready now — they lead.
The secret-retrieval and scanner workstreams are **audit-first**: their fix steps (S-004,
S-006) are gated on the audit steps (S-003, S-005) that resolve the blocking unknowns. S-007
is an audit/review whose output may or may not spawn a code change; any code change it implies
is scoped as a new step after the review, not assumed here.

---

## S-001 — Authorize `DELETE /BundledRequests`

### What changes
The delete action on `BundledRequestsController` resolves the owning project for the supplied
bundle id and applies the same project-modify authorization the controller's create/update
actions already apply, returning the established denied/not-found responses. The stale comment
that documents the missing check is removed.

### Why it changes
**Addresses SF-1 / SC-01.** Delete is currently reachable by any authenticated user, while
create and update are gated. This is a missing privilege boundary on a destructive operation
and contradicts the documented requirement (Project Write/Admin). Aligning delete with the
existing pattern closes the gap with the least possible novelty.

### Dependencies
None functionally. If U-2 finds no bundle-id → project-id read path on the persistent source,
this step adds that read path (additive, no schema change).

### Verification intent
- A caller without Project Write/Admin on the owning project is denied (403); no deletion occurs.
- A caller with Project Write/Admin succeeds and the bundle step is deleted.
- A non-existent bundle id yields the not-found response, not a successful "deleted" result.
- Tests assert both the allow and the deny path at the controller decision layer.

---

## S-002 — Relocate root-level security docs under `docs/`

### What changes
`QUANTUM_ENCRYPTION_UPGRADE.md` and `OAUTH_400_ERROR_DIAGNOSIS.md` are moved out of the
repository root into an appropriate `docs/` location (or removed if confirmed obsolete under
U-6). Any in-repo references to them are updated.

### Why it changes
**Addresses SF-4 (hygiene) / SC-04.** Both files violate the CLAUDE.md rule that artifacts live
under `docs/{feature}/`. This is pure housekeeping with no behavioural impact and is separated
from the encryptor review so it can land immediately.

### Dependencies
None. Gated only by U-6 (confirm no external link/onboarding dependency before deletion vs.
move; default is move).

### Verification intent
- Repository root no longer contains the two documents.
- Any in-repo link to them still resolves.
- No source or build artifact references the old paths.

---

## S-003 — Audit `CanReadSecrets` effective vs. target rule

### What changes
No production code changes. Produces an in-repo audit note recording: the **current** effective
set of principals for whom secret plaintext is returned (tracing `CanReadSecrets` and the role
model), the **target** rule per #438, and the precise delta to close. Resolves U-1.

### Why it changes
**Addresses SF-2 / U-1.** The fix cannot be designed safely until it is established whether a
broad role currently bypasses the per-environment grant and exactly what the target rule is.
Designing SD-2 without this risks either failing to close the gap or over-restricting and
breaking a legitimate service-account flow.

### Dependencies
None. Output gates S-004.

### Verification intent
- The audit note states the current rule, the target rule, and the delta, with the relevant
  code paths cited.
- U-1 is marked resolved in the HLPS with the agreed target rule, confirmed by the issue owner.

---

## S-004 — Constrain secret retrieval to intended principals

### What changes
The authorization decision feeding the secret-read path is adjusted so plaintext secrets are
returned only to principals holding the per-environment "read secrets" grant, per the target
rule fixed in S-003. The existing masking/empty-value behaviour for non-holders is preserved.
No new role and no schema change.

### Why it changes
**Addresses SF-2 / SC-02.** Closes the over-permissioning identified in #438 using the existing
grant mechanism.

### Dependencies
**Depends on S-003** (U-1 resolved). Implementation cannot begin until the target rule is agreed.

### Verification intent
- A principal holding the per-environment grant receives plaintext (unchanged).
- A broadly-privileged principal **without** the grant no longer receives plaintext and instead
  gets the established masked/empty result (or denial), per the agreed rule.
- Tests cover both holder and non-holder, including the previously-bypassing role.
- No regression to non-secure property reads.

---

## S-005 — Triage Aikido findings for CLI tools + PowerShell module

### What changes
No production code changes. Produces an in-repo triage record enumerating each Aikido finding
for the CLI tools and the PowerShell module, each categorised as true positive / false positive
/ accepted risk, with severity and a remediation recommendation. Resolves U-5.

### Why it changes
**Addresses SF-3 / U-5.** The findings are not characterised in-repo; remediation cannot be
ordered or scoped until they are. Triage-first prevents speculative changes to security-relevant
tool code.

### Dependencies
None. Output gates S-006. Requires access to the Aikido report (User-provided).

### Verification intent
- Every reported finding appears in the record with a category, severity, and recommendation.
- Confirmed HIGH/CRITICAL true positives each have a scoped follow-up (an S-006 sub-item or an
  explicit, owner-assigned deferral).

---

## S-006 — Remediate confirmed CLI / PowerShell findings

### What changes
Implements fixes for the HIGH/CRITICAL true positives confirmed in S-005. Exact changes are
scoped per finding in JIT Specs once S-005 lands; this step is a placeholder for that scoped
work and may decompose into several sub-steps.

### Why it changes
**Addresses SF-3 / SC-03.** Eliminates the confirmed exploitable issues in the tools and module.

### Dependencies
**Depends on S-005.** The set and shape of fixes are unknown until triage completes.

### Verification intent
- Each remediated finding has a test or a documented manual verification demonstrating the issue
  is closed.
- No regression to existing tool/module behaviour for legitimate use.

---

## S-007 — Review encryptor diff (key-derivation, nonce, naming)

### What changes
No production code change in this step. Produces a review note on the shipped
`QuantumResistantPropertyEncryptor` diff covering: (a) key-derivation stability across every
key configuration in use (U-3), (b) the random-nonce-under-static-key strategy and whether it
is acceptable at DOrc's secret volume, and (c) a cohesive-naming-compliant type name and the
full list of registration/instantiation sites a rename would touch (U-4). The note states, for
each, whether a follow-up code step is required.

### Why it changes
**Addresses SF-4 (review) / SC-04 / U-3 / U-4.** The encryptor is live and security-critical.
A focused review establishes whether the concerns are cosmetic or substantive before any code
is touched, and ensures any rename is a verified pure refactor that preserves backward-compatible
decryption (C-03, C-04).

### Dependencies
None. Any code change it recommends (key-derivation fix and/or rename) is added as a new,
separately-reviewed step — it is not bundled into this review step.

### Verification intent
- The review note resolves U-3 (key-derivation stable, or a defect identified) and U-4 (rename
  feasibility + site list).
- A naming recommendation consistent with the CLAUDE.md standard is recorded.
- The nonce strategy has a documented accept/▲-risk decision.
- If no code change is warranted, that conclusion is stated explicitly so the finding is closed,
  not left ambiguous.

---

## Release & Review Notes

- **Independent landing.** S-001 and S-002 are self-contained and may each land on their own.
  S-004 lands only after S-003; S-006 only after S-005.
- **Adversarial gate.** Per CLAUDE.md, this plan and each subsequent code diff pass the
  adversarial review panel. Audit/review steps (S-003, S-005, S-007) are reviewed as plans
  (clarity, completeness, correctness of the delta), not as syntax.
- **Checkpoints.** User approval is required after HLPS approval and after IS approval, and
  before executing each step unless auto-pilot is enabled.
