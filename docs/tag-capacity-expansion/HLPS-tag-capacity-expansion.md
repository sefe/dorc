# HLPS: Server Tag Capacity Expansion

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | **DELIVERED (CORRECTED — server tags only)** 2026-07-17; database scope reverted, see Correction below |
| **Author**  | Agent                                    |
| **Date**    | 2026-07-17                               |
| **Folder**  | docs/tag-capacity-expansion/             |
| **Branch**  | claude/tag-capacity-expansion            |
| **Origin**  | Split out of `docs/env-details-component-tabs/` by user direction (PR #773); §3 findings were produced and verified by that feature's adversarial panel |

> **v2 changes** (round-1 panel, see `REVIEW-HLPS-round1.md`): Success Criteria and
> Constraints sections added; enforcement-layer statement corrected (EF `HasMaxLength`
> is metadata, not a save-time validator); U-4 given an explicit fallback; deployment
> ordering, risks, alternatives, and out-of-scope sections added; U-2 split; stored-proc
> liveness question registered.

---

## ⚠ CORRECTION (v3, 2026-07-17, post-delivery user domain review)

**U-3 is overturned.** The code survey verified `ArrayName` is a pure pass-through with
no in-code consumers — which is true — but the *domain* meaning was wrong: **`ArrayName`
records the storage array the source database sits on**. It is infrastructure metadata,
not a free-text field available for repurposing as tags. Code-level "safe to repurpose"
was never sufficient grounds; the field carries operational meaning to its human readers.

**Consequences applied on this branch:**

- All database-tag work is **reverted to `main`**: `DATABASE.Array_Name` stays
  `NVARCHAR(250)` / EF `HasMaxLength(50)`; no `[StringLength]` on `DatabaseApiModel`;
  the chip editor (`database-tags.ts`), the "Array Name" → "Tags" relabels, and the
  related grid/dialog changes are removed; the swagger `DatabaseApiModel` fragment is
  restored from `main`.
- U-2a, U-2b, and the database half of U-1's application are **void**.
- The delivered scope is **server `ApplicationTags` capacity only** (NVARCHAR(4000) at
  every layer, API 400 on overrun, UI pre-validation) plus the follow-up hook for the
  PR #773 component `Tags` columns (U-4 fallback unchanged).
- A regression test now pins the corrected state:
  `DeploymentContextTagWidthTests.DatabaseFields_KeepTheirCurrentWidths`.

If database tagging is still wanted, it must be a **new dedicated column/model**, not a
repurposed field — `DB_Type` is likewise unavailable (exact-match category semantics,
see `REVIEW-STEPS.md`). That is a separate HLPS, to be started only on user request.

---

## 1. Problem Statement

Tags are used far more heavily than the current limits allow. Server tags live in
`ApplicationTags` (semicolon-separated, edited via `components/server-tags.ts` +
`tags-input.ts`, parsed by `helpers/tag-parser.ts`); the database equivalent is the
`ArrayName` field (a plain text input today, verified pure pass-through — U-3).

The layers **disagree with each other**:

| Layer | Server `ApplicationTags` | Database `ArrayName` |
|-------|--------------------------|----------------------|
| SQL (`Dorc.Database/dbo/Tables/`) | `SERVER.Application_Server_Name NVARCHAR(1000)` | `DATABASE.Array_Name NVARCHAR(250)` |
| Stored proc | `usp_Insert_Server_Detail.sql` param `NVARCHAR(1000)` (liveness unknown — U-6) | — |
| EF (`EntityTypeConfigurations/`) | `HasMaxLength(250)` | `HasMaxLength(50)` |
| API validation | none | none |
| UI | `tags-input.ts`: no limit | `add-edit-database.ts`: `maxlength=50` |

**Where enforcement actually happens** (corrected in v2): EF's `HasMaxLength` is model
metadata, not a save-time validator — writes through `SaveChanges` succeed up to the real
column width. So on dacpac-deployed databases the effective limits are **1000** (server)
and **250** (database, further capped to **50 by the UI**); anything longer fails at the
DB layer with an opaque provider error. On `EnsureCreated()`-bootstrapped databases the
EF metadata *becomes* the column widths (250/50) — the dual-source drift is already live
behaviour today, not a hypothetical.

## 2. Desired Outcome

One generous, **consistent** tag limit (value = U-1) enforced identically at every layer
for server tags, database tags, and — per U-4 — the three component-type `Tags` columns
from PR #773, with overruns rejected as a clear 400 at the API boundary instead of a
truncation or DB exception.

## 3. Carry-over findings (verified by the prior panel; re-verified on this branch)

1. **HIGH** — `usp_Insert_Server_Detail.sql` declares `@APPLICATION_SERVER_NAME
   NVARCHAR(1000)`: widening the column alone leaves a truncation path through the proc.
   *v2 note:* the proc has **no in-repo callers** (only the sqlproj references it;
   `install-scripts/TestHarness.ps1` builds an offline DataTable and never calls it) —
   see U-6: widen it, or delete a dead proc.
2. **MED** — `add-edit-database.ts` uses one shared `maxFieldLength = 50` across **four**
   fields (DB name, type, instance, Array Name). Raising it naively loosens the other
   three; per-field limits are required.
3. **MED** — verified in-repo tag consumers, each to be re-verified at the chosen limit:
   `ServersPersistentSource.GetAppServerDetails` (`Contains("appserv")`),
   `DaemonsPersistentSource` (projection), `Tools.PostRestoreEndurCLI/RefreshEndur.cs`
   (`Contains` match), `VariableScopeOptionsResolver` (semicolon split → deployment
   variables; on the #773 branch this is a shared helper — see U-4).
4. **MED (technical)** — do not justify the limit with indexability: `NVARCHAR(4000)` is
   8000 bytes, over SQL Server's 1700-byte index key limit, and the existing `LIKE '%…%'`
   filters can't seek anyway. The honest rationale for a sized column over
   `NVARCHAR(MAX)` is avoiding LOB semantics/row-overflow on hot ref-data tables.
5. **LOW** — API 400s via `[StringLength]` DataAnnotations + `[ApiController]` automatic
   400 (verified present on both controllers).
6. **LOW** — long-value rendering must be checked in **five** surfaces:
   `page-servers-list.ts`, `attached-servers.ts`, `attached-databases.ts`,
   `page-databases-list.ts`, and `attach-server.ts` (renders `ApplicationTags` in the
   attach dialog — added in v2).
7. **LOW** — longer multi-tag strings amplify `Contains` false positives (e.g.
   `"appserv"`). Disposition is U-5; `RefDataAppServers.feature` ("non empty list of
   appservers") is the natural regression test, alongside `RefDataServers.feature`.
8. **LOW** — `Tests.Acceptance` exercises `ApplicationTags` (`RefDataServers.feature`)
   and the appserv filter (`RefDataAppServers.feature`); new validation must not break
   either. (`Support/DataAccessor.cs` verified out of scope: its NVarChar(250) params
   don't cover tags and its DATABASE insert omits `Array_Name`.)

## 4. Constraints

- **Dual-source schema, same step** (inherited from the prior HLPS, and §1 shows the
  drift is already live): SSDT column widths and EF `HasMaxLength` must change
  identically in one step; the gate compares them side by side.
- **Runtime rollout ordering**: the dacpac (columns + proc parameter) must be published
  to existing databases **before** an API/UI that accepts longer values ships — otherwise
  overruns hit the DB layer again, the exact failure §1 describes (see R-1).
- **Client regeneration**: `[StringLength]` on DTOs changes the OpenAPI `maxLength`
  metadata; the committed `swagger.json`/client must be updated via the regeneration
  workflow, not hand-edited. The prior feature's minimal-splice precedent and drift
  discovery apply (`SPEC-S-006-client-regeneration.md` on the #773 branch).
- **Dev-environment limits** (inherited from TOOLCHAIN-S-001, same environment class):
  no SQL Server and no Linux dacpac build — schema/proc changes gate on textual review +
  CI; boundary behaviour gates on unit/component tests; live round-trip transfers to the
  user's environment.
- C# / .NET, existing repo patterns; test-first; adversarial review per step.

## 5. Proposed scope (strategic — IS defines steps)

1. **Limit** (U-1): recommend **`NVARCHAR(4000)`**. Sizing evidence: the widest layer
   today is 1000, no surveyed consumer implies a need beyond 4× that, and 4000 avoids
   LOB semantics (finding 4). `MAX` remains the fallback if 4000 ever proves tight.
2. **Apply consistently**: SSDT columns (`SERVER.Application_Server_Name`,
   `DATABASE.Array_Name`), the stored-proc parameter (or proc deletion — U-6), EF
   `HasMaxLength`, `[StringLength]` on `ServerApiModel.ApplicationTags` and
   `DatabaseApiModel.ArrayName`, per-field UI `maxlength`s (finding 2). **Component
   `Tags` columns**: included when the #773 schema is present (see U-4 fallback).
3. **Fail loudly**: API-boundary 400s (finding 5).
4. **Optional** (U-2a): chip-style `tags-input` editor for database tags (`server-tags.ts`
   pattern, `tag-parser.ts` split/join); (U-2b) relabel "Array Name" → "Tags" in grids
   and dialog. For chip editors the UI limit is enforced on the **joined
   semicolon-separated string** (the IS defines the enforcement point; `tags-input.ts`
   has no single input to cap).

## 6. Success Criteria

- **SC-1 (boundary)**: for each write endpoint accepting tags
  (`RefDataServersController.Put/Post`, `RefDataDatabasesController` equivalents, and
  component controllers when in scope), a value of exactly N chars is accepted and N+1
  is rejected with HTTP 400 and a readable message — `Dorc.Api.Tests`.
- **SC-2 (layer agreement)**: a single check (test or gate artifact) shows all layers
  declare the same N per column: SSDT DDL, proc parameter (if kept), EF configuration,
  DTO `[StringLength]`, UI `maxlength`/joined-string limit.
- **SC-3 (round-trip)**: a tag string longer than the old effective ceiling (>1000 chars
  for servers, >250 for databases) persists and re-reads unmodified through the API
  layer — API-level test with mocked persistence for mapping, plus live round-trip in
  the user's environment per the constraints.
- **SC-4 (rendering)**: the five §3-finding-6 surfaces render a near-N tag set without
  layout breakage — web component tests where feasible, otherwise the manual pass.
- **SC-5 (no regressions)**: all suites at their recorded baselines;
  `RefDataServers.feature` and `RefDataAppServers.feature` unaffected (compile + CI).
- **SC-6 (chip editor, if U-2a approved)**: database tags edited as chips split/join
  identically to server tags (`tag-parser.ts` round-trip test).

## 7. Out of scope

- Non-API ingress paths: direct external proc callers (beyond the U-6 disposition) and
  `RefreshEndur.cs`'s own write paths.
- Tightening `Contains`-based tag matching (unless U-5 decides otherwise).
- Normalised tag tables (rejected in the prior HLPS's alternatives; unchanged here).
- The #773 feature itself; only its `Tags` columns are touched, per U-4.

## 8. Unknowns Register

| ID | Unknown | Blocking? | Owner | Proposed resolution |
|----|---------|-----------|-------|---------------------|
| U-1 | Limit value | ~~Yes~~ **RESOLVED 2026-07-17** | User | **NVARCHAR(4000)** |
| U-2a | Chip-style tags editor for database tags | **RESOLVED 2026-07-17** | User | **Yes** — chip editor via `tags-input` + `tag-parser` |
| U-2b | Relabel "Array Name" → "Tags" in grid/dialog | **RESOLVED 2026-07-17** | User | **Yes** — display-only relabel |
| U-3 | `ArrayName` semantics | ~~VERIFIED~~ **OVERTURNED 2026-07-17** | User | Storage array the source DB sits on — infrastructure metadata, not repurposable as tags. Database scope reverted (see Correction, v3) |
| U-4 | PR #773 merge order vs the three component `Tags` columns | No — with explicit fallback | Agent (IS) | **Fallback**: if #773 is unmerged when the schema step executes, deliver the two existing columns now and record a follow-up item to widen the component columns on rebase/merge; if merged, all five in one step. The IS schema step re-checks at execution time |
| U-5 | `Contains` false-positive amplification | **RESOLVED 2026-07-17** | User | **Accept & document**; `RefDataAppServers.feature` guards the behaviour |
| U-6 | `usp_Insert_Server_Detail` external liveness | **RESOLVED 2026-07-17** | User | Unknown externally → **widen the parameter** (safe either way) |

## 9. Risks

- **R-1 (rollout ordering)**: raising API/UI limits before the dacpac widens existing
  databases reintroduces the DB-layer failure. Mitigation: constraint §4 ordering; release
  note mirrors the prior feature's dacpac-first requirement.
- **R-2 (dual-source drift)**: already live (§1 — EnsureCreated databases have 250/50
  columns today). The same-step constraint prevents widening from worsening it; the gate's
  DDL↔EF comparison is the check.
- **R-3 (client regen)**: OpenAPI `maxLength` changes ride the known-drifted generated
  client; the minimal-splice approach from SPEC-S-006 applies until the client true-up
  task lands.

## 10. Alternatives considered

- **`NVARCHAR(MAX)`**: maximal headroom, LOB/row-overflow costs on hot ref-data tables,
  no practical benefit at surveyed usage — fallback only (U-1).
- **Normalised tag table**: cleanest long-term model; rejected in the prior HLPS as out
  of proportion to the need, unchanged here — widening does not preclude it later.
- **Validation-only (no widening)**: enforcing 250 everywhere would make behaviour
  consistent but contradicts the point of the feature (more capacity, "in anger").

## 11. Process note

DRAFT → IN REVIEW under the CLAUDE.md lifecycle; only the adversarial panel approves,
then the user checkpoint resolves U-1 (blocking) and the default-yes/veto items
(U-2a/U-2b/U-5/U-6) before an IS is drafted.
