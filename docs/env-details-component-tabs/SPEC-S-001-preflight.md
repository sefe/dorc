# SPEC S-001 — Pre-flight: resolver characterization test + toolchain record

| Field      | Value                                   |
|------------|-----------------------------------------|
| **Status** | APPROVED (executed under auto-pilot)    |
| **IS step**| S-001 (IS v2, APPROVED)                 |
| **Date**   | 2026-07-17                              |

## Requirements

### R1 — Characterization test (SC-6 baseline)
New test class in `Dorc.Core.Tests` (MSTest + NSubstitute, matching existing
`VariableScopeOptionsResolverTests` conventions), exercising
`VariableScopeOptionsResolver.SetPropertyValues` against a mocked `IVariableResolver`
that records every `SetPropertyValue` call (both overloads) as an ordered (name, value)
list.

Two fixtures:
1. **Representative environment** — two servers (one multi-tag `"appserv;web tier"`, one
   single-tag `"web tier"` — exercising both the scalar-when-one and array-when-many
   per-tag emissions and the space→underscore quirk), a daemon on one server, an
   `Endur`-typed database (name prefixed `END_DB_` to exercise short-name derivation) and
   an `Endur Reporting` database, database permissions with one user holding two roles
   (exercising the group-by), owner emails present.
2. **Minimal environment** — no servers, no databases, no owner emails. This is the
   closest analogue to "environment without new component types" that SC-6's regression
   half protects.

Assertions: exact ordered sequence of emitted property names per fixture, plus value
assertions for the collection variables (AllServers contents, EnvironmentServers shape
including daemons, per-tag scalar vs array, DatabasePermissions grouping, EnvOwnerEmails
contents).

Constraint carried from IS S-005: when the resolver's constructor later grows, the ONLY
permitted edit to this test is stub wiring for the new sources (returning empty); the
recorded call-set assertions must not change.

### R2 — Toolchain record
Recorded outcomes, each explicit yes/no with evidence:
- backend builds (`dotnet build`)
- unit suites run (`Dorc.Core.Tests`, `Dorc.Api.Tests`)
- `Dorc.Monitor.IntegrationTests` executable here?
- `Dorc.Api` serves `/swagger/v1/swagger.json` locally?
- `dorc-web` installs/builds?
- full stack (API + SQL DB + web dev server) runnable? → decides SC-2 evidence mode

Record lands in `TOOLCHAIN-S-001.md` in this folder.

## Out of scope
Any production-code change. Any new-component work.

## Gate
Adversarial review of the test's fidelity (does it truly freeze the call-set?) and the
toolchain record's completeness. Mutation demonstration: a temporary resolver mutation
must fail the test (not committed).
