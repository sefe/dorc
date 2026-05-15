# HLPS: Environment APIs Tab

| Field      | Value                                  |
|------------|----------------------------------------|
| **Status** | APPROVED                               |
| **Author** | Agent                                  |
| **Date**   | 2026-05-06                             |
| **Folder** | docs/api-tab/                          |
| **Branch** | claude/implement-api-tab-01dlx         |

---

## 1. Problem Statement

The environment-detail page in `dorc-web` exposes a **Components** tab with sub-tabs for Servers, Databases, Daemons, Containers, Cloud, and APIs. The first three are functional; Containers, Cloud, and APIs are placeholders left over from the Components-tab reorganisation in PR #192 (commit `3e08d64`). The placeholder bodies render only "API management functionality will be implemented here".

The codebase has no concept of an **API** as a domain entity:

- No table in `src/Dorc.Database/dbo/Tables/` (only AD_GROUP, DATABASE, ENVIRONMENT_USER_MAP, PERMISSION, SERVER, SQL_PORTS, USERS).
- No EF model in `src/Dorc.PersistentData/Model/` and no `IApisPersistentSource` in `src/Dorc.PersistentData/Sources/`.
- No `ApiApiModel` in `src/Dorc.ApiModel/`; `EnvironmentContentApiModel` has no API collection (`EnvironmentContentApiModel.cs:27-73`).
- No controller in `src/Dorc.Api/Controllers/`.
- No client model in `src/dorc-web/src/apis/dorc-api/models/`.

Operators currently have no way in DOrc to record or look up the HTTP endpoints that an environment exposes (e.g. `https://api-uat.example.com/orders`). That information lives in tribal knowledge, wikis, or scattered config — making smoke testing, troubleshooting, and onboarding slower than they need to be.

A second concern raised by the user: endpoints frequently differ between environments only in **host/port/path-segment**, and those differences typically mirror values already held in the environment's variable bag (`PropertyValue` rows scoped to the environment). The feature should leverage the existing variable-substitution machinery (`Dorc.Core.VariableResolution.PropertyParser` — `$VarName$` syntax, `PropertyParser.cs:7`) so that an endpoint can be authored once with placeholders and resolved against the environment's own property values for display.

---

## 2. Scope

### In scope
- A new **API** domain entity persisted alongside an environment, with the minimum attributes required for v1 (final shape is **U-1**).
- CRUD on that entity from the existing `env-apis` placeholder, conforming to the Components-tab UX patterns already established by `env-databases` / `env-servers` (`env-databases.ts:18-186`).
- Resolution of `$Variable$` placeholders in endpoint strings against the environment's own scoped `PropertyValue` rows, reusing `IVariableResolver` / `PropertyEvaluator` rather than duplicating substitution logic.
- A read view that shows both the raw endpoint (with placeholders) and the resolved endpoint (with substitutions applied).
- Permission gating consistent with sibling Components tabs (`UserEditable` flag — `env-databases.ts:73`).
- Regeneration of the OpenAPI client and addition of `Apis` to `EnvironmentContentApiModel`.

### Out of scope (v1)
- Live health-check / ping of resolved endpoints (would require outbound HTTP from the API host and a security review).
- Sharing one API record across many environments (attach/detach semantics) — depends on **U-2**.
- Authentication-credential storage (token vaults, OAuth client secrets).
- Deployment-pipeline integration (using API records as deploy targets) — explicitly a follow-up.
- Audit history of API changes (depends on **U-7**).

---

## 3. Goals and Success Criteria

| ID    | Criterion |
|-------|-----------|
| SC-01 | Authorised users can list, add, edit, and delete the APIs associated with an environment from the Components → APIs sub-tab, with grid behaviour matching the Databases tab. |
| SC-02 | An endpoint stored as `https://$ApiHost$:$ApiPort$/v1` and read against an environment whose `ApiHost = api-uat.example.com` and `ApiPort = 8443` displays as `https://api-uat.example.com:8443/v1` in the resolved column. |
| SC-03 | Unresolved placeholders (no matching property) leave the raw token visible in the resolved column and are visually flagged so the operator notices missing variables (consistent with `PropertyEvaluator.Evaluate` returning the original string when a token is unresolvable — `PropertyEvaluator.cs:31-33`). |
| SC-04 | A non-editable user (one for whom `environment.UserEditable === false`) sees the grid but cannot mutate it — buttons disabled, mirroring `env-databases.ts:73`. |
| SC-05 | The OpenAPI client is regenerated and `EnvironmentContentApiModel.Apis` is populated by `RefDataEnvironmentsDetailsApi.refDataEnvironmentsDetailsIdGet` so `env-apis` can drive its grid the same way `env-databases` drives `attached-databases`. |
| SC-06 | Build is green (`dotnet build`, `npm run build` in `src/dorc-web`); unit tests for the new persistent source and controller pass; a new FE test exercises the resolution display. |

