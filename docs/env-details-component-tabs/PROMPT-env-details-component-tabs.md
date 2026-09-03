# Prompt: Finish Environment Details Component Tabs + Expand Server/Database Tag Capacity

> Paste this prompt into a fresh session on branch `claude/env-details-component-tabs-p7othg`.
> Follow the development process in `CLAUDE.md`: produce an HLPS, then an IS, then JIT specs
> under `docs/env-details-component-tabs/`, each moving DRAFT → IN REVIEW → APPROVED via the
> adversarial review panel, with user checkpoints after HLPS approval, after IS approval, and
> before each step.

## Context

DOrc is a deployment orchestration tool. Frontend: Lit + Vaadin web components in
`src/dorc-web` (TypeScript, typed API client generated from `src/dorc-web/src/apis/dorc-api/swagger.json`).
Backend: C# — `Dorc.Api` (ASP.NET controllers), `Dorc.ApiModel` (DTOs), `Dorc.PersistentData`
(EF Core entities + `IEntityTypeConfiguration`s + persistent sources), `Dorc.Database`
(SSDT SQL project). Tests: `Dorc.Api.Tests`, `Dorc.Core.Tests`, `src/dorc-web/tests`.

The environment details page (`src/dorc-web/src/pages/page-environment.ts`) has top-level tabs
Metadata, Variables, Components, Projects, Deployments, Tenants, Monitor, Users. The
**Components** tab (`src/dorc-web/src/pages/page-environment-components.ts`) hosts sub-tabs
routed in `src/dorc-web/src/router/routes.ts` under `/environment/:id/components/*`.

## Current state (verified 2026-07-16)

| Sub-tab    | Component (`src/dorc-web/src/components/environment-tabs/`) | State |
|------------|--------------------------------------------------------------|-------|
| Servers    | `env-servers.ts`                                             | Working |
| Databases  | `env-databases.ts`                                           | Working |
| Daemons    | `env-daemons.ts`                                             | Working |
| Containers | `env-containers.ts`                                          | **Placeholder stub** |
| Cloud      | `env-cloud.ts`                                               | **Placeholder stub** |
| APIs       | `env-apis.ts`                                                | **Placeholder stub** |

Each stub (~39 lines) extends `PageEnvBase` and renders only
"*… functionality will be implemented here.*" There is **no backend support at all** for
containers, cloud resources, or environment APIs — no SQL tables, EF entities, persistence
sources, API models, or controllers exist for them.

## Task 1 — Implement the three unfinished Component sub-tabs

For each of **Containers**, **Cloud**, and **APIs**, deliver the full stack, mirroring the
established Servers pattern end to end:

1. **SQL** — new tables in `Dorc.Database` following the style of `dbo/Tables/SERVER.sql`,
   with environment join tables in the `deploy` schema like `EnvironmentServer`
   (see `ServerEntityTypeConfiguration.cs` for the join shape).
2. **EF Core** — entity in `Dorc.PersistentData/Model` + configuration in
   `Dorc.PersistentData/EntityTypeConfigurations` (mirror `Server.cs` /
   `ServerEntityTypeConfiguration.cs`), registered in the DbContext.
3. **Persistence source** — interface + implementation mirroring
   `Sources/ServersPersistentSource.cs` (list, get, create, update, delete,
   attach-to/detach-from environment).
4. **API** — DTO in `Dorc.ApiModel` (extending `EnvironmentUiPartBase` like
   `ServerApiModel`) and a RefData controller mirroring
   `Dorc.Api/Controllers/RefDataServersController.cs`, including the same
   authorization/permission handling.
5. **Client regeneration** — update `swagger.json` and the generated
   `apis/dorc-api` models/apis in `dorc-web` using the repo's existing OpenAPI
   generator workflow.
6. **UI** — replace each stub with a real tab modelled on `env-servers.ts`:
   Vaadin grid of attached items, attach/detach controls (see `attach-server.ts`
   / `attached-servers.ts`), add/edit dialog (see `add-edit-server.ts`), actions
   gated on `environment.UserEditable`, loading via `PageEnvBase.loadEnvironmentInfo()`.

