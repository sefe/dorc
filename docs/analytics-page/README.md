# Analytics Page (`/analytics`)

Deployment analytics and statistics dashboard. Formerly served at `/about` via
`page-about`; renamed to `/analytics` / `page-analytics` (the `/about` path now
redirects). The reusable chart wrapper was renamed `hegs-chart` → `dorc-chart`.

## Shared loading spinner (`dorc-spinner`)

While replacing the analytics page's loader with the site's standard centered
spinner, the overlay + spinner block that had been copy-pasted into ~25 pages
and components was extracted into a single reusable
`src/dorc-web/src/components/dorc-spinner.ts`. Each call site keeps its original
visibility toggle — either a conditional render (`${this.loading ? ... : ...}`)
or a `?hidden="${...}"` binding. Sites whose content sits in a raised stacking
context override the overlay stacking via the `--dorc-spinner-z-index` custom
property (default `2`; a few list pages use `1000`). `hegs-dialog.ts` (a modal
backdrop) and `log-dialog.ts` (an imperative DOM spinner) intentionally keep
their own implementations.

## Failure taxonomy (unified — resolves issue #729)

All analytics surfaces share one outcome definition, applied in every
populate/select proc:

- **Failure** = `Status IN ('Failed', 'Errored', 'Error')` — `Errored` is the
  current `DeploymentRequestStatus` enum value; `'Error'` covers legacy rows.
- **Success** = `Status IN ('Completed', 'Success')`.
- **Cancelled** = `Status = 'Cancelled'` (reported separately, never counted
  as a failure, and excluded from deployment-volume counts).
- **`Abandoned` and other non-terminal statuses** count toward totals where
  present (e.g. environment/user `TotalDeployments`) but toward neither
  success nor failure — a deliberate choice, not an omission.
- Component-level metrics use the **`DeploymentResultStatus` domain** instead
  (`Complete`/`Warning`/`Failed`/`Cancelled`/...): only attempts that executed
  to a terminal state (`Complete`, `Warning`, `Failed`) are counted, because
  archival snapshots mark never-executed component rows `Cancelled`.

## Expanded analytics (items 1–9)

See `docs/analytics-expansion/` for the HLPS/IS. New aggregates, all following
the truncate-and-rebuild populate pattern over `deploy` + `archive`:

| Endpoint | Table / proc | Chart |
|---|---|---|
| `GET /AnalyticsMonthlyOutcome` | `AnalyticsMonthlyOutcome` / `sp_PopulateAnalyticsMonthlyOutcome` | Monthly volume (prod vs non-prod stacked), failure-rate % line, cancellations. Volume and the failure-rate denominator are **completed deployments only** (cancellations charted separately, never in the volume); a restarted-but-unfinished request can transiently appear under its previous completion month |
| `GET /AnalyticsEnvironmentWait` | `AnalyticsEnvironmentWait` / `sp_PopulateAnalyticsEnvironmentWait` | Top 10 environments by queue wait (median + P90 of Requested→Started). Negative waits and waits over 7 days are excluded as data artifacts/outliers |
| `GET /AnalyticsProjectDuration` | `AnalyticsProjectDuration` / `sp_PopulateAnalyticsProjectDuration` | Per-project duration median + P90 (top 15 by volume) |
| `GET /AnalyticsComponentReliability` | `AnalyticsComponentReliability` / `sp_PopulateAnalyticsComponentReliability` | Failure % per component over **executed** attempts only (`Complete`/`Warning`/`Failed`; min 20). Attempt tables are not archived, so this covers the retained window of history only |
| `GET /AnalyticsRecoveryTime` | `AnalyticsRecoveryTime` / `sp_PopulateAnalyticsRecoveryTime` | Median hours from a failed deployment to the next success. Pairs are computed within project+environment, reported per project; consecutive failures each pair to the same recovery, so streaky outages weight the median, and gaps include calendar inactivity |
| `/AnalyticsDuration` (extended) | `AnalyticsDuration` +P50/P90/P95 columns | Percentile stat cards |
| `/AnalyticsEnvironmentUsage` (extended) | `AnalyticsEnvironmentUsage` +`LastSuccessfulDeployment` | Stalest environments (days since last success). Environments that have **never** succeeded have no date to measure from and are excluded from this chart |

A client-side filter bar (from/to month + project) applies to the deployment
river and monthly outcome charts; the pure filtering helpers live in
`src/dorc-web/src/pages/page-analytics-data.ts`.

> **ActiveBatch dependency:** the external refresh job must be updated to call
> the five new `sp_PopulateAnalytics*` procs listed above (alongside the
> existing ones) or the new charts will show their empty states.

## Architecture

| Layer | Component |
|-------|-----------|
| Page | `src/dorc-web/src/pages/page-analytics.ts` |
| Chart wrapper | `src/dorc-web/src/components/chart/dorc-chart.ts` |
| API | `Dorc.Api/Controllers/AnalyticsController.cs` |
| Data source | `Dorc.PersistentData/Sources/AnalyticsPersistentSource.cs` |
| Tables / procs | `Dorc.Database/deploy/**/Analytics*` |

### Headline statistics are computed server-side

The eight summary cards (totals, failures, busiest day, averages, top-3
projects + percentage) are aggregated by `GetDeploymentSummary()` and exposed
at `GET /AnalyticsDeploymentSummary` as `AnalyticsDeploymentSummaryApiModel`.
This replaced an earlier design that downloaded the entire per-project-per-date
history to the browser and aggregated it client-side. The per-date endpoint and
its generated client were removed as part of that change.

The remaining charts (theme river, project pie, environment/user/component bar
charts, time-pattern heatmap) are still built client-side from their respective
endpoints, which return pre-aggregated rows.

## Data refresh dependency (external)

The `Analytics*` tables are **not** populated by anything in this repository.
The `sp_PopulateAnalytics*` stored procedures and the legacy
`DeploymentsByProject*` population are invoked by an external ActiveBatch job
that is **not open-sourced** (the scheduling system is sensitive). Consequences:

- A fresh deployment shows empty charts until that external job has run.
- Staleness is governed entirely by the external job's cadence.
- The pages and API tolerate empty/missing analytics data gracefully (empty
  states, zeroed summaries — no exceptions, no `NaN`/`Infinity`).

`sp_PopulateAnalyticsTimePattern` pins `SET DATEFIRST 7` so the stored
`DayOfWeek` (SQL `WEEKDAY`, 1-7, Sunday-based) is independent of server
configuration; the API converts it to a 0-6 index and guards against
out-of-range values.

## Security / visibility decision

All `AnalyticsController` endpoints are `[Authorize]` only — **any authenticated
user can see system-wide analytics**, including environment usage, component
usage, and per-user deployment/failure counts. This is an **intentional**
decision for this internal tool: analytics are not scoped per project or per
user. If that ever needs to change, gate the controller with
`ISecurityPrivilegesChecker` (see `TerraformController` for the pattern) or
filter results to the caller's authorized projects.
