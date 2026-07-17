# IS: Environment Details Component Tabs — Implementation Sequence

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | DRAFT                                            |
| **Author**  | Agent                                            |
| **Date**    | 2026-07-17                                       |
| **HLPS**    | HLPS-env-details-component-tabs.md (APPROVED v5) |
| **Folder**  | docs/env-details-component-tabs/                 |
| **Branch**  | claude/env-details-component-tabs-p7othg         |

Strategic roadmap only — exact signatures, DDL, and component internals belong to the JIT
spec of each step. Every step ends with the Adversarial Review quality gate; the JIT spec
for step N is authored (with a one-step lookahead) before N executes.

---

## Step Index

| ID    | Title                                                        | Addresses            | Depends On |
|-------|--------------------------------------------------------------|----------------------|------------|
| S-001 | Pre-flight: resolver characterization test + toolchain check | SC-6, R-6, U-11      | —          |
| S-002 | Schema: entity, join, and audit tables (SSDT)                | SC-4, R-4            | —          |
| S-003 | EF model: entities, configurations, DbSets                   | SC-4, R-4            | S-002      |
| S-004 | Persistence + audit sources, DI registration in both registries | SC-2, U-8, R-6    | S-003      |
| S-005 | API: DTOs, RefData controllers (CRUD + typed attach/detach + ByEnvId), controller tests | SC-2, SC-3, SC-4, U-9 | S-004 |
| S-006 | Deployment variable integration (resolver extension)         | SC-6, R-6, R-7       | S-001, S-004 |
| S-007 | Typed client regeneration                                    | SC-5, R-3, U-11      | S-005      |
| S-008 | UI: three tabs + attach/attached/add-edit components + web tests | SC-1, SC-2, SC-3 | S-007      |
| S-009 | Final verification sweep and documentation close-out         | SC-1..SC-6, U-10     | S-006, S-008 |

Two independent tracks after S-004: the integration track (S-006) and the API/UI track
(S-005 → S-007 → S-008). They may proceed in either order; S-009 gates on both.

---

## S-001 — Pre-flight: resolver characterization test + toolchain check

### What changes
A characterization test is added to `Dorc.Core.Tests` capturing the complete set of
`SetPropertyValue` calls (names and values on a mocked `IVariableResolver`) that
`VariableScopeOptionsResolver.SetPropertyValues` produces for a representative environment
fixture — before any production code changes. Alongside, the step verifies the dev
toolchain end to end: backend solution builds, `Dorc.Api` runs locally far enough to serve
`/swagger/v1/swagger.json`, `dorc-web` installs and builds, and existing test suites run.

### Why it changes
SC-6's regression guarantee is only falsifiable against a baseline recorded **before**
S-006 touches the resolver (HLPS §6). The toolchain check retires U-11 at the start
rather than mid-sequence: if the API cannot run locally, the client-regen approach for
S-007 must be renegotiated with the user before any dependent work is scheduled.

### Dependencies
None. Must complete before S-006; the toolchain result gates S-007's plan.

### Verification intent
- The characterization test passes against unmodified production code and fails if any
  existing `SetPropertyValue` emission is removed or altered (demonstrated by a temporary
  mutation during review, not committed).
- U-11 outcome recorded in this step's review: API served the spec / did not, with the
  fallback decision if not.

---

## S-002 — Schema: entity, join, and audit tables (SSDT)

### What changes
`Dorc.Database` gains, in the `deploy` schema: `Container`, `CloudResource`,
`ApiRegistration` entity tables (HLPS §5.1 fields; `Tags` sized to match current server
behaviour, 250, pending the separate tag-capacity PR); `EnvironmentContainer`,
`EnvironmentCloudResource`, `EnvironmentApiRegistration` join tables, each with an
**explicit composite primary key** (EnvId, item id) and FKs to both sides; and
`ContainerAudit`, `CloudResourceAudit`, `ApiRegistrationAudit` tables mirroring
`DaemonAudit`.

