# HLPS: Environment Details Component Tabs Completion + Tag Capacity Expansion

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | DRAFT                                        |
| **Author**  | Agent                                        |
| **Date**    | 2026-07-16                                   |
| **Folder**  | docs/env-details-component-tabs/             |
| **Branch**  | claude/env-details-component-tabs-p7othg     |

---

## 1. Problem Statement

The environment details page presents a **Components** tab
(`src/dorc-web/src/pages/page-environment-components.ts`) with six sub-tabs routed under
`/environment/:id/components/*`. Three of them — **Containers**, **Cloud**, and **APIs** —
are placeholder stubs (`env-containers.ts`, `env-cloud.ts`, `env-apis.ts`, ~39 lines each)
that render only "*… functionality will be implemented here.*" Users navigating to these
tabs see dead ends in a shipped UI. There is **no backend support at all** for these three
component types: no SQL tables, EF entities, persistent sources, API models, or controllers.

Separately, **tags cannot be used at the scale users now need**. Server tags
(`ApplicationTags`, semicolon-separated) are capped at 250 characters by EF configuration,
and database tags (`ArrayName`) at 50 characters — but the layers disagree with each other,
so the effective limit is the *lowest* layer and overruns fail late (silent truncation or a
DB-layer exception) instead of failing clearly at the API boundary:

| Layer | Server `ApplicationTags` | Database `ArrayName` |
|-------|--------------------------|----------------------|
| SQL (`Dorc.Database/dbo/Tables/`) | `NVARCHAR(1000)` (`SERVER.Application_Server_Name`) | `NVARCHAR(250)` (`DATABASE.Array_Name`) |
| EF (`EntityTypeConfigurations/`) | `HasMaxLength(250)` (`ServerEntityTypeConfiguration.cs:32`) | `HasMaxLength(50)` (`DatabaseEntityTypeConfiguration.cs`) |
| API validation | none | none |
| UI | `tags-input.ts`: no limit | `add-edit-database.ts`: `maxlength=50` |

## 2. Desired Outcome

1. All six Components sub-tabs are functional. Containers, Cloud, and APIs each support:
   viewing items attached to the environment, attaching/detaching existing items, and
   creating/editing/deleting items — with the same permission gating as Servers.
2. Server and database tags accept values far beyond 250 characters, with **one consistent
   limit enforced at every layer** and a clear 400 response when exceeded.

## 3. Scope

**In scope:**
- `Dorc.Database` (new tables + widened columns), `Dorc.PersistentData` (entities,
  configurations, `DeploymentContext`, persistent sources), `Dorc.ApiModel` (DTOs),
  `Dorc.Api` (new RefData controllers, `EnvironmentContentApiModel` extension, validation)
- `src/dorc-web`: regenerated `dorc-api` client (`npm run dorc-api-gen`), replacement of the
  three stub tabs, tag-related components (`tags-input.ts`, `server-tags.ts`,
  `add-edit-database.ts`)
- Unit tests in `Dorc.Api.Tests` and web component tests in `src/dorc-web/tests`

**Out of scope:**
- Live introspection of container runtimes / cloud providers / API gateways — this
  iteration is **reference-data CRUD + environment mapping only** (matching how Servers
  work: DOrc records what exists; it does not discover it). (See U-3.)
- Deployment/variable-resolution integration for the new component types
  (`VariableScopeOptionsResolver` reads server tags; no equivalent for new types yet).
- Changing the semicolon tag encoding or migrating tags to a normalised table (see §7
  Alternatives).
- The existing working tabs (Servers, Databases, Daemons) beyond the tag changes above.

## 4. Constraints

- C# / .NET, existing repo patterns; namespaces `Dorc.[Component].[Feature]`; no grab-bag
  class names.
- Schema changes are declarative via the SSDT project; only non-destructive (widening)
  changes to existing columns.
- The typed web client is generated from `swagger.json` via `npm run dorc-api-gen`
  (`typescript-rxjs`); hand-editing generated files is not acceptable.
- Write operations on the new component types must reuse
  `ISecurityPrivilegesChecker.CanModifyEnvironment` gating, as
  `RefDataServersController.Put/Delete` does.
- Test-first development; adversarial review per step (CLAUDE.md).

## 5. Proposed Solution Shape (strategic — details in IS/JIT specs)

### 5.1 Three new component types (mirror the Servers vertical)

For each of **Container**, **CloudResource**, and **ApiEndpoint** (working names, see U-1):

1. **SQL**: `dbo` table + `deploy.Environment<Type>` join table (page-compressed PK,
   style of `SERVER.sql` / `EnvironmentServer`).
2. **EF**: model class + `IEntityTypeConfiguration` + `DbSet` on `DeploymentContext`
   (mirror `Server.cs` / `ServerEntityTypeConfiguration.cs`).
3. **Persistence**: `I<Type>sPersistentSource` + implementation (list/get/add/update/
   delete/attach/detach, mirroring `ServersPersistentSource`).
4. **API**: DTO extending `EnvironmentUiPartBase`; `RefData<Type>sController` with the
   same authorization shape as `RefDataServersController` (writes require
   `CanModifyEnvironment` on every mapped environment); audit rows mirroring
   `IServersAuditPersistentSource` usage.
5. **Environment content**: extend `EnvironmentContentApiModel` (and its population in the
   RefDataEnvironmentsDetails path) with collections for the three new types so
   `PageEnvBase.envContent` feeds the tabs the same way `AppServers` feeds `env-servers`.
