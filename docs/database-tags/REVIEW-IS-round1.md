# IS Review — Round 1 (2026-07-17)

Panel: 2 independent reviewers — R-O (coverage & ordering), R-F (environment
feasibility & gate-evidence producibility). Both verdicts: **REVISE**.

R-F's verification notes worth keeping: all S-001 seams confirmed practical
(substitutable constructors; the four-DbSet join composes under `DbContextMock`'s
shared LINQ-to-Objects provider; `EnvironmentUnifier`'s int overload is mock-safe but
its string-name overloads use `EF.Functions.Collate` and must be avoided);
`ToQueryString` works offline on the existing rig; `TagString` in netstandard2.0
ApiModel has no circularity (write without ImplicitUsings/Nullable); swagger targets
confirmed (`ByType` :3488, `dbType` :3706, `DatabaseApiModel.Type` :8843 no
maxLength); `Script.PostDeployment.sql` + `<PostDeploy>` mechanism exists; sqlproj
pins `CompatibilityMode 100`.

## Triage (Accept / Downgrade / Defer / Reject)

| # | Source | Sev | Finding | Triage | v2 disposition |
|---|--------|-----|---------|--------|----------------|
| 1 | R-O F-1 | HIGH | SC-3's spec↔UI constant test assigned to no step | **Accept** | S-004 deliverable + gate item (extend `tag-limits.test.ts`); Step Index updated |
| 2 | R-O F-2 | MED | Absent-vs-empty carve-out contradicts SC-4's literal wording | **Accept** | Explicit reconciliation paragraph in S-004 ("null" = supplied-but-valueless; absence retains no-filter) + absent-param regression test |
| 3 | R-O F-3 | MED | "Four assertions" vs SC-5's five; SC-2 site-9 clause unattributed | **Accept** | Gate enumerates all five; S-005 Addresses gains SC-2 (site-9 clause) |
| 4 | R-O F-4 | MED | U-4 relabel ungated | **Accept** | Relabel verification added to S-005 gate |
| 5 | R-O F-5 | MED | U-1 collision-throw had no test fixture | **Accept** | S-003 collision-throw freeze fixture (sites 1/4/5/6 throw; site 7 array) |
| 6 | R-O F-6 | LOW | U-5 casing fixture unnamed | **Accept** | S-003 `Endur;endur` fixture |
| 7 | R-O F-7 | LOW | DataAccessor 250-width conditional unowned | **Accept** | S-004: fixtures at API unit seam; DataAccessor not on path, untouched |
| 8 | R-O F-8 | LOW | S-003 optionally splittable | **Defer** | Keep bundled (mirrors HLPS §5.3; each deliverable has a named artifact); split only if the S-003 gate proves unwieldy |
| 9 | R-F F-1 | MED | CI/user-environment evidence routing absent | **Accept** | Delegation lines added to S-002 and S-006 (dacpac→CI; U-2 execution→CI+user env; transfer notes) |
| 10 | R-F F-2 | MED | U-2 script compat-100 constraint unstated | **Accept** | S-006 pins compat-100-safe T-SQL as the gate's key review item |
| 11 | R-F F-3 | MED | ThinClient assertion has no existing seam | **Accept** | S-005 names the seam: extract predicate to `tag-parser` + component test via protected setters, statics reset |
| 12 | R-F F-4 | LOW | Spliced descriptions must originate as C# annotations | **Accept** | S-004 states it explicitly |
| 13 | R-F F-5 | LOW | Script location hedge points at nonexistent convention | **Accept** | U-7 audit pinned to `src/install-scripts/AuditDatabaseTags.sql` |
| 14 | R-F F-6 | LOW | S-001 seam caveats (Collate overloads; single-enumerator mock) | **Accept** | Recorded in S-001 |

Rejected: none.

## Outcome

v2 published. Round-2 delta check of the 14 dispositions, then (auto-pilot approved at
the HLPS checkpoint) execution begins at S-001.
