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
| S-001 | Authorize `DELETE /BundledRequests`                          | SF-1, SC-01, SC-05 | — | **IMPLEMENTED** † |
| S-002 | Relocate root-level security docs under `docs/`              | SF-4, SC-04        | — | **IMPLEMENTED** † |
| S-003 | Audit `CanReadSecrets` effective vs. target rule — **PARKED** | SF-2, U-1          | —          |
| S-004 | Constrain secret retrieval to intended principals — **PARKED** | SF-2, SC-02, SC-05 | S-003, U-1 |
| S-005 | Triage Aikido findings (whole surface) — **DONE**            | SF-3, SC-03, U-5   | —          |
| S-006a | Dependency CVE bumps (Xml, Identity.Client, Text.Json)      | SF-3, SC-03        | S-005 | **IMPLEMENTED** ‡ |
| S-006b | Pin GitHub Actions to commit SHA + sweep workflows          | SF-3, SC-03        | S-005 | **IMPLEMENTED** ‡ |
| S-006c | LDAP filter escaping in `ActiveDirectorySearcher`          | SF-3, SC-03, SC-05 | S-005 | **IMPLEMENTED** † |
| S-006d | Safe rendering for directory/user `innerHTML` sinks         | SF-3, SC-03, SC-05 | S-005 | **IMPLEMENTED (partial)** ‡ |
| S-006e | Path-containment hardening (file readers/copiers, pipe name)| SF-3, SC-03, SC-05 | S-005      |
| S-006f | Accept-with-justification records (false positives)        | SF-3, SC-03        | S-005      |
| S-007 | Review encryptor diff (key-derivation, nonce, naming)        | SF-4, SC-04, U-3, U-4 | — | **DONE** (REVIEW-S-007) |

† / ‡ **IMPLEMENTED** on branch `claude/codebase-review-priorities-bUsqp`, **pending adversarial
review**. A .NET 8 SDK was installed in the working environment and the changes were **built and
tested on Linux**: `LdapSearchFilterEscaperTests` 7/7, `BundledRequestsControllerDeleteTests`
4/4, `Dorc.Core.Tests` 127/127. The S-006a dependency bumps resolve as intended
(`Cryptography.Xml` 8.0.3, `Identity.Client` 4.81.0 across Api/Core/PersistentData) with clean
builds. The Windows-only test projects and the .NET Framework 4.8 projects cannot be exercised
on Linux — the Windows CI remains the final gate (22 pre-existing `WindowsIdentity` failures in
`Dorc.Api.Tests` are environmental, not from this work).

**Ordering rationale.** S-001 and S-002 are independent, low-risk, and ready now — they lead.
The secret-retrieval workstream (**S-003/S-004**) is **PARKED** pending team discussion of the
#438 policy (U-1): excluding admins/owners from plaintext secrets is a deliberate reduction of
privilege with operational impact, so it is not designed here. The current rule is traced and
recorded in U-1 for that discussion. All other steps remain actionable. The scanner workstream's audit (S-005) is **complete**
— `AUDIT-S-005-aikido-triage.md` resolved U-5 — so its remediation sub-steps (S-006a…f) are
ready to scope, ordered by real risk × ease: zero-code-risk bumps and Action pinning first,
then the LDAP fix (highest real risk), then safe rendering, then defence-in-depth path
containment. S-007 is an audit/review whose output may or may not spawn a code change; any code
change it implies is scoped as a new step after the review, not assumed here.

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
- **Aikido finding T-11 (leaked-secret alert on `OAUTH_400_ERROR_DIAGNOSIS.md:194`, a truncated
  example JWT — false positive) is cleared by this relocation/removal.** Confirm the alert
  resolves after the file is moved out of the scanned root.

---

## S-003 — Audit `CanReadSecrets` effective vs. target rule

> **PARKED (2026-06-05)** pending team discussion of the #438 policy. The *current* rule has
> already been traced (see U-1); what remains is the *target*-rule decision, which is the team's
> to make. Do not start until U-1 is resolved.

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

> **PARKED (2026-06-05)** — gated on S-003 and the U-1 team decision.

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

## S-005 — Triage Aikido findings (whole surface) — **DONE**

### Outcome
Complete. `AUDIT-S-005-aikido-triage.md` enumerates and assesses all 43 Aikido findings
(categories T-1…T-12) with in-context exploitability verdicts and remediation recommendations.
U-5 is resolved. Key conclusions: the surface is broader than #605's "CLI + PowerShell" framing;
the highest-risk items are T-1 (LDAP injection, `Dorc.Core`) and T-2 (XSS, `dorc-web`); most
"critical" path-traversal hits are internal/operator-trust inputs (defence-in-depth, lower real
risk); three findings are false positive / accepted. Remediation is grouped into S-006a…f below.

---

## S-006a — Dependency CVE bumps

### What changes
Bump `System.Security.Cryptography.Xml` to ≥8.0.3 (`Dorc.PowerShell`), `Microsoft.Identity.Client`
to ≥4.81.0 (across the five referencing projects), and `System.Text.Json` to the patched 8.x
(`Tools.DeployCopyEnvBuildCLI`). No source changes expected.

### Why it changes
**Addresses SF-3 / T-3, T-9, T-10.** Zero-code-risk version bumps that clear 9 alerts (including
two HIGH) — the cheapest, safest first move.

### Dependencies
S-005 (done). Independent of all other S-006 sub-steps.

### Verification intent
- Restore/build succeeds on the bumped versions with no breaking API changes.
- Existing test suites pass.
- The corresponding Aikido SCA alerts resolve.

