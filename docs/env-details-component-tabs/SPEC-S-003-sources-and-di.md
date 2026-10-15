# SPEC S-003 — Persistence + audit sources, DI registration in both registries

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-003 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

## Boundary adjustments from the IS (recorded for the gate)
1. **DTOs move here from S-004.** The repo convention is that persistent sources return
   `Dorc.ApiModel` DTOs (`ServersPersistentSource` → `ServerApiModel`), so the three DTOs
   are created in this step; S-004 consumes them.
2. **Paged (`ByPage`) reads deferred.** The S-007 UI needs complete reads (attach dialog)
   and `ByEnvId` reads (attached grid) only; no consumer for a paged endpoint exists in
   scope. Implementing the heavyweight paging pipeline three times with no consumer fails
   YAGNI — S-004's "paged/complete reads" is satisfied by complete reads, deviation
   flagged to its gate.
3. **Audit sources are insert-only.** Audit *query* endpoints belong to audit UI pages,
   which are out of HLPS scope; `Insert<Type>Audit` mirrors
   `DaemonAuditPersistentSource.InsertDaemonAudit` (including the no-op-update skip).

## Requirements

### R1 — DTOs (`Dorc.ApiModel`)
`ContainerApiModel`, `CloudResourceApiModel`, `ApiRegistrationApiModel`, each extending
`EnvironmentUiPartBase`, with Id + the §5.1 fields. Sources populate `EnvironmentNames`;
`UserEditable` is not computed at source level (authorization is explicit in controllers;
tabs gate on `environment.UserEditable`).

### R2 — Attach/detach outcome type
One shared enum `EnvironmentAttachmentOutcome` (`Attached`, `AlreadyAttached`, `Detached`,
`NotAttached`, `ItemNotFound`, `EnvironmentNotFound`) in `Dorc.PersistentData.Sources`,
so controllers can map outcomes to 2xx/4xx without exception-driven flow. The
already-attached check is behavioural (exists-check) per IS S-003; the composite PK
remains the DB backstop.

### R3 — Sources (`Dorc.PersistentData.Sources` + interfaces)
`I<Type>sPersistentSource` / implementation per type: `GetAll()`, `GetById(int)`,
`GetByName(string)`, `Add(model)`, `Update(id, model)`, `Delete(id)`,
`GetForEnvironmentId(int)`, `GetEnvironmentNamesForId(int)`,
`AttachToEnvironment(id, envId)`, `DetachFromEnvironment(id, envId)`.
Constructor takes `IDeploymentContextFactory` only. **Do-not-copy list enforced**
(HLPS §5.2): every dereference null-guarded (unknown-id delete returns false, no NRE);
every projected collection eager-loaded via `Include`; no unloaded navigation reads.
`I<Type>AuditPersistentSource` / implementation: `Insert<Type>Audit(username, action,
id, fromValue, toValue)` per `DaemonAuditPersistentSource`.
The six new `DbSet`s are added to `IDeploymentContext` so sources work against the
interface (test-mockable).

### R4 — DI registration
- `PersistentDataRegistry` (Lamar, API side): six `For<I>().Use<T>().Scoped()` lines.
- `Dorc.Monitor/Registry/PersistentSourcesRegistry` (ServiceCollection): six
  `AddTransient` lines (resolver integration S-005 consumes the three main sources).

### R5 — Tests
- `ContainersPersistentSourceTests` (pattern-setter, `Dorc.Api.Tests/Sources`, mocked
  `IDeploymentContext` + `DbContextMock`): unknown-id delete → false without NRE;
  get-by-id populates `EnvironmentNames`; duplicate attach → `AlreadyAttached`; detach
  when not attached → `NotAttached`; attach/detach happy paths mutate the collection and
  save; add/update map all fields; audit insert skips no-op updates.
- Parity tests for CloudResources and ApiRegistrations covering the do-not-copy items
  and attach outcomes.
- R-6 DB-free resolution tests: Lamar `Container` built over `PersistentDataRegistry`
  resolves all six new interfaces (no DB touched — factory only stores the string);
  `ServiceCollection` + Monitor `PersistentSourcesRegistry.Register` resolves the three
  main sources (in `Dorc.Monitor.Tests`).

## Gate
Diff-only adversarial review; all tests green; existing suites at S-001 baseline.
