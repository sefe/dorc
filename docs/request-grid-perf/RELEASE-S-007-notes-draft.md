# RELEASE NOTES — Request-grid performance fix

**Step ID:** S-007
**Date:** 2026-05-05
**Topic slug:** `request-grid-perf`

This document is the **draft release-notes content** for the next DOrc release that includes the request-grid performance fix. The team should copy the relevant section into the project's release / GitHub-Release / changelog mechanism when the release is cut. DOrc does not currently maintain a single in-repo release-notes file, so this document is the delivery artefact for HLPS Constraint §4 / SC5 documentation requirement.

---

## For end users

### Request-grid filter behaviour change

The deployment-request grid (the main monitor page and the environment-pinned monitor page) has had its filter behaviour tightened to support the recent performance fix:

| Field | Before | After |
|-------|--------|-------|
| **Project** | Substring (matched anywhere in the project name) | **Prefix** — matches project names that **start with** the entered text |
| **Environment** | Substring | **Prefix** — matches environment names that **start with** the entered text |
| **Build** | Substring | Substring (unchanged) |

If you previously typed a fragment from the middle of a project or environment name to find it (e.g., `"ulator"` to match `"Calculator"`), that no longer works — type the start of the name instead.

The filter input placeholders have been updated to indicate the new semantic. On the main grid each filter input now reads `"starts with"` (Project, Environment) or `"contains"` (Build); on the environment-pinned monitor, the shared filter input reads `"Project / Build"` (it matches projects that start with the text **or** build numbers that contain the text — hover for the long-form explanation).

### Why this change

This was driven by a production CPU-saturation incident on 2026-05-05. The substring match generated a non-SARGable `LIKE '%…%'` SQL predicate that forced a full-table scan of `deploy.DeploymentRequest` on every grid request, and that became unsustainable as the table grew and the request grid was polled by many concurrent monitor sessions. The prefix-match flips it to `LIKE '…%'`, which is index-seekable.

### What's not changed

- All other request-grid filters (Id, Username, Status, Components) work exactly as before.
- The build-number filter still matches anywhere within the build number — that one stays substring because the supporting fields (Project, Environment) typically narrow the row set first.
- API behaviour and the request URL contract are unchanged.

---

## For operators / DBAs

### Schema change

This release adds two non-clustered indexes to `deploy.DeploymentRequest`:

- `IX_DeploymentRequest_Environment` — composite `(Environment ASC, Id DESC)`
- `IX_DeploymentRequest_Project` — composite `(Project ASC, Id DESC)`

See `docs/request-grid-perf/DEPLOY-S-002-deployment-request-indexes.md` for size estimates, the `ONLINE` decision (the authored DDL is edition-agnostic with `ONLINE = OFF` by default — Enterprise deployments can flip to `ONLINE = ON` before publish), and the rollback `DROP INDEX` script.

### Post-deploy CPU baseline check (HLPS SC4 hand-off)

Per the governing HLPS, CPU baseline confirmation against the pre-incident workload is the operations team's responsibility. The S-006 perf runbook (`docs/request-grid-perf/PERF-S-006-verification-runbook.md`) contains the queries and pass criteria. After the release lands in production, please:

1. Capture the four reference queries from S-006 in production over a representative load window.
2. Confirm logical reads on the request-grid query are materially below the pre-incident range (~370K) and that plans show index seeks.
3. Confirm `IX_Status_IsProd`-using queries (the existing monitor `GetRequestsWithStatus` lookups) continue to seek that index.
4. Report any divergence back to the development team for follow-up.

### Cached plan invalidation

Adding the new indexes will trigger plan invalidation for existing `deploy.DeploymentRequest` queries on first reference post-deploy. Brief CPU bump expected as plans recompile; subsides within minutes.

---

## For developers / reviewers

The complete artefact set for this change is in `docs/request-grid-perf/`:

- `HLPS-…` — high-level problem statement (APPROVED, 3-model adversarial review).
- `IS-…` — implementation sequence (APPROVED).
- `SPEC-S-001…` — `ContainsExpression` audit (APPROVED) + `AUDIT-S-001-…` deliverable (APPROVED): zero Critical findings; HLPS hypothesis confirmed.
- `SPEC-S-002…` (APPROVED) + `DEPLOY-S-002-…` deployment notes — index design.
- `SPEC-S-003…` (APPROVED) — `EnvironmentNameExact` substring → equality.
- `SPEC-S-004…` (APPROVED) — Project + EnvironmentName per-field filters: `Contains` → `StartsWith`.
- `SPEC-S-005…` (APPROVED) — UI affordance updates.
- `PERF-S-006-…` — verification runbook (results pending production execution).
- `RELEASE-S-007-…` — this document.

Each artefact passed at least a 2-model adversarial review (3-model for the higher-risk HLPS / IS / SPEC-S-002 / SPEC-S-004). All HIGH findings were addressed; LOW findings either accepted or deferred per fix-scope discipline.

The PR description should reference this folder as the source of truth.