---

## S-006b — Pin GitHub Actions to commit SHA

### What changes
Pin the third-party action flagged at `release.yml:214` to a full commit SHA, and sweep both
workflow files (`release.yml`, `release-publish.yml`) for the same mutable-tag pattern.

### Why it changes
**Addresses SF-3 / T-4.** Removes a supply-chain exposure (mutable action reference).

### Dependencies
S-005 (done). Independent.

### Verification intent
- All third-party actions referenced by commit SHA (with a version comment).
- Workflows still parse and run.
- The Aikido alert resolves.

### Delivery note
Pinned `EnricoMi/publish-unit-test-result-action/windows` to
`c950f6fb443cb5af20a377fd0dfaa78838901040` (**v2.23.0**, SHA cross-checked against two GitHub
pages). The `actions/*` and `microsoft/*` refs are first-party (GitHub/Microsoft) and left on
their version tags — only the third-party action was flagged.

---

## S-006c — LDAP filter escaping in `ActiveDirectorySearcher`

### What changes
The account name interpolated into the LDAP filter in `ActiveDirectorySearcher` is escaped using
the standard LDAP filter-escaping routine (or bound via a parameterised search) so filter
metacharacters in the input cannot alter the query.

### Why it changes
**Addresses SF-3 / T-1 / SC-05.** Highest real-risk finding: the SIDs resolved here drive
authorization, so filter injection is security-relevant. Highest-value code fix in the cluster.

### Dependencies
S-005 (done). Independent.

### Verification intent
- A unit test asserts that input containing LDAP metacharacters (e.g. `*`, `(`, `)`, `\`) is
  neutralised and does not change the filter's logical structure.
- Legitimate account lookups continue to resolve correctly.

---

## S-006d — Safe rendering for directory/user `innerHTML` sinks

### What changes
Replace `innerHTML` string-building with safe rendering (`textContent` / templated nodes, or
explicit HTML-escaping) at sinks where user- or directory-sourced data can flow in — beginning
with `addUserOrGroupTemplateHelper.ts` (T-2). Each remaining `innerHTML` site from T-5 is assessed;
those carrying only controlled/enum values may be left with a recorded justification or converted
for consistency, but any site reachable by free-text/user/DB data is converted.

### Why it changes
**Addresses SF-3 / T-2, T-5 / SC-05.** Closes the genuine XSS sink (T-2) and removes the fragile
pattern where it matters. Natural pairing with the frontend-test bootstrap (issue #595).

### Dependencies
S-005 (done). Independent. (Synergy, not dependency, with #595 test infra.)

### Verification intent
- The directory-search renderer no longer interprets display-name/logon markup as HTML
  (demonstrated by a test rendering a name containing HTML and asserting it is text, not nodes).
- An inventory of `innerHTML` sites records, per site, converted vs. accepted-with-justification.

### Delivery note (partial — directory-sourced sinks done; identifier/enum sites deferred)
**Converted to Lit `render()`/`html` (escaped):**
- `addUserOrGroupTemplateHelper.renderSearchResults` (T-2, AD `DisplayName`/`FullLogonName`) —
  with `addUserOrGroupTemplateHelper.test.ts` (vitest/jsdom, 2/2 passing: ordinary render +
  `<img onerror>`/`<script>` payload escaped to inert text).
- `edit-database-permissions._boundUsersRenderer` / `_boundPermissionsRenderer` (T-5, directory
  `DisplayName`) — typechecked clean.

**Deferred T-5 sites (lower risk — admin/DB identifiers and controlled enums), recommended as a
follow-up batch with frontend tests:** `page-project-components.ts` (`buildNumber`, `status`
enum), `attach-server.ts` (`Name`), `attach-database.ts` (`Name`), `add-edit-database.ts`
(`GroupName`), `add-sql-port.ts` (`template`), `page-scripts-list.ts` (`Path`, JSON viewer),
`page-deploy.ts` (`ProjectName`), `page-project-bundles.ts` (`Type` enum, JSON viewer). These
interpolate controlled identifiers rather than free user/directory text; converting them removes
the fragile pattern but is not closing an open injection of attacker-controlled markup.

---

## S-006e — Path-containment hardening

### What changes
Add canonicalise-and-contain validation to the flagged file readers/writers and Terraform
code-source copy paths (T-6, T-7): resolve the target with `Path.GetFullPath` and assert it stays
under the intended root before opening/copying; validate the pipe-name input matches the expected
GUID shape. Delivered as one batched hardening change rather than per-file edits.

### Why it changes
**Addresses SF-3 / T-6, T-7 / SC-05.** Defence-in-depth: the inputs are currently internal /
admin-configured (same trust), but containment checks make the traversal patterns safe regardless
and close the CWE-22 alerts.

### Dependencies
S-005 (done). Independent.

### Verification intent
- A test feeding a traversal payload (`../`) to each hardened entry point is rejected/contained.
- Legitimate script-group and Terraform-copy flows are unaffected.

---

## S-006f — Accept-with-justification records

### What changes
No code change. Record the false-positive / accepted-risk findings (T-8 operator CLI path, T-11
example JWT — also cleared by S-002, T-12 build-time file read) with justification, and suppress
them in Aikido per the team's convention so the dashboard reflects reality.

### Why it changes
**Addresses SF-3 / SC-03.** Closing the loop on non-actionable findings keeps the signal clean and
documents why they were not changed.

### Dependencies
S-005 (done). T-11 verification coordinates with S-002.

### Verification intent
- Each accepted finding has an in-repo justification and is suppressed/annotated in the scanner.

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
