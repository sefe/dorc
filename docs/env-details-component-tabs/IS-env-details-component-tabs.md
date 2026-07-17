# IS: Environment Details Component Tabs — Implementation Sequence

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | **DELIVERED** 2026-07-17 — S-001..S-008 executed, all step gates APPROVED (REVIEW-STEPS.md); SC-2 manual round-trip transfers to the user's environment (VERIFICATION-S-008.md) |
| **Author**  | Agent                                            |
| **Date**    | 2026-07-17                                       |
| **HLPS**    | HLPS-env-details-component-tabs.md (APPROVED v5) |
| **Folder**  | docs/env-details-component-tabs/                 |
| **Branch**  | claude/env-details-component-tabs-p7othg         |

Strategic roadmap only — exact signatures, DDL, and component internals belong to the JIT
spec of each step. Every step ends with the Adversarial Review quality gate; the JIT spec
for step N is authored (with a one-step lookahead) before N executes.

> **v2 changes** (round-1 panel findings, see `REVIEW-IS-round1.md`): former S-002/S-003
> merged into one dual-source schema step per HLPS §4; S-001 added to the client-regen
> step's dependencies; R-5 name-collision check given an owning step; U-11's full-stack
> half moved to pre-flight; step-index "Addresses" corrected; verification intents made
> checkable at each step's own boundary. Steps renumbered S-001..S-008.
>
> **Note for the IS-approval checkpoint:** per HLPS U-9, this approval is the user's
> designated moment to veto **typed attach/detach endpoints** (S-004) in favour of
> extending the existing `ChangeEnvComponent` dispatcher.

---

## Step Index

| ID    | Title                                                        | Addresses                  | Depends On |
|-------|--------------------------------------------------------------|----------------------------|------------|
| S-001 | Pre-flight: resolver characterization test + toolchain record | SC-6, R-6, U-11           | —          |
| S-002 | Schema, dual-sourced: SSDT + EF in one step                  | SC-4, R-4, U-8             | —          |
| S-003 | Persistence + audit sources, DI registration in both registries | SC-4, U-8, R-6          | S-002      |
| S-004 | API: DTOs, RefData controllers (CRUD + typed attach/detach + ByEnvId), controller tests | SC-2, SC-3, SC-4, U-9 | S-003 |
| S-005 | Deployment variable integration (resolver extension)         | SC-6, R-6, R-7             | S-001, S-003 |
| S-006 | Typed client regeneration                                    | SC-5, R-3, R-5, U-11       | S-001, S-004 |
| S-007 | UI: three tabs + supporting components + web tests           | SC-1, SC-2, SC-3, U-11     | S-006      |
| S-008 | Final verification sweep and documentation close-out         | SC-1..SC-6, R-4, U-10      | S-005, S-007 |

Two independent tracks after S-003: the integration track (S-005) and the API/UI track
(S-004 → S-006 → S-007). Verified order-independent: no variable DTO appears in the
generated client spec, so S-005 landing before or after S-006 does not perturb the
regenerated-client diff (SC-5 safe either way). S-008 gates on both tracks.

---

## S-001 — Pre-flight: resolver characterization test + toolchain record