---

## 4. Constraints

- **C-01** — Must follow `CLAUDE.md` coding standards: cohesive class names (no `*Manager` / `*Helper` / `*Service-as-grab-bag`), namespace `Dorc.[Component].[Feature]`, C#, current LTS .NET.
- **C-02** — Must not modify existing variable-substitution semantics; the API tab is a **consumer** of `IVariableResolver`, not an author of new resolution rules.
- **C-03** — Must not introduce a new permission system; reuse `UserEditable` and `ClaimsPrincipal`-based authorisation already enforced by `IEnvironmentsPersistentSource`.
- **C-04** — Must not break existing Components-tab routing; the route `/environment/:id/components/apis` is already wired (`routes.ts:361-367`) and stays as is.
- **C-05** — Schema changes ship via the existing `Dorc.Database` SQL project (`.sqlproj`), not via EF migrations (the rest of the schema is hand-authored SQL — see `dbo/Tables/`).

---

## 5. Proposed Solution Direction

The feature decomposes into five layers, in dependency order. Exact methods, signatures, and SQL DDL are deliberately deferred to JIT specs.

### SD-1 — Schema and EF entity for an API record
A new `dbo.API` table (or whatever name the team prefers; see **U-1** for the exact column set) with at minimum: `Id`, `Name`, `Endpoint` (raw, with `$Var$` placeholders allowed), `Description`. Mapping to environments follows the choice in **U-2**: either FK `EnvironmentId` (composition) or a new `ENVIRONMENT_API_MAP` join table (shared, attach/detach). A matching EF entity in `Dorc.PersistentData.Model.Api` and configuration in `Dorc.PersistentData.EntityTypeConfigurations`.

### SD-2 — Persistent source and ApiModel
`IApisPersistentSource` / `ApisPersistentSource` for CRUD and environment-scoped queries. `ApiApiModel` in `Dorc.ApiModel` (raw fields only). A separate `ApiApiModel` shape returned to the FE that includes a server-resolved `EndpointResolved` field (option (b) in **U-3**) — the resolution step calls into `IVariableResolver` seeded from `IPropertyValuesPersistentSource.GetEnvironmentProperties(envName)`. This keeps substitution logic single-sourced.

### SD-3 — Controller surface
A new `RefDataApisController` with the standard verbs (`GET` by env, `POST`, `PUT`, `DELETE`) following the shape of `RefDataDatabasesController` and `RefDataEnvironmentsDetailsController`. Add `IEnumerable<ApiApiModel> Apis` to `EnvironmentContentApiModel` and populate it in `ApiServices.GetEnvironmentsDetails` (`ApiServices.cs:60-85`) so the existing `RefDataEnvironmentsDetailsApi` endpoint already used by `env-databases` returns APIs in the same payload — no second round-trip on tab load.

### SD-4 — OpenAPI client regeneration
Run the existing FE OpenAPI generator (`src/dorc-web/openapitools.json`) to produce `ApiApiModel.ts` and `RefDataApisApi.ts`. Hand edits to generated files are forbidden; regeneration is the only path.

### SD-5 — Frontend
Replace the `env-apis.ts` placeholder with a real component modelled directly on `env-databases.ts`:
- A `vaadin-grid` of APIs with sortable Name / Endpoint / Resolved Endpoint / Description columns.
- An "Add API" button opening a `vaadin-dialog` with an `add-edit-api` form (name + raw endpoint + description); validation rejects empty name and empty endpoint.
- A row-level edit/delete affordance, gated on `UserEditable`.
- The Resolved Endpoint column uses a cell renderer that styles unresolved tokens (e.g. `$NotFound$`) distinctly to satisfy SC-03.
- An optional autocomplete on the endpoint input that suggests environment-scoped property names — depends on **U-6**.

