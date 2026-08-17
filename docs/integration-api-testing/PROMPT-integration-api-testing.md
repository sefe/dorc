# Prompt: DOrc API Integration Suite — Data-Fidelity Coverage

> Copy everything below the line into a fresh Claude Code (or equivalent agent) session
> opened at the root of the `dorc` repository.

---

## Mission

Design and build an integration test suite that exercises **every HTTP endpoint exposed by
`Dorc.Api`** and proves that **no data is lost or silently mutated across the API
boundary**. The suite must run the real API pipeline (routing, model binding, auth,
serialisation, EF Core, SQL Server schema) — not mocked services — so that a field dropped
anywhere between the wire and the database is caught by a failing test.

You must follow the development process defined in `CLAUDE.md` (HLPS → IS → JIT Specs,
checkpoints, adversarial review). The reference pattern is `docs/monitor-robustness/`.
Persist all planning artifacts to `docs/integration-api-testing/`. Do **not** start writing
test code until the HLPS and IS are approved.

## What "nothing is lost from a data perspective" means

Encode each of these as a named, reusable test invariant. Every endpoint test must state
which invariants it asserts:

1. **Round-trip fidelity** — the payload accepted by a POST/PUT is returned identically by
   the corresponding GET, compared field-by-field with a deep comparer. Every property that
   is legitimately *not* round-tripped (server-generated IDs, timestamps, secrets that are
   write-only by design) must be named in an explicit per-DTO exclusion list — never
   silently skipped by the comparer.
2. **Update isolation** — updating field X must not null, default, or clobber field Y.
   Seed a fully-populated entity, update one field, assert all others are byte-identical.
3. **Collection completeness** — paged/filtered/sorted list endpoints return every seeded
   record exactly once: no drops or duplicates across page boundaries, stable under the
   sort orders the UI uses (check `PagedDataOperators` / `DataPagerExtension` usage).
4. **Delete precision** — a delete removes the target row and its *intended* cascades only.
   Assert both the deletion and the survival of unrelated seeded rows.
5. **Mapping coverage (schema drift guard)** — for each DTO↔entity pair, a reflection-based
   meta-test enumerates persisted entity properties (from `Dorc.PersistentData/Model`) and
   fails if a property is neither mapped into the API model (`Dorc.ApiModel`) nor present
   on an explicit, justified exclusion list. This is what catches "someone added a column
   and the API silently drops it".
6. **Audit completeness** — operations documented as audited (property values, servers,
   databases, scripts, project audit, daemon audit — see the `*AuditController`s) actually
   write their audit rows, and the audit rows carry the old/new values without truncation.
7. **Encryption round-trip** — secure/encrypted property values (see `IPropertyEncryptor`)
   decrypt back to the original plaintext through the API, and are never returned in
   plaintext where the contract says they must be masked.

## Repository facts (verify, don't trust blindly)

- API: `src/Dorc.Api` — ASP.NET Core **net8.0**, Lamar DI, EF Core + SQL Server,
  Swashbuckle/OpenAPI, rate limiting, SignalR.
- **56 controllers**: 55 under `src/Dorc.Api/Controllers/` plus
  `src/Dorc.Api/ConfigValuesController.cs`. Families: RefData CRUD (projects,
  environments, servers, databases, users, groups, permissions, components, properties,
  property values…), audit readers, deployment (`RequestController`,
  `DeploymentV2Controller`, `BundledRequestsController`), daemons, access control,
  metadata/config, and external-integration endpoints (`TerraformController`,
  `MakeLikeProdController`, `DirectorySearchController`, `CopyEnvBuildController`…).
- Auth is scheme-switchable in `Program.cs` (Negotiate / JWT bearer / OAuth2
  introspection) via `GetAuthenticationScheme()`; endpoints are role-guarded
  (`IRolePrivilegesChecker`, `ISecurityObjectFilter`).
- Test convention: **MSTest** everywhere. The existing integration pattern to imitate is
  `src/Dorc.Monitor.IntegrationTests` — DI-composed test base
  (`Init/MonitorServiceTestBase.cs`) plus `appsettings.test.json` with a
  `__DORC_TEST_DB_CONNSTR__` placeholder substituted by the pipeline.
- `src/Tests.Acceptance` (Reqnroll) targets an already-deployed instance; the new suite
  must instead be self-hosting and runnable locally and in CI with no deployed
  environment.