### What changes
A characterization test is added to `Dorc.Core.Tests` capturing the complete set of
`SetPropertyValue` calls (names and values on a mocked `IVariableResolver`) that
`VariableScopeOptionsResolver.SetPropertyValues` produces for a representative environment
fixture — before any production code changes. Alongside, the step produces a **recorded
toolchain outcome** covering all of U-11: backend solution builds; `Dorc.Api` runs locally
far enough to serve `/swagger/v1/swagger.json`; `dorc-web` installs and builds; existing
unit suites run; whether `Dorc.Monitor.IntegrationTests` is executable here (it reads a
real `DOrcConnectionString`, currently a placeholder); and whether the **full stack**
(API + SQL database + web dev server together) can run — which decides SC-2's evidence
plan (manual round-trip here vs transfer to the user's environment) now, not at S-007.

### Why it changes
SC-6's regression guarantee is only falsifiable against a baseline recorded **before**
S-005 touches the resolver (HLPS §6). U-11 — both halves — is retired at the start: if
the API cannot run locally, S-006's approach is renegotiated with the user before any
dependent work is scheduled; if the full stack cannot run, SC-2's fallback is invoked
deliberately at pre-flight rather than discovered at the last step.

### Dependencies
None. Must complete before S-005; its toolchain record gates S-006 and sets S-007's SC-2
evidence mode.

### Verification intent
- The characterization test passes against unmodified production code and fails if any
  existing `SetPropertyValue` emission is removed or altered (demonstrated by a temporary
  mutation during review, not committed).
- The toolchain record lists an explicit yes/no per item above, including the
  integration-test-suite executability needed by the R-6 gates in S-003/S-005.

---

## S-002 — Schema, dual-sourced: SSDT + EF in one step

### What changes
**One step, one commit-set, both schema sources** (HLPS §4: new tables "must land
identically in both, in the same step"). `Dorc.Database` gains, in the `deploy` schema:
`Container`, `CloudResource`, `ApiRegistration` entity tables (HLPS §5.1 fields; `Tags`
sized 250 to match current server behaviour, pending the separate tag-capacity PR);
`EnvironmentContainer`, `EnvironmentCloudResource`, `EnvironmentApiRegistration` join
tables, each with an **explicit composite primary key** (EnvId, item id) and FKs to both
sides; and `ContainerAudit`, `CloudResourceAudit`, `ApiRegistrationAudit` mirroring
`DaemonAudit` (audit in scope per checkpoint default on U-8). `Dorc.PersistentData`
simultaneously gains the matching model classes (namespace-qualified where Lamar is
imported — U-1), `IEntityTypeConfiguration`s mapping exactly that DDL, audit entities,
and `DbSet`s on `DeploymentContext`.

### Why it changes
Everything downstream hangs off the schema, and R-4 orders schema first so dacpac publish
can precede API/Monitor deployment. Landing SSDT and EF together means no committed state
exists in which `EnsureCreated()` and the dacpac diverge — the same
"never exists in a committed state" reasoning S-003 applies to DI registration.

### Dependencies
None.

### Verification intent
- SSDT project builds (dacpac produced); `DeploymentContext` builds its model without
  errors (model-level test per existing repo conventions).
- The gate's central artifact: a side-by-side DDL↔configuration comparison per table
  (names, schema, columns, lengths, keys) — checkable entirely at this step's boundary.
- No modification to any existing table.

---

## S-003 — Persistence + audit sources, DI registration in both registries

### What changes
Interfaces and implementations for the three persistent sources (list, get, add, update,
delete, get-by-environment, attach, detach) plus three audit sources, registered in
`PersistentDataRegistry.cs` (API) **and** `Dorc.Monitor/Registry/PersistentSourcesRegistry.cs`
(deploy-time consumption per HLPS §5.7). Implementations follow the shape of
`ServersPersistentSource` under the HLPS §5.2 do-not-copy list: null-guards before
dereference, eager loading (`Include`) for every projected collection, no unloaded
navigation reads. Attach performs a **behavioural exists-check** (already-attached →
handled failure result) rather than relying on the composite PK alone, so the semantics
are testable through the repo's mocked-`IDeploymentContext` pattern; the PK remains the
database-level backstop (verified at S-004's clean-4xx test — the PK escape path itself
is not mock-testable and is explicitly deferred there).

### Why it changes
The API (S-004) and the resolver (S-005) both consume these sources; registering in both
registries up front prevents the R-6 DI-resolution failure mode from ever existing in a
committed state.

### Dependencies
S-002.

### Verification intent
- Unit tests per source covering the do-not-copy defects explicitly: unknown-id delete is
  a no-op/false (no NRE); get-by-id returns populated environment names for attached
  items; duplicate attach returns the handled failure via the exists-check (SC-4
  groundwork, mock-testable at this boundary).
- Both registries resolve the new interfaces. The Monitor-side R-6 check is a **DB-free
  container-resolution test** (`DeploymentContextFactory` only stores the connection
  string, so no database is needed); whether the fuller `Dorc.Monitor.IntegrationTests`
  suite can also run is taken from the S-001 toolchain record.

---

## S-004 — API: DTOs, RefData controllers (CRUD + typed attach/detach + ByEnvId), controller tests

### What changes
Three DTOs extending `EnvironmentUiPartBase` in `Dorc.ApiModel`; three controllers
(`RefDataContainersController`, `RefDataCloudResourcesController`,
`RefDataApiRegistrationsController`) exposing: paged/complete reads, `ByEnvId` reads
(HLPS §5.4), create, update, delete, and **typed attach/detach endpoints** (HLPS §5.5;
U-9 — the `ChangeEnvComponent` dispatcher is not extended; **this IS's approval is the
designated veto point**). Authorization per HLPS §5.3: create requires PowerUser/Admin;
update/delete require `CanModifyEnvironment` on every mapped environment with
PowerUser/Admin fallback when unattached; attach/detach require `CanModifyEnvironment` on
the target environment; all failures return 403. Every write lands an audit row.

Execution and review proceed **pattern-first**: the Containers vertical (DTO + controller
+ tests) is built and reviewed as the pattern-setter; CloudResources and ApiRegistrations
replicate it, reviewed as parity-diffs against the pattern. One step, two review passes —
keeps the ~24-endpoint surface from diluting the 403-semantics review.

### Why it changes
This is the contract the client and UI are generated against; authorization and audit are
in from the first commit rather than retrofitted (HLPS §2, SC-3).

### Dependencies
S-003.

### Verification intent
- `Dorc.Api.Tests` per controller: each write path tested for the privileged and
  unprivileged caller (403 asserted — SC-3), the unattached-item fallback, duplicate
  attach surfacing as a clean 4xx (SC-4 — including the PK-backstop path deferred from
  S-003), and audit row emission.
- Swashbuckle annotations present so S-006 generates a complete client.

---

## S-005 — Deployment variable integration (resolver extension)

### What changes
Per HLPS §5.7: three collection variable names + three `<Type>Names_` prefixes added to
`PropertyValueScopeOptionsFixed`; typed variable DTOs (modelled on `VariableValueServers`)
added to `Dorc.ApiModel` — the HLPS lists variable DTOs under `Dorc.Core` in §3 but
specifies `Dorc.ApiModel.MonitorRunnerApi` placement in §5.7.1; this step follows §5.7.1,
which is what the runner-side `VariableValueJsonConverter` type-resolution requires.
`AddPropertiesForServerNamesByType` is generalized (preserving space→underscore and
scalar-when-one/array-when-many semantics) and shared; `VariableScopeOptionsResolver`
gains the three sources by constructor injection and emits the new variables
**conditionally** — only when at least one item of that type is attached.

### Why it changes
The U-3 checkpoint decision. Conditional emission keeps the change inert for environments
not using the new types (SC-6, R-6); the shared generalization avoids triplicating tag
logic.

### Dependencies
S-001 (characterization baseline must exist first), S-003 (sources).

### Verification intent
- The S-001 characterization test's **recorded call-set assertions pass unchanged**; the
  only permitted edit to the test is construction wiring for the three new sources
  (stubbed to return empty), since the constructor signature necessarily grows.
- New `Dorc.Core.Tests` cover: each collection variable emitted when items attached and
  absent when not; per-tag variables including both generalized quirks; environments
  mixing tagged and untagged items.
- R-7 gate: query against existing property definitions (via `IPropertiesPersistentSource`
  usage patterns) for collisions with the new fixed names/prefixes; resolver precedence
  documented in the step's JIT spec.
- R-6 gate: DB-free container-resolution test confirming the Monitor container builds the
  resolver with its new dependencies (integration-suite execution per the S-001 record).

---

## S-006 — Typed client regeneration

### What changes
`swagger.json` refreshed from the locally-run API (per `dorc-web/README.md` and the S-001
toolchain record), then `npm run dorc-api-gen` regenerates the `dorc-api` client.

### Why it changes
The UI (S-007) consumes generated models/apis; hand-editing generated files is prohibited
(HLPS §4).

### Dependencies
S-001 (toolchain record — if it says the API cannot run locally, this step halts and
escalates per U-11 before S-007 is scheduled), S-004 (endpoints must exist to appear in
the spec).

### Verification intent
- **R-3 gate**: regenerated diff reviewed — additions for the new types plus at most
  formatting-stable churn; no removals of existing models/apis (SC-5); generator pinned
  via `npm run dorc-api-gen`.
- **R-5 gate**: generated model/api names checked for collisions or confusable adjacency
  with existing generated types (`ApiRegistration*` vs existing `ApiEndpoints`;
  `Container*` names) — the HLPS commits this check to the client-regen step.
- `dorc-web` compiles against the regenerated client.

---

## S-007 — UI: three tabs + supporting components + web tests

### What changes
`env-containers.ts`, `env-cloud.ts`, `env-apis.ts` replaced with real tabs mirroring
`env-servers.ts` structure but self-fetching via the `ByEnvId` endpoints on activation
(HLPS §5.4 — automatic load, reads not gated on editability). Supporting components per
type: attached-items grid, attach dialog, add/edit dialog; write controls disabled when
`!environment.UserEditable`; refresh via the `environment-stale` pattern. As in S-004,
execution is pattern-first: the Containers tab and its components set the pattern, the
other two replicate, with the same two-pass review.

### Why it changes
SC-1/SC-2: this is the user-visible deliverable the stubs promised.

### Dependencies
S-006.

### Verification intent
- Web component tests per tab and per dialog in `src/dorc-web/tests` following existing
  patterns (grid renders fetched items; controls disabled/enabled by editability; attach
  flow fires the expected API call and refresh event).
- No placeholder text remains in the repo (SC-1's grep gate).
- SC-2 evidence per the mode decided at S-001: manual UI round-trip here, or the U-11
  fallback (round-trip in the user's environment; test-level evidence gates).

---

## S-008 — Final verification sweep and documentation close-out

### What changes
No new production code. Full test-suite run across `Dorc.Api.Tests`, `Dorc.Core.Tests`,
`src/dorc-web/tests`; `Tests.Acceptance` impact check (U-10 — verify, not assume, that
existing acceptance features are unaffected by the resolver change); SC-1..SC-6 checklist
walked and evidenced; HLPS/IS statuses closed out; release note drafted covering the R-4
deploy ordering requirement (dacpac before API/Monitor).

### Why it changes
The HLPS success criteria are the contract; this step is where the contract is signed.

### Dependencies
S-005 and S-007 (i.e. everything).

### Verification intent
- Every SC has recorded evidence (test name, diff, or screenshot).
- Any unresolved item becomes an explicit user escalation, not a silent gap.
