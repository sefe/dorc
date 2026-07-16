# HLPS: Environment Details Component Tabs Completion

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | IN REVIEW (v3, post round-2 revision)        |
| **Author**  | Agent                                        |
| **Date**    | 2026-07-16                                   |
| **Folder**  | docs/env-details-component-tabs/             |
| **Branch**  | claude/env-details-component-tabs-p7othg     |

> **v2 changes:** all round-1 panel findings applied (see `REVIEW-HLPS-round1.md`).
> Tag capacity expansion **removed from this HLPS** — it will be delivered in a separate PR
> with its own HLPS, carrying over the tag-related round-1 findings.

---

## 1. Problem Statement

The environment details page presents a **Components** tab
(`src/dorc-web/src/pages/page-environment-components.ts`) with six sub-tabs routed under
`/environment/:id/components/*` in `src/dorc-web/src/router/routes.ts`. Three of them —
**Containers**, **Cloud**, and **APIs** — are placeholder stubs (`env-containers.ts`,
`env-cloud.ts`, `env-apis.ts`, ~39 lines each) that render only
"*… functionality will be implemented here.*" Users navigating to these tabs meet dead ends
in a shipped UI.

There is **no backend support at all** for these three component types: no SQL tables, EF
entities, persistent sources, API models, or controllers.

## 2. Desired Outcome

