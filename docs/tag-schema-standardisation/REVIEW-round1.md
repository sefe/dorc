# Adversarial Review — Round 1

| Field | Value |
|---|---|
| **Date** | 2026-08-10 |
| **Under review** | `d42fa49`, `e9eebd4`, `ba1f9ec` (schema move, rename, docs) |
| **Panel** | 4 reviewers, 2 model architectures, independent, no shared context |
| **Verdict** | REVISE — 1 CRITICAL, 4 HIGH, 7 MEDIUM, 9 LOW |

Lenses: (1) database migration safety, (2) rename completeness and string-keyed
couplings, (3) runtime behaviour regressions, (4) standards conformance and
documentation honesty.

Reviewers 1 and 3 both generated real deployment scripts with `sqlpackage /a:Script`
rather than reading the diff; reviewer 1 additionally built synthetic empty and
half-migrated target models. Every finding below that concerns the deployment is
backed by an artifact, not an argument.

---

## Triage

### CRITICAL

| # | Finding | Verdict |
|---|---|---|
| C-1 | The `ByType` → `ByTag` route rename was not propagated to `ApiCaller`'s literal endpoint-path map or to `Tools.PostRestoreEndurCLI`, which also still sent `type` as the query key. All six `GetEndurDatabase` call sites would have thrown after deployment. Found independently by reviewers 2, 3 and 4. | **ACCEPT** — fixed in `c64cae4`, before the other three reviewers reported. `ApiCallerEndpointRouteTests` added and mutation-tested: it fails when the old path is restored. HLPS U-6 and SC-5 corrected, since U-6 had recorded "no in-repo consumers" on the strength of a search whose output was truncated. |

### HIGH

| # | Finding | Verdict |
|---|---|---|
| H-1 | The casing renames in `ApplyDeployCasingToRefDataTables.sql` are two `sp_rename` calls with no transaction. An interruption between them strands the table under its staging name, and the guard — which tests for the *original* name — can never fire again. The next publish would then create a new empty table beside the stranded data. | **ACCEPT** — each block now runs in a transaction under `SET XACT_ABORT ON`, and each has a second branch that completes a half-finished rename. |
| H-2 | `IX_DATABASE_Group_ID` is destroyed, not renamed. The rebuild builds the replacement from the model alone, so an index absent from the model dies with the original — and the post-deployment block written to rename it could therefore never fire. Proven by adding the index to the target model and observing DacFx emit `DROP INDEX` with no recreation. | **ACCEPT** — declared in `deploy/Tables/Database.sql` so the rebuild creates it; the dead block deleted. Verified: the upgrade script now contains `CREATE NONCLUSTERED INDEX [IX_Database_GroupId]`. HLPS gains P-2a, including the warning that triggers and extended properties are exposed the same way. |
| H-3 | `AuditDatabaseTags.sql` is documented as a pre-deploy audit but had been repointed at `deploy.Database`, so it could only run *after* the migration — by which time `NormalizeDatabaseTags.sql` has already rewritten the padded rows its Report 2 exists to find. It would have reported zero, always. | **ACCEPT** — made schema-adaptive: it resolves `deploy.Database`/`Tags` or `dbo.DATABASE`/`DB_Type` at run time and issues the reports through `sp_executesql`. |
| H-4 | "No occurrence of the superseded names survives" was overstated: `dbType`, `ByType` and `Type` vocabulary remained in test names, comments and one XML doc param. | **ACCEPT** — cleaned up. SC-5 rewritten to name the terms that were actually missed and to require an unpaginated search. |
| H-5 | Process deviation: HLPS and IS were written *after* implementation, still marked DRAFT, with no `REVIEW-*.md` records — unlike the sibling `docs/` folders. The documents read as forward plans but describe completed work. | **ACCEPT** — both relabelled **AS-BUILT**, HLPS §5a states the deviation plainly, and this file is the missing panel record. Raised to the user rather than quietly relabelled. |

### MEDIUM