### Why it changes
Everything downstream hangs off the schema, and R-4 orders schema first so dacpac publish
can precede API deployment. The composite PK closes the duplicate-attach hole the existing
`EnvironmentServer`/`EnvironmentDatabase` tables have (HLPS §5.1); audit tables are in
scope per the checkpoint default on U-8.

### Dependencies
None.

### Verification intent
- SSDT project builds (dacpac produced).
- DDL review confirms: `deploy` schema, composite PKs present, FK directions correct,
  audit table shape matches `DaemonAudit`.
- No modification to any existing table.

---

## S-003 — EF model: entities, configurations, DbSets

### What changes
`Dorc.PersistentData` gains `Container`, `CloudResource`, `ApiRegistration` model classes
(namespace-qualified where Lamar is imported — U-1), three `IEntityTypeConfiguration`
classes mapping exactly the S-002 DDL (table names, schema, column names, lengths, join
tables with composite keys), audit entities, and `DbSet`s on `DeploymentContext`.

### Why it changes
Second half of the dual schema source (HLPS §4): `EnsureCreated()` must produce the same
shape the dacpac publishes, or fresh and upgraded databases diverge (R-4).

### Dependencies
S-002 (the DDL it must mirror).

### Verification intent
- Side-by-side comparison of each configuration against the S-002 DDL at review (R-4
  gate).
- A model-level test (pattern per existing `Dorc.Api.Tests`/`Dorc.Core.Tests` conventions)
  confirms the context builds its model without errors.

---

## S-004 — Persistence + audit sources, DI registration in both registries

### What changes
Interfaces and implementations for the three persistent sources (list, get, add, update,
delete, get-by-environment, attach, detach) plus three audit sources, registered in
`PersistentDataRegistry.cs` (API) **and** `Dorc.Monitor/Registry/PersistentSourcesRegistry.cs`
(deploy-time consumption per HLPS §5.7). Implementations follow the shape of
`ServersPersistentSource` under the HLPS §5.2 do-not-copy list: null-guards before
dereference, eager loading (`Include`) for every projected collection, no unloaded
navigation reads.

### Why it changes
The API (S-005) and the resolver (S-006) both consume these sources; registering in both
registries up front prevents the R-6 DI-resolution failure mode from ever existing in a
committed state.

### Dependencies
S-003.

### Verification intent
- Unit tests per source covering the do-not-copy defects explicitly: unknown-id delete is
  a no-op/false (no NRE); get-by-id returns populated environment names for attached
  items; attach is idempotent-safe against the composite PK (duplicate attach surfaces as
  a handled failure, not an exception escape — SC-4 groundwork).
