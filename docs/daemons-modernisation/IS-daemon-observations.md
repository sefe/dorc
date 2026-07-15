# IS: Daemon Observations — Implementation Sequence

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **HLPS**    | HLPS-daemon-observations.md (APPROVED)                  |
| **Folder**  | docs/daemons-modernisation/                             |

---

## Step Index

| ID    | Title                                                        | Addresses     | Depends On |
|-------|--------------------------------------------------------------|---------------|------------|
| S-012 | Daemon observations — schema, entity, probe wiring, UI col   | SD-1..SD-6    | —          |

Single step — the scope is bounded enough (one new table, one new source, one probe-path write, one API-model extension, one UI column) that splitting adds ceremony without clarity. SPEC-S-012 carries the full acceptance criteria.

---

## S-012 — Daemon observations end-to-end

### What changes
- **Schema**: new `deploy.DaemonObservation` table + two indexes + two FKs (ON DELETE NO ACTION on both).
- **Entity + config**: `DaemonObservation.cs` + fluent `DaemonObservationEntityTypeConfiguration.cs` + `DbSet<DaemonObservation>` on `DeploymentContext` + `OnModelCreating` wiring.
- **Persistence source**: `IDaemonObservationPersistentSource` + impl with `InsertObservation`, `GetObservations`, `GetLastSeenByDaemon` (bulk-returns latest observation per daemon for the list page).
- **Probe wiring**: `ServiceStatus.ProbeDaemonStatuses` resolves Server/Daemon IDs up-front (HLPS SD-3 option b — pre-populate in `BuildDaemonList`), writes an observation row per probed daemon inside a swallowed try/catch.
- **DaemonStatus model + wire**: `Dorc.Core.DaemonStatus` gets internal `ServerId` / `DaemonId` fields populated in `BuildDaemonList`; wire model `DaemonStatusApiModel` unchanged (observations aren't relevant to the status response).
- **List-page model**: `DaemonApiModel` gets optional `LastSeenDate` (DateTime?) and `LastSeenStatus` (string). `DaemonsPersistentSource.GetDaemons()` computes these via a grouped subquery / correlated lookup.
- **Controller**: `DaemonStatusController` (or a new `DaemonObservationController`) gets a `GET` paged-read endpoint. Chose new controller for parity with `DaemonAuditController`.
- **UI**: `page-daemons-list.ts` adds a "Last seen" sortable column rendering a relative-time span with full-timestamp tooltip. "Never" for null.
- **Test**: `Tests.Acceptance/DatabaseTests/DaemonSchemaMigrationTests.cs` gets one new test asserting the observation table + a round-trip insert/read.

### Why it changes
Restores the useful *intent* behind the DF-7 side effect that HLPS-daemons-modernisation removed, but does it **properly**:
- Write is telemetry, not mapping mutation (preserves C-01 distinction from DF-7).
- Observations are additive and survive daemon deletion (like DaemonAudit, not like ServerDaemon).
- UI gains actionable insight: operators can sort/filter daemons by when they were last observed running.

### Dependencies
None. Depends only on the daemons modernisation baseline already in place on `feat/649-daemons-modernisation`. Specifically:
- `deploy.Daemon` and `deploy.ServerDaemon` exist (S-001).
- `ServiceStatus.ProbeDaemonStatuses` is the clean version from S-005.
- `DaemonsPersistentSource` is post-S-004/S-007 shape.

### Verification intent
- **Compile**: `dotnet build` and `npx tsc --noEmit` clean.
- **DB migration**: `DaemonSchemaMigrationTests.OBS_ObservationTableCreatedAndWritable` — publish dacpac, insert + read back an observation row.
- **Probe-path observation write**: verified via manual QA (requires a running API + SQL + reachable server to probe). Load Daemons button triggers the probe; query `deploy.DaemonObservation` to see new rows. Automated coverage of the full probe path requires Windows `ServiceController` and is beyond the CI harness scope.
- **UI**: Last Seen column visible; sorts ascending/descending; empty state renders "Never" for daemons with no observations.
