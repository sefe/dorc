# IS: Database Tags via DB_Type — Implementation Sequence

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | DRAFT (v1)                                   |
| **Author**  | Agent                                        |
| **Date**    | 2026-07-17                                   |
| **HLPS**    | HLPS-database-tags.md (APPROVED v3; checkpoint resolutions in its §8) |
| **Folder**  | docs/database-tags/                          |
| **Branch**  | claude/tag-capacity-expansion (PR #774, per U-6) |

Fixed by the HLPS checkpoint: normalization script **shipped** (U-2); displays
relabelled to "Tags" (U-4); same PR #774 (U-6); U-7 audit acknowledged; execution
**auto-pilot** (gates still run per step).

---

## Step Index

| ID    | Title                                                        | Addresses                          | Depends On |
|-------|--------------------------------------------------------------|------------------------------------|------------|
| S-001 | Baselines + characterization freeze of sites 1–7             | SC-1, C-5                          | —          |
| S-002 | Schema, dual-sourced: widen `DB_Type` + proc parameter + EF  | SC-3 (schema layers), C-1, U-3     | S-001      |
| S-003 | Tag-membership semantics: `TagString`, expression factory, rewrite sites 1–7, write-normalization, `ToQueryString` artifact | SC-1, SC-2, SC-3 (translation artifact) | S-001, S-002 |
| S-004 | API: validation (`[StringLength]`, lookup-param rejection), boundary tests, swagger splice | SC-4, C-4                     | S-003      |
| S-005 | UI: chip editor, attach-overlap, ThinClient membership, relabel | SC-5, U-4                        | S-004      |
| S-006 | Operational scripts (U-2 normalization, U-7 audit), final sweep, rollout notes, PR #774 update | SC-6, U-2, U-7, R-1..R-4, U-6 | S-003, S-005 |

Sequencing rationale: characterization before any rewrite (C-5); schema before
matching so the EF width metadata and DDL move together ahead of code that assumes
them; API validation before the UI that pre-validates against it (each layer never
outruns the layer beneath — same discipline as the two prior features). S-005's
relabel and chip scaffolding could overlap S-002..S-004 but are kept sequential for
gate clarity.

---

## S-001 — Baselines + characterization freeze

- Record suite baselines (Core / Api / Monitor / web) as of the step's start commit.
- Characterization tests at the unit seam (`DbContextMock` + NSubstitute'd
  `IDeploymentContext`; interface-injected resolver) freezing **current** behaviour on
  normalized single-value `Type` data for: sites 1–5 (both `GetDatabaseByType`
  overloads, Endur users query, config-file path, permissions filter incl. its
  null/empty guard), site 6 (Endur Reporting/External lookups), site 7 (**both**
  `DbServer_*` and `DbName_*` families: distinct-type grouping, scalar-vs-array
  shapes, space→underscore naming) — extending the existing resolver call-set
  characterization rather than duplicating it.
- Fixtures must include: single-value rows, a null-`Type` row (asserting today's
  behaviour: excluded at sites 1–6; **site 7 currently throws NRE** — freeze that as
  the documented current behaviour the rewrite intentionally changes), and a
  multi-tag row demonstrating today's miss-everything behaviour (so S-003 flips those
  assertions deliberately, not accidentally).
- Gate: panel verifies the frozen call-set matches the HLPS survey (no site skipped,
  both variable families covered).

## S-002 — Schema, dual-sourced (one step)

- `DATABASE.sql`: `DB_Type` `NVARCHAR(250)` → `NVARCHAR(4000)`.
- `usp_Insert_Database_Detail.sql`: `@DB_TYPE` `NVARCHAR(50)` → `NVARCHAR(4000)`
  (widen, not delete — U-3 mirrors the server proc decision).
- `DatabaseEntityTypeConfiguration`: `Type` `HasMaxLength(50)` → `HasMaxLength(4000)`
  via `TagLimits.MaxTagStringLength`.
- `DeploymentContextTagWidthTests`: move `Type` from the frozen set to the widened
  assertions; `ArrayName`/`Name`/`ServerName` stay pinned at 50 (C-3; the ArrayName
  comment from the tag-capacity correction is retained).
- Gate artifact: side-by-side DDL↔EF width comparison (the established form).

## S-003 — Tag-membership semantics