- A typed client exists at `src/Dorc.Api.Client` (`DeployApiClient`) — evaluate reusing it
  as the test-facing client so the suite also exercises the client's serialisation; note
  in the HLPS if it covers too little of the surface to be the only vehicle.
- Database schema lives in `src/Dorc.Database` (SQL project); EF model in
  `src/Dorc.PersistentData`.

## Unknowns your HLPS must resolve (blocking)

1. **Test host** — `WebApplicationFactory<Program>` in-process vs Kestrel. Confirm
   `Program` is accessible to a test assembly (top-level statements may need
   `InternalsVisibleTo` or a `public partial class Program` marker).
2. **Auth stubbing** — a test authentication handler that issues principals with
   configurable roles/claims (admin, read-only, unprivileged), registered only in the test
   host. Production auth code must not be weakened. The suite should also assert that an
   *unprivileged* principal cannot read data an endpoint is supposed to filter — data
   *leakage* is the dual of data loss.
3. **Database provisioning** — Testcontainers-for-MSSQL vs LocalDB vs an injected
   connection string honouring the existing `__DORC_TEST_DB_CONNSTR__` convention. Decide
   how the schema is created (deploy the `Dorc.Database` dacpac vs EF-generated schema —
   prefer the dacpac: the suite should test the *real* schema) and how per-test isolation
   works (transaction rollback, respawn, or unique-name seeding).
4. **External side effects** — endpoints touching Terraform, Azure DevOps, Active
   Directory (`DirectorySearchController`), OpenSearch, SignalR, file shares, or target
   servers cannot run their far side in CI. For each: fake at the DI seam (assert the
   *persisted* data is intact) or exclude with written justification. Silent exclusion is
   forbidden.
5. **Deployment pipeline endpoints** — `RequestController`/`DeploymentV2Controller`
   enqueue work consumed by `Dorc.Monitor`. Scope this suite to persistence fidelity of
   the request/its properties, not deployment execution (that is
   `Dorc.Monitor.IntegrationTests`' job). State this boundary in the HLPS.
6. **CI wiring** — where in `pipelines/` the suite runs, expected runtime budget, and how
   the DB dependency is provisioned there.

## Coverage contract (self-policing)

Build a **coverage meta-test**: at test time, enumerate all routes from the API's own
OpenAPI/Swagger document (or `IApiDescriptionGroupCollectionProvider`) and fail if any
route has neither (a) at least one integration test registered against it, nor (b) an
entry in a checked-in exclusion manifest with a one-line justification. New controllers
then break the build until they are covered or consciously excluded — coverage cannot rot.

## Deliverables

1. `docs/integration-api-testing/HLPS-integration-api-testing.md` — with Unknowns
   Register. **Checkpoint: stop for approval.**
2. `docs/integration-api-testing/IS-integration-api-testing.md` — atomic steps (suggested
   shape: S-001 test host + auth handler + DB fixture; S-002 comparer + invariant library +
   coverage meta-test; S-003.. one step per controller family, RefData CRUD first since it
   is the bulk of the surface; final step CI wiring). **Checkpoint: stop for approval.**
3. JIT spec per step, then implementation: a new **`src/Dorc.Api.IntegrationTests`**
   MSTest project (namespace `Dorc.Api.IntegrationTests`, following the naming rules in
   `CLAUDE.md` — no `Helper`/`Util` grab-bag classes), added to `Dorc.sln`.
4. Adversarial review per step, per `CLAUDE.md`.

## Quality bar

- Test-first within each step; every test asserts named invariants — no
  `Assert.IsNotNull(response)`-grade tests.
- Deterministic and parallel-safe: no shared mutable seed data between test classes, no
  ordering dependencies, no `Thread.Sleep` polling.
- Failure output must name the endpoint, the invariant, and the exact property path that
  diverged (e.g. `EnvironmentApiModel.Details.ThinClientServer: seeded "X", got null`).
- Do not modify production code except minimal, reviewed test seams (e.g. the `Program`
  visibility marker); any production change gets called out in the step's review.
- Runtime budget: the full suite should stay in single-digit minutes so it can gate PRs.

Start by reading `CLAUDE.md`, `docs/monitor-robustness/`, `src/Dorc.Api/Program.cs`, and
`src/Dorc.Monitor.IntegrationTests/`, then produce the HLPS and stop for review.
