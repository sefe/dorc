# SPEC — S-002 — Supporting Indexes on `deploy.DeploymentRequest`

**Status:** IN REVIEW
**Date:** 2026-05-05
**Step ID:** S-002
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED

---

## 1. Purpose

S-002 adds non-clustered index(es) on `[deploy].[DeploymentRequest]` to make the predicates introduced by S-003 (equality on Environment) and S-004 (prefix on Project, prefix on Environment) index-seekable in production. Without S-002, the SARGable predicates in S-003/S-004 still scan the table — so the structural fix only delivers its intended cost reduction once the supporting indexes exist.

This spec is requirements-only. It does not prescribe the exact DDL, column order, sort direction, included-column set, or filename — those are delivery-time decisions guided by the requirements below.

## 2. Requirements

### R1 — Query patterns the new index(es) must support
The index(es) added by S-002 must provide an index-seek access path for the following query shapes against `[deploy].[DeploymentRequest]`:

- **Q-Env-Equality** — equality predicate on `Environment` (used by S-003's `EnvironmentNameExact` rewrite).
- **Q-Env-Prefix** — prefix-match predicate on `Environment` (`LIKE '@p%'`, used by S-004's `EnvironmentName StartsWith`).
- **Q-Proj-Prefix** — prefix-match predicate on `Project` (`LIKE '@p%'`, used by S-004's `Project StartsWith`).

For each shape, the chosen plan when the predicate is selective enough must include an index seek (not a scan) on a new index added by S-002. **All three query shapes must be served**, not just one or two — if a single composite key cannot cover both Environment-leading and Project-leading predicates simultaneously (which is the typical case for SQL Server, since a composite key only seeks on its leading column or a contiguous prefix), then S-002 ships **two** indexes: one supporting Q-Env-* and one supporting Q-Proj-Prefix. Plan verification is owned by S-006; this spec does not prescribe the verification mechanism.

The index design should also reasonably support the common combination of an Environment-bearing predicate and the page's typical sort order (the request grid orders by `Id` by default, descending in most consumer surfaces). A composite key whose trailing component matches the sort eliminates a separate sort operation and is the recommended shape; an alternative shape that satisfies R1 is acceptable if the JIT-Spec author records the rationale at Delivery time.

### R2 — Plan-interaction with `IX_Status_IsProd`
The existing `IX_Status_IsProd` non-clustered index (on `Status, IsProd`) supports queries that filter by Status and IsProd. The new index(es) from S-002 must not regress those queries — i.e., for a query that today seeks `IX_Status_IsProd`, the optimiser must continue to choose `IX_Status_IsProd` (or a strictly better path) post-deploy. Verification of plan stability for combined Status + Environment queries is owned by S-006 (per the IS amendment).

### R3 — SSDT pattern
Index DDL ships through the existing SSDT database project at `src/Dorc.Database`. The default placement is one `*.index.sql` file per index under `Schema Objects/Schemas/deploy/Tables/Indexes/` (the dominant pattern in the codebase). The JIT-Spec author may instead place the DDL inline in `DeploymentRequest.table.sql` (the existing pattern for that table's `IX_Status_IsProd`) if it better matches the project's local convention, but must record the choice and rationale.

Index name(s) follow the existing convention: `IX_<Table>_<Columns>` (or a clearly equivalent pattern observable in the SSDT project).

### R4 — Production-safe deployment
The DDL must not require an exclusive lock that blocks live workload during creation in production. **Open per-step blocking unknown U4 must be resolved before Delivery entry.** Resolution paths:

- **U4 → Enterprise**: the DDL uses `WITH (ONLINE = ON)` so creation is online; deployment can run during business hours.
- **U4 → Standard**: `ONLINE = ON` is unavailable; the DDL omits it and the deployment plan calls for a maintenance window. The SSDT script itself must be edition-agnostic — it does not specify `ONLINE` — and the maintenance-window approach is documented in the deployment notes (see R7).

The default authored DDL omits `ONLINE` so the script is safe on Standard and the maintenance-window default is the conservative position. If U4 is resolved as **Enterprise**, the recommended path is to add `ONLINE = ON` so the index can be deployed without a maintenance window; the JIT-Spec author records the chosen path in the deployment notes (R7). The risk profile of a hot-creation pass on Enterprise is the user / DBA's call but the default *recommendation* is "use ONLINE if available."

### R5 — U7 (per-environment skew) impact
Open per-step blocking unknown U7 informs whether a single composite key is sufficient or whether a filtered index, included columns, or a different column order is preferable. **U7 must be resolved before Delivery entry.** The spec accepts U7's resolution as belonging to the Delivery phase; the JIT-Spec author drafts the DDL on the basis of HLPS's default candidate (composite `(Environment, Id DESC)` and `(Project, Id DESC)`) and refines based on the actual U7 answer at Delivery time. If skew is severe enough to motivate a non-default design (e.g., filtered indexes), the JIT-Spec author updates this spec rather than improvising at Delivery time.

### R6 — Build cleanly under SSDT
The SSDT project (`Dorc.Database.sqlproj`) builds without warnings against the existing toolchain after the new DDL lands. Existing references to `[deploy].[DeploymentRequest]` (procedures, views, other indexes) continue to bind. The JIT-Spec author runs an SSDT build locally before submitting the spec for code review.

### R7 — Deployment notes
The spec deliverable includes deployment-side notes covering:
- Whether `ONLINE = ON` is used (per U4).
- Estimated index size (best-effort, based on table row count and key width — even an order-of-magnitude estimate is sufficient).
- Whether a maintenance window is required and a recommended window length.
- Rollback plan: a `DROP INDEX` script kept alongside the deployment artefact.

The deployment notes ship as part of this spec's revision trail or in a sibling file under `docs/request-grid-perf/`. They do **not** need to be a separate adversarial-reviewed artefact — the JIT-Spec gate covers them.

## 3. Out of scope

- **Other indexes on `deploy.DeploymentRequest`** beyond what R1's query patterns require. Adding "while we're here" optimisation indexes is HLPS scope creep.
- **Indexes on tables other than `deploy.DeploymentRequest`.** The audit (S-001) confirmed no other consumer is at HLPS-scope-relevant risk.
- **Statistics maintenance, index rebuild jobs, fillfactor tuning.** Operational concerns owned by the DBA.
- **Cleanup of the existing `IX_Status_IsProd` index.** It continues to be used; this spec touches only new indexes.
- **Performance measurement.** Owned by S-006.
- **Application-side code changes.** Owned by S-003, S-004.

## 4. Acceptance Criteria

S-002 is "done" when **all** hold:
1. The new index DDL exists in the SSDT project and matches the chosen pattern (separate file under `Indexes/` or inline in `DeploymentRequest.table.sql`).
2. The DDL satisfies R1, R2 (no design that obviously duplicates `IX_Status_IsProd`'s key prefix; verification of plan stability is S-006), R3, and R4 (edition-agnostic by default per R4); the index name(s) follow the convention.
3. The SSDT project builds cleanly (R6).
4. Deployment notes (R7) exist and cover the four bullets.
5. Open unknowns U4 and U7 are recorded as resolved in the deployment notes (or this spec, if updated post-resolution) before the work enters Delivery.
6. Adversarial Quality Gate has approved the SSDT diff.

## 5. Risks & Open Questions

- **Index footprint on a large table.** If `deploy.DeploymentRequest` has grown into the millions of rows, the new index(es) consume non-trivial storage and add per-insert cost. The deployment notes (R7) require a size estimate so the user / DBA can sanity-check.
- **Plan regression on existing queries.** R2 requires no regression on `IX_Status_IsProd`-using queries. The optimiser may, however, switch to a new index for a query it previously ran fine on `IX_Status_IsProd` if the new index covers the predicate cheaper. That is *not* a regression even if the plan changes; S-006's plan capture confirms.
- **U4 + U7 not resolved before Delivery.** The IS marks this as per-step blocking; the spec re-asserts the gate. If the user chooses to proceed anyway, the JIT-Spec author drafts to the default (Standard, default skew) and the user accepts residual risk explicitly.

## 6. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6, Haiku 4.5 | Submitted to a 3-model panel — schema-changing step, higher risk profile. GPT 5.4 substituted with Haiku 4.5. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6, Haiku 4.5 | Opus and Sonnet **APPROVE WITH MINOR FINDINGS**; Haiku **APPROVE** (no findings). Triage: 3 ACCEPTED (Sonnet F1 MEDIUM — R1 now explicitly requires seek paths for *all three* query patterns, with two-index outcome stated when a single composite cannot cover both Environment- and Project-leading predicates; Opus F2 LOW — R2 added to AC2; Sonnet F2 LOW — R4 ONLINE recommendation strengthened from "may" to "recommended" on Enterprise); 1 NOTED (Opus F4 — Haiku 4.5 / Opus 4.7 are out-of-band substitutes for the formally listed reviewer pool, transparent acknowledgement); 3 DEFERRED (Opus F1 R5 redundancy, Opus F3 SSDT publish mechanism, Sonnet F3 R3 path over-prescription, Sonnet F4 AC item 6 governing-spec pointer — all stylistic). |
