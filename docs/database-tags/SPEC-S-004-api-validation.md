# SPEC S-004 — API Validation + Contract (database-tags)

**Status**: EXECUTED 2026-07-17 · **IS**: S-004 · **Gate**: boundary evidence (incl.
absent-vs-empty) + splice diff + agreement test.

## Deliverables

- `DatabaseApiModel.Type`: `[StringLength(TagLimits.MaxTagStringLength)]`, same
  message shape as the server model (names the member, `{1}` = 4000).
- `RefDataDatabasesController.GetByType`: `type` must be a single non-empty tag —
  null/empty/whitespace or `;`-bearing → 400 **before** the source is called (an empty
  needle would match every untagged database — HLPS round-2 NEW-1). XML doc updated to
  membership semantics ("matches any one entry of the semicolon-separated tag list").
- `RefDataDatabaseUsersController.GetDbUsersPermissions`: a **supplied** `dbType` gets
  the same rule; an **omitted** one keeps no-filter semantics (SC-4 reconciliation,
  IS v3). XML doc updated.
- Swagger splice: `DatabaseApiModel.Type` gains `maxLength: 4000, minLength: 0` —
  exactly mirroring the Swashbuckle-emitted `ServerApiModel.ApplicationTags` fragment
  shape; diff is 2 added lines, trailing newline preserved. **Note (recorded for the
  gate)**: this generator emits *no* `description` fields on query parameters anywhere
  in the spec, so there is no param-description fragment to splice — the membership
  semantics live in the C# XML docs (the annotation source the IS mandates) and will
  flow into any future full spec regeneration.
- Spec↔UI agreement test extended: `DatabaseApiModel.Type.maxLength ===
  MAX_TAG_STRING_LENGTH` (`tests/helpers/tag-limits.test.ts`).

## Tests (11 new)

- `TagCapacityValidationTests`: `DatabaseTags_AtLimit_Valid` (4000 accepted),
  `DatabaseTags_OverLimit_InvalidWithReadableMessage` (4001 → message names member +
  4000).
- `DatabaseTagLookupParamTests`: GetByType rejects null/empty/whitespace/`;`-bearing
  without calling the source (4 rows) and calls through on a valid tag;
  GetDbUsersPermissions rejects supplied-invalid (3 rows) and the **absent-param
  regression** proves omission still reaches the source as `null` (no filter).
- Web: both spec↔UI agreement assertions green (chromium project).

## Consumer re-check

`RefreshEndur` calls `ByType` with `type=Endur` — a valid single tag, unaffected by
the new rejection rule; its single-tag resolution behaviour is frozen by S-001/S-003
tests (recorded per IS S-004).

## Suite state after S-004

Api 238 pass / 22 platform (baseline) · Core unchanged 150/150 · web agreement 2/2.
