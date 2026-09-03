# IS — Analytics Expansion

**Status:** APPROVED (auto-pilot). Steps are ordered; each is atomic.

- **S-001 SQL: unified failure taxonomy (#729)** — edit
  `sp_Select_Deployments_By_Project_Date`, `sp_Select_Deployments_By_Project_Month`,
  `sp_PopulateAnalyticsEnvironmentUsage`, `sp_PopulateAnalyticsUserActivity` to
  use failure `IN ('Failed','Errored','Error')`, success `IN ('Completed','Success')`.
- **S-002 SQL: new tables + populate procs** — `AnalyticsMonthlyOutcome`,
  `AnalyticsEnvironmentWait`, `AnalyticsProjectDuration`,
  `AnalyticsComponentReliability`, `AnalyticsRecoveryTime`; ALTER
  `AnalyticsDuration` (P50/P90/P95) and `AnalyticsEnvironmentUsage`
  (LastSuccessfulDeployment); extend their populate procs; register all new
  files in the sqlproj.
- **S-003 Backend: EF + sources** — entity classes, DbSets in
  `IDeploymentContext`/`DeploymentContext`, OnModelCreating mappings; new
  `IAnalyticsPersistentSource` methods + implementations (ordering, bounded
  result sets, null-safe mapping).
- **S-004 Backend: API models + controller** — new ApiModels; extend
  `AnalyticsDurationApiModel` and `AnalyticsEnvironmentUsageApiModel`; new
  `AnalyticsController` endpoints with the established IActionResult/500 pattern.
- **S-005 Backend tests** — persistent-source tests (mock DbSets) and
  controller ok/500 tests for each new endpoint; run with local .NET 8.
- **S-006 Frontend contract** — swagger.json paths + schemas; generated-style
  TS models + API clients; barrels + `.openapi-generator/FILES`.
- **S-007 Frontend page** — filter bar (date range + project, client-side,
  pure helper functions for testability); new charts: monthly outcome
  (volume/failure-rate/cancellations, prod vs non-prod), environment wait
  top-10, per-project duration medians, component reliability, recovery time,
  environment staleness. Empty states per existing pattern.
- **S-008 Frontend tests** — extend `page-analytics.test.ts` mocks for the new
  endpoints; unit-test the filter helpers; run vitest (chromium).
- **S-009 Docs + review** — update `docs/analytics-page/README.md`
  (taxonomy, new endpoints, ActiveBatch dependency); adversarial panel
  (3 models) on the full diff; triage; fix accepted findings; push to PR #728;
  comment on issue #729.
