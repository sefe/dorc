# Step Gate Log — tag-capacity-expansion

## S-001..S-003 — baselines, schema, API (commits a607bb3, b759916, 5c56168) — REVISE → fixed
Gate verified the side-by-side DDL↔EF artifact (all three DDL items and both EF
properties at 4000, nothing else changed, frozen widths locked by the model test) and
suite baselines (+11 tests over S-001 baselines, no regressions).

- **HIGH** "DaemonsPersistentSource re-verification dropped without record" → **fixed**:
  `TagConsumerSourcesAtCapacityTests.DaemonsSource_GetServersForDaemon_...` exercises the
  real projection with a near-limit string.
- **MED** "Contains re-verification was a tautology" → **fixed**: the companion test
  drives the real `ServersPersistentSource.GetAppServerDetails` filter with near-limit
  strings (embedded-substring match asserted as the documented U-5 semantics).
  `RefreshEndur` (CLI tool, no test project) remains inspection-verified: its `Contains`
  match is the same BCL semantics now exercised through `GetAppServerDetails` — recorded
  as the accepted evidence form.
- **MED** "auto-400 inferred, not demonstrated" → **recorded as the chosen evidence
  form**: DTO-level `Validator` boundary tests stand in for per-endpoint binding tests.
  The gate itself verified the inference: all four tag-accepting write endpoints bind via
  `[FromBody]` on `[ApiController]` controllers and the API does not suppress
  model-state filtering, so no endpoint bypasses validation. Live 400 behaviour is part
  of the S-006 user-environment round-trip.
- **LOW** missing `MemberNames` assert on the database boundary test → **fixed**.
- **LOW** swagger.json trailing newline dropped by the splice → **fixed**.
- **LOW** annotations-package flow to net48 consumers verified safe by the gate
  (PackageReference-based, facade type-forwarding); not Linux-buildable — CI covers,
  per TOOLCHAIN-S-001.

## S-004 + S-005 — UI (commits 5f35b55, 85ed6db) — REVISE → fixed
Noteworthy discovery: main already carried dead `manage-database-tags` wiring in
`page-databases-list.ts` (listener + stub handler, no emitter, no dialog) — S-005
completed it rather than inventing a parallel path. Gate verified: per-field limits
intact, no ArrayName bypass path, PUT payload data-loss-safe against the real
persistent-source update semantics, relabel complete (zero "Array Name" left), folded
backend fixes drive the real sources.
- **HIGH** "environment-stale dispatched but nothing on the databases tab listens —
  grid stale after tag save" (the mirror copied the dispatch, not the subscription) →
  **fixed**: `env-databases.ts` now listens and refreshes, mirroring `env-servers`.
- **MED** at-limit accept was 8 chars, not 4000 → **fixed**: exactly-4000 accept test.
- **MED** error visibility unasserted → **fixed**: notification-card content asserted for
  database-tags; add-edit-database's inline `ErrorMessage` path now has its own test.
- **LOW ×3 deferred** (recorded): near-limit cell-content inspection (SC-4 rides the
  user-environment pass); `database = undefined` guard mirrors server-tags' pre-existing
  pattern and is unreachable from the wired dialogs; dead `?? this.tags` fallback
  unreachable in practice.
