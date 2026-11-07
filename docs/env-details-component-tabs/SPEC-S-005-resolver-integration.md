# SPEC S-005 — Deployment variable integration (resolver extension)

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-005 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

## Requirements

### R1 — Fixed names (`Dorc.ApiModel/PropertyValueScopeOptionsFixed.cs`)
`EnvironmentContainers`, `EnvironmentCloudResources`, `EnvironmentApiRegistrations`
(collection variables) and `ContainerNames` → `"ContainerNames_"`,
`CloudResourceNames` → `"CloudResourceNames_"`,
`ApiRegistrationNames` → `"ApiRegistrationNames_"` (per-tag prefixes), following the
`ServerNames` → `"ServerNames_"` style.

### R2 — Variable DTOs (`Dorc.ApiModel.MonitorRunnerApi`)
`VariableValueContainers`, `VariableValueCloudResources`, `VariableValueApiRegistrations`
— plain property bags mirroring `VariableValueServers`' (plural-name-per-item) convention,
carrying the §5.1 *descriptive* fields. The surrogate `Id` is deliberately omitted: the
mirror convention wins (no existing `VariableValue*` DTO exposes a DB key) and a key has
no deploy-time use. (Recorded post-gate per S-005 review finding.) Placement in `Dorc.ApiModel` is what
`VariableValueJsonConverter`'s type resolution requires (HLPS §3/§5.7.1 discrepancy
resolved in IS v2).

### R3 — Resolver extension (`Dorc.Core/VariableScopeOptionsResolver.cs`)
- Constructor gains the three `I<Type>sPersistentSource` dependencies.
- **Conditional emission appended after the existing emissions**: for each type, fetch
  `GetForEnvironmentId(environment.EnvironmentId)`; only when at least one item is
  attached, emit the collection variable and the per-tag name lists. Appending keeps the
  existing call sequence byte-identical for environments without new components (SC-6).
- Tag splitting is **shared, not triplicated**: `AddPropertiesForServerNamesByType`'s
  body generalizes to a helper taking (prefix, (name, tags) pairs) preserving both
  quirks — space→underscore in tag names; scalar-when-one / array-when-many. The
  servers call delegates to it.
- **One deliberate improvement over the server path** (recorded for the gate): the shared
  helper skips null/empty `Tags` instead of throwing `NullReferenceException` as the old
  inline code did for a null `ApplicationTags`. The characterization fixtures use non-null
  tags, so the frozen call-set is unaffected; a null-tagged server previously crashed the
  whole deployment, which is the do-not-copy spirit applied to behaviour.

### R4 — Tests (`Dorc.Core.Tests`)
- Characterization tests: constructor wiring gains three stubs returning empty —
  the ONLY permitted edit; recorded call-set assertions untouched (IS S-005).
- New `VariableScopeOptionsResolverComponentVariablesTests`: per type — collection
  variable emitted with mapped fields when attached, absent when not; per-tag variables
  honour both quirks; items with null/empty tags appear in the collection but yield no
  per-tag variables; environments mixing tagged/untagged items.

### R5 — R-6/R-7 gates
- R-6: Monitor container resolves the resolver with its new dependencies —
  `PersistentSourcesRegistryResolutionTests` (S-003) covers source resolution; add
  resolver-level resolution to the Monitor test.
- R-7 (variable-namespace collision): explicit request overrides take precedence over
  topology emissions; topology values take precedence over configuration/repository
  fallback values. `SetPropertyValue` does not supersede an explicit request override.
  Deployment operators should treat
  `EnvironmentContainers`, `EnvironmentCloudResources`, `EnvironmentApiRegistrations`,
  `ContainerNames_*`, `CloudResourceNames_*`, `ApiRegistrationNames_*` as reserved unless
  deliberately overriding topology for the approved request. A
  live-DB query for collisions is an ops action recorded in S-008's checklist (cannot be
  run here — no database; TOOLCHAIN-S-001 #8).

### Attachment and naming policy

These three collections describe **direct attachments to the requested environment**,
not attachments inherited through parent environments. This differs deliberately from
inherited property-value lookup: attaching a resource to a parent is not an implicit
deployment target grant to every child. Clone options attach existing definitions only
when explicitly selected.

Tag grouping is case-insensitive and preserves the first encountered spelling, matching
the existing server-variable convention. Variable lookup remains case-sensitive; use
one consistent spelling of a tag across definitions and scripts. Spaces become
underscores. Distinct tags such as `web api` and `web_api` that normalize to the same
variable key now fail resolution explicitly rather than silently replacing a value.
Changing this convention to canonical lower-case names would rename existing variables
and requires a separately approved compatibility change.

Membership and definition edits affect subsequent requests, not an already approved
Terraform binary plan. Review a fresh plan after topology changes; applying an old
approval does not re-resolve the planned resource targeting.

## Gate
Characterization tests pass with wiring-only edit (verify via git diff of the test file);
new tests green; both DI registries resolve; existing suites at baseline.
