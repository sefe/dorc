# SPEC — S-003 — `EnvironmentNameExact` Substring → Equality

**Status:** APPROVED — auto-pilot grant 2026-05-05
**Date:** 2026-05-05
**Step ID:** S-003
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED

---

## 1. Purpose

Replace the `EnvironmentNameExact` filter branch in `RequestsStatusPersistentSource.GetRequestStatusesByPage` so it generates an EF Core LINQ predicate that compiles to SQL `WHERE [Environment] = @p` (equality) instead of `WHERE [Environment] LIKE '%@p%'` (substring). The shared `DataPagerExtension.ContainsExpression` helper is **not** modified — its other consumers retain current behaviour. The public API contract for the request-status endpoint is unchanged: the filter path name (`EnvironmentNameExact`), the request shape, and the response shape stay the same.

This is the smallest behaviour-changing step in the IS and the single largest contributor to relieving the production CPU saturation per HLPS analysis. HLPS U3 confirmed `env-monitor.ts` is the sole consumer; HLPS SC1 is the success criterion.

## 2. Requirements

### R1 — Predicate change
The branch in `GetRequestStatusesByPage` that today routes `EnvironmentNameExact` through `ContainsExpression` is rewritten to apply an equality predicate against the `EnvironmentName` projection of `DeploymentRequestApiModel` (or the underlying `Environment` column on `DeploymentRequest` — the JIT author chooses; semantically equivalent because the projection forwards the column verbatim).

The implementation approach is unconstrained at IS / spec level: a small inline LINQ `Where(req => req.EnvironmentName == filterValue)`, a tiny dedicated helper alongside `ContainsExpression`, or any other SARGable equality construction is acceptable. The constraint is the *generated SQL*, not the C# shape.

### R2 — Generated SQL is an equality predicate
The EF-translated SQL for the `EnvironmentNameExact` filter contains an exact `=` comparison and **does not** contain `LIKE '%…%'`. This must be assertable in test (see R5). Tolerated companion patterns: under default EF null-handling, EF Core may emit an `IS NULL` companion branch alongside `=` (see §5); R2 is satisfied as long as `=` is present and `LIKE '%` is absent — additional `IS NULL` clauses are not failures.

### R3 — Helper untouched
`DataPagerExtension.ContainsExpression` is not modified. No call site outside `RequestsStatusPersistentSource.GetRequestStatusesByPage` changes behaviour.

### R4 — Other branches in the same method untouched
The other filter branches in `GetRequestStatusesByPage` — `Project`, `EnvironmentName` (the per-field, non-Exact one), `BuildNumber`, and the generic catch-all — are **not** changed by S-003. Those are S-004's domain. The diff for S-003 is bounded to the `EnvironmentNameExact` branch.

### R5 — Test coverage
Add or update unit tests in the appropriate test project (`Dorc.Api.Tests` or `Dorc.PersistentData.Tests`, JIT author chooses based on existing test placement) covering:

- A request with an `EnvironmentNameExact` filter produces a queryable whose translated SQL contains `=` and not `LIKE '%`. The assertion mechanism is delivery-time (`IQueryable.ToQueryString()` requires a relational provider — SQLite-backed or SQL-Server-backed; the EF Core in-memory provider does not support `ToQueryString()` and is unsuitable for SQL-shape assertions).
- A request with no `EnvironmentNameExact` filter does not produce the new `EnvironmentName` equality predicate (regression guard against the new equality clause being applied unconditionally — does not assert absence of `=` elsewhere in the SQL, since other legitimate equality predicates may exist on integer columns or joins).
- Existing grid filter tests for the other branches (Project, EnvironmentName, BuildNumber) continue to pass without modification.

The exact assertion library, naming, and test-fixture pattern are JIT-author choices guided by adjacent test code in the chosen project.

### R6 — Behaviour parity for the env-monitor consumer
The `env-monitor.ts` consumer pushes a fully-qualified environment name (per HLPS U3). Equality and substring-against-the-same-value return the same row set when the value is the complete environment name — so the change is row-set-neutral for the production caller. Verification: the test in R5 uses a multi-environment seed where one environment name is a substring of another (e.g., `PROD`, `PROD-NA`). For a filter value equal to a complete env name (the production caller's pattern), equality returns the same row(s) the substring path would have returned — and explicitly does **not** return the partial-match row that the substring path *would* have included. The latter assertion confirms the predicate is correctly narrower than substring in the multi-entry case, which is the safety property HLPS U3's resolution rests on.

### R7 — Build cleanly
The solution builds (`dotnet build` for the affected projects) without new warnings. CI is the canonical build gate; local verification of the affected projects (`Dorc.PersistentData`, `Dorc.Api`, `Dorc.Api.Tests`) is sufficient for the JIT-author's local check.

## 3. Out of scope

- Project / EnvironmentName / BuildNumber per-field filter changes — owned by S-004.
- `ContainsExpression` helper modifications.
- Other paged-grid endpoints.
- Frontend changes — `env-monitor.ts` continues to send `path: 'EnvironmentNameExact'`; no change there.
- Any plan / index work — owned by S-002 and S-006.
- Renaming the `EnvironmentNameExact` path — its name is now correct (it really is exact); no rename needed.

## 4. Acceptance Criteria

S-003 is "done" when **all** hold:
1. The `EnvironmentNameExact` branch in `GetRequestStatusesByPage` emits an equality predicate per R1; the diff is surgical and matches R4 (no other branches changed).
2. The captured EF SQL for the equality path satisfies R2.
3. Tests per R5 are added and pass; existing tests pass.
4. `ContainsExpression` is untouched per R3.
5. Build is clean per R7.
6. Adversarial Quality Gate has approved the diff.

## 5. Risks & Open Questions

- **Plan does not seek without S-002.** If S-003 lands before S-002, the equality predicate still scans (no index on `Environment`). This is acceptable as a transient state because the equality predicate is still cheaper than substring. The IS bundles S-003 with S-002 for delivery-time review; the JIT author may also bundle them in the same commit.
- **EF translation surprises.** Some EF Core configurations translate `==` against nullable `string` columns to `IS NULL` plus equality checks under `UseRelationalNulls = false`. If the captured SQL contains an `IS NULL` branch instead of pure `=`, R2 still passes (the `=` is present) but the plan may differ. Verification of plan shape is S-006's responsibility; the JIT author records the captured SQL in the test snapshot for R5.
- **Test assertion brittleness.** Asserting against EF-generated SQL strings is brittle across EF versions. The JIT author should use a pattern check (substring contains `=` and does not contain `LIKE '%`) rather than a full string match.

## 6. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6 | Submitted to a 2-model panel — narrow surgical change. |
| R1    | 2026-05-05 | APPROVED | Opus 4.7, Sonnet 4.6 | Both **APPROVE WITH MINOR FINDINGS**. Triage: ACCEPTED 4 (Sonnet F1 MEDIUM — R2 cross-references §5's IS NULL acceptance; Opus F1 LOW — R5 wording on "no equality predicate" tightened to refer to the *new* equality clause; Opus F2 + Sonnet F3 LOW — R6 verification scenario expanded to explicitly assert the multi-entry narrower-than-substring property; Sonnet F2 LOW — R5 mention of "in-memory provider" corrected to relational-only). DEFERRED 4 (Opus F3 column-name agnosticism, Opus F4 R7 test project list, Opus F5 R5 IS NULL tolerance hint redundant with new R2 wording, Opus F6 informational). Status APPROVED — auto-pilot grant; CHECKPOINT-3 skipped per user instruction. |
