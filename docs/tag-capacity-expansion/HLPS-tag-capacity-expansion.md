# HLPS: Server & Database Tag Capacity Expansion

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | DRAFT — awaiting adversarial panel review |
| **Author**  | Agent                                    |
| **Date**    | 2026-07-17                               |
| **Folder**  | docs/tag-capacity-expansion/             |
| **Branch**  | claude/tag-capacity-expansion            |
| **Origin**  | Split out of `docs/env-details-component-tabs/` by user direction; carry-over findings below were produced and verified by that feature's adversarial review panel (`REVIEW-HLPS-round1.md` on branch `claude/env-details-component-tabs-p7othg`, PR #773) |

---

## 1. Problem Statement

Tags are used far more heavily than the current limits allow ("use in a lot more anger").
Server tags live in `ApplicationTags` (semicolon-separated, edited via
`components/server-tags.ts` + `tags-input.ts`, parsed by `helpers/tag-parser.ts`); the
database equivalent is the `ArrayName` field (a plain text input today).

The layers **disagree with each other**, so the effective limit is the lowest layer and
overruns fail late (silent truncation or an opaque DB exception) instead of clearly at
the API boundary:

| Layer | Server `ApplicationTags` | Database `ArrayName` |
|-------|--------------------------|----------------------|
| SQL (`Dorc.Database/dbo/Tables/`) | `SERVER.Application_Server_Name NVARCHAR(1000)` | `DATABASE.Array_Name NVARCHAR(250)` |
| Stored proc | `usp_Insert_Server_Detail.sql` param `@APPLICATION_SERVER_NAME NVARCHAR(1000)` | — |
| EF (`EntityTypeConfigurations/`) | `ServerEntityTypeConfiguration.cs` `HasMaxLength(250)` | `DatabaseEntityTypeConfiguration.cs` `HasMaxLength(50)` |
| API validation | none | none |
| UI | `tags-input.ts`: no limit | `add-edit-database.ts`: `maxlength=50` |

Additionally, the new environment-component types delivered in PR #773 (`Container`,
`CloudResource`, `ApiRegistration`) each carry a `Tags NVARCHAR(250)` column sized "to
match current server behaviour, pending the tag-capacity PR" — they should be widened in
the same change.

## 2. Desired Outcome

One generous, **consistent** tag limit enforced at every layer for server tags, database
tags, and the three component-type tags, with overruns rejected as a clear 400 at the API
boundary.

## 3. Carry-over findings (verified by the prior adversarial panel — must be addressed)

1. **HIGH** — `usp_Insert_Server_Detail.sql` declares `@APPLICATION_SERVER_NAME
   NVARCHAR(1000)`: widening the column alone leaves a silent 1000-char truncation path
   through the proc — the exact failure mode this work exists to eliminate.
2. **MED** — `add-edit-database.ts` uses one shared `maxFieldLength = 50` constant across
   **four** fields (DB name, type, instance, Array Name). Naively raising it loosens the
   other three far beyond their EF `HasMaxLength(50)` limits; per-field limits are
   required.
3. **MED** — the prior risk survey found these additional in-repo tag consumers (all
   width-compatible with widening, but each must be re-verified at the chosen limit):
   `ServersPersistentSource.GetAppServerDetails` (`Contains("appserv")` filter),
   `DaemonsPersistentSource` (tag projection), `Tools.PostRestoreEndurCLI/RefreshEndur.cs`
   (tag matching), `VariableScopeOptionsResolver` (tag splitting → deployment variables;
   note PR #773 generalized this into a shared helper).
4. **MED (technical correction)** — do not justify the limit with indexability:
   `NVARCHAR(4000)` exceeds SQL Server's 1700-byte index key limit and the existing
   `LIKE '%…%'` filters can't seek an index anyway. The honest rationale for a sized
   column over `NVARCHAR(MAX)` is avoiding LOB semantics/row-overflow on hot ref-data
   tables.
5. **LOW** — API 400s should come from `[StringLength]` DataAnnotations +
   `[ApiController]` automatic 400 (controllers already carry `[ApiController]`).
6. **LOW** — long-value rendering must be verified in *all* grids showing tags:
   server grids (`page-servers-list.ts`, `attached-servers.ts`) **and** database grids
   (`attached-databases.ts`, `page-databases-list.ts`).
7. **LOW** — longer multi-tag strings amplify substring-match false positives in
   `Contains`-based filters (e.g. `"appserv"`); record as an accepted behaviour or
   tighten matching.
8. **LOW** — `Tests.Acceptance/Features/RefDataServers.feature` exercises
   `ApplicationTags`; new API validation must not break it.

## 4. Proposed scope (strategic)

1. Pick the limit — **recommend `NVARCHAR(4000)`** (rationale per finding 4; `MAX` is the
   alternative if 4000 ever proves tight — decide at checkpoint, see U-1).
2. Apply consistently: SSDT columns (`SERVER.Application_Server_Name`,
   `DATABASE.Array_Name`, three component `Tags` columns), the stored-proc parameter
   (finding 1), EF `HasMaxLength` in all five configurations, `[StringLength]` on the
   five DTO properties, per-field UI `maxlength`s (finding 2).
3. Fail-loudly: API-boundary 400s (finding 5); a boundary test at N accepted / N+1
   rejected.
4. **Optional (U-2)**: give databases the chip-style `tags-input` editing experience that
   servers have (`server-tags.ts` pattern, split/join via `tag-parser.ts`), making
   database tags first-class rather than a plain `ArrayName` text field.

## 5. Unknowns Register

| ID | Unknown | Blocking? | Owner | Proposed resolution |
|----|---------|-----------|-------|---------------------|
| U-1 | Limit value: 4000 vs MAX vs other | **Yes** | User | Recommend 4000 (finding 4 rationale) |
| U-2 | Chip-style tags editor for database `ArrayName` | No (default **yes**) | User may veto | Reuses `tags-input` + `tag-parser`; small scope |
| U-3 | Is `ArrayName` semantically "tags", or does anything treat it as a single array name? | **Yes** | Agent (verify) then User | Survey consumers before re-labelling the field in the UI |
| U-4 | Rebase/merge base | No | Agent | If PR #773 merges first, rebase to pick up the component `Tags` columns; the schema work assumes their presence |

## 6. Process note

This document is a DRAFT: per CLAUDE.md it must pass the adversarial review panel and a
user checkpoint before an Implementation Sequence is drafted. The prior feature's process
artifacts are the reference pattern (`docs/env-details-component-tabs/`).
