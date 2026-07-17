# SPEC S-005 — UI (database-tags)

**Status**: EXECUTED 2026-07-17 · **IS**: S-005 · **Gate**: SC-5's five assertions +
relabel verification + render sweep.

## Deliverables

- **`add-edit-database`**: the free-text "Application Tag" field (50-char, shared
  `maxFieldLength`) is replaced by the `tags-input` chip editor labelled **"Tags"**,
  keeping the per-tag charset (`^[a-zA-Z0-9&.\- ]+$`, now enforced per chip via
  Tagify's `pattern` option). Chip edits flow through a new `tags-changed` event into
  `DatabaseType` (keeping the existing exists/completeness validation live); save
  re-reads the chips and rejects an over-limit joined string with a visible error
  notification and **no API call**; exactly-at-limit passes through.
- **`tags-input`**: additive upgrades — optional `pattern` property (per-tag
  validation) and a `tags-changed` CustomEvent on add/remove (guarded `.on?.()` so
  test shims without Tagify's event API keep working).
- **`attach-database`** (site 8): duplicate check is now **tag-set overlap** via
  `splitTags`/`hasTag`; the warning names the overlapping tag(s); single-value
  behaviour unchanged. Display texts relabelled ("Tags:", "Duplicate Tag").
- **`env-control-center`** (site 9): the ThinClient app-DB-server resolution uses
  `hasTag` membership, so a multi-tag database now resolves.
- **`tag-parser`**: new `hasTag` mirroring the backend `TagString.HasTag` contract
  (trimmed exact entry match, Ordinal, null/empty/whitespace never matches).
- **Relabels (U-4)**: `attached-databases` grid header and `page-databases-list`
  filter placeholder → "Tags" (display only). The responsive-grids test's
  header-based selector updated accordingly.

## Tests (15 new/updated)

- `tests/helpers/tag-parser.test.ts` (6): membership, no-substring, trimming,
  Ordinal casing, null/empty, round-trip.
- `tests/components/database-tags-ui.test.ts` (8): chip round-trip; over-limit
  visible rejection with zero API calls; exactly-4000 accepted; overlap warning
  (multi-tag overlap / identical single tags / disjoint quiet / warning names the
  tag); ThinClient membership resolution via the protected `environment`/`envContent`
  setter seam (the IS-named seam — synchronous, no network).
- `responsive-grids.test.ts`: selector updated for the relabelled header (render
  sweep re-verified).

## Suite state after S-005

web **136/136** (16 files) · app build clean.

## S-001..S-003 gate triage (arrived during S-005; log in REVIEW-STEPS.md)

Verdict PASS; F-1/F-2 (evidence gaps) closed with new fixtures in this step's
commit; F-3 closed by trimming lookup params at both controllers (both matching
families now see identical needles); F-4/F-5 closed by S-004's rejection (same PR).
