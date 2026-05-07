# IS: Environment APIs Tab

| Field      | Value                                                |
|------------|------------------------------------------------------|
| **Status** | APPROVED                                             |
| **Author** | Agent                                                |
| **Date**   | 2026-05-06                                           |
| **Folder** | docs/api-tab/                                        |
| **Branch** | claude/implement-api-tab-01dlx                       |
| **Parent** | [`HLPS-api-tab.md`](./HLPS-api-tab.md) (APPROVED) |

---

## Overview

This IS sequences the work to replace the placeholder `env-apis.ts` with a working environment-private "APIs" sub-tab that supports CRUD, environment-scoped variable substitution in endpoints, and audit-trail symmetry with sibling tabs (Databases, Servers, Daemons).

The decisions baked into this sequence:

- **Env-private composition** (U-2). The `deploy.Api` table carries an `EnvId` FK with `ON DELETE CASCADE`. No join table. No attach/detach UI.
- **Server-side variable resolution** (U-3 default). The BE returns `EndpointResolved` alongside the raw `Endpoint` so the FE never re-implements `PropertyParser`.
- **Reuse of `UserEditable`** (U-4 default). No new privilege class.
- **Full CRUD in v1** (U-5 default).
- **No autocomplete on the endpoint input** in v1 (U-6 default).
- **Audit via `RefDataAudit` / `EnvironmentHistory`** (U-7 default), matching `EnvironmentsPersistentSource.AddHistoryAction` usage (`EnvironmentsPersistentSource.cs:183` etc).
- **Clickable resolved URLs** for `https?://` schemes (U-8 default).

The sequence is presented as ordered atomic steps S-001 through S-008. Steps may run sequentially because each builds on the previous (schema → entity → DTO → controller → client → UI → tests → docs). S-002 and S-003 could be parallelised by two authors but the dependency on S-001 schema deployment makes serial execution safer.

---

## Step Dependency Graph

```
S-001 (schema)
  └─> S-002 (EF entity + persistent source)
        └─> S-003 (ApiModel + EnvironmentContent integration + variable resolution)
              └─> S-004 (controller)
                    └─> S-005 (OpenAPI client regen)
                          └─> S-006 (frontend env-apis component + add-edit-api)
                                └─> S-007 (BE + FE tests)
                                      └─> S-008 (docs/release notes)
```

No step is independent of S-001. S-007 may begin sketching FE test fixtures in parallel with S-006, but the test files cannot be finalised until S-006 components exist.

---

## S-001 — Schema: `deploy.Api` table

**What changes.** A new `deploy.Api` table is added to the `Dorc.Database` SQL project (the live schema lives under `src/Dorc.Database/Schema Objects/Schemas/deploy/Tables/`, alongside `Environment.table.sql` and the existing join tables `EnvironmentDatabase.sql` / `EnvironmentServer.sql`). The table carries:

