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
carrying the §5.1 fields. Placement in `Dorc.ApiModel` is what
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
- R-7 (variable-namespace collision): precedence rule documented here — fixed scope
  emissions are applied via `SetPropertyValue` at resolution time and therefore override
  any user-defined property of the same name for that deployment, exactly as
  `AllServers`/`ServerNames_*` already do. Deployment operators must treat
  `EnvironmentContainers`, `EnvironmentCloudResources`, `EnvironmentApiRegistrations`,
  `ContainerNames_*`, `CloudResourceNames_*`, `ApiRegistrationNames_*` as reserved. A
  live-DB query for collisions is an ops action recorded in S-008's checklist (cannot be
  run here — no database; TOOLCHAIN-S-001 #8).

## Gate
Characterization tests pass with wiring-only edit (verify via git diff of the test file);
new tests green; both DI registries resolve; existing suites at baseline.