6. **UI**: replace each stub with a tab mirroring `env-servers.ts`: `attached-<type>s`
   grid, `attach-<type>` dialog, `add-edit-<type>` dialog, actions disabled when
   `!environment.UserEditable`, refresh via `environment-stale` pattern.

Proposed minimal schemas (to be confirmed — U-1; all string fields NVARCHAR, generous):

- **Container**: Id, Name, Image, Registry, HostServerName (nullable), Tags
- **CloudResource**: Id, Name, Provider, ResourceType, ResourceIdentifier (URI/ARN/id),
  Subscription (nullable), Tags
- **ApiEndpoint**: Id, Name, BaseUrl, Version (nullable), HealthCheckUrl (nullable), Tags

All three carry `Tags` from day one, sharing the server tag limit and `tag-parser.ts`
semantics.

### 5.2 Tag capacity expansion

1. Single limit constant **4000 characters** (recommended; see U-6) applied to:
   - `SERVER.Application_Server_Name` → `NVARCHAR(4000)` (widen from 1000)
   - `DATABASE.Array_Name` → `NVARCHAR(4000)` (widen from 250)
   - `HasMaxLength(4000)` in both entity configurations
   - New component types' `Tags` columns
2. API-layer max-length validation on the affected DTO properties so overlong input
   returns 400 with a clear message (today: EF/SqlClient behaviour is truncation or an
   opaque exception).
   Rationale for 4000 over `NVARCHAR(MAX)`: tags are filtered with `Contains` (e.g.
   `ServersPersistentSource.cs:77`); keeping the column a sized NVARCHAR preserves the
   option of indexing and avoids MAX-column row-overflow costs on hot ref-data tables.
3. UI: raise `add-edit-database.ts` `maxlength` for `ArrayName` to 4000; verify
   `tags-input.ts` / grids render large tag sets acceptably (`page-servers-list.ts:689`,
   `attached-servers.ts:181`).
4. **Optional (U-7)**: replace the plain `ArrayName` text input with the chip-style
   `tags-input` editor (as `server-tags.ts` does), making database tags first-class.

## 6. Success Criteria

- Navigating to `/environment/<env>/components/containers|cloud|apis` shows a working
  grid-based tab (no placeholder text remains in the repo).
- Each new type supports create, edit, delete, attach, detach end-to-end through the UI,
  with write actions rejected (403 / disabled controls) for users lacking environment
  write permission.
- A server tag string and database tag string of 3,900 characters round-trips through
  UI → API → DB → UI unmodified; a 4,100-character string is rejected with HTTP 400 and a
  human-readable message.
- All existing and new unit/component tests pass; `dorc-web` builds clean.

## 7. Alternatives Considered

- **NVARCHAR(MAX) for tags**: maximum headroom, but forfeits indexability and adds
  row-overflow I/O on tables queried with `Contains` filters. Rejected pending U-6.
- **Normalised tag table (Tag + join tables)**: cleanest long-term model, enables per-tag
  queries and dedup, but a much larger migration touching every tag consumer, and out of
  proportion to the stated need (longer tag strings). Deferred — the 4000-char widening
  does not preclude it later.
- **One generic "EnvironmentComponent" table for containers/cloud/APIs**: fewer tables,
  but degenerates into a grab-bag entity with nullable type-specific columns and weakens
  typing through the whole stack. Rejected on cohesion grounds (CLAUDE.md naming
  principle applies to schemas too).

## 8. Unknowns Register

| ID | Unknown | Blocking? | Proposed resolution |
|----|---------|-----------|---------------------|
| U-1 | Field schemas for Container / CloudResource / ApiEndpoint | **Yes** | §5.1 proposal; user confirms/amends at HLPS checkpoint |
| U-2 | Attach semantics: many-to-many (like servers/databases) or owned by one environment | **Yes** | Recommend many-to-many for consistency with all existing component types |
| U-3 | Deployment/variable integration now, or CRUD + mapping only | **Yes** | Recommend CRUD-only this iteration; integration is a separate HLPS |
| U-4 | Permission model for new types | **Yes** | Recommend identical to servers: reads authenticated, writes gated by `CanModifyEnvironment` on mapped environments |
| U-5 | `swagger.json` regeneration workflow (file is committed; generator consumes it — how is it refreshed from the running API?) | No — resolvable during IS | Investigate repo scripts/CI; worst case, hand-extend swagger.json consistently with existing entries before running `dorc-api-gen` |
| U-6 | Tag limit value: 4000 vs MAX vs other | **Yes** (user preference) | Recommend 4000 |
| U-7 | Chip-style tags editor for database `ArrayName` | **Yes** (user preference, small scope impact) | Recommend yes — reuses `tags-input` + `tag-parser` |
| U-8 | Audit coverage for new types (servers have `ServerAudit`; databases likewise) | No — default to parity | Mirror the servers audit pattern unless user objects |

## 9. Risks

- **R-1**: `EnvironmentContentApiModel` is on a hot path (environment details load); three
  new eager collections could slow the endpoint. Mitigation: load only what the tabs need;
  measure before/after.
- **R-2**: Widening `Array_Name`/`Application_Server_Name` is non-destructive, but any
  external consumer (reports, scripts) assuming old widths could truncate downstream.
  Mitigation: call out in release notes; no in-repo consumers found beyond those listed.
- **R-3**: Generated client churn — `dorc-api-gen` regenerates the whole `dorc-api`
  folder; unrelated diff noise possible if generator version drifted. Mitigation: pin to
  repo's `@openapitools/openapi-generator-cli` version via `npm run`.
