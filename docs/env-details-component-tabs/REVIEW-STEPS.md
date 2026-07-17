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

## S-003 — sources + DI (this commit) — gate pending
Boundary adjustments vs IS recorded in SPEC-S-003 (DTOs pulled forward from S-004;
ByPage deferred; audit sources insert-only). Includes the S-002-gate-driven
DeleteEnvironment fix.
