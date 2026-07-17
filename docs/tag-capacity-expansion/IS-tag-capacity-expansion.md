# IS: Server & Database Tag Capacity Expansion — Implementation Sequence

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | IN REVIEW (v2, post round-1 revision)        |
| **Author**  | Agent                                        |
| **Date**    | 2026-07-17                                   |
| **HLPS**    | HLPS-tag-capacity-expansion.md (APPROVED v2; checkpoint decisions in its §8) |
| **Folder**  | docs/tag-capacity-expansion/                 |
| **Branch**  | claude/tag-capacity-expansion                |

Fixed by the HLPS checkpoint: limit `N = 4000`; chip editor yes; "Array Name" → "Tags"
relabel yes; `Contains` behaviour accepted & documented; proc parameter widened.

> **v2 changes** (round-1 panel, `REVIEW-IS-round1.md`): cross-language limit mechanism
> stated honestly (C# + TS constants + spec-agreement test — no single-symbol claim);
> spec-splice availability moved to S-001 with an explicit S-003 fallback; the
> server-tags mirror conditional resolved (databases get a grid-launched tags dialog)
> and the UI step split into S-004/S-005; consumer re-verification given owners;
> S-002 names `ArrayName` only; index matrix corrected; sequencing rationale honest.

---

## Step Index

| ID    | Title                                                     | Addresses                    | Depends On |
|-------|-----------------------------------------------------------|------------------------------|------------|
| S-001 | Baselines, toolchain + splice-workflow check, U-4 record  | SC-5, U-4                    | —          |
| S-002 | Schema, dual-sourced: widen columns + proc parameter      | SC-2 (schema/EF layers), R-2, U-6 | S-001 |
| S-003 | API validation, consumer re-verification, spec metadata   | SC-1, SC-2 (API layer), SC-3, SC-5, R-3, HLPS §3.3 | S-002 |
| S-004 | UI: chip editor + joined-string enforcement + per-field limits | SC-2 (UI layer), SC-6   | S-003      |
| S-005 | UI: grid tag-dialog integration, relabel, rendering sweep | SC-4, U-2a/U-2b              | S-004      |
| S-006 | Final verification sweep + release notes                  | SC-1..SC-6, R-1, U-5         | S-003, S-005 |

Sequencing rationale (honest version): schema → API → UI ordering guarantees each layer
never *loosens* past the layer beneath it as commits land (today's API has no validation
at all, so S-003 only tightens — the ordering is about the UI steps never outrunning the
API/schema). The relabel and chip-component scaffolding are N-independent and *could*
overlap S-002/S-003; they are kept sequential for simplicity and gate clarity, not
necessity.

---

## S-001 — Baselines, toolchain + splice-workflow check, U-4 record

### What changes
No production code. Records in `TOOLCHAIN-S-001.md` (this folder):
1. Test-suite baselines **on this branch** (main-based — prior-feature counts do not
   apply).
2. Light toolchain re-check: backend builds, suites run, `dorc-web` builds/tests.
3. **Spec-update workflow availability** (round-1 finding): can the running API serve
   `/swagger/v1/swagger.json` here (as the prior feature's record showed for that
   branch), and is the scripted-splice technique reproducible on this branch? The
   #773 splice artifacts are cross-branch; the technique must be re-derived locally —
   record feasibility now so S-003 doesn't discover a missing mechanism.
4. **U-4 determination**: is the #773 component schema present (merged/rebased) at
   execution time? Fixes S-002/S-003 scope to five columns or two. **If unmerged, the
   record explicitly creates the follow-up item** ("widen component `Tags` columns on
   #773 merge") which S-006's release note must carry — the HLPS U-4 commitment gets a
   named owner and landing place.

### Verification intent
The record exists with explicit outcomes for all four items; the U-4 answer states the
resulting scope.

---

## S-002 — Schema, dual-sourced: widen columns + proc parameter

### What changes
One step, both schema sources (HLPS §4):
- SSDT: `SERVER.Application_Server_Name` NVARCHAR(1000)→(N);
  `DATABASE.Array_Name` NVARCHAR(250)→(N); `usp_Insert_Server_Detail`
  `@APPLICATION_SERVER_NAME` NVARCHAR(1000)→(N) (U-6: widen, not delete).
- EF: `ServerEntityTypeConfiguration` `ApplicationTags` `HasMaxLength(250)`→`(N)`;
  `DatabaseEntityTypeConfiguration` — **the `ArrayName` property only** —
  `HasMaxLength(50)`→`(N)`. (Round-1 finding: that configuration holds four properties
  at 50; `Name`/`Type`/`ServerName` are explicitly untouched — `Server_Name`+`DB_Name`
  sit under a unique index that N would overflow on EnsureCreated databases.)
- Component `Tags` columns + configurations likewise **iff** S-001 recorded the #773
  schema present.

### Verification intent
- Gate artifact: side-by-side DDL↔EF comparison per changed column; **explicit check
  that no other property/column width changed** (esp. the other three Database fields).
- Model-build test asserting the EF model's max length equals N for each changed
  property and remains 50/250 for the untouched Database fields. (The prior feature's
  `DeploymentContextModelTests` pattern is cross-branch; the S-002 JIT spec carries the
  pattern inline — build the model offline via the `EnsureCreated` static-flag
  workaround and inspect `IModel` properties.)
- Dacpac build delegated to CI (HLPS §4 dev-environment limits); repo builds, suites at
  S-001 baselines.

---

## S-003 — API validation, consumer re-verification, spec metadata

### What changes
- **Limit constants, cross-language (round-1 correction)**: there is no single symbol
  spanning C# and TypeScript. Mechanism: `Dorc.ApiModel` gains a C# constant (e.g.
  `TagLimits.MaxTagStringLength = 4000`) referenced by the `[StringLength]` attributes
  and any backend checks; `dorc-web` gains a TS constant (e.g.
  `helpers/tag-limits.ts`) for S-004/S-005. **Agreement is proven, not assumed**: a web
  test parses the committed `swagger.json` and asserts the affected model schemas'
  `maxLength` equals the TS constant; the C# side's agreement with the spec is inherent
  (the spec is generated from the attributes). This is SC-2's API/UI-layer check.
- `[StringLength(TagLimits.MaxTagStringLength)]` on `ServerApiModel.ApplicationTags`
  and `DatabaseApiModel.ArrayName` (+ component DTO `Tags` iff in scope) —
  `[ApiController]` yields automatic 400s.
- **Consumer re-verification (round-1 HIGH — HLPS §3.3, now owned here)**: unit tests
  exercising each surveyed consumer with a near-N multi-tag string —
  `VariableScopeOptionsResolver` (split → per-tag variables emitted correctly at N),
  `DaemonsPersistentSource` projection (passes through unmodified),
  `RefreshEndur`-style `Contains` match (documented behaviour per U-5); plus the
  existing `GetAppServerDetails` filter. Results recorded for the S-006 sweep.
- Committed `swagger.json`: affected model schemas gain `maxLength` via the splice
  technique re-derived per S-001's feasibility record; **fallback** if the API cannot
  serve a spec here: hand-derive the two fragment edits by running Swashbuckle in CI or
  copying the attribute-generated fragment shape from an equivalent annotated model —
  recorded openly at the gate. Expectation (grounded by in-repo precedent
  `PropertyFilter.maxLength`): spec-only diff, zero generated-file churn.

### Verification intent
- `Dorc.Api.Tests`: per tag-accepting write endpoint — N accepted, N+1 → 400 with
  readable message (SC-1); >1000-char mapping round-trip (SC-3 mocked half).
- Consumer tests above green; `Tests.Acceptance` compiles; both features untouched
  (SC-5 compile half).
- Client diff spec-only (or additions-only if generation proves necessary).

---

## S-004 — UI: chip editor + joined-string enforcement + per-field limits

### What changes
- `add-edit-database.ts`: shared `maxFieldLength = 50` becomes per-field limits —
  name/type/instance stay 50 (their EF widths; widening them is out of scope), the
  `ArrayName` input is replaced by the chip-style editor (U-2a) integrated in the
  dialog.
- New `database-tags` component mirroring `server-tags.ts` (chips via `tags-input`,
  split/join via `tag-parser.ts`), saving through the databases API.
- **Joined-string enforcement in both stacks' save paths**: `server-tags.ts` (which
  today has no limit at all) and the new database path validate the joined
  semicolon-separated string against the S-003 TS constant before submitting, with a
  clear inline error — the UI never sends what the API will 400.

