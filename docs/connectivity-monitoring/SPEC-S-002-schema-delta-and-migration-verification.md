---
name: SPEC-S-002 — Schema delta + migration verification
description: JIT Specification for S-002 — adds DATABASE.CreateDate (the #593 piggyback) on top of the connectivity columns already on the post-merge PR #374 tip, then dry-deploys the cumulative SSDT delta against a production-shape DB to resolve HLPS U-7.
type: spec
status: IN REVIEW
---

# SPEC-S-002 — Schema delta + migration verification

| Field | Value |
|---|---|
| **Status** | IN REVIEW (R2) |
| **Step** | S-002 |
| **Author** | Agent |
| **Date** | 2026-04-28 |
| **HLPS** | `docs/connectivity-monitoring/HLPS-connectivity-monitoring.md` (APPROVED) |
| **IS** | `docs/connectivity-monitoring/IS-connectivity-monitoring.md` (APPROVED) |
| **Governing decision** | `docs/connectivity-monitoring/DECISION-S-001-drift.md` (APPROVED — Pending user approval) |
| **Folder** | `docs/connectivity-monitoring/` |
| **Branch** | `copilot/create-server-db-existence-check` (PR #374's source branch on `sefe/dorc`); current tip `7bfee34e1b4273a9455e1711e3a60ac2358cf0de` |

---

## 1. Context

### What this step addresses

S-002 lands the **schema half** of the connectivity-monitoring contract (HLPS SC-1) and the schema half of the #593 DB-creation-date piggyback (HLPS SC-10 / SC-11):

- The six connectivity columns (`LastChecked`, `IsReachable`, `UnreachableSince` on each of `SERVER` and `DATABASE`) **are already present** in the post-merge tip `7bfee34e`. Per DECISION-S-001-drift.md §3 Q5, they are structurally additive against `main` with no obstruction and no intervening edits to either table. S-002 does **not** re-add them.
- The seventh column — `DATABASE.CreateDate` (`DATETIME2 NULL`) — is **not** in PR #374's diff and is the only schema change S-002 lands. It is the substrate for the read/API surface that S-008 and S-009 will populate. Landing it in S-002 keeps all schema work atomic per IS S-002 ("added in this same schema step to keep all schema work atomic").

The step also performs the **dry-deploy verification** that resolves HLPS U-7: confirm that the SSDT publish applies the cumulative delta cleanly against a non-production DB snapshotted from production-shape state, with no data fix-up, no `BlockOnPossibleDataLoss` triggers, and no regression on existing reads.

### Scope

- **One SSDT table-source edit**: `src/Dorc.Database/dbo/Tables/DATABASE.sql` — add the `CreateDate DATETIME2 NULL` column.
- **No `<Build Include>` change**: the file is already listed in `src/Dorc.Database/Dorc.Database.sqlproj` (verified — it was in the project before PR #374 and PR #374 only edited the file content).
- **No pre-deploy / post-deploy script** required: the seven additions are all NULLable additive columns; SSDT's schema phase issues the `ALTER TABLE … ADD …` automatically and does not need data-fixup scripting (cf. `BlockOnPossibleDataLoss=True` default tolerance for nullable adds).
- **Documentation deliverable**: the JIT spec records, and the executing agent appends to, a dry-deploy run-log section that captures the publish output, the schema-compare delta against the snapshot, and a sample `SELECT TOP 10 * FROM dbo.DATABASE` against the post-publish DB confirming existing reads still return expected results.

Out of scope:
- C# `Database` model changes (S-008 owns adding `CreateDate` to the EF entity).
- Any persistence-source query or write-path changes (S-004 / S-008).
- Any API model, controller, or UI surface changes (S-006 / S-007 / S-009).
- The connectivity columns themselves (already present at `7bfee34e`).
- The `SERVER.CreateDate` symmetry — HLPS SC-10 / SC-11 are scoped to `DATABASE` only (per HLPS §3 ring-fence and IS coverage map: SC-10 → S-008, SC-11 → S-009, both database-only).

### Governing constraints

- **HLPS SC-1**: schema delta lands cleanly. The delta in scope is `DATABASE.CreateDate` plus the inherited 6 connectivity columns from PR #374.
- **HLPS U-7** (target resolution: S-002 dry-deploy): SQLProj-generated migration applies cleanly against a current production-shape DB snapshot — no data loss, no manual DBA steps, no re-publish loops. U-7 is **not** discharged by spec drafting; it is discharged by the §3 verification protocol succeeding.
- **HLPS §4 (additive only)**: every added column is nullable. No NOT NULL columns; no defaults that would back-fill data; no constraint additions to existing rows.
- **CLAUDE.md naming**: column name `CreateDate` is descriptive, PascalCase, no grab-bag suffix.

---

## 2. Production code change

### 2.1 `dbo.DATABASE` — add `CreateDate`

**Target**: edit `src/Dorc.Database/dbo/Tables/DATABASE.sql`. The canonical pre-edit shape is the file as it exists at PR #374's branch tip `7bfee34e1b4273a9455e1711e3a60ac2358cf0de`; the implementer should `git show 7bfee34e:src/Dorc.Database/dbo/Tables/DATABASE.sql` to see the authoritative starting state. The §10 Pre-execution self-audit requires confirming the working tip has not advanced past `7bfee34e` in a way that touches this file before the edit lands.

**Shape after the edit** (in plain-language; not a copy-paste-ready DDL block per CLAUDE.local.md JIT-spec abstraction rules):
- All existing columns and constraints/indexes remain byte-identical to the file at `7bfee34e`. The implementer makes **no** edit to any pre-existing line.
- Append one new column `CreateDate` of type `DATETIME2`, NULLable, immediately after the existing `UnreachableSince` column, before the table-level `CONSTRAINT` lines. Placement is for diff hygiene; it does not affect behaviour.

**Behavioural notes**:
- HLPS U-9 (resolved 2026-04-28 — treat as a value update): the column will hold the catalog-creation date as observed by the connectivity check. On overwrite, EF / S-008 will emit an Information-level log entry naming old/new value (per HLPS SC-10). S-002 does not implement that behaviour — it only provisions storage.
- Default value: **none**. Existing rows on first publish will have `CreateDate = NULL` until S-008's first connectivity cycle populates them. The cold-start "Not checked" UX is documented in HLPS U-10.

### 2.2 SQLProj inclusion

No edit. `src/Dorc.Database/dbo/Tables/DATABASE.sql` is already a `<Build Include>` in `Dorc.Database.sqlproj` (verifiable via `grep "DATABASE.sql" src/Dorc.Database/Dorc.Database.sqlproj`). The edit above is to file content only.

### 2.3 Pre-deploy / post-deploy script delta — none in S-002, but the cumulative DACPAC carries existing scripts

**S-002 itself adds no Pre-Deployment or Post-Deployment script.** The S-002 source change is column-only.

However, the **cumulative DACPAC** that the operator publishes against the production-shape snapshot in §3 carries everything currently in tree at the post-merge tip `7bfee34e`, including:

- `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql` (PR #651 daemons-modernisation — stages legacy `dbo.SERVICE` rows before the schema phase drops the table).
- `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` (PR #651 daemons-modernisation — copies staged rows into `deploy.Daemon` after the schema phase).
- `src/Dorc.Database/Scripts/Post-Deployment/SeedRefDataAuditActions.sql` (PR #651 audit-action seed).
- `src/Dorc.Database/Scripts/Post-Deployment/CleanupOrphanedScripts.sql` (long-standing housekeeping).
- `src/Dorc.Database/Scripts/Pre-Deployment/AddCancelledFieldsToArchiveDeploymentRequests.sql` (long-standing).

These scripts are **already on `main`** (PR #651 merged 2026-04-24). If the operator's production-shape snapshot was taken **before** that merge, the publish will execute the daemons rename + data migration alongside the S-002 column add — which is in scope for U-7 (clean apply, no manual DBA steps). If the snapshot was taken **after**, the daemons scripts re-run idempotently (each is guarded by `IF OBJECT_ID … IS NOT NULL` per the daemons SPEC-S-002).

**The §3 verification protocol must therefore record the snapshot's state relative to the PR #651 merge as part of evidence collection** — see §3.2 step 0. The schema-action set the operator should expect on a pre-#651 snapshot is wider than the seven column adds; on a post-#651 snapshot it should be exactly the seven column adds.

DOrc does NOT pre-stage SQL `ALTER` scripts for additive nullable column changes; SSDT issues them automatically during the schema phase via DACPAC. No additional script work is required from S-002 to deliver SC-1.

---

## 3. Verification plan

The verification is split between **agent-runnable build checks** (§3.1) and an **operator-run dry-deploy** (§3.2). The operator owns §3.2 because the agent has no non-production DB access; the agent's role on §3.2 is to capture the operator's reported evidence into the §6 run-log.

The previous draft separated an "offline schema-compare" check into §3.2 — that has been merged into §3.2 below because generating an offline publish script still requires a DB-extracted DACPAC (`sqlpackage /Action:Extract`), and the operator running that extraction is the same operator who will publish in §3.2; folding them avoids artifact duplication.

### 3.1 Agent-runnable build verification

Performed by the agent on the local checkout immediately after the §2.1 source edit:

- `dotnet build src/Dorc.Database/Dorc.Database.sqlproj` succeeds with no warnings or errors. (Captured: stdout / stderr summary.)
- `dotnet build` of the whole solution succeeds (the SSDT edit must not break C# compilation).
- After commit and push, CI on the new branch tip completes green — measured against the all-green baseline at `7bfee34e` recorded in DECISION-S-001 §8 (Test Results, CodeQL, build ×2, Analyze csharp ×2, Analyze js-ts ×2, Analyze actions, Aikido, Dependabot — 11/11 success). The acceptance bar is "no NEW failures vs that baseline".

### 3.2 Operator-run dry-deploy (resolves U-7)

Performed by the operator on a non-production DOrc instance whose database has been restored from a recent production backup or otherwise snapshotted to production-shape state. The exact instance is the operator's choice — the spec does not pin a specific environment.

**Publish profile**: there is no `*.publish.xml` profile committed to `src/Dorc.Database/`. The operator must publish with these settings (the SSDT/sqlpackage defaults match all four; record any deviation in §6):

- `BlockOnPossibleDataLoss=True`
- `DropObjectsNotInSource=False`
- `AllowIncompatiblePlatform=False`
- `AllowDropBlockingAssemblies=False`

If the operator's pipeline / IDE setup deviates from any of these, the run-log must capture the actual values.

**Procedure:**

0. **Snapshot lineage**: record whether the snapshot was taken **before** or **after** PR #651 merged into main (`f70404e6`, 2026-04-24). On a pre-#651 snapshot the daemons-rename pre/post-deploy scripts will execute alongside the S-002 column adds — that is in scope for U-7. On a post-#651 snapshot the daemons scripts re-run idempotently and the publish should issue only the seven column adds.

1. **Capture pre-state**: record:
   - `SELECT COUNT(*) AS RowCount, MAX(DB_ID) AS MaxId FROM dbo.DATABASE;`
   - `SELECT COUNT(*) AS RowCount, MAX(Server_ID) AS MaxId FROM dbo.SERVER;`
   - `SELECT TOP 10 * FROM dbo.DATABASE ORDER BY DB_ID;` (verbatim — paste into §6)
   - `SELECT TOP 10 * FROM dbo.SERVER ORDER BY Server_ID;` (verbatim — paste into §6)

2. **Publish the SSDT project** against the snapshot DB using `sqlpackage /Action:Publish` (or VS / pipeline equivalent) with the publish-profile settings above. Record the full sqlpackage command line (or the IDE's publish summary equivalent) into §6.

3. **Confirm clean apply**: the publish completes without errors, without `BlockOnPossibleDataLoss` triggering, and without the operator needing to hand-run any fix-up script. The publish log is captured **verbatim** in §6 (truncating only the row-by-row "Update complete." chatter).

4. **Verify the schema-action set against the expected list**. The seven additive operations expected are (tick each in §6):

   | # | Operation | Expected on snapshot lineage |
   |---|---|---|
   | 1 | `ALTER TABLE [dbo].[SERVER] ADD [LastChecked] DATETIME2 NULL` | always (production tip is pre-PR-#374) |
   | 2 | `ALTER TABLE [dbo].[SERVER] ADD [IsReachable] BIT NULL` | always |
   | 3 | `ALTER TABLE [dbo].[SERVER] ADD [UnreachableSince] DATETIME2 NULL` | always |
   | 4 | `ALTER TABLE [dbo].[DATABASE] ADD [LastChecked] DATETIME2 NULL` | always |
   | 5 | `ALTER TABLE [dbo].[DATABASE] ADD [IsReachable] BIT NULL` | always |
   | 6 | `ALTER TABLE [dbo].[DATABASE] ADD [UnreachableSince] DATETIME2 NULL` | always |
   | 7 | `ALTER TABLE [dbo].[DATABASE] ADD [CreateDate] DATETIME2 NULL` | always — the S-002 add |

   On a pre-PR-#651 snapshot the publish will additionally execute the daemons rename DDL + the StageServicesForMigration / MigrateStagedServicesToDaemons scripts; those are noted in §6 but do not count as findings.

   Other categories of operation that are **acceptable bookkeeping** and recorded but not findings:
   - SSDT-emitted `sp_refreshsqlmodule` / `sp_refreshview` calls.
   - Idempotency-guarded `:r` script execution lines.
   - DACPAC version metadata writes to `[dbo].[__RefactorLog]` or equivalent.

   Any operation outside the seven expected adds + the bookkeeping allow-list + the (conditional) daemons scripts is a **finding** that must be triaged before the run-log records PASS.

5. **Capture post-state**: re-run the §3.2 step 1 queries. Acceptance:
   - Both `COUNT(*)` and `MAX(*Id)` values are byte-identical to pre-state.
   - The seven new columns appear in the post-state `SELECT TOP 10` output as `NULL` for every existing row (no back-fill).
   - All pre-existing column values in the sample rows are byte-identical to the pre-state output.

6. **Existing-reads regression check** (UI-side, no Status column expected at S-002 time — that lands in S-007):
   - Servers page (`/servers`) loads without error and displays a row count consistent with pre-state `SERVER` `COUNT(*)`.
   - Databases page (`/databases`) loads without error and displays a row count consistent with pre-state `DATABASE` `COUNT(*)`.
   - At least one named server and one named database that existed pre-publish are present in the page output with their pre-publish display fields unchanged.

7. **Record evidence in §6**: the operator (or the agent on the operator's reported evidence) populates every slot in the §6 run-log template. PASS may not be recorded in the Outcome line until every numbered slot is non-empty.

If the dry-deploy reveals a regression — e.g. publish errors, row-count delta, sample-row delta, schema-compare findings outside the allow-list, UI breakage — the run-log records the specific failure and S-002 returns to REVISION until the cause is found and the SPEC amended. Possible causes the JIT-spec author has considered:
- Production-shape DB has an unexpected non-trivial diff vs the DACPAC (some object out of sync that nobody knew about). Resolution: rebase the SSDT project to re-include the missing object, or scope the snapshot more carefully.
- A check constraint on `dbo.DATABASE` rejects the `ALTER TABLE … ADD CreateDate` (e.g. a `CHECK` that requires `CreateDate IS NOT NULL` on all existing rows). The publish would fail on the schema phase with a constraint violation. Resolution: scoped to S-002 only if the constraint was ours to add; otherwise escalate. DECISION-S-001 §3 Q5 inspected the `dbo.DATABASE` table DDL and surfaced no constraints that would reject the add; if schema-compare reveals one it is a finding.

---

## 4. Branch and commit strategy

- **Branch**: continue on PR #374's source branch `copilot/create-server-db-existence-check` (currently at `7bfee34e`). No new branch — per user directive 2026-04-28 ("all changes should be done on the existing PR") and DECISION-S-001 §2 (chosen route = revise on the same PR).
- **Commit 1 (agent, source edit)**: one focused commit for the schema edit. Suggested commit message form: `db(connectivity-monitoring): add DATABASE.CreateDate column for #593 piggyback (S-002)`. The Co-Authored-By trailer is required per project convention.
- **Commit 2 (run-log evidence)**: appends the §6 run-log block to this spec and updates the HLPS Unknowns Register addendum to mark U-7 as `Verified by S-002 (DECISION-S-002 run-log; commit <SHA>)`. This commit may be authored by **either the operator or the agent** — whoever has the evidence in hand. If the operator authors it, they push directly to `copilot/create-server-db-existence-check`; if the agent authors it, the operator hands the agent the run-log content first. Suggested commit message: `docs(connectivity-monitoring): S-002 dry-deploy run-log (U-7 resolved)`.
- **If the branch tip has advanced** between Commit 1 and Commit 2 (e.g. R3+ adversarial fixes on adjacent files have landed in the interim), Commit 2 is appended on top of the new tip — no rebase or force-push is required because §6 is a docs-only addendum at the bottom of the spec file. Conflicts on §6 are not expected (no other step writes to that section) but if they occur the resolution is mechanical (preserve both edits).
- **No squash, no force-push** — the PR #374 branch is the source of truth for the route.
- **No push to `main`** — per user directive 2026-04-28 nothing lands on main outside the standard merge.

---

## 5. Acceptance criteria

A1. The file `src/Dorc.Database/dbo/Tables/DATABASE.sql` on PR #374's branch contains a tenth column `CreateDate DATETIME2 NULL` adjacent to the existing connectivity columns. The other nine columns and all constraints / indexes are unchanged.

A2. `dotnet build src/Dorc.Database/Dorc.Database.sqlproj` succeeds locally with no warnings or errors. The full-solution build, run as part of CI on the post-edit tip, completes green (matching the §8 CI baseline already established in DECISION-S-001).

A3. The offline schema-compare check (§3.2) shows exactly the seven expected column adds and no other structural diffs. If incidental DACPAC-bookkeeping operations (e.g. metadata refresh statements) appear, they are recorded but do not block acceptance.

A4. The dry-deploy verification (§3.3) is performed by the operator and the run-log appended to §6 below. The publish completes cleanly; row counts and sample rows are unchanged; existing reads regress nothing. **U-7 is then explicitly marked as resolved in the HLPS Unknowns Register addendum** (a one-line note pointing to this spec's run-log; the agent makes the edit when the operator reports the result).

A5. No source change outside `src/Dorc.Database/dbo/Tables/DATABASE.sql` and the run-log entry in this spec. No EF mapping, no model property, no API surface, no UI change in this commit.

A6. The spec passes Adversarial Review per CLAUDE.local.md §4 with unanimous approval before execution begins (per Pre-Execution Self-Audit E in the Delivery Loop).

---

## 6. Dry-deploy run-log (to be appended on execution)

Reserved section — populated by the operator and/or the agent after the dry-deploy completes per §3.2. The Outcome line at the bottom may not be `PASS` until **every numbered slot below has a non-empty value**. The template:

```
Date:                                 YYYY-MM-DD
Operator:                             <name>
Branch tip SHA at publish:            <full SHA the operator extracted the DACPAC from>
Snapshot source:                      <which prod-like DB; e.g. "GMDV-DORDB02 backup of 2026-04-25">
Snapshot lineage vs PR #651 merge:    BEFORE / AFTER / UNKNOWN — affects which scripts run (per §3.2 step 0)
SSDT publish target:                  <which non-prod DOrc DB>

Publish profile values used:
  BlockOnPossibleDataLoss:            True (or note actual)
  DropObjectsNotInSource:              False (or note actual)
  AllowIncompatiblePlatform:           False (or note actual)
  AllowDropBlockingAssemblies:         False (or note actual)

Publish command (verbatim):           <sqlpackage CLI / VS publish summary / pipeline log link>

Build-verification result (§3.1):     PASS / FAIL — dotnet build sqlproj + solution
CI-on-tip result (§3.1):              PASS / FAIL — link to GitHub Actions run

Pre-state queries (paste verbatim):
  dbo.SERVER:    RowCount=...   MaxId=...
  dbo.DATABASE:  RowCount=...   MaxId=...
  dbo.SERVER TOP 10:
    <paste>
  dbo.DATABASE TOP 10:
    <paste>

Publish log (verbatim, drop only "Update complete." chatter):
  <paste>

Schema-action checklist (tick each that the publish issued):
  [ ] 1. ALTER TABLE [dbo].[SERVER]   ADD [LastChecked]      DATETIME2 NULL
  [ ] 2. ALTER TABLE [dbo].[SERVER]   ADD [IsReachable]      BIT       NULL
  [ ] 3. ALTER TABLE [dbo].[SERVER]   ADD [UnreachableSince] DATETIME2 NULL
  [ ] 4. ALTER TABLE [dbo].[DATABASE] ADD [LastChecked]      DATETIME2 NULL
  [ ] 5. ALTER TABLE [dbo].[DATABASE] ADD [IsReachable]      BIT       NULL
  [ ] 6. ALTER TABLE [dbo].[DATABASE] ADD [UnreachableSince] DATETIME2 NULL
  [ ] 7. ALTER TABLE [dbo].[DATABASE] ADD [CreateDate]       DATETIME2 NULL

Daemons rename DDL + scripts (only if snapshot lineage = BEFORE):
  Observed?                           YES / NO / N/A — list the additional ops if YES

Operations outside the seven adds + bookkeeping allow-list + (conditional) daemons scripts:
  <list any operation that doesn't fit; empty list = no findings>

Post-state queries (paste verbatim):
  dbo.SERVER:    RowCount=...   MaxId=...
  dbo.DATABASE:  RowCount=...   MaxId=...
  dbo.SERVER TOP 10:
    <paste>
  dbo.DATABASE TOP 10:
    <paste>

Pre/post identity check:
  Row counts identical:               PASS / FAIL
  MAX-ID identical:                   PASS / FAIL
  Pre-existing column values byte-identical in TOP 10 sample: PASS / FAIL
  New columns NULL in TOP 10 sample:  PASS / FAIL

UI regression checks (Servers and Databases pages, separately):
  /servers loads:                     PASS / FAIL
  /servers row count visible matches dbo.SERVER COUNT(*): PASS / FAIL
  Named server <name> present unchanged: PASS / FAIL — record name
  /databases loads:                   PASS / FAIL
  /databases row count visible matches dbo.DATABASE COUNT(*): PASS / FAIL
  Named database <name> present unchanged: PASS / FAIL — record name

U-7 addendum commit SHA (per A4): <SHA of the HLPS-addendum commit>

Outcome:                              PASS (U-7 resolved) / FAIL (cause + remediation)
```

---

## 7. Out of scope

- Adding `CreateDate` to the EF `Database` entity — that is S-008.
- Surfacing `CreateDate` in `DatabaseApiModel` — that is S-009.
- Surfacing `CreateDate` in the UI Databases page — that is S-009.
- Adding `CreateDate` to `dbo.SERVER` — out of scope per HLPS ring-fence (HLPS SC-10 / SC-11 are DB-only).
- Connectivity column re-verification beyond the dry-deploy — S-005 / S-011 own runtime verification of the connectivity feature.

---

## 8. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| The agent's source edit is correct but the operator cannot perform the dry-deploy promptly, blocking S-002 acceptance. | The source edit (Commit 1) can be committed and pushed independently; dry-deploy run-log (Commit 2) is a follow-up. CHECKPOINT-3 (per-step user sign-off) absorbs the wait. **Per IS §2 dependencies**: S-004 / S-005 / S-006 / S-007 / S-008 / S-009 / S-010 / S-011 all depend (transitively or directly) on S-002 being fully verified — only **S-003** is genuinely parallelisable while S-002 is in flight, because S-003 is pure-logic with no schema or persistence touch points. Approving S-002 partially (source-edit only, run-log pending) does **not** unblock the schema-dependent steps; the user may authorise S-003 to begin in parallel under separate JIT spec. |
| Production-shape DB has a CreateDate column already (perhaps added out-of-band years ago and never seen by SSDT). | The §3.2 step 4 schema-action checklist catches this — operation 7 would not appear on the publish list. If absent, the operator records the finding and the SPEC author resolves before publish (likely by adding the column to the SSDT source with the existing definition to neutralise the diff). |
| The SSDT publish fails on production-shape but passes on the operator's lightweight scratch DB. | The §3.2 protocol mandates publishing against a snapshot derived from production state, not a scratch DB. The §6 run-log captures the snapshot source (and lineage relative to PR #651) so a reviewer can verify the target was production-shape. |
| Dependabot or Aikido security checks flag the new column for an unrelated reason (false positive). | CI run on the new tip will reveal — if a green-baseline regression appears, S-002 returns to REVISION. The DECISION-S-001 baseline establishes 11/11 green at `7bfee34e`. |
| The §3.2 step 4 schema-action checklist allows "bookkeeping" operations as non-findings. The operator could mis-classify a real schema change as bookkeeping. | The allow-list in §3.2 step 4 is enumerated (`sp_refreshsqlmodule` / `sp_refreshview`, `:r` script execution lines, `__RefactorLog` writes). Anything outside the allow-list is a finding by default. The Adversarial Review gate (A6) is the second protection — reviewer must inspect the populated checklist before approving. |
| Snapshot lineage relative to PR #651 is unknown and the operator can't determine it. | §3.2 step 0 records `BEFORE / AFTER / UNKNOWN`. If `UNKNOWN`, the publish behaviour is still observable — the operator records what scripts actually executed and the schema-action checklist still binds. The spec author treats `UNKNOWN` lineage with `[ ]` ticked on items 1–7 and "no daemons scripts observed" as evidence the snapshot was effectively post-PR-#651. |

---

## 9. Adversarial Review

S-002 is reviewed by an Adversarial panel of 2-3 reviewers per CLAUDE.local.md §4. Suggested lenses:

- **Clarity / completeness**: is the source edit unambiguous? Are the §3 verifications enumerable and binary (pass/fail)? Is the run-log template structured enough to be filled in mechanically?
- **Risk / feasibility**: is the dry-deploy plausible to execute without DB / pipeline access the agent doesn't have? Are §8 risks well-captured? Is the operator handover (§3.3 step 6) realistic?
- **Evidence rigour**: are the schema-compare and dry-deploy evidence types specific enough that a reviewer can determine PASS vs FAIL on the run-log alone?

Reviewers must NOT evaluate pseudocode for syntactic correctness (the spec uses plain-language column descriptions per CLAUDE.local.md JIT-spec abstraction rules). Reviewers must evaluate whether the spec, executed as written, would produce the SC-1 / U-7 outcomes — not whether the executing implementer would have made different stylistic choices.

Findings follow the same triage rules as other artifacts (Accept / Downgrade / Defer / Reject; cycle limit 3 rounds).

---

## 10. Pre-execution self-audit (to be confirmed before code edit)

- [ ] HLPS status APPROVED + user-approved
- [ ] IS status APPROVED + user-approved
- [ ] DECISION-S-001 status APPROVED + user-approved
- [ ] This SPEC status APPROVED + user-approved
- [ ] PR #374 branch tip is `7bfee34e` (or the agent records the new SHA in §6 and verifies items below against that SHA instead)
- [ ] No edits to `src/Dorc.Database/dbo/Tables/DATABASE.sql` between `7bfee34e` and the current tip (verify via `git log 7bfee34e..HEAD -- src/Dorc.Database/dbo/Tables/DATABASE.sql` returning empty)
- [ ] No edits to `src/Dorc.Database/dbo/Tables/SERVER.sql` between `7bfee34e` and the current tip (same pattern; the connectivity columns must still be present)
- [ ] The six connectivity columns are still present at the working tip (`git show <tip>:src/Dorc.Database/dbo/Tables/SERVER.sql | grep -c "LastChecked\|IsReachable\|UnreachableSince"` returns 3; same for `DATABASE.sql` returns 3)
- [ ] CI on the current PR #374 branch tip is green (re-fetch via `gh api` if more than a few hours have elapsed since DECISION-S-001 §8 baseline of 11/11 green)
- [ ] No in-flight adversarial reviews on connectivity-monitoring artifacts
- [ ] Working tree clean (no uncommitted tracked changes that would conflate with the §2.1 edit)

If any item is unchecked, **STOP** and address it.

---

## 11. Review History

### R1 — DRAFT → REVISION

R1 conducted by three reviewers in parallel (clarity/completeness, risk/feasibility, evidence rigour). All three returned `APPROVE_WITH_FIXES`. Combined triage:

| Theme | Reviewers | Severity | Disposition | Resolution |
|---|---|---|---|---|
| Cumulative DACPAC carries pre/post-deploy scripts from PR #651 daemons-modernisation; spec's "only ALTER TABLE … ADD" claim was incomplete | B (F-B1) | HIGH | Accept | §2.3 rewritten to enumerate the in-tree pre/post-deploy scripts and explain the snapshot-lineage dependency. §3.2 step 0 added to record snapshot lineage. §6 template gained a snapshot-lineage slot and a "daemons rename DDL + scripts (only if snapshot lineage = BEFORE)" slot. |
| §6 run-log template missing slots required to discharge A4 | C (F-E1) | HIGH | Accept | §6 expanded substantially: pre/post MAX-IDs, seven-row schema-action checklist, all four publish-profile values, separate Servers/Databases page checks, build-verification PASS/FAIL, CI-on-tip PASS/FAIL, publish command verbatim, sample rows pre/post (verbatim), U-7 addendum commit SHA, evidence-binding rule that PASS may not be set until every slot is non-empty. |
| §8 row 1 blocking analysis was wrong against IS coverage map (claimed S-005/S-006/S-007/S-010 unblocked by partial S-002 — they are not) | B (F-B5) | MEDIUM | Accept | §8 row 1 rewritten with the IS-derived list: only S-003 is genuinely parallelisable while S-002 is in flight (S-003 is pure-logic). All other downstream steps depend on S-002 being fully verified. |
| §2.1 column enumeration risked drifting from the file at the SHA | A (F-A1) | MEDIUM | Accept | §2.1 reworded to anchor on `git show 7bfee34e:src/Dorc.Database/dbo/Tables/DATABASE.sql` as canonical pre-edit shape; the spec no longer enumerates the existing columns inline. |
| §3.2 (offline schema-compare) had a "soft incidental" predicate | A (F-A2) | MEDIUM | Accept | §3.2 (now §3.2 step 4) replaced the soft predicate with an explicit allow-list of bookkeeping ops; anything outside the allow-list is a finding. |
| §3.3 step 5 "page load OK" not enumerable | A (F-A3) | MEDIUM | Accept | Step now reframed (now §3.2 step 6): explicit row-count match + named-row-present check, separately for Servers and Databases pages. |
| Publish profile unspecified — "default" is not a repo artifact | B (F-B2) | MEDIUM | Accept | §3.2 enumerates the four required publish-profile values (`BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False`, `AllowIncompatiblePlatform=False`, `AllowDropBlockingAssemblies=False`); §6 template captures actual values. |
| Operator handover §3.3 step 6 lacked evidence-format precision | B (F-B3) | MEDIUM | Accept | §3.2 now requires verbatim publish log, verbatim TOP 10 sample-row pastes; §6 template enforces verbatim slots. |
| §4 split-commit didn't survive operator latency / self-commit preference | B (F-B4) | MEDIUM | Accept | §4 reworded: Commit 2 may be authored by operator OR agent; if branch tip advances, Commit 2 is a docs-only append on the new tip with no rebase needed. |
| §3.2 offline schema-compare required a snapshot DACPAC the spec didn't say how to obtain | B (F-B6) | MEDIUM | Accept | Old §3.2 collapsed into §3.2 (renumbered): single online dry-deploy path; the schema-action checklist is captured during the publish itself, no separate offline DACPAC required. |
| U-7 framing prematurely said "resolved in S-002" | C (F-C1) | MEDIUM | Accept | §1 governing constraints reworded to "U-7 (target resolution: S-002 dry-deploy)"; "U-7 is not discharged by spec drafting; it is discharged by the §3 verification protocol succeeding." |
| U-9 resolution date was 2026-04-27; HLPS shows 2026-04-28 | C (F-C2) | MEDIUM | Accept | Date corrected in §2.1. |
| §6 template "Sample row diff" too small | C (F-E2) | MEDIUM | Accept | Template now has separate verbatim pre-state and post-state TOP 10 paste slots plus an explicit identity-check block. |
| §3.3 step 5 referenced a "Status" column that doesn't ship until S-007 | C (F-C3) | LOW | Accept | Step (now §3.2 step 6) reframed to drop the Status-column assertion; UI checks are page-load + row-count + named-row-present only at S-002 time. |
| §3.3 trigger example was implausible (triggers fire on INSERT/UPDATE, not ALTER ADD) | B (F-B7), C (F-C4) | LOW | Accept | Replaced with check-constraint example which is the actual risk space. Negative claim about Q5 reworded to be specific ("inspected the table DDL and surfaced no constraints"). |
| §10 self-audit missing items | A (F-A5) | LOW | Accept | Added items: no edits between `7bfee34e` and current tip on either table; six connectivity columns still present at the working tip; working tree clean. |
| §3.3 / §4 split-commit handover not explicit | A (F-A6) | LOW | Accept | Subsumed by F-B4 resolution. |
| §3.2 agent-runnable vs operator-only ambiguity | A (F-A7) | LOW | Accept | Subsumed by F-B6 resolution (collapsed into single operator-run §3.2). |
| §6 PASS/FAIL outcome lacked evidence-binding | C (F-E3) | LOW | Accept | §6 now has explicit "PASS may not be recorded until every numbered slot is non-empty" rule; U-7 addendum commit SHA slot added. |
| §6 run-log template under-structured for fill-in | A (F-A4) | LOW | Accept | Subsumed by F-E1 resolution (template substantially expanded). |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers must verify R1 fixes, check for regressions, and (per CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on R1 text that was implicitly accepted.

### R2 — IN REVIEW → (pending)

(R2 to be added after resubmission)