| # | Finding | Verdict |
|---|---|---|
| M-1 | Four `*RenameStage` refactorlog operations are dead — DacFx emits nothing for them — and their comment contradicts the post-deployment script's comment about the same mechanic. They would have written dead keys into `dbo.__RefactorLog`. | **ACCEPT** — deleted; the comment now states the correct mechanic and points at the post-deployment script. |
| M-2 | The rebuild resets the identity counter to `MAX(Id)`, so `Id`s of deleted databases get reissued; `deploy.DatabaseAudit.DatabaseId` has no FK, so old audit history silently re-attaches. | **ACCEPT** — `StashDatabaseIdentityValue.sql` (pre-deploy) and `RestoreDatabaseIdentityValue.sql` (post-deploy) capture and reapply the counter, reseeding upwards only. HLPS U-4 corrected — it had claimed identity was preserved. |
| M-3 | The five FK drops precede the rebuild in separate committed batches, so an aborted rebuild leaves referential integrity missing until a re-publish. The production publish profile is outside the repo and cannot be checked. | **PARTIAL ACCEPT** — the window is real; the fix is not ours to make. Recorded as HLPS **U-8**, open, flagged for rollout. Reviewer confirmed the state is recoverable: a re-publish against a synthetic half-migrated model resumes cleanly. |
| M-4 | `DatabaseTagLookupParamTests` assertions were weakened to `Contains(msg, "tag")`, which the message's prose satisfies regardless of the parameter's name. | **ACCEPT** — now asserts `"'tag'"`, quoted, so it pins the parameter name. |
| M-5 | `DeploymentContextTagMappingTests`' docstring claimed to make EF/SSDT divergence a build failure, but the widths and the index uniqueness/filter divergences are unasserted. | **ACCEPT** — docstring now states exactly what is and is not asserted, and why the unasserted divergences are left alone. |
| M-6 | The sqlproj did not build off Windows (`:r .\` includes), so SC-3 — the only automated guard that nothing still points at the old tables — was not reproducible. | **ACCEPT** — includes changed to `./`; the sqlproj now builds in place. |
| M-7 | The test class name says "Tag" but most of its assertions are general mapping facts — heading toward a catch-all. | **ACCEPT** — renamed `ReferenceDataSchemaMappingTests`. |

### LOW

| # | Finding | Verdict |
|---|---|---|
| L-1 | `AuditDatabaseTags.sql`'s recursive CTE errors on a 4000-character tag string: `Tags + ';'` caps at 4000 and drops the sentinel, leaving `LEFT(..., -1)`. Fails exactly on the rows tag-capacity-expansion makes possible. | **ACCEPT** — `CAST(... AS NVARCHAR(MAX))` before concatenation. |
| L-2 | `NormalizeDatabaseTags.sql` re-runs a row-by-row cursor over the whole table on every publish despite being a one-time normalisation. | **ACCEPT** — pre-filtered to rows that could possibly need work. |
| L-3 | Server tag UI labels still read "Application Tags" / "Server Applications" while the database side reads "Tags" — the user-visible half of the concept this work set out to unify. | **ACCEPT** — all four now read "Tags". |
| L-4 | `ApiCallerEndpointRouteTests` guards paths but not query-key names, the half that fails as a 400 rather than a 404. | **ACCEPT (documented, not closed)** — the keys are string literals at each call site and cannot be enumerated by a test; closing it properly means giving `ApiCaller` a typed request surface. Stated in the test's docstring as a known limit. |
| L-5 | `RefDataDatabases.feature` posts `{"DatabaseName", "DatabaseType", "DbServerName", "DbCluster"}`, none of which has ever matched `DatabaseApiModel`. | **DEFER** — pre-existing dead payload that never bound to anything; out of scope per CLAUDE.md's diff-only rule. Worth a separate fix. |
| L-6 | `EnvironmentDatabase_Environment_EnvID_fk` and `EnvironmentServer_Environment_Id_fk` keep the legacy suffix naming beside the FKs just renamed. | **REJECT** — they target `Environment`, which this work does not touch. Correctly scoped out; belongs with whoever migrates `Environment`. |
| L-7 | `docs/database-tags/` and `docs/tag-capacity-expansion/` quote column names this change removed. | **ACCEPT** — superseded-by banners added. They are historical decision records and their bodies are left intact. |
| L-8 | `RefDataEnvironmentsUsersController`'s `<param name="type">"Endur" db type</param>` uses tag vocabulary. | **REJECT** — that parameter is a `UserAccountType` enum, not a tag. The doc comment is misleading, but it is pre-existing and unrelated. |
| L-9 | `deploy.Database.Name` / `deploy.Server.Name` stay nullable while comparable deploy tables have `Name NOT NULL`, and the HLPS never said so. | **ACCEPT as scope decision** — recorded as HLPS **U-9**. Tightening it needs a data gate and backfill, which is a behaviour change on top of a migration whose value is that it changes none. Deliberately deferred rather than silently omitted. |

---

## Corrections to reviewer claims

Two reviewer assertions did not survive checking and were **not** actioned:

- One reviewer premised a finding on "most deploy tables have `ObjectId`". A count
  shows 4 of ~29 do, all security-scoped; the flat `deploy/Tables/` folder these two
  join uses none. The reviewer checked this itself and withdrew the point.
- One reviewer flagged the EF/SSDT width mismatches (`HasMaxLength(32)` vs
  `NVARCHAR(250)`) as possibly introduced here. `git show` on the base commit confirms
  they predate the change. Left alone, and now named explicitly in the mapping test's
  docstring so the next reader does not have to re-derive it.

## Round outcome

All CRITICAL and HIGH findings closed. Of the MEDIUMs, M-3 is open by necessity
(U-8 — needs the production publish profile, which is not in this repository) and
L-4 is documented rather than closed. Nothing accepted remains unimplemented.

Re-verification after remediation: upgrade / redeploy / fresh-database scripts all
regenerated and re-read; `Dorc.Core.Tests` 155, `Dorc.Api.Tests` 270,
`Dorc.Monitor.Tests` 77, `dorc-web` 137, `tsc` clean, sqlproj builds in place.