- Identity primary key.
- `EnvId` FK to `deploy.Environment(Id)` with `ON DELETE CASCADE`.
- `Name` (NVARCHAR, indexed via a `(EnvId, Name)` unique constraint to prevent duplicates within an environment).
- `Endpoint` (NVARCHAR, raw — placeholders allowed).
- `Description` (NVARCHAR, nullable).
- `Type` (NVARCHAR, constrained to {`REST`, `SOAP`, `gRPC`} via a CHECK constraint or a small enum table — JIT-author chooses; CHECK constraint is simpler and matches the codebase's preference for inline validation).
- `AuthType` (NVARCHAR, constrained to {`None`, `Basic`, `Bearer`, `OAuth`}).
- `HealthCheckPath` (NVARCHAR, nullable — relative path appended to `Endpoint` for health probes; v1 only **stores** it).
- `OwnerProjectId` (INT, nullable, FK to `deploy.Project(Id)`).
- `Tags` (NVARCHAR, nullable, free-form mirroring `Server.ApplicationTags`).

**Why this shape.** Mirrors the columnar style of sibling tables (`DATABASE.sql`, `Environment.table.sql`); cascade-delete encodes the env-private decision (U-2) at the database level so an env deletion cannot orphan API rows.

**Out of scope.** Per-row history columns (created-by / modified-by). History flows through `EnvironmentHistory` per S-002.

**Files touched.** New SQL file under `Schema Objects/Schemas/deploy/Tables/Api.table.sql`. Optional `Indexes/IX_Api_EnvId.index.sql` if performance review of the JIT spec calls for it. The `.sqlproj` file picks the new files up via wildcard or explicit include — JIT spec confirms the project's include style.

**Done when.** Schema project builds, `dacpac` validates, deployment script applies cleanly to a clean database.

---

## S-002 — EF entity, configuration, and `IApisPersistentSource`

**What changes.** Three additions to `Dorc.PersistentData`:

1. `Dorc.PersistentData.Model.Api` POCO matching the columns from S-001. The class lives in the same folder as `Database.cs` / `Server.cs` / `Daemon.cs` and follows the same property style.
2. `Dorc.PersistentData.EntityTypeConfigurations.ApiConfiguration` mapping the POCO to `deploy.Api`, declaring the `EnvId` relationship (one Environment to many APIs), the unique `(EnvId, Name)` index, and the cascade behaviour. Registered in the existing context (the JIT spec verifies the registration site).
3. `Dorc.PersistentData.Sources.Interfaces.IApisPersistentSource` and its implementation `ApisPersistentSource` exposing CRUD scoped by environment: get-by-env, get-by-id, create, update, delete. Each mutating operation calls `EnvironmentHistoryPersistentSource.AddHistoryAction(...)` so audit symmetry with `EnvironmentsPersistentSource.cs:183` is preserved (U-7 default).

**Why split this way.** Keeps schema, entity mapping, and persistence behaviour in their conventional homes. Matches the layering already used by `DatabasesPersistentSource` / `IDatabasesPersistentSource`.

**Out of scope.** Variable resolution. The persistent source returns raw rows; resolution is layered on top in S-003.

**Files touched.** New: `Model/Api.cs`, `EntityTypeConfigurations/ApiConfiguration.cs`, `Sources/Interfaces/IApisPersistentSource.cs`, `Sources/ApisPersistentSource.cs`. Modified: the EF context where `DbSet<Api>` is registered, and `PersistentDataRegistry.cs` for DI binding.

**Done when.** `dotnet build` succeeds; a smoke unit test that creates and reads an API row against an in-memory or SQLite EF provider passes.

---

## S-003 — `ApiApiModel`, `EnvironmentContentApiModel.Apis`, and server-side resolution

**What changes.** Three additions in `Dorc.ApiModel` and `Dorc.Api`:

1. `ApiApiModel` in `Dorc.ApiModel` carrying every column from S-001 **plus** an `EndpointResolved` string (server-computed) and a `ResolutionStatus` flag indicating whether all tokens resolved or some remained unresolved. The flag drives the FE warning style for SC-03.
2. A new `Apis` collection on `EnvironmentContentApiModel` (`EnvironmentContentApiModel.cs:27-73`). The collection is populated in `ApiServices.GetEnvironmentsDetails` (`ApiServices.cs:60-85`) so that the existing `RefDataEnvironmentsDetailsApi.refDataEnvironmentsDetailsIdGet` call (already used by `env-databases`) returns APIs in the same payload — **no second round-trip** on tab load.
3. A small server-side resolution helper that, given an `IPropertyValuesPersistentSource` and the environment name, builds a transient `IVariableResolver` seeded with that environment's properties and evaluates each API's raw `Endpoint`. The helper consumes the existing `PropertyEvaluator` / `PropertyParser` (`Dorc.Core.VariableResolution.PropertyParser.cs:7`) — no new substitution rules. Unresolved tokens fall through unchanged, matching `PropertyEvaluator.Evaluate` behaviour (`PropertyEvaluator.cs:31-33`), and the helper sets `ResolutionStatus` accordingly.

**Why server-side.** Single source of truth for substitution semantics; FE stays a thin renderer. Future deploy-time consumers (R-02) get the same resolution path for free.

**Out of scope.** Caching of resolved values. v1 resolves on every read; the property cache inside `VariableResolver` already amortises lookups within a single request.

**Files touched.** New: `Dorc.ApiModel/ApiApiModel.cs`, `Dorc.Api/Services/ApiEndpointResolver.cs` (name chosen to be cohesive per `CLAUDE.md` — describes what it resolves, not "Helper" / "Manager" / "Service" as a grab-bag). Modified: `Dorc.ApiModel/EnvironmentContentApiModel.cs`, `Dorc.Api/Services/ApiServices.cs`, `Dorc.Api/Interfaces/IApiServices.cs` if the public surface expands.

**Done when.** A unit test in `Dorc.Api.Tests` mocks `IPropertyValuesPersistentSource` to return `{ApiHost = api-uat.example.com, ApiPort = 8443}` and asserts a stored endpoint `https://$ApiHost$:$ApiPort$/v1` resolves to `https://api-uat.example.com:8443/v1`. A second test asserts that an unknown token leaves the raw `$Foo$` in place and flips `ResolutionStatus` to a non-OK value.

---

## S-004 — `RefDataApisController`

**What changes.** A new controller `RefDataApisController` under `Dorc.Api/Controllers/` exposing the standard verbs:

- `GET` by environment id (returns the same shape as the `Apis` collection on `EnvironmentContentApiModel`, useful for refresh-without-reload patterns mirroring `RefDataDatabasesController` usage in `env-databases`).
- `POST` to create a new API row scoped to a specific environment.
- `PUT` to update an existing row.
- `DELETE` by id, with the same `ClaimsPrincipal`-based authorisation gate that the sibling controllers use (`RefDataEnvironmentsController.cs` for the verb pattern).

Authorisation reuses the existing `IRolePrivilegesChecker` / `UserEditable` flow — no new privilege class (U-4 default).

**Why a dedicated controller.** Symmetry with `RefDataDatabasesController`, `RefDataServersController`, `RefDataDaemonsController`. Keeps the `RefDataEnvironmentsDetailsController` payload as a read-only aggregate view; mutations go through the type-specific controllers.

**Out of scope.** Bulk operations. v1 is one-API-at-a-time, matching sibling controllers.

**Files touched.** New: `Dorc.Api/Controllers/RefDataApisController.cs`. Possibly new DI bindings in `Program.cs` if the controller introduces new injected services beyond what S-002 / S-003 register.

**Done when.** `dotnet build` succeeds; a controller unit test (`Dorc.Api.Tests`) covers happy path and the `UserEditable=false` rejection for at least one mutating verb. Swagger / OpenAPI document for the new routes is generated by the existing pipeline.

---

## S-005 — OpenAPI client regeneration

**What changes.** Run the existing client generator (`src/dorc-web/openapitools.json`) so that `src/dorc-web/src/apis/dorc-api/` gains:

- `models/ApiApiModel.ts`
- `apis/RefDataApisApi.ts`
- An updated `EnvironmentContentApiModel.ts` carrying the new `Apis` collection.

**Why regen-only.** Generated files are forbidden from hand-editing in this codebase; regenerating is the only path. JIT spec captures the exact generator command and any flags already used by the project (the `package.json` script is the canonical reference).

**Out of scope.** Any custom extensions to generated models go in `model-extensions/` (the precedent is `EnvironmentContentBuildsApiModelExtended.ts`). v1 does not need an extension class.

**Files touched.** Generated additions and updates under `src/dorc-web/src/apis/dorc-api/`. The diff is mechanical; reviewers focus on **whether the schema generated matches the intent**, not on TypeScript style of generated files.

**Done when.** `npm run build` (or whichever build command the project uses post-regen) succeeds; the new `RefDataApisApi.refDataApisGet(...)` symbol is callable from a TypeScript file.

---

## S-006 — Frontend: replace `env-apis.ts` and add `add-edit-api`

**What changes.** Two FE deliverables:

1. **`env-apis.ts` rewrite.** Modelled on `env-databases.ts` (`env-databases.ts:18-186`). Extends `PageEnvBase`. Renders a `<vaadin-details>` shell containing:
   - A header row with an "Add API" button gated on `environment?.UserEditable` (mirroring `env-databases.ts:73`).
   - A grid (sortable columns: Name, Type, Endpoint (raw), Endpoint (resolved), AuthType, Tags, Owner Project, Actions) sourced from `EnvironmentContentApiModel.Apis`.
   - The "Endpoint (resolved)" column uses a custom cell renderer: when `ResolutionStatus` is OK and the resolved string parses as `https?://`, render an anchor with `target="_blank"`; when unresolved tokens remain, render the resolved string with a styled badge or tooltip listing the missing variable names.
   - A delete confirmation flow per row, following the pattern in `attached-databases`.
   - An edit affordance per row that opens the same dialog as "Add API", pre-populated.
   - On successful create / update / delete, call `RefDataEnvironmentsDetailsApi.refDataEnvironmentsDetailsIdGet` to refresh, mirroring `env-databases.ts:127-135`.

2. **`add-edit-api.ts`** (new). A form component opened inside a `vaadin-dialog` (per the dialog pattern at `env-databases.ts:74-84`). Fields: Name (required), Endpoint (required, free-form, helper text mentioning `$Var$` syntax), Description, Type (combo-box with REST/SOAP/gRPC), AuthType (combo-box with None/Basic/Bearer/OAuth), HealthCheckPath, OwnerProject (combo-box backed by the existing project list), Tags. Validation: non-empty Name, non-empty Endpoint, unique Name within the environment (BE enforces; FE pre-checks against the loaded list to give a fast error). Submit calls the relevant `RefDataApisApi` verb.

**Why both.** Splitting the form keeps `env-apis.ts` readable and matches the existing `add-edit-database.ts` precedent.

**Out of scope.** Variable autocomplete in the Endpoint input (U-6 default — deferred). Inline cell editing (the codebase uses dialog-based editing across sibling tabs).

**Files touched.** Modified: `src/dorc-web/src/components/environment-tabs/env-apis.ts` (full rewrite). New: `src/dorc-web/src/components/add-edit-api.ts`. Possibly a small `attached-apis.ts` if the JIT spec author prefers the same separation `env-databases` uses (`attached-databases.ts`); my recommendation is **inline the grid into `env-apis.ts`** for v1 because the grid is small and adding a separate component is premature abstraction (per `CLAUDE.md` guidance — "Don't add features, refactor, or introduce abstractions beyond what the task requires").

**Done when.** `npm run build` succeeds; manual smoke test in the dev server shows the placeholder text replaced by a real grid; create / edit / delete round-trip works against a local API; a stored `https://$KnownVar$/v1` resolves visibly in the Resolved column.

---

## S-007 — Tests

**What changes.**

- **BE — `Dorc.Api.Tests`.** Tests for `ApisPersistentSource` CRUD (env-scope filter; cascade-delete confirmation), `ApiEndpointResolver` (happy resolution + partial-resolution + no-tokens passthrough), `RefDataApisController` (happy paths + `UserEditable=false` rejection on at least one mutating verb), and the integration of `Apis` into `ApiServices.GetEnvironmentsDetails`.
- **FE — `src/dorc-web` Vitest.** A spec mounting `env-apis` with a fake `EnvironmentContentApiModel` payload containing one resolved and one unresolved endpoint, asserting both render correctly and that the unresolved badge appears. A second spec for `add-edit-api` exercising form validation (missing name, missing endpoint, duplicate-name pre-check).

**Why this scope.** Mirrors the test surface area implied by recent IS docs (e.g. `docs/audit-pages/IS-audit-pages.md` and `docs/request-grid-perf/SPEC-S-003-environmentnameexact-equality.md`) — unit tests at the boundaries that matter, not end-to-end.

**Out of scope.** Browser E2E (no infrastructure for it in the repo). Performance tests against large API counts (k6 tests live under `src/dorc-web/k6-tests/`; v1 doesn't add load there).

**Files touched.** New tests under `src/Dorc.Api.Tests/` and `src/dorc-web/src/components/__tests__/` (matching whichever convention the project's existing Vitest specs use — JIT spec confirms).

**Done when.** Both test suites pass locally and in CI.

---

## S-008 — Documentation and release notes

**What changes.**

- A short user-facing note in `docs/api-tab/` describing the feature, the `$Var$` syntax (linking to the existing variable system), and the v1 limitations (no autocomplete, no health-check execution, no shared-API attach).
- An entry in whatever release-notes convention the team uses for FE-visible changes (recent practice: a `RELEASE-S-00X-notes-draft.md` under the feature folder, e.g. `docs/request-grid-perf/RELEASE-S-007-notes-draft.md`).

**Why.** Operators need to discover the feature; future contributors need a pointer to the env-private design decision so they don't re-litigate U-2.

**Out of scope.** Marketing copy, screenshots — the team's house style is plain Markdown notes.

**Files touched.** New: `docs/api-tab/README.md` (or similarly named user-doc) and `docs/api-tab/RELEASE-S-008-notes-draft.md`.

**Done when.** Both files reviewed and merged.

---

## Cross-cutting risks already covered by HLPS

- **R-01** (SSRF on outbound calls) — explicitly out of scope; v1 only **stores** `HealthCheckPath`.
- **R-02** (deploy-time vs request-time resolution mismatch) — v1 is viewing-only; deploy-time integration is a follow-up.
- **R-03** (credentials embedded in endpoint paths) — deferred behind a future "secure" flag analogous to `Property.Secure`.

These do not gate any step in this IS.

---

## Acceptance and Next Step

This IS is **APPROVED** alongside the HLPS at the user's instruction to "consider all checkpoints approved". S-001 through S-008 have been executed on branch `claude/implement-api-tab-01dlx`; PR #677 carries the resulting code, tests, and user-facing docs.

**Requested user input:**
1. Confirm or override the eight defaults inherited from HLPS U-3 through U-8 (recapped at the top of this document).
2. Approve the step decomposition, or suggest re-ordering / splitting / merging.
3. Identify any organisational constraints not captured (DBA review windows for the schema deployment, frontend code-freeze windows, etc).
