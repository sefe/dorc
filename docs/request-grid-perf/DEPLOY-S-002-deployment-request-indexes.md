# DEPLOYMENT NOTES — S-002 — `deploy.DeploymentRequest` indexes

**Date:** 2026-05-05
**Step ID:** S-002
**Governing spec:** [`SPEC-S-002-deployment-request-indexes.md`](SPEC-S-002-deployment-request-indexes.md) — APPROVED

These notes accompany the SSDT additions for S-002 and satisfy SPEC-S-002 R7. They are intended for the user / DBA performing the production deployment.

## 1. New objects shipped

Two new non-clustered indexes on `[deploy].[DeploymentRequest]`:

- **`IX_DeploymentRequest_Environment`** — composite key `(Environment ASC, Id DESC)`. Supports the `EnvironmentNameExact` equality predicate (S-003) and `EnvironmentName StartsWith` prefix predicate (S-004). The trailing `Id DESC` aligns with the request grid's default sort.
- **`IX_DeploymentRequest_Project`** — composite key `(Project ASC, Id DESC)`. Supports the `Project StartsWith` prefix predicate (S-004), with the same trailing-sort alignment.

Both ship as separate `.index.sql` files under `Schema Objects/Schemas/deploy/Tables/Indexes/` (the dominant pattern in the SSDT project).

## 2. `ONLINE` decision (U4)

**Current authored DDL:** `ONLINE = OFF`. This is the conservative default per SPEC-S-002 R4: edition-agnostic, safe on Standard and Enterprise, but creation acquires a Sch-M lock that blocks live workload while the index builds.

**If U4 resolves as Enterprise** (recommended for production with non-trivial table size): toggle the option to `ONLINE = ON` in both `.index.sql` files before the Release deployment. The build can then run during business hours.

**If U4 resolves as Standard**: leave `ONLINE = OFF` and schedule a maintenance window. See §4 below.

This decision must be confirmed with the user / DBA before the SSDT artefact is published to production.

## 3. Estimated size

`deploy.DeploymentRequest` row count is unknown to this spec (production access not available; HLPS U8 is open). Best-effort sizing assumes the production table has grown to multi-hundred-thousand to low-millions of rows based on the incident's observed ~370K logical reads per scan.

Per index:
- Key width: ~128–256 bytes (NVARCHAR(64) Environment + INT Id) for the Environment index; ~128–256 bytes (NVARCHAR(64) Project + INT Id) for the Project index.
- Order-of-magnitude estimate at 1M rows: ~150–300 MB per index. At 10M rows: ~1.5–3 GB per index. These are upper bounds; SQL Server compresses and pads page boundaries so actual figures will be lower.
- Add-time temp space: roughly equivalent to the final index size for sort-in-tempdb; the authored DDL has `SORT_IN_TEMPDB = OFF` so the build uses the data file's filegroup. If tempdb capacity is tight versus user filegroup capacity, the DBA may flip `SORT_IN_TEMPDB = ON` at deploy time.

The DBA should sanity-check available space on the data filegroup before running the publish.

## 4. Maintenance window guidance

If `ONLINE = OFF` (Standard, or by choice on Enterprise):

- Each index build acquires a Sch-M lock on `deploy.DeploymentRequest` for the duration of the build. During this lock, all reads and writes against the table block.
- Estimated build duration at 1M rows on commodity SAN/SSD: 30 seconds to 2 minutes per index. At 10M rows: 5–15 minutes per index. Scale linearly above that.
- Recommended window length: **15 minutes** for tables under ~5M rows, **30–45 minutes** for larger. Schedule during the lowest-traffic period for the DOrc API (overnight or weekend per the team's normal release cadence).
- The two indexes can be built sequentially within the same window — there is no cross-index dependency.

If `ONLINE = ON` (Enterprise):

- Build runs without an exclusive lock. Live workload sees no functional outage but does experience a brief CPU and I/O bump while the build replicates rows in the version store.
- No specific window required; deployment can run during business hours alongside the rest of the release.

## 5. Rollback plan

If a production issue surfaces post-deploy and the indexes need to be removed:

```sql
USE [Dorc];
GO
DROP INDEX [IX_DeploymentRequest_Environment] ON [deploy].[DeploymentRequest];
DROP INDEX [IX_DeploymentRequest_Project] ON [deploy].[DeploymentRequest];
GO
```

Drops are metadata-only operations (Sch-M lock for milliseconds) on either edition, so rollback is fast and safe. After the rollback, the SSDT publish for the rollback release should also remove the `<Build Include …>` lines from the `.sqlproj` so subsequent publishes do not reintroduce the indexes. Until S-003 / S-004 application-side changes are also rolled back, the SARGable predicates from those steps will scan the table — a return to the pre-S-002 cost profile but no worse.

The S-006 evidence document captures pre-fix and post-fix baselines for reference.

## 6. Plan-stability check (post-deploy)

The S-006 perf step covers the formal verification (plans + logical reads). Operationally, post-deploy the DBA should sanity-check:

- The two new indexes appear in `sys.indexes` for `deploy.DeploymentRequest` and have non-zero `rows` counts (filled during the build).
- A spot-check `SELECT … WHERE Environment = '<known-env>'` shows an index seek on `IX_DeploymentRequest_Environment` in the actual plan.
- `IX_Status_IsProd` is still chosen for the existing Status-bearing queries (e.g., the monitor's `GetRequestsWithStatus` lookups). If the optimiser flips to a new index for one of those queries, that is fine **iff** the new plan is cheaper or equal cost. A regression here is reportable to the agent and triggers a JIT-Spec revisit.

## 7. Hand-off to operations (SC4)

Per SPEC-S-002 §3 and HLPS SC4, post-deploy CPU baseline confirmation against the pre-incident workload is the operations team's responsibility. The S-006 evidence document captures the controlled-environment numbers; ops compares production CPU against those numbers in the days following deployment and reports back any divergence.

## 8. Status

This document is the deployment-notes deliverable for S-002. It does not require a separate adversarial-review gate per SPEC-S-002 R7; the JIT-Spec gate covers it. Updates to U4 / U7 resolutions land here as they are made.