### Verification intent
Web component tests: chip round-trip fidelity for database tags (SC-6); joined-string
over-limit rejected client-side with the error visible, at-limit accepted; per-field
limits asserted (name/type/instance still 50); server-tags enforcement covered too.

---

## S-005 — UI: grid tag-dialog integration, relabel, rendering sweep

### What changes
- **Resolved from round-1 (no longer conditional)**: databases get a grid-launched tags
  dialog mirroring the servers pattern — a tags button in `database-controls.ts` (which
  today has only Edit/Delete) firing a `manage-database-tags` event, handled with a
  dialog in `page-databases-list.ts` and `attached-databases.ts`, exactly as
  `server-controls.ts:manage-server-tags` → `page-servers-list.ts`/`attached-servers.ts`
  do.
- Relabel "Array Name" → "Tags" in `attached-databases.ts`, `page-databases-list.ts`,
  and the dialog (U-2b; display-only, DTO field unchanged).
- Rendering check of the five HLPS-finding-6 surfaces with a near-N tag set
  (component tests where feasible; otherwise recorded for the user-environment pass).

### Verification intent
Web component tests: tags button present and gated like its server counterpart; dialog
opens with the chip editor; relabel visible in grid headers/dialog; near-N rendering
evidence per surface or an explicit transfer note.

---

## S-006 — Final verification sweep + release notes

### What changes
No production code. SC-1..SC-6 walked with evidence; suites at S-001 baselines; release
note: **dacpac (columns + proc) before API/UI** (R-1), the `Contains` false-positive
acceptance documented (U-5), consumer re-verification results summarized (HLPS §3.3),
the live >1000-char round-trip + rendering pass recorded as transferring to the user's
environment, and — if S-001 took the two-column path — the **U-4 follow-up item**
(widen component `Tags` columns on #773 merge) stated prominently.

### Verification intent
Every SC has recorded evidence or an explicit user-transfer note; the U-4 follow-up, if
applicable, appears in both the release note and the PR description.