### SD-6 — Tests
- BE: unit tests for `ApisPersistentSource` (CRUD, env-scope filter) and a controller test that mocks `IVariableResolver` and asserts `EndpointResolved` is populated correctly for a known property bag.
- FE: a Vitest spec that mounts `env-apis`, feeds a fake `EnvironmentContentApiModel` with one API and one resolved variable, and asserts both raw and resolved cells render as expected.

---

## 6. Unknowns Register

| ID  | Question | Blocking? | Resolution |
|-----|----------|-----------|------------|
| U-1 | What attributes does an "API" record carry beyond Name + Endpoint + Description? Candidates: Type/Protocol, AuthType, HealthCheckPath, OwnerProject, Tags. | **YES** | **RESOLVED 2026-05-06 — user accepted all candidates.** v1 attributes: `Name`, `Endpoint` (raw, with `$Var$` placeholders allowed), `Description`, `Type` (REST / SOAP / gRPC), `AuthType` (None / Basic / Bearer / OAuth), `HealthCheckPath`, `OwnerProject` (FK to existing `Project`), `Tags` (free-form string, mirroring `Server.ApplicationTags` — `Server.cs:11`). |
| U-2 | Is an API a **first-class shared entity** with attach/detach to many environments, or an **environment-private composition** (FK back to environment, deleted on env delete)? | **YES** | **RESOLVED 2026-05-06 — user chose env-private composition.** `dbo.API` carries an `EnvironmentId` FK with `ON DELETE CASCADE`. No join table. No attach/detach UI. The FE pattern of choice is therefore inline add/edit, not the `attach-database.ts` pattern. |
| U-3 | Where does variable substitution happen — server-side (BE returns `EndpointResolved`), client-side, or both? | NO | **DEFAULT TAKEN: server-side.** BE populates `EndpointResolved` using `IVariableResolver` seeded from `IPropertyValuesPersistentSource.GetEnvironmentProperties(envName)`. Override at IS review if you'd rather keep resolution client-side. |
| U-4 | Permission model — is `UserEditable` sufficient, or do APIs need their own privilege class? | NO | **DEFAULT TAKEN: reuse `UserEditable`.** Same gate the Databases / Servers / Daemons tabs already enforce. |
| U-5 | Is v1 full CRUD, or list-only with create/edit/delete deferred? | NO | **DEFAULT TAKEN: full CRUD.** |
| U-6 | Should the endpoint input offer autocomplete from environment-scoped property names? | NO | **DEFAULT TAKEN: no autocomplete in v1.** Free-form `$Var$` text. Tracked as a follow-up. |
| U-7 | Do API changes flow through the existing `RefDataAudit` / environment-history audit trail? | NO | **DEFAULT TAKEN: yes.** Wired through `RefDataAudit` for symmetry with Databases / Servers. |
| U-8 | Should resolved endpoints be **clickable** when the resolved string parses as `https?://…`? | NO | **DEFAULT TAKEN: yes.** Click opens in a new tab. Unresolved or non-URL values render as plain text. |

All blocking unknowns are resolved; the IS may proceed.

---

## 7. Out-of-Scope Risks

- **R-01** — If a future requirement is to call resolved endpoints from the API host (health checks, smoke tests), outbound HTTP from `Dorc.Api` to arbitrary URLs introduces SSRF and credential-leak surface. Mitigation: handle in a follow-up HLPS that explicitly addresses egress controls.
- **R-02** — If APIs become deploy targets (e.g. used by `Dorc.Runner` to pick a base URL), the resolution semantics need to match the deploy-time variable resolver, not the request-time one. Defer to a follow-up; v1 is **viewing only**.
- **R-03** — If endpoints contain credentials in the path (anti-pattern but common), accidental display in a grid can leak them. Mitigation: document that endpoints must not embed credentials; consider a future "secure" flag analogous to `Property.Secure`.

---

## 8. Acceptance and Next Step

This HLPS is **APPROVED**. All blocking unknowns are resolved (U-1, U-2). Defaults were taken on U-3 through U-8 at the user's instruction to "consider all checkpoints approved" and may be revisited if the implementation surfaces issues.

The next checkpoint is the **IS document** (`docs/api-tab/IS-api-tab.md`) breaking the work into ordered atomic steps; both were approved as a unit and the implementation has shipped on this branch.
