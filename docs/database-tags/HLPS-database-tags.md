# HLPS: Database Tags via the DB_Type Column

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | DRAFT (v1)                               |
| **Author**  | Agent                                    |
| **Date**    | 2026-07-17                               |
| **Folder**  | docs/database-tags/                      |
| **Branch**  | claude/tag-capacity-expansion (PR #774) — confirm at checkpoint (U-6) |
| **Origin**  | User directive after the ArrayName correction (docs/tag-capacity-expansion/ HLPS v3): "let's fix the database tags too but use the db type column you've identified" |

---

## 1. Problem Statement

The tag-capacity feature's database half was reverted because `Array_Name` turned out to
be storage-array metadata. The user has now designated **`DATABASE.DB_Type`** as the
database tags column. The UI already half-believes this: three grids
(`page-databases-list`, `attached-databases`, `env-databases`) split `Type` on `;` and
render chips. But the rest of the stack treats `Type` as a single opaque category
string:

**Exact whole-string matches on `Type` (complete survey, 2026-07-17):**

| # | Site | Match | Execution |
|---|------|-------|-----------|
| 1 | `DatabasesPersistentSource.GetDatabaseByType(EnvironmentApiModel, type)` :157 | `d.Type == type` | EF-translated |
| 2 | `UsersPersistentSource.GetEnvironmentUsers` :61 | `db.Type == "Endur"` | EF-translated |
| 3 | `UserPermsPersistentSource.GetUserDbPermissions` :70 | `r.DbType == dbType` | EF-translated (filter on projection before `ToList`) |
| 4 | `DatabasesPersistentSource.GetDatabaseByType(envName, type)` :119 | `x.Type == type` | in-memory (post-`Single` materialization) |
| 5 | `PropertiesPersistentSource.GetConfigurationFilePath` :206 | `d.Type == "Endur"` | in-memory |
| 6 | `VariableScopeOptionsResolver` :81/:90 | `d.Type == "Endur Reporting"` / `"Endur External"` | in-memory (API models) |
| 7 | `VariableScopeOptionsResolver` :100–119 | groups by **whole** `Type` string; emits `DbServer_<type>` (space→underscore) | in-memory |
| 8 | `attach-database.ts` :158–166 | `db.Type === selected.Type` duplicate check | UI |

**Consequence today**: a value like `Endur;Reporting` renders as two chips but matches
*nothing* — `GetDatabaseByType("Endur")` misses it, the Endur users query misses it, the
config-file path lookup misses it, and site 7 emits a deployment variable literally
named `DbServer_Endur;Reporting`. Chips are cosmetic; semantics are single-category.

**Capacity chain is also inconsistent** (same disease as server tags):
SQL `NVARCHAR(250)` · `usp_Insert_Database_Detail @DB_TYPE NVARCHAR(50)` (narrower than
its own column) · EF `HasMaxLength(50)` · API no validation · UI free-text
`maxlength=50`.

**Already tag-compatible, no change needed**: the three chip-rendering grids; the
databases-list `Type` filter (server-side paged `Contains` substring via
`DataPagerExtension` — same documented semantics as server-tag filtering, prior U-5).

## 2. Desired Outcome

`DB_Type` becomes a real semicolon-separated tag list:

1. **Membership semantics** — "database has tag T" means its `Type` list contains an
   entry exactly equal to T (trimmed), at every consumer in §1. `Endur;Reporting`
   satisfies `GetDatabaseByType(env, "Endur")`.
2. **Per-tag deployment variables** — site 7 groups per tag, mirroring the server-tags
   pattern already in the same file: `DbServer_<tag>` (space→underscore), scalar when
   one DB has the tag, array when several.
3. **Capacity 4000 at every layer** — SQL column + proc parameter + EF +
   `[StringLength(TagLimits.MaxTagStringLength)]` + swagger `maxLength` + UI
   joined-string validation. Reuses the existing `TagLimits` /
   `MAX_TAG_STRING_LENGTH` constants — no new limit values.
4. **Chip editing** — `add-edit-database` gets the `tags-input` chip editor for the
   Type field (the server-tags pattern); `attach-database`'s duplicate check becomes
   tag-set overlap.
5. **Backwards compatibility** — for existing single-value data, every consumer
   behaves **identically** (characterization-tested): a single tag equals the old
   whole-string match, and site 7 emits the same variable set.

## 3. Design position (for panel review)

- **Matching form, EF sites (1–3)**: inline delimiter-wrap pattern
  `(";" + db.Type + ";").Contains(";" + tag + ";")` — translates to SQL, exact per-tag
  (no substring false positives), correct for single-value legacy rows (no `;` present).
  A static helper cannot appear inside an EF expression tree, so the two `Database`
  sites share an `Expression<Func<Database, bool>>` factory; site 3 filters a
  projection, so the pattern is inlined there.
- **Matching form, in-memory sites (4–8)**: shared tag-string type (proposed:
  `Dorc.ApiModel.TagString` beside `TagLimits` — split/trim/has-tag on the `;`
  convention; UI mirror already exists as `tag-parser.ts`).
- **Write normalization**: `DatabasesPersistentSource` Add/Update normalize `Type`
  (split, trim entries, drop empties, re-join `;`) so the EF delimiter pattern is sound
  for all rows written from now on. Legacy rows with padded entries (`a; b`) predate
  normalization — see U-2.
- **Duplicate-tag ambiguity**: sites 1/4/5/6 use `SingleOrDefault` today and **throw**
  if two databases in one environment share the matched value; per-tag membership makes
  collisions likelier. Position: keep the throw (a tag used for `GetDatabaseByType`-style
  resolution designates *the* database of that kind in an environment) — see U-1.
- **Emission-set change is opt-in**: multi-tag values only exist where a user creates
  them; until then site 7's output is unchanged (SC-1). When a user does add
  `Endur;Reporting` to one DB, `DbServer_Endur` and `DbServer_Reporting` both emit —
  that is the point of the feature, and reserved-name collisions
  (`EndurDatabaseName` etc.) are unaffected because those come from sites 6's own
  lookups, not the per-tag loop.

## 4. Constraints

- **C-1 (dual-source, one step)**: SQL DDL + proc + EF `HasMaxLength` change in the
  same step (established R-2 discipline; `EnsureCreated` databases take widths from EF).
- **C-2 (layer ordering)**: schema → API → UI, as before; dacpac-first at rollout.
- **C-3 (no loosening of frozen fields)**: `Name`, `ServerName`, `ArrayName` stay at
  their widths; the existing width-lock test moves `Type` to the widened set and keeps
  the other three frozen (ArrayName remains pinned per the tag-capacity v3 Correction).
- **C-4 (generated client)**: swagger via the established minimal-splice workflow.
- **C-5 (single-tag invariance)**: SC-1 characterization lands **before** the matching
  rewrite, freezing current behaviour on single-value data.

## 5. Proposed scope (strategic — IS defines steps)

1. Characterization tests freezing single-value behaviour of sites 1–7 + resolver
   emission call-set.
2. Schema: `DB_Type` 250→4000, `@DB_TYPE` 50→4000, EF 50→4000 (+width-test update).
3. Matching semantics: expression factory + `TagString` + rewrite sites 1–7 +
   write-normalization; membership tests incl. `Endur;Reporting` cases.
4. API: `[StringLength]` on `DatabaseApiModel.Type`, boundary tests, swagger splice
   (maxLength + GetDatabaseByType param description).
5. UI: chip editor in `add-edit-database` (joined-limit validation, visible rejection),
   tag-overlap duplicate check in `attach-database`, relabel per U-4.
6. Final sweep + rollout notes (+ optional U-2 normalization script).

## 6. Success Criteria

- **SC-1 (invariance)**: on single-value `Type` data, all converted sites return
  identical results and site 7 emits an identical variable set — characterization
  tests written against current behaviour, passing unchanged after the rewrite.
- **SC-2 (membership)**: a `Endur;Reporting` database satisfies tag lookups for both
  tags at sites 1–6; site 7 emits `DbServer_Endur` **and** `DbServer_Reporting`;
  scalar/array shape verified for shared tags.
- **SC-3 (layer agreement)**: 4000 at DDL/proc/EF/spec/UI — machine-checked where the
  server feature checks it (EF width test, spec↔UI constant test, DDL gate artifact).
- **SC-4 (boundary)**: 4000 accepted / 4001 rejected as 400 with a readable message on
  database write endpoints.
- **SC-5 (UI)**: chip round-trip; over-limit joined string visibly rejected with no API
  call; exactly-4000 accepted.
- **SC-6 (baselines)**: all suites at recorded baselines; no regressions.

## 7. Out of scope

- A normalized tag table (unchanged position from both prior HLPSs).
- Changing the databases-list filter's substring semantics.
- Server-tag or component-tag (`Container`/`CloudResource`/`ApiRegistration`) changes.
- Renaming the `DB_Type` column/property itself (display relabel only, per U-4).

## 8. Unknowns Register

| ID | Unknown | Blocking? | Owner | Proposed resolution |
|----|---------|-----------|-------|---------------------|
| U-1 | Two databases in one environment sharing a resolution tag → `SingleOrDefault` throws | No | Agent | **Keep the throw** (same failure as duplicate exact `Type` today); document in rollout notes |
| U-2 | Legacy rows with padding/casing oddities (`a; b`) defeat the EF delimiter pattern | No | User (checkpoint) | **Proposed**: one-time dacpac post-deploy normalization of `DB_Type` (trim entries containing `;`); fallback: accept + document |
| U-3 | `usp_Insert_Database_Detail` external liveness (no in-repo callers found) | No | Agent | Mirror server U-6: **widen the parameter** (safe either way) |
| U-4 | Relabel `Type` displays ("Database Type" field, grid headers) to "Tags" | No — default yes | User (checkpoint) | Display-only relabel, mirroring the approved server-side convention |
| U-5 | Case sensitivity of tag matching | No | Agent | Follow the database collation for EF sites (as today's `==` does) and `Ordinal` in-memory (as today's `==` does) — i.e. **no change** to comparison semantics, only to tokenization |
| U-6 | PR scoping: fold into PR #774 vs separate PR | **Yes** (checkpoint) | User | Default: same branch/PR #774 (it is the tag-capacity PR and the user said "fix the database tags **too**") |

## 9. Risks

- **R-1 (silent variable additions)**: multi-tag values change the emitted deployment
  variable set (`DbServer_<tag>` per tag). Mitigated: opt-in per §3; SC-1 freezes
  single-tag output; release note tells operators what changes when they multi-tag.
- **R-2 (throw-frequency increase)**: U-1 collisions surface as 500s where today they
  are rare. Mitigated: unchanged failure mode, clearer docs; monitoring unchanged.
- **R-3 (rollout ordering)**: as before — dacpac first (C-2).
- **R-4 (legacy padded rows)**: EF-site misses until U-2 normalization runs; in-memory
  sites trim and are immune. Mitigated by the U-2 script or documented as a data-hygiene
  prerequisite.

## 10. Alternatives considered

- **New dedicated `Tags` column**: cleanest separation, but the user explicitly chose
  `DB_Type` — the UI already renders it as chips, and a second overlapping concept
  (category *and* tags) would be worse than one coherent tag list.
- **Substring matching (server-style `Contains`)**: rejected for resolution sites —
  `GetDatabaseByType("Endur")` must not match a hypothetical `EndurX` tag; exact
  per-tag membership is required where a variable's value is resolved from the match.
- **`FirstOrDefault` on collisions**: hides genuine config errors nondeterministically;
  rejected (U-1).

## 11. Process note

DRAFT → IN REVIEW under the CLAUDE.md lifecycle; adversarial panel first, then the user
checkpoint resolves U-2/U-4/U-6 before the IS is drafted.
