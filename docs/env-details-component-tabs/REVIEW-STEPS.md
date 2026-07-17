# Step Gate Log — env-details-component-tabs

Each IS step's adversarial gate verdict and finding triage. Diff-only review scope.

## S-001 — pre-flight (commit 9389bbd) — APPROVE
- MED "value freeze incomplete for nested shapes" → **Accepted, fixed in 7e67612**
  (nested server/service/dbPerm values asserted; External + multi-per-type DB branches
  added to the frozen baseline — last legitimate moment to extend it, pre-S-005).
- MED "S-002 dacpac gate amended in a side record" → **Accepted**: amendment carried into
  SPEC-S-002 Gate section explicitly.
- LOW ordering-rests-on-runtime-behaviour, LOW MSTEST0037 style → noted for S-005
  reviewer; no action.

## S-002 — dual-source schema (commit 7e67612) — APPROVE
Reviewer verified DDL↔EF parity empirically via `Database.GenerateCreateScript()` diff
against the SSDT files: all nine tables match on columns, types, lengths, nullability,
PKs (incl. composite join PKs), FK directions, and unique index names.
- MED "FK delete behaviour: EF-conventional CASCADE vs SSDT NO ACTION" → **Accepted with
  decision**: keep SSDT NO ACTION (matches every existing join table); sources detach
  client-side before delete, and `EnvironmentsPersistentSource.DeleteEnvironment` now
  Includes+Clears the three new collections (fixed in S-003) so environment deletion
  cannot hit the FK. Divergence is identical to the existing EnvironmentServer pattern.
- LOW DATETIME vs datetime2, LOW FK constraint-name divergence in EnsureCreated DBs →
  inherited repo pattern, accepted as-is.
- LOW reflection coupling in model test → accepted (documented in-code).

## S-003 — sources + DI (commit 5296e8d) — APPROVE
All do-not-copy items verified path-by-path; triplication fidelity confirmed by
normalized diff; attach/detach outcomes exhaustive with SaveChanges only on mutation;
DeleteEnvironment change verified inside the existing transaction.
- MED "Monitor registry got 3 of the spec's 6 registrations (audit sources omitted)" →
  **Accepted, fixed in 7ec9e25's parent commit (dbd5b1b)**: three audit registrations added.
- LOW missing no-save assertions on two attach tests → fixed same commit.
- LOW GetAll/GetByName untested; LOW Include not falsifiable under mocked contexts →
  accepted (inherent to repo test pattern, enforcement by inspection).

## S-005 — resolver integration (commit e03bd7e) — APPROVE
Gate criterion held: characterization edit verified wiring-only; helper fidelity exact
for non-null tags; conditional emission appended last; runner deserialization symmetric
with VariableValueServers[]; all consumers resolve via DI (no direct construction).
- LOW SPEC silent on Id omission in variable DTOs → SPEC-S-005 R2 amended.
- LOW quirks tested via containers path only → accepted (shared helper).
- LOW no JSON round-trip test → delivered in S-008 sweep
  (`VariableValueComponentSerializationTests`, 3 tests).

## S-004 — API controllers (commit dbd5b1b) — REVISE → fixed (7ec9e25)
Pattern-setter conformed to the SPEC endpoint/auth table exactly; replicas logic-identical;
HLPS §5.3 conformance verified with no deviation; Swashbuckle complete for generation.
- HIGH missing Detach unprivileged→403 tests ×3 → **fixed**: added with no-mutation
  assertion.
- MED Delete TOCTOU could 200-with-false → **fixed**: maps to 404, race test added.
- LOW switch default equated unexpected outcome with success → **fixed**: explicit
  success cases, throw on unexpected.
- LOW residual concurrency races (double-detach 500, duplicate-name unique-index 500)
  → deferred (outside SC-4 scope; recorded here).
- LOW replica `var container` naming residue → fixed during replication rerun.

## S-006 + S-007 — client regen + UI (commits a7b9ef6, a1342d8) — REVISE → APPROVED (fix d5bb36b)
S-006 verified clean: byte-identical claim, drift narrative, spec/client consistency, and
index emission style all confirmed; the per-op oauth header blocks in new files are a
recorded drift artifact, runtime-benign (runtime.ts overwrites Authorization).
- HIGH "tabs never load on the cold-cache path" (PageEnvBase assigns `environment` —
  firing notifyEnvironmentReady — before `environmentId`; deep links / hard refreshes
  rendered a permanently empty grid) → **fixed d5bb36b**: tabs derive the id from
  `environment.EnvironmentId`; loader parameterized. Fix-verification pass traced both
  cold and warm paths and confirmed the regression test fails against pre-fix code.
- MED lifecycle/gating derivation untested → **fixed**: two lifecycle tests reproduce the
  exact base-class ordering with a stubbed typed client (containers as representative;
  replicas verified identical by diff).
- LOW attach dialogs lacked envId>0 guard → **fixed** in all three.
- LOW notes for the drift true-up task: oauth header churn on next full regen;
  openapitools.json trailing newline.
Verification pass verdict: APPROVE (135/135 web tests, build clean).

## S-008 — final sweep — complete
Evidence in VERIFICATION-S-008.md; deferred S-005 round-trip tests delivered; U-10
compile-level check done; release notes carry the R-4/R-7 ops actions.