### Unknowns Register seeds (blocking — resolve at HLPS checkpoint)

- **U-1 Schema:** What fields define a Container, a Cloud resource, and an API entry?
  Propose a minimal schema for each (e.g. Container: name, image, registry, host/server,
  tags; Cloud: name, provider, resource type, resource id/URI, subscription, tags;
  API: name, base URL, version, health endpoint, tags) and get user sign-off before IS.
- **U-2 Attach semantics:** Are these entities shared across environments (many-to-many,
  like servers/databases) or owned by a single environment? Recommend many-to-many for
  consistency; confirm.
- **U-3 Deployment integration:** Do any of these need to participate in deployment/variable
  resolution now (like `VariableScopeOptionsResolver` does for servers), or is CRUD +
  environment mapping sufficient for this iteration? Recommend CRUD-only first; confirm.
- **U-4 Permissions:** Same write-permission model as servers (PowerUser/Admin)? Confirm.

## Task 2 — Expand tag capacity on servers and databases (well beyond 250 chars)

Tags are being used far more heavily than the current limits allow. Server tags live in
`ApplicationTags` (semicolon-separated, edited via `components/server-tags.ts` +
`components/tags-input.ts`, parsed by `helpers/tag-parser.ts`). The database equivalent is
the `ArrayName` field (plain text input today).

Current limits by layer — note the layers already disagree with each other:

| Layer | Server tags | Database ArrayName |
|-------|-------------|--------------------|
| SQL (`Dorc.Database/dbo/Tables/`) | `SERVER.Application_Server_Name NVARCHAR(1000)` | `DATABASE.Array_Name NVARCHAR(250)` |
| EF (`Dorc.PersistentData/EntityTypeConfigurations/`) | `ServerEntityTypeConfiguration.cs:32` → `HasMaxLength(250)` | `DatabaseEntityTypeConfiguration.cs` → `HasMaxLength(50)` |
| API validation (`Dorc.ApiModel`, controllers) | none | none |
| UI | `tags-input.ts` — no explicit limit | `add-edit-database.ts` `maxFieldLength = 50` |

Required outcome:

1. Pick one generous limit — **recommend `NVARCHAR(4000)`** (avoid `NVARCHAR(MAX)` unless
   justified: tags are filtered with `Contains` in e.g.
   `ServersPersistentSource.cs:77` and MAX columns can't be indexed) — and apply it
   **consistently** to SQL project, EF configuration, and UI for **both**
   `SERVER.Application_Server_Name` and `DATABASE.Array_Name`. Widening via SSDT is a
   safe, non-destructive schema change.
2. Add explicit API-layer validation (max length) so overlong input returns a clear 400
   instead of a truncation or a DB exception.
3. Fix the UI `maxlength` attributes to match the new limit
   (`add-edit-database.ts`), and verify `tags-input.ts`/`server-tags.ts` handle long
   tag sets gracefully (grid rendering of many tags in `page-servers-list.ts:689` and
   `attached-servers.ts:181`).
4. **Propose (decide with user at HLPS):** give databases the same `tags-input` chip-style
   editing experience as servers instead of the plain `ArrayName` text field, so database
   tags become first-class (split/join via `tag-parser.ts`).

## Quality bar

- Test-first: unit tests for new persistence sources and controllers in the existing test
  projects; web component tests in `src/dorc-web/tests` following existing patterns.
- Adversarial review panel (2–4 reviewers) on every step per `CLAUDE.md`; max 3 rounds.
- C# only; no grab-bag class names (`Manager`, `Helper`, …); namespaces follow
  `Dorc.[Component].[Feature]`.
- All planning artifacts persisted under `docs/env-details-component-tabs/`.
- Commit per IS step with descriptive messages; push to
  `claude/env-details-component-tabs-p7othg`.