All six Components sub-tabs are functional. Containers, Cloud, and APIs each support:
viewing items attached to the environment, attaching/detaching existing items, and
creating/editing/deleting items — with **no ungated write endpoints** and 403 semantics
exactly as specified in §5.3 (adopting the Daemons controller's gating *discipline*;
the specific privilege per operation is §5.3's, pending U-4).

## 3. Scope

**In scope:**
- `Dorc.Database`: three new entity tables + three environment join tables + three
  `<Type>Audit` tables (the servers/daemons audit pattern is per-type tables —
  `deploy/Tables/ServerAudit.sql`, `DaemonAudit.sql`; the generic `RefDataAudit` is
  project-scoped and unsuitable — see U-8)
- `Dorc.PersistentData`: entities, `IEntityTypeConfiguration`s, `DbSet`s on
  `DeploymentContext`, persistent sources (including audit sources per U-8), **and their
  DI registration in `PersistentDataRegistry.cs`** (Monitor's
  `Registry/PersistentSourcesRegistry.cs` is not touched unless the Monitor needs the new
  types — it does not, this iteration)
- `Dorc.ApiModel`: three DTOs
- `Dorc.Api`: three RefData controllers (CRUD + typed attach/detach, see U-9), audit wiring
- `src/dorc-web`: regenerated `dorc-api` client, replacement of the three stub tabs and
  their attach/attached/add-edit child components
- Tests: `Dorc.Api.Tests` (controller/source unit tests), `src/dorc-web/tests` (component
  tests). `Tests.Acceptance` checked for impact; new feature coverage there is optional
  parity (see U-10).

**Out of scope:**
- Tag capacity expansion on servers/databases — **separate PR/HLPS** (user direction).
- Live introspection of container runtimes / cloud providers / API gateways. The
  *recommended* position (pending U-3) is reference-data CRUD + environment mapping only,
  matching how Servers work: DOrc records what exists, it does not discover it.
- Deployment/variable-resolution integration for the new component types (pending U-3).
- The existing working tabs (Servers, Databases, Daemons).
- `EnvironmentContentApiModel` and the monolithic environment-details load path — the new
  tabs deliberately do **not** extend it (see §5.4).

## 4. Constraints

- C# / .NET, existing repo patterns; namespaces `Dorc.[Component].[Feature]`; no grab-bag
  class names.
- Schema is dual-sourced: SSDT project (existing DBs, dacpac publish) **and** the EF model
  (`DeploymentContext` uses `EnsureCreated()` for fresh databases). New tables must land
  identically in both, in the same step.
- The typed web client is regenerated from the running API's spec:
  `dorc-web/README.md:213` — obtain `swagger.json` from `/swagger/v1/swagger.json` of a
  locally-run `Dorc.Api`, then `npm run dorc-api-gen`. Hand-editing generated client files
  or the committed `swagger.json` is not acceptable. If the dev environment cannot run the
  API locally, this becomes a blocker escalated at the corresponding IS step.
- Write endpoints must return **403** (not 200-with-false) for authorization failures.
- Test-first development; adversarial review per step (CLAUDE.md).

## 5. Proposed Solution Shape (strategic — details in IS/JIT specs)

Working entity names (see U-1): **`Container`**, **`CloudResource`**, **`ApiRegistration`**
(not `ApiEndpoint` — that collides with the existing `Dorc.Api.Services.ApiEndpoints` and
its generated client model; "Component" is also avoided as it already means *deployable
project component* in this domain, e.g. `ApiServices.ChangeEnvComponent`).

### 5.1 Data layer

For each type: a `deploy`-schema table (the modern convention — `deploy.Daemon` — rather
than legacy `dbo` uppercase tables) plus a `deploy.Environment<Type>` join table with an
**explicit composite primary key** on (EnvId, \<Type\>Id). Note: the existing
`deploy.EnvironmentServer` / `deploy.EnvironmentDatabase` SSDT definitions have **no**
PK/unique constraint and permit duplicate attach rows — the new join tables must not copy
that omission.

EF: model class + `IEntityTypeConfiguration` + `DbSet` on `DeploymentContext`, matching the
SSDT DDL exactly (see §4 dual-source constraint).

Proposed minimal schemas (to be confirmed — U-1; all string fields sized NVARCHAR):

- **Container**: Id, Name, Image, Registry, HostServerName (nullable), Tags
- **CloudResource**: Id, Name, Provider, ResourceType, ResourceIdentifier (URI/ARN/id),
  Subscription (nullable), Tags
- **ApiRegistration**: Id, Name, BaseUrl, Version (nullable), HealthCheckUrl (nullable), Tags

All three carry `Tags` from day one (limit aligned with whatever the separate tag-capacity
PR decides; until then, 250 to match current server behaviour).

### 5.2 Persistence layer

`I<Type>sPersistentSource` + implementation per type: list/get/add/update/delete +
attach/detach (see U-9), registered in `PersistentDataRegistry.cs`.

**Mirror the shape of `ServersPersistentSource`, not its defects.** Explicit do-not-copy
list (verified in round-1 review):

1. `DeleteServer` dereferences the entity outside its null-guard (NRE on unknown id) —
   new sources must guard.
2. Related collections are read without `Include` (no lazy-loading proxies are configured),
   so e.g. `svr.Environments` is silently empty and `UserEditable` (computed via `All()`
   over an empty sequence) is wrongly `true` — new sources must eager-load what they
   project.
3. Iteration of child collections without loading them (`server.Daemons`).

### 5.3 API layer

DTO extending `EnvironmentUiPartBase`; `RefData<Type>sController` per type.

**Authorization follows the `RefDataDaemonsController` precedent, not
`RefDataServersController`** (whose `Post` is entirely ungated and whose `Put`/`Delete`
gating over *mapped* environments is vacuous for unattached items, and whose `Delete`
returns 200-with-false):

- Create: `IsPowerUser || IsAdmin` (403 otherwise).
- Update/Delete: `CanModifyEnvironment` on **every** mapped environment; if the item is
  mapped to no environment, fall back to `IsPowerUser || IsAdmin`. 403 otherwise.
- Attach/Detach: `CanModifyEnvironment` on the target environment. 403 otherwise.
- Reads: any authenticated user.

Audit rows mirror the servers/daemons audit pattern (before/after JSON — see U-8).

### 5.4 Environment data flow (revised in v2)

The new tabs do **not** extend `EnvironmentContentApiModel`. That model is populated
monolithically (`ApiServices.GetEnvironmentsDetails` loads every collection on every
environment-details request) and cached module-wide by `page-env-base.ts`; adding three
collections would tax every environment load whether or not the tabs are opened.

Instead each tab self-fetches on activation via a new per-type
`GET RefData<Type>s/ByEnvId/{envId}` endpoint on its own controller. The nearest existing
precedent is the Daemons tab, which fetches independently of `envContent` — though via
`DaemonStatusController` by environment *name*, and only on a manual "Load Daemons" click
whose button is disabled when `!UserEditable`. Two aspects of that precedent are
**not copied**: the new tabs load automatically on activation, and reads are *not* gated
on editability (per §5.3, reads are open to any authenticated user). `PageEnvBase` still
supplies `environment` (for `UserEditable`) and `environmentId`.

### 5.5 Attach/detach endpoints (U-9)

Today's attach/detach for servers/databases runs through
`RefDataEnvironmentsDetailsController.Put` → `ApiServices.ChangeEnvComponent`, a
stringly-typed `{"server"|"database"} × {"attach"|"detach"}` dispatcher with a string
switch for audit. **Recommended:** give each new controller typed
`PUT <type>/{id}/environments/{envId}` attach / `DELETE …` detach endpoints instead of
growing the dispatcher to five magic strings; the new UI components call the typed
endpoints. The dispatcher and existing UI are untouched.

### 5.6 UI

Replace each stub with a tab mirroring `env-servers.ts`: `attached-<type>s` grid,
`attach-<type>` dialog, `add-edit-<type>` dialog, actions disabled when
`!environment.UserEditable`, refresh via the `environment-stale` event pattern — but data
loading per §5.4. Client regenerated per §4.

## 6. Success Criteria

- **SC-1**: Navigating to `/environment/<env>/components/containers|cloud|apis` shows a
  working grid-based tab; the placeholder text "will be implemented here" no longer exists
  in the repo (today: exactly three files).
- **SC-2**: For each new type, create → attach → edit → detach → delete succeeds end-to-end
  through the UI for a user with environment write permission. Demonstrated by: unit tests
  in `Dorc.Api.Tests` per endpoint, web component tests per new component, and a manual UI
  round-trip recorded at the step's review (if the dev environment cannot run the full
  stack — see U-11 — the manual round-trip transfers to the user's environment and the
  test-level evidence stands as the gate).