- Both registries resolve the new interfaces in a container-build test (R-6 gate,
  Monitor side via `MonitorServiceTestBase`'s use of `PersistentSourcesRegistry.Register`).

---

## S-005 — API: DTOs, RefData controllers, controller tests

### What changes
Three DTOs extending `EnvironmentUiPartBase` in `Dorc.ApiModel`; three controllers
(`RefDataContainersController`, `RefDataCloudResourcesController`,
`RefDataApiRegistrationsController`) exposing: paged/complete reads, `ByEnvId` reads
(HLPS §5.4), create, update, delete, and **typed attach/detach endpoints** (HLPS §5.5,
U-9 — the `ChangeEnvComponent` dispatcher is not extended). Authorization per HLPS §5.3:
create requires PowerUser/Admin; update/delete require `CanModifyEnvironment` on every
mapped environment with PowerUser/Admin fallback when unattached; attach/detach require
`CanModifyEnvironment` on the target environment; all failures return 403. Every write
lands an audit row.

### Why it changes
This is the contract the client and UI are generated against; authorization and audit are
in from the first commit rather than retrofitted (HLPS §2, SC-3).

### Dependencies
S-004.

### Verification intent
- `Dorc.Api.Tests` per controller: each write path tested for the privileged and
  unprivileged caller (403 asserted — SC-3), the unattached-item fallback, duplicate
  attach (SC-4: clean 4xx), and audit row emission.
- Swashbuckle annotations present so S-007 generates a complete client.

---

## S-006 — Deployment variable integration (resolver extension)

### What changes
Per HLPS §5.7: three collection variable names + three `<Type>Names_` prefixes added to
`PropertyValueScopeOptionsFixed`; typed variable DTOs (modelled on `VariableValueServers`)
added to `Dorc.ApiModel` (where the runner-side `VariableValueJsonConverter` resolves
types); `AddPropertiesForServerNamesByType` generalized (preserving space→underscore and
scalar-when-one/array-when-many semantics) and shared; `VariableScopeOptionsResolver`
gains the three sources by constructor injection and emits the new variables
**conditionally** — only when at least one item of that type is attached.

### Why it changes
The U-3 checkpoint decision. Conditional emission keeps the change inert for environments
not using the new types (SC-6, R-6); the shared generalization avoids triplicating tag
logic.

### Dependencies
S-001 (characterization baseline must exist first), S-004 (sources).

### Verification intent
- The S-001 characterization test still passes unmodified (SC-6 empty-environment
  regression).
- New `Dorc.Core.Tests` cover: each collection variable emitted when items attached and
  absent when not; per-tag variables including both generalized quirks; environments
  mixing tagged and untagged items.
- R-7 gate: query against existing property definitions (via `IPropertiesPersistentSource`
  usage patterns) for collisions with the new fixed names/prefixes; resolver precedence
  documented in the step's JIT spec.
- R-6 gate: `Dorc.Monitor.IntegrationTests` container builds and resolves the resolver
  with its new dependencies.

---

## S-007 — Typed client regeneration

### What changes
`swagger.json` refreshed from the locally-run API (per `dorc-web/README.md` and the S-001
toolchain result), then `npm run dorc-api-gen` regenerates the `dorc-api` client.

### Why it changes
The UI (S-008) consumes generated models/apis; hand-editing generated files is prohibited
(HLPS §4).

### Dependencies
S-005 (endpoints must exist to appear in the spec). If S-001 recorded that the API cannot
run locally, this step halts and escalates per U-11 before S-008 is scheduled.

### Verification intent
- Regenerated diff reviewed: additions for the new types plus at most formatting-stable
  churn; no removals of existing models/apis (SC-5).
- `dorc-web` compiles against the regenerated client.

---

## S-008 — UI: three tabs + supporting components + web tests

### What changes
`env-containers.ts`, `env-cloud.ts`, `env-apis.ts` replaced with real tabs mirroring
`env-servers.ts` structure but self-fetching via the `ByEnvId` endpoints on activation
(HLPS §5.4 — automatic load, reads not gated on editability). Supporting components per
type: attached-items grid, attach dialog, add/edit dialog; write controls disabled when
`!environment.UserEditable`; refresh via the `environment-stale` pattern.

### Why it changes
SC-1/SC-2: this is the user-visible deliverable the stubs promised.

### Dependencies
S-007.

### Verification intent
- Web component tests per tab and per dialog in `src/dorc-web/tests` following existing
  patterns (grid renders fetched items; controls disabled/enabled by editability; attach
  flow fires the expected API call and refresh event).
- No placeholder text remains in the repo (SC-1's grep gate).
- Manual UI round-trip per SC-2, or its U-11 fallback.

---

## S-009 — Final verification sweep and documentation close-out

### What changes
No new production code. Full test-suite run across `Dorc.Api.Tests`, `Dorc.Core.Tests`,
`src/dorc-web/tests`; `Tests.Acceptance` impact check (U-10 — verify, not assume, that
existing acceptance features are unaffected by the resolver change); SC-1..SC-6 checklist
walked and evidenced; HLPS/IS statuses closed out; release note drafted covering the R-4
deploy ordering requirement (dacpac before API/Monitor).

### Why it changes
The HLPS success criteria are the contract; this step is where the contract is signed.

### Dependencies
S-006 and S-008 (i.e. everything).

### Verification intent
- Every SC has recorded evidence (test name, diff, or screenshot).
- Any unresolved item becomes an explicit user escalation, not a silent gap.
