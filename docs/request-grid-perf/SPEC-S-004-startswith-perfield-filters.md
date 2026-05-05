# SPEC — S-004 — Project + EnvironmentName per-field filters: substring → `StartsWith`

**Status:** IN REVIEW
**Date:** 2026-05-05
**Step ID:** S-004
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED

---

## 1. Purpose

Switch the per-field grid filters introduced by PR #338 in `RequestsStatusPersistentSource.GetRequestStatusesByPage` from substring (`Contains`) to prefix (`StartsWith`) for `Project` and `EnvironmentName`, while keeping `BuildNumber` on substring. The change applies in **both** call paths inside the method:

- **AND-path** (`hasDistinctDetailValues == true`) — used by the global request grid (`page-monitor-requests.ts`) when each per-field input has a distinct value.
- **OR-path** (`hasDistinctDetailValues == false`) — used by the env-pinned monitor (`env-monitor.ts`), whose shared `detailsFilter` pushes the same value into the `Project` and `BuildNumber` paths simultaneously.

Per HLPS U1 (resolved) and HLPS SC2: `Project → StartsWith`, `EnvironmentName → StartsWith`, `BuildNumber → substring retained`. The supporting non-clustered indexes from S-002 (`IX_DeploymentRequest_Environment`, `IX_DeploymentRequest_Project`) make `StartsWith` an index seek; `BuildNumber` substring still scans, but the scan is bounded by the upstream Project / Environment seek when those filters are also present (HLPS SC2). BuildNumber-only filtering remains explicitly out-of-scope of the perf guarantee.

## 2. Requirements

### R1 — Predicate change for Project and EnvironmentName
The branches in `GetRequestStatusesByPage` that today route `Project`, `EnvironmentName`, or `BuildNumber` through `ContainsExpression` are rewritten so that:

- `Project` filter values produce a LINQ predicate that EF Core compiles to `WHERE [Project] LIKE '@p%'` — i.e., prefix, SARGable.
- `EnvironmentName` filter values produce a LINQ predicate that EF Core compiles to `WHERE [Environment] LIKE '@p%'` — prefix, SARGable.
- `BuildNumber` filter values continue to produce a substring predicate (`LIKE '%@p%'`); behaviour and SQL are unchanged for that field.

The implementation approach is unconstrained at spec level: the JIT author may add a new `StartsWithExpression` helper alongside `ContainsExpression` in `DataPagerExtension`, branch inline in `GetRequestStatusesByPage` per filter path, or any other approach that produces the required SQL. The constraint is the *generated SQL*, not the C# shape.

### R2 — Both AND-path and OR-path affected
The predicate change applies in **both** code paths. Specifically:

- **AND-path** (`hasDistinctDetailValues == true`): when the request grid sends distinct Project / EnvironmentName / BuildNumber values, each is ANDed via `Where(...)`. Project and EnvironmentName must use `StartsWith`; BuildNumber must use substring.
- **OR-path** (`hasDistinctDetailValues == false`): the env-monitor's shared `detailsFilter` pushes one value to multiple paths; predicates are accumulated into `filterLambdas` and combined via `WhereAny`. The Project and BuildNumber lambdas (and EnvironmentName, if present) must respect the same per-path predicate semantics — so the OR-combination is `Project StartsWith @p OR BuildNumber Contains @p` (heterogeneous semantics within one OR is intentional and matches HLPS SC2 wording).

### R3 — Helper untouched (or extended additively)
`DataPagerExtension.ContainsExpression` is not modified in a way that changes its current callers' behaviour. If the JIT author adds a `StartsWithExpression` helper, it is a new function alongside `ContainsExpression`, not a parameterisation of the existing one (the additive change preserves binary compatibility for any external consumer and keeps the diff narrow).

### R4 — `EnvironmentNameExact` branch untouched
The `EnvironmentNameExact` equality branch from S-003 is **not** modified by S-004. Its diff scope is locked.

