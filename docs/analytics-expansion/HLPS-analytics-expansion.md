# HLPS — Analytics Expansion (items 1–9)

**Status:** APPROVED (user-selected options 2026-06-11; auto-pilot enabled; adversarial review at end)

## Problem

The `/analytics` dashboard shows only static totals. It is blind to trends,
prod/non-prod split, queue contention, duration distribution, cancellations,
component reliability, recovery time, and environment staleness — all of which
are derivable from data DOrc already captures. Additionally (issue #729) the
"failure" definition is inconsistent across surfaces, and the literal `'Error'`
used in some procs does not match the actual enum value `Errored`.

## Scope (user-approved decisions)

1. **Failure taxonomy unified** — failure = `Status IN ('Failed','Errored','Error')`,
   success = `IN ('Completed','Success')`, cancelled = `Status = 'Cancelled'`.
   Applied to ALL analytics procs, existing and new. Resolves #729.
2. **Pre-aggregated pattern** — new populate procs + tables, like existing
   analytics. The external ActiveBatch job must be updated to call the new
   procs; until then new charts show empty states (documented dependency).
3. **Filter bar (item 9)** — client-side date-range (year/month) + project
   filter applied to the already-loaded month-level aggregates. No API params.
4. **Auto-pilot** — HLPS/IS written, implementation in one run, adversarial
   panel review at the end.

## Feature → artifact map

| Item | Feature | Storage | Endpoint |
|---|---|---|---|
| 1,2,5 | Monthly outcome trend (volume, failures, cancellations) split by IsProd | NEW `AnalyticsMonthlyOutcome` (Year, Month, IsProd, CountOfDeployments, Failed, Cancelled) | `GET /AnalyticsMonthlyOutcome` |
| 3 | Queue/wait time per environment (Requested→Started) | NEW `AnalyticsEnvironmentWait` (EnvironmentName, AvgWaitMinutes, MedianWaitMinutes, P90WaitMinutes, SampleCount) | `GET /AnalyticsEnvironmentWait` |
| 4 | Duration percentiles (global P50/P90/P95) + per-project medians | ALTER `AnalyticsDuration` (+3 nullable cols); NEW `AnalyticsProjectDuration` (ProjectName, MedianDurationMinutes, P90DurationMinutes, SampleCount) | extend `/AnalyticsDuration`; `GET /AnalyticsProjectDuration` |
| 6 | Component reliability (failure + retry rates from attempt tables) | NEW `AnalyticsComponentReliability` (ComponentName, AttemptCount, FailedCount, RetryAttemptCount) | `GET /AnalyticsComponentReliability` |
| 7 | Recovery time (failed → next success, same project+environment) | NEW `AnalyticsRecoveryTime` (ProjectName, MedianRecoveryHours, AvgRecoveryHours, SampleCount) | `GET /AnalyticsRecoveryTime` |
| 8 | Environment staleness (last successful deployment) | ALTER `AnalyticsEnvironmentUsage` (+`LastSuccessfulDeployment` nullable) | extend `/AnalyticsEnvironmentUsage` |
| 9 | Filter bar (date range + project) | none (client-side) | none |
| #729 | Unified failure definition | EDIT `sp_Select_Deployments_By_Project_Date/_Month`, `sp_PopulateAnalyticsEnvironmentUsage`, `sp_PopulateAnalyticsUserActivity` | none |

## Constraints

- All new tables follow the truncate-and-rebuild populate pattern; procs union
  `deploy` + `archive` schemas where both hold data.
- New `AnalyticsDuration` / `AnalyticsEnvironmentUsage` columns are nullable so
  the dacpac deploys against populated tables without defaults.
- `PERCENTILE_CONT` (SQL Server 2012+) used via the
  `SELECT DISTINCT ... OVER (PARTITION BY ...)` pattern.
- Attempt tables are not archived (FK cascade on archive); component
  reliability reflects the retained window only — documented.
- New SQL files must be registered in `Dorc.Database.sqlproj` (explicit
  `<Build Include>`).
- Frontend follows the generated-client style; `swagger.json` + generator
  `FILES` manifest updated in lockstep (per the convention hardened in PR #728).

## Unknowns Register

| # | Unknown | Status |
|---|---|---|
| U1 | ActiveBatch job update timing (external) | NON-BLOCKING — empty states until ops updates the job; documented |
| U2 | Whether legacy rows use `'Error'`/`'Success'` literals | NON-BLOCKING — taxonomy includes both legacy and enum spellings |
| U3 | Attempt-table data volume | NON-BLOCKING — offline batch proc |

## Success criteria

- All existing + new backend analytics tests pass (built with .NET 8 locally).
- Frontend tsc/eslint/lit-analyzer clean; vitest suite passes incl. new tests.
- Every failure-counting surface uses the unified taxonomy (closes #729).
- New charts render meaningful empty states with no data.
- Adversarial panel verdict ≥ APPROVE-WITH-NITS.
