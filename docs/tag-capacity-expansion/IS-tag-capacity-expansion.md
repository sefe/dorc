# IS: Server & Database Tag Capacity Expansion — Implementation Sequence

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | DRAFT                                        |
| **Author**  | Agent                                        |
| **Date**    | 2026-07-17                                   |
| **HLPS**    | HLPS-tag-capacity-expansion.md (APPROVED v2; checkpoint decisions in its §8) |
| **Folder**  | docs/tag-capacity-expansion/                 |
| **Branch**  | claude/tag-capacity-expansion                |

Strategic roadmap only — exact signatures and DDL belong to each step's JIT spec. Every
step ends with the adversarial review gate. Decisions already fixed by the HLPS
checkpoint: limit = **NVARCHAR(4000)** (constant `N = 4000` below); chip editor **yes**;
"Array Name" → "Tags" relabel **yes**; `Contains` behaviour accepted & documented;
stored-proc parameter **widened**, not deleted.

---

## Step Index

| ID    | Title                                                    | Addresses            | Depends On |
|-------|----------------------------------------------------------|----------------------|------------|
| S-001 | Baselines + toolchain re-check + U-4 merge-state check   | SC-5, U-4            | —          |
| S-002 | Schema, dual-sourced: widen columns + proc parameter     | SC-2, SC-3, R-2, U-6 | S-001      |
| S-003 | API validation + spec/client metadata update             | SC-1, SC-2, SC-3, R-3 | S-002     |
| S-004 | UI: per-field limits, chip editor, relabel               | SC-2, SC-4, SC-6     | S-003      |
| S-005 | Final verification sweep + release notes                 | SC-1..SC-6, R-1      | S-004      |

Sequential by design: each layer's limit must not outrun the layer beneath it (HLPS §4
rollout-ordering constraint applied to the development order as well — the tree never
holds a commit where an outer layer accepts more than the schema beneath it).

---

## S-001 — Baselines + toolchain re-check + U-4 merge-state check

### What changes
No production code. Records: (a) test-suite baselines **on this branch** (main-based —
the prior feature's counts do not apply); (b) a light toolchain re-check (the SDK/node
installs from TOOLCHAIN-S-001 live in the same container class but this is a different
branch checkout — verify build + suites run); (c) the **U-4 determination**: is the #773
component schema present on the branch at execution time (merged/rebased) or not? That
answer fixes S-002/S-003/S-004 scope to five columns or two.

### Why
SC-5 needs honest baselines; U-4's fallback needs a recorded decision point, not an
assumption discovered mid-step.

### Verification intent
A `TOOLCHAIN-S-001.md` record in this folder with explicit outcomes, including the U-4
answer and the resulting scope (two-column or five-column).

---

## S-002 — Schema, dual-sourced: widen columns + proc parameter

### What changes
In one step (HLPS §4 same-step constraint): SSDT `SERVER.Application_Server_Name`
NVARCHAR(1000)→NVARCHAR(N); `DATABASE.Array_Name` NVARCHAR(250)→NVARCHAR(N);
`usp_Insert_Server_Detail` `@APPLICATION_SERVER_NAME` NVARCHAR(1000)→NVARCHAR(N) (U-6:
widen, not delete); EF `ServerEntityTypeConfiguration` `HasMaxLength(250)`→`(N)`,
`DatabaseEntityTypeConfiguration` `HasMaxLength(50)`→`(N)`; component `Tags` columns +
configurations likewise **iff** S-001 recorded the #773 schema present.

### Why
Everything above the schema depends on it (development-order constraint); widening is
non-destructive for existing data.

### Verification intent
- Gate artifact: side-by-side DDL↔EF comparison per changed column (primary local gate;
  dacpac build delegated to CI per HLPS §4 dev-environment limits).
- Model-build test (pattern: the prior feature's `DeploymentContextModelTests`) asserts
  the EF model's max lengths equal N for each changed property — this makes SC-2's
  layer-agreement machine-checkable for the EF layer.
- No other DDL or configuration changes in the diff.

---

## S-003 — API validation + spec/client metadata update

### What changes
`[StringLength(N)]` on `ServerApiModel.ApplicationTags` and
`DatabaseApiModel.ArrayName` (+ the three component DTO `Tags` properties iff in scope
per S-001) — `[ApiController]` turns violations into automatic 400s. A shared constant
(e.g. `TagLimits.MaxTagStringLength` in `Dorc.ApiModel`) is the single source for N at
the API and UI layers, so SC-2's agreement check reduces to referencing one symbol.
Committed `swagger.json` model schemas gain the corresponding `maxLength` via the
scripted-splice workflow (HLPS §4 client-regen constraint); regenerate/diff the client —
typescript-rxjs models are structural interfaces, so the expectation (verified at
execution) is spec-only changes with no generated-file churn.

### Why
SC-1's clear-400 contract; the API must not accept more than the schema beneath it
(S-002 ordering) and the UI layer above it keys off the same constant.

### Verification intent
- `Dorc.Api.Tests`: per write endpoint accepting tags — N accepted, N+1 rejected with
  400 and a readable message (SC-1); mapping round-trip test with a >1000-char value
  (SC-3's mocked-persistence half).
- `Tests.Acceptance` compiles; `RefDataServers.feature`/`RefDataAppServers.feature`
  untouched (SC-5's compile-level half).
- Client diff: spec fragments only, or additions-only if generation is required.

---

## S-004 — UI: per-field limits, chip editor, relabel

### What changes
- `add-edit-database.ts`: the shared `maxFieldLength = 50` becomes **per-field** limits
  (carry-over finding 2): name/type/instance keep their current 50, `ArrayName` moves to
  the chip editor (below) — no field silently loosens.
- New `database-tags` editing path mirroring `server-tags.ts`: chip-style `tags-input`
  + `tag-parser.ts` split/join for `ArrayName` (U-2a), integrated in the add/edit dialog
  (and the grid detail flow if `server-tags` has one — mirror, don't invent).
- Joined-string enforcement: on save, both `server-tags.ts` and the new database path
  validate the joined semicolon string against N (single constant, HLPS §5.4) with a
  clear inline error — the UI never submits a string the API will 400.
- Relabel "Array Name" → "Tags" in `attached-databases.ts`, `page-databases-list.ts`,
  and the dialog (U-2b; display-only).
- Rendering check of the five carry-over-finding-6 surfaces with a near-N tag set
  (component tests where feasible).

### Why
SC-4/SC-6 and the last layer of SC-2; UI limits must not outrun the API (S-003 ordering).

### Verification intent
- Web component tests: chip round-trip for database tags (split/join fidelity — SC-6);
  joined-string over-limit rejected client-side with the error shown; per-field limits
  asserted (name/type/instance still 50, tags path N); relabel visible in grid/dialog
  headers; near-N rendering on the five surfaces (or recorded manual pass per HLPS §4).

---

## S-005 — Final verification sweep + release notes

### What changes
No production code. SC-1..SC-6 walked with evidence; suites at S-001 baselines; release
note written: **dacpac (columns + proc) must be published before the new API/UI ships**
(R-1 rollout ordering), the `Contains` false-positive acceptance documented (U-5), and
the live >1000-char round-trip + rendering pass recorded as transferring to the user's
environment (HLPS §4 limits).

### Verification intent
Every SC has recorded evidence or an explicit user-transfer note; unresolved items become
user escalations, not silent gaps.