### R5 — Test coverage
Test coverage for S-004 follows the same pragmatic envelope as S-003: the `Dorc.Api.Tests` project today has no EF Core relational test infrastructure (no SQLite / no InMemory provider that supports `ToQueryString()`), so SQL-shape assertions are not feasible without scope expansion. The JIT author MUST add at least the following testable evidence:

- Unit-testable assertions, where feasible without new infrastructure, that:
  - The Project / EnvironmentName / BuildNumber predicate selection paths route correctly. If a new `StartsWithExpression` helper is added, it has its own unit tests modelled on the existing `ContainsExpression` tests in `Dorc.Api.Tests/DataPagerExtensionTests.cs` (which assert the LINQ expression tree shape — that pattern works without an EF context).
- The deviation from the SPEC's preferred SQL-shape assertion is explicitly recorded in the commit message and is the same deviation taken on S-003.

If the JIT author chooses to invest in adding SQLite-backed EF Core test infrastructure, that is acceptable scope expansion **iff** the user explicitly approves before delivery. Otherwise, the deviation is the same as S-003.

### R6 — Build cleanly
The solution builds (`dotnet build` for affected projects) without new warnings.

### R7 — Diff scope
Bounded to:
- `RequestsStatusPersistentSource.cs` — the three Project / EnvironmentName / BuildNumber filter branches (and the `hasDistinctDetailValues` accumulation logic if the JIT author needs to refactor it for clarity, with care).
- Optionally `DataPagerExtension.cs` — adding a new `StartsWithExpression` helper alongside `ContainsExpression`.
- Optionally `Dorc.Api.Tests/DataPagerExtensionTests.cs` — new tests for the new helper.

No other files are modified.

## 3. Out of scope

- `EnvironmentNameExact` branch — owned by S-003.
- Other paged-grid endpoints — explicitly excluded.
- Frontend changes — owned by S-005.
- Index work — owned by S-002.
- Performance verification — owned by S-006.
- Modifying `ContainsExpression` in a way that affects existing callers.
- Renaming any filter `Path` constants (frontend and backend share the contract `'Project'`, `'EnvironmentName'`, `'BuildNumber'`; renames are out-of-scope and would expand the change to dorc-web).
- Fixing the BuildNumber-only filter performance — explicitly out of HLPS scope.

## 4. Acceptance Criteria

S-004 is "done" when **all** hold:
1. Project and EnvironmentName predicate produce `LIKE '@p%'` SQL in both AND-path and OR-path; BuildNumber unchanged.
2. R3 satisfied — existing `ContainsExpression` callers (other endpoints) remain semantically identical.
3. R4 satisfied — `EnvironmentNameExact` branch untouched.
4. R5 satisfied — at least the helper-level unit tests if a new helper was added; SQL-shape deviation recorded if applicable.
5. R7 satisfied — diff scope bounded to the listed files.
6. Build clean per R6.
7. Adversarial Quality Gate has approved the diff.

## 5. Risks & Open Questions

- **Substring → prefix is a user-visible behaviour change.** A user typing the middle of a project name (e.g., "ulator" to find "Calculator") will no longer match. Mitigation: this is the SC5 / Constraint §4 hand-off to S-005 (UI affordance update) and S-007 (release notes). Within S-004, the change is implemented; communication is downstream.
- **OR-path heterogeneity.** Combining `Project StartsWith` with `BuildNumber Contains` in a single OR clause is unusual but matches HLPS SC2 wording precisely. The env-monitor user typing into `detailsFilter` gets "starts with this in Project OR contains this in BuildNumber" — which is the closest behaviour to "search anywhere matching this fragment" that respects the SARGable Project predicate. The user should be aware of this via S-005 affordance text.
- **Test coverage envelope is the same as S-003.** Documented deviation, same rationale.

## 6. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6, Haiku 4.5 | Submitted to a 3-model panel — main behaviour change. |