- **SC-3**: Each write endpoint returns 403 for a user lacking the §5.3 privilege, verified
  by controller unit tests; UI controls are disabled when `!environment.UserEditable`.
- **SC-4**: Duplicate attach of the same item to the same environment is impossible
  (composite PK) and surfaces as a clean 4xx, not a 500.
- **SC-5**: All existing tests still pass; `dorc-web` builds clean; regenerated client
  diff contains only additions for the new types.

## 7. Alternatives Considered

- **One generic "EnvironmentComponent" table** for all three types: fewer tables, but a
  grab-bag entity with nullable type-specific columns, weak typing through the stack, and
  it compounds the existing "Component" terminology overload. Rejected on cohesion grounds.
- **Extending `EnvironmentContentApiModel`** (v1 approach): rejected in round-1 review —
  taxes the hot environment-details path for data only three tabs need (§5.4).
- **Extending `ApiServices.ChangeEnvComponent`** for attach/detach: keeps one endpoint
  shape but grows a stringly-typed dispatcher; recommended against (§5.5, U-9).

## 8. Unknowns Register

| ID | Unknown | Blocking? | Owner | Proposed resolution |
|----|---------|-----------|-------|---------------------|
| U-1 | Field schemas + entity names for Container / CloudResource / ApiRegistration | **Yes** | User | §5.1 proposal; confirm/amend at HLPS checkpoint. Note: `Container` collides with `Lamar.Container` (DI) — usable with namespace qualification, or pick e.g. `ContainerInstance` |
| U-2 | Attach semantics: many-to-many or owned by one environment | **Yes** | User | Recommend many-to-many — matches servers and databases (daemons map to *servers*, not environments, so the precedent rests on those two) |
| U-3 | Deployment/variable integration now, or CRUD + mapping only | **Yes** | User | Recommend CRUD-only this iteration; integration is a separate HLPS |
| U-4 | Authorization model | **Yes** | User | Recommend §5.3 (Daemons precedent, strengthened for unattached items) |
| U-8 | Audit coverage for new types | No | Agent (IS) | Default: parity with servers/daemons audit pattern |
| U-9 | Typed attach/detach endpoints vs extending `ChangeEnvComponent` dispatcher | No | Agent (IS) | Recommend typed endpoints (§5.5); user may veto at checkpoint |
| U-10 | `Tests.Acceptance` parity coverage for new RefData controllers | No | Agent (IS) | Default: not this iteration; existing acceptance features unaffected |
| U-11 | Can the dev environment run the stack locally — `Dorc.Api` (for `/swagger/v1/swagger.json` client regen) and, for SC-2's manual round-trip, API + SQL database + web dev server together? | No — becomes blocking at the client-regen IS step if the API can't run | Agent (IS) | Verify early in IS; if the full stack can't run, SC-2 falls back to its test-level evidence and the manual UI round-trip transfers to the user's environment |

(v1's U-6/U-7 concerned tag capacity and move to the separate tag PR's HLPS. v1's U-5 —
the swagger regeneration workflow — was *resolved* by `dorc-web/README.md` into the §4
constraint and superseded by U-11; it does not carry to the tag PR.)

## 9. Risks

- **R-5**: Naming: `ApiRegistration` still lands near existing generated-client models,
  and an entity named `Container` collides with `Lamar.Container` (the repo's DI library)
  in files importing both namespaces — resolvable by qualification, surfaced under U-1.
  Final names are checked against the generated client during the client-regen step.
  (Risk IDs R-1/R-2 from v1 are retired: R-1 was removed by the §5.4 design change; R-2 —
  tag-consumer truncation — carries to the separate tag PR.)
- **R-3**: Generated client churn — regeneration rewrites the whole `dorc-api` folder;
  diff noise if generator version drifted. Mitigation: pin via repo's
  `@openapitools/openapi-generator-cli` through `npm run dorc-api-gen`; SC-5 requires an
  additions-only diff.
- **R-4**: Dual schema source drift — SSDT and EF must ship the same DDL in the same step;
  dacpac publish must precede API deployment. Mitigation: IS orders schema steps first;
  step review compares SSDT DDL against EF configuration.