- `Dorc.ApiModel.TagString` (beside `TagLimits`): split/trim/drop-empties/dedup
  (Ordinal, keep-first)/re-join; `HasTag` (Ordinal; false for null/empty/whitespace
  argument or null/empty value); order-preserving. UI mirror stays `tag-parser.ts`.
- Expression factory for the EF sites: delimiter-wrap pattern per HLPS §3; applied at
  sites 1–2; inlined at site 3 (projection filter). Capture `ToQueryString` output of
  the pattern via the offline `DeploymentContext` rig as a gate artifact (SC-3).
- Rewrite in-memory sites 4–6 and site 7 to `TagString` membership / per-tag grouping
  (both `DbServer_*` and `DbName_*`; scalar when exactly one DB carries the tag, array
  otherwise; null-`Type` DBs contribute nothing — the documented crash→skip deviation).
- Write-normalization in `DatabasesPersistentSource` Add/Update per HLPS §3 rules.
- `DatabasePermissions` / `DatabaseDefinition.Type` stays verbatim (position recorded —
  assert in a test that the raw joined string passes through).
- Membership tests: `Endur;Reporting` satisfies both tags at sites 1–6; site 7 emits
  all four per-tag variables for it; shared-tag scalar/array shapes; padded fixtures
  at the unit seam documenting the seam's limits; S-001 characterizations still green
  except the two deliberately flipped groups (multi-tag misses; site-7 null NRE).
- Gate: panel reviews semantics vs HLPS §3 (incl. NEW-1 contract) and the SQL artifact.

## S-004 — API validation + contract

- `DatabaseApiModel.Type`: `[StringLength(TagLimits.MaxTagStringLength, ...)]` with
  the member-naming message shape (SC-4).
- Lookup-param rejection (400) for `;`-bearing and null/empty/whitespace values at the
  `RefDataDatabases/ByType` `type` param and `GetDbUsersPermissions` `dbType` param
  (site 3's existing null/empty guard becomes an explicit 400 at the controller for
  consistency — note: absent param today silently means "no filter"; **absent stays
  no-filter, only explicitly-empty is rejected** at the permissions endpoint).
- Boundary tests: 4000 accepted / 4001 rejected (member + limit named); param
  rejection tests; consumer re-check that `RefreshEndur`'s `type=Endur` call is
  unaffected for single-tag data (covered by S-001 freeze; record in gate log).
- Swagger splice (C-4 minimal-splice workflow): `DatabaseApiModel.Type` `maxLength`,
  membership-semantics descriptions on both params. Byte-identical-generation proof
  as before.
- Gate: boundary evidence + splice diff.

## S-005 — UI

- `add-edit-database`: chip editor (`tags-input` + `tag-parser`, `FakeTagify` seam in
  tests) for the Type field; per-tag pattern retained (`^[a-zA-Z0-9&.\- ]+$`);
  joined-string ≤ `MAX_TAG_STRING_LENGTH` pre-validation with visible rejection and
  no API call; exactly-at-limit accepted.
- `attach-database`: duplicate check becomes tag-set overlap (warn names the
  overlapping tag(s); single-value behaviour unchanged).
- `env-control-center`: ThinClient app-DB-server resolution becomes tag membership
  via `tag-parser`.
- Relabel (U-4): dialog field and remaining headers → "Tags" (display only).
- Gate: vitest evidence for SC-5's four assertions; a render sweep over the two chip
  components confirming no double-splitting regressions.

## S-006 — Operational scripts + final sweep

- U-2 normalization: dacpac post-deploy script, idempotent, normalizes ALL `DB_Type`
  rows with the S-003 rules (trim/drop-empties/dedup/re-join; all-dropped → NULL).
- U-7 audit: standalone read-only SQL shipped under `install-scripts/` (or the
  established location for such queries): reports `;`-bearing rows, padded rows, and
  per-environment tag collisions (the U-1 throw candidates), per environment.
- Final verification sweep vs S-001 baselines; VERIFICATION-S-006.md with the SC
  table; rollout notes (dacpac-first; audit-before-deploy; `DatabasePermissions.Type`
  verbatim note; variable shape rule for multi-tagging).
- PR #774 title/body updated to the combined server + database tag scope (U-6),
  including the correction history.
- Gate: full-suite evidence + docs review.

---

## Cross-step invariants

- Every step: suites at baselines before hand-off; commits carry the step ID.
- No step loosens a layer below its predecessor (C-2 ordering).
- Docs updated in the same commit as the code they describe.
