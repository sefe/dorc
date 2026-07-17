# HLPS: Database Tags via the DB_Type Column

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | IN REVIEW (v2 — round-1 panel findings applied, see REVIEW-HLPS-round1.md) |
| **Author**  | Agent                                    |
| **Date**    | 2026-07-17                               |
| **Folder**  | docs/database-tags/                      |
| **Branch**  | claude/tag-capacity-expansion (PR #774) — confirm at checkpoint (U-6) |
| **Origin**  | User directive after the ArrayName correction (docs/tag-capacity-expansion/ HLPS v3): "let's fix the database tags too but use the db type column you've identified" |

> **v2 changes** (round-1 panel, 3 reviewers, all REVISE — findings triaged in
> `REVIEW-HLPS-round1.md`): survey completed (site 9 `env-control-center`, site 7's
> `DbName_<type>` half, `DatabasePermissions` projection, RefreshEndur CLI as external
> endpoint consumer); the false "opt-in" backward-compatibility claim replaced with a
> pre-deploy data audit (U-7); SQL trailing-space semantics delta admitted and U-2
> widened to trim **all** rows; SC-1 rescoped honestly to the unit seam with
> `ToQueryString` as the EF-translation evidence form; null contract, `;`-in-tag
> invariant, and write-normalization rules (order/dedup/empty) made explicit.

---

## 1. Problem Statement

The tag-capacity feature's database half was reverted because `Array_Name` turned out
to be storage-array metadata. The user has now designated **`DATABASE.DB_Type`** as the
database tags column. The UI already half-believes this: two chip-rendering components
(`page-databases-list`, `attached-databases` — the latter surfaced on both the
databases page and the environment Databases tab) split `Type` on `;` and render chips
under an "Application Tag" heading. But the rest of the stack treats `Type` as a single
opaque category string:

**Whole-string consumers of `Type` (survey v2, 2026-07-17):**

| # | Site | Match | Execution |
|---|------|-------|-----------|
| 1 | `DatabasesPersistentSource.GetDatabaseByType(EnvironmentApiModel, type)` :157 | `d.Type == type` | EF-translated |
| 2 | `UsersPersistentSource.GetEnvironmentUsers` :61 | `db.Type == "Endur"` | EF-translated |
| 3 | `UserPermsPersistentSource.GetUserDbPermissions` :70 | `r.DbType == dbType` | EF-translated (filter on projection before `ToList`) |
| 4 | `DatabasesPersistentSource.GetDatabaseByType(envName, type)` :119 | `x.Type == type` | in-memory (post-`Single` materialization). Exposed as `RefDataDatabases/ByType`; consumed externally by `Tools.PostRestoreEndurCLI/RefreshEndur.cs:26-30` with `type = "Endur"` |
| 5 | `PropertiesPersistentSource.GetConfigurationFilePath` :206 | `d.Type == "Endur"` | in-memory |
| 6 | `VariableScopeOptionsResolver` :81/:90 | `d.Type == "Endur Reporting"` / `"Endur External"` | in-memory (API models) |
| 7 | `VariableScopeOptionsResolver` :100–136 | groups by **whole** `Type` string; emits **both** `DbServer_<type>` and `DbName_<type>` (space→underscore, scalar when one DB, array when several) | in-memory |
| 8 | `attach-database.ts` :156–163 | `db.Type === selected.Type` duplicate check | UI |
| 9 | `env-control-center.ts` :319 | `s.Type === environment.Details.ThinClient` — resolves *the* app DB server for the SQL-password-reset dialog (in-code comment: "ThinClient is a DB tag") | UI (data via `ApiServices.GetDbServers`) |

**Projection consumer (no match, takes the raw string):**
`VariableScopeOptionsResolver` :156 embeds `Type` verbatim into each
`DatabaseDefinition` inside the `DatabasePermissions` deployment variable
(`Type` also participates in `DatabaseDefinition.Equals`/`GetHashCode` —
`VariableValueDbPerm.cs:14-15,26`; no in-repo caller of that `Equals`).

**Consequence today**: a value like `Endur;Reporting` renders as two chips but matches
*nothing* — sites 1–6 and 9 miss it, and site 7 emits variables literally named
`DbServer_Endur;Reporting` / `DbName_Endur;Reporting`. Chips are cosmetic; semantics
are single-category. (Multi-tag values cannot currently be *created* through
`add-edit-database` — its input pattern `^[a-zA-Z0-9&.\- ]+$` forbids `;` — but the
column has been writable via API and SQL for years, so pre-existing multi-tag rows
must be treated as live data: see U-7.)

**Capacity chain is also inconsistent** (same disease as server tags):
SQL `NVARCHAR(250)` · `usp_Insert_Database_Detail @DB_TYPE NVARCHAR(50)` (narrower
than its own column) · EF `HasMaxLength(50)` · API no validation · UI free-text
`maxlength=50`. (Test-side: `Tests.Acceptance/Support/DataAccessor.cs:51` inserts with
its own `NVarChar(250)` parameter — widen if capacity fixtures ever flow through it.)

**Already tag-compatible, no change needed**: the two chip-rendering components; the
databases-list `Type` filter (server-side paged `Contains` substring via
`DataPagerExtension` — same documented semantics as server-tag filtering, prior U-5).

## 2. Desired Outcome

`DB_Type` becomes a real semicolon-separated tag list:

1. **Membership semantics** — "database has tag T" means its `Type` list contains an
   entry exactly equal to T (trimmed), at sites 1–6 and 8–9. `Endur;Reporting`
   satisfies `GetDatabaseByType(env, "Endur")` and the ThinClient server lookup.
2. **Per-tag deployment variables** — site 7 groups per tag, mirroring the server-tags
   pattern already in the same file: **both** `DbServer_<tag>` and `DbName_<tag>`
   (space→underscore), scalar when one DB has the tag, array when several.
3. **Capacity 4000 at every layer** — SQL column + proc parameter + EF +
   `[StringLength(TagLimits.MaxTagStringLength)]` + swagger `maxLength` + UI
   joined-string validation. Reuses the existing `TagLimits` /
   `MAX_TAG_STRING_LENGTH` constants — no new limit values.
4. **Chip editing** — `add-edit-database` gets the `tags-input` chip editor for the
   Type field (the server-tags pattern), keeping today's per-tag character set
   (`^[a-zA-Z0-9&.\- ]+$` — which already excludes `;`); `attach-database`'s duplicate
   check becomes tag-set overlap.
5. **Backwards compatibility, honestly scoped** — for existing normalized single-value
   data, every consumer behaves identically (characterization-tested at the unit seam,
   SC-1). Two admitted deltas: (a) SQL `=` ignores trailing spaces, the delimiter
   pattern does not — remediated by the U-2 normalization of **all** rows; (b) rows
   already containing `;` change behaviour by design — surfaced by the U-7 pre-deploy
   audit, not waved away as "opt-in".

## 3. Design position (for panel review)

- **Matching form, EF sites (1–3)**: inline delimiter-wrap pattern
  `(";" + db.Type + ";").Contains(";" + tag + ";")` — translates to SQL, exact per-tag
  (no substring false positives), correct for normalized single-value rows (no `;`
  present). A static helper cannot appear inside an EF expression tree, so the two
  `Database` sites share an `Expression<Func<Database, bool>>` factory; site 3 filters
  a projection, so the pattern is inlined there. **Evidence form for translatability**:
  the offline `DeploymentContext` rig (as used by `DeploymentContextTagWidthTests`)
  plus `IQueryable.ToQueryString()` — the generated SQL is captured as a gate artifact,
  no live database needed.
- **Matching form, in-memory sites (4–7, 9)**: shared tag-string type (proposed:
  `Dorc.ApiModel.TagString` beside `TagLimits` — split/trim/has-tag on the `;`
  convention); UI mirror already exists as `tag-parser.ts`.
- **Null/empty contract**: null or empty `Type` ⇒ **no tags** — never matches any tag,
  contributes nothing to site 7. EF sites are null-safe via EF's `COALESCE` wrapping of
  nullable concat (null row yields `";;"`, matching no non-empty tag — same outcome as
  today's `NULL == tag`). Site 7 today **throws NRE** on a null-`Type` database
  (`:104-105`); the rewrite makes null mean "skip" — an intended, documented deviation
  from SC-1 (crash → correct behaviour).
- **Tag charset invariant**: an individual tag must not contain `;` (it is the
  delimiter) — enforced by the chip editor's per-tag pattern and by API-side
  normalization; a *lookup* parameter containing `;` (possible at the public
  `RefDataDatabases/ByType` and `dbType` permission-filter endpoints) is rejected as a
  400 rather than silently performing adjacent-sublist matching.
- **Write normalization** (`DatabasesPersistentSource` Add/Update): split on `;`, trim
  each entry, drop empties, **dedup exact (Ordinal) duplicates keeping first
  occurrence**, re-join with `;`, **preserving original order** (order determines
  chip display and nothing else — site 7 shapes depend on tag *sets*, not order);
  all-dropped result stores `NULL`. Note: normalization runs on every update, so an
  unrelated edit rewrites a legacy padded value — intended, it converges data. Legacy
  rows that are never rewritten are covered by the U-2 script.
- **Trailing-space delta (admitted)**: SQL `=` ignores trailing spaces, so a stored
  `"Endur "` matches `== "Endur"` today at EF sites but would not match the delimiter
  pattern. Remediation: the U-2 one-time normalization trims **all** `DB_Type` rows
  (not only `;`-containing ones). Query-side `Trim()` (EF-translatable) was considered
  and rejected as permanent per-query cost for a one-time data problem — see §10.
- **Case sensitivity (U-5)**: tokenization changes, comparison semantics do not — EF
  sites keep database collation (as `==` does today), in-memory sites keep Ordinal (as
  `==` does today). This **inherits** a pre-existing divergence: the two
  `GetDatabaseByType` overloads already disagree on casing (site 1 collation, site 4
  Ordinal). Documented, not widened, not fixed here. Within one value, dedup is
  Ordinal, so `Endur;endur` survives as two tags and site 7 emits `DbServer_Endur`
  and `DbServer_endur` — matching today's behaviour for two databases whose whole
  `Type` strings differ only by case.
- **Duplicate-tag ambiguity**: sites 1/4/5/6 use `SingleOrDefault` today and **throw**
  if two databases in one environment share the matched value; per-tag membership makes
  collisions likelier — including at the external RefreshEndur CLI, where a throw
  becomes a restore-tooling failure. Position: keep the throw (a resolution tag
  designates *the* database of that kind in an environment); the U-7 audit finds
  existing collisions before deploy — see U-1.
- **`DatabasePermissions` position**: the `DatabaseDefinition.Type` payload keeps the
  raw joined string verbatim (scripts consuming it see exactly what the column holds);
  called out in the release note.

## 4. Constraints

- **C-1 (dual-source, one step)**: SQL DDL + proc + EF `HasMaxLength` change in the
  same step (established R-2 discipline; `EnsureCreated` databases take widths from EF).
- **C-2 (layer ordering)**: schema → API → UI, as before; dacpac-first at rollout.
- **C-3 (no loosening of frozen fields)**: `Name`, `ServerName`, `ArrayName` stay at
  their widths; the existing width-lock test moves `Type` to the widened set and keeps
  the other three frozen (ArrayName remains pinned per the tag-capacity v3 Correction).
- **C-4 (generated client)**: swagger via the established minimal-splice workflow.
- **C-5 (characterization first)**: SC-1 tests land **before** the matching rewrite,
  freezing current behaviour on normalized single-value data at the unit seam.

## 5. Proposed scope (strategic — IS defines steps)

1. Characterization tests freezing single-value behaviour of sites 1–7 (both variable
   families at site 7) + resolver emission call-set, at the unit seam.
2. Schema: `DB_Type` 250→4000, `@DB_TYPE` 50→4000, EF 50→4000 (+width-test update).
3. Matching semantics: expression factory + `TagString` + rewrite sites 1–7 +
   write-normalization + `ToQueryString` translation artifact; membership tests incl.
   `Endur;Reporting` cases, null-`Type`, padded and duplicate-tag fixtures.
4. API: `[StringLength]` on `DatabaseApiModel.Type`, `;`-in-lookup-param rejection,
   boundary tests, swagger splice (maxLength + membership-semantics descriptions on
   the `ByType` `type` param **and** the permissions `dbType` param).
5. UI: chip editor in `add-edit-database` (per-tag pattern retained, joined-limit
   validation, visible rejection), tag-overlap duplicate check in `attach-database`,
   tag-membership ThinClient lookup in `env-control-center`, relabel per U-4.
6. Operational scripts + final sweep: U-2 normalization script (trim/normalize **all**
   rows), U-7 pre-deploy audit query (multi-tag rows + tag-collision report per
   environment), rollout notes, suite baselines.

## 6. Success Criteria

- **SC-1 (invariance at the unit seam)**: on normalized single-value `Type` data, all
  converted sites return identical results and site 7 emits an identical variable set —
  **both** `DbServer_*` and `DbName_*` families — via characterization tests written
  against current behaviour at the LINQ-to-Objects seam, passing unchanged after the
  rewrite. Admitted exclusions (SQL-level padding parity, null-`Type` crash→skip,
  pre-existing multi-tag rows) are listed in §2.5/§3 and covered by U-2/U-7 instead.
- **SC-2 (membership)**: an `Endur;Reporting` database satisfies tag lookups for both
  tags at sites 1–6 and the site-9 ThinClient lookup; site 7 emits `DbServer_Endur`,
  `DbServer_Reporting`, `DbName_Endur`, `DbName_Reporting`; scalar/array shape
  verified for shared tags in both families.
- **SC-3 (layer agreement + translation)**: 4000 at DDL/proc/EF/spec/UI — machine-
  checked where the server feature checks it (EF width test, spec↔UI constant test,
  DDL gate artifact) — plus the captured `ToQueryString` SQL for the delimiter pattern
  as the EF-translation artifact.
- **SC-4 (boundary)**: 4000 accepted / 4001 rejected as a 400 whose message names the
  member and the 4000 limit (mirrors `TagCapacityValidationTests` message shape);
  `;`-bearing lookup params rejected as 400.
- **SC-5 (UI)**: chip round-trip; over-limit joined string visibly rejected with no API
  call; exactly-4000 accepted; `attach-database` warns on tag-set **overlap** (single-
  value behaviour unchanged); `env-control-center` resolves the app DB server when
  ThinClient matches any one tag.
- **SC-6 (baselines)**: all suites at recorded baselines; no regressions.

## 7. Out of scope

- A normalized tag table (unchanged position from both prior HLPSs).
- Changing the databases-list filter's substring semantics.
- Server-tag or component-tag (`Container`/`CloudResource`/`ApiRegistration`) changes.
- Renaming the `DB_Type` column/property itself (display relabel only, per U-4).
- Fixing the pre-existing casing divergence between the `GetDatabaseByType` overloads.

## 8. Unknowns Register

| ID | Unknown | Blocking? | Owner | Proposed resolution |
|----|---------|-----------|-------|---------------------|
| U-1 | Two databases in one environment sharing a resolution tag → `SingleOrDefault` throws (incl. via RefreshEndur CLI) | No | Agent | **Keep the throw** (same failure as duplicate exact `Type` today); U-7 audit surfaces existing collisions pre-deploy; document in rollout notes |
| U-2 | Legacy padded values (`"Endur "`, `"a; b"`) defeat the delimiter pattern that SQL `=` forgave | No | User (checkpoint) | **One-time dacpac post-deploy normalization of ALL `DB_Type` rows** (trim entries, drop empties, re-join); fallback: accept + document as data-hygiene prerequisite |
| U-3 | `usp_Insert_Database_Detail` external liveness (no in-repo callers found) | No | Agent | Mirror server U-6: **widen the parameter** (safe either way) |
| U-4 | Relabel `Type` displays ("Application Tag" headers, dialog field) to "Tags" | No — default yes | User (checkpoint) | Display-only relabel, mirroring the approved server-side convention |
| U-5 | Case sensitivity of tag matching | No | Agent | **No change to comparison semantics, only tokenization** — DB collation at EF sites, Ordinal in-memory, inheriting (not widening) the pre-existing overload divergence documented in §3 |
| U-6 | PR scoping: fold into PR #774 vs separate PR | **Yes** (checkpoint) | User | Default: same branch/PR #774 (it is the tag-capacity PR and the user said "fix the database tags **too**") |
| U-7 | Unknown pre-existing multi-tag / padded / colliding rows in production data | No — but gates rollout | User (ops, with provided script) | **Pre-deploy audit query shipped with the feature**: reports `;`-bearing rows, padded rows, and per-environment tag collisions, so behaviour changes at deploy are enumerated, not discovered |

## 9. Risks

- **R-1 (behaviour change on pre-existing multi-tag rows)**: where `;`-bearing rows
  already exist, deploy changes behaviour immediately — lookups that missed them start
  matching (or throwing on collision, U-1), and site 7 splits their emissions, which
  can flip an existing variable's **shape** (scalar→array) as well as adding names, in
  both the `DbServer_*` and `DbName_*` families. Mitigation: the U-7 audit enumerates
  affected rows/environments before deploy; the release note explains the shape rule.
- **R-2 (throw-frequency increase)**: U-1 collisions surface as 500s (and RefreshEndur
  failures) where today they are rare. Mitigated: unchanged failure mode, U-7
  pre-enumeration, clearer docs.
- **R-3 (rollout ordering)**: as before — dacpac first (C-2).
- **R-4 (padded-row misses)**: EF-site misses on any padded row until the U-2
  normalization runs (in-memory sites trim and are immune). Mitigated by U-2 covering
  **all** rows; residual risk only if the user chooses the accept-and-document
  fallback.

## 10. Alternatives considered

- **New dedicated `Tags` column**: cleanest separation, but the user explicitly chose
  `DB_Type` — the UI already renders it as chips, and a second overlapping concept
  (category *and* tags) would be worse than one coherent tag list.
- **Substring matching (server-style `Contains`)**: rejected for resolution sites —
  `GetDatabaseByType("Endur")` must not match a hypothetical `EndurX` tag; exact
  per-tag membership is required where a variable's value is resolved from the match.
- **`FirstOrDefault` on collisions**: hides genuine config errors nondeterministically;
  rejected (U-1).
- **Query-side `Trim()` in the EF pattern**: EF-translatable and would forgive padded
  rows forever, but pays LTRIM/RTRIM on every query to solve a one-time data problem,
  and still would not fix per-entry padding (`a; b`); rejected in favour of U-2.

## 11. Process note

DRAFT → IN REVIEW under the CLAUDE.md lifecycle; round-1 panel (3 reviewers) returned
REVISE — this v2 applies the triaged findings (`REVIEW-HLPS-round1.md`). A round-2
delta review verifies the fixes, then the user checkpoint resolves U-2/U-4/U-6 (and
acknowledges U-7) before the IS is drafted.
