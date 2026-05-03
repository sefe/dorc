# HLPS: Daemon Observations — "last seen" tracking on probe

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **Parent**  | FOLLOW-UPS.md F-2 (originally deferred from HLPS-daemons-modernisation) |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Problem Statement

The status-probe path on `DaemonStatusController` GET polls every attached daemon across every server in an environment. Before S-005 the probe's side effect silently mutated `SERVER_SERVICE_MAP` (auto-attaching discovered services) — a bad side effect that we removed. The **intent** behind that side effect was reasonable: operators want to know when a given daemon was last observed running, so stale/unused daemons can be identified and cleaned up.

Today the probe result is displayed in the UI and thrown away. Nothing persists "we saw daemon X on server Y at time T with status Running". There is no "last seen" column. There is no way to filter or report on daemons that haven't been seen in ≥N days.

This HLPS proposes persisting probe observations so that:
- Each time the "Load Daemons" button is clicked, the probe result is appended to an observation history.
- The UI surfaces a "Last seen" column and timestamp on the daemon list.
- Operators can filter or sort by staleness.

---

## 2. Scope

**In scope:**
- `src/Dorc.Database/` — new `deploy.DaemonObservation` table + pre/post-deploy wiring. No legacy-table migration (feature is new).
- `src/Dorc.PersistentData/` — `DaemonObservation` entity + EF config + DbSet + source + registry.
- `src/Dorc.Core/ServiceStatus.cs` — write an observation row for each probe result (success AND error — both are meaningful observations).
- `src/Dorc.ApiModel/` — `DaemonObservationApiModel`, update `DaemonStatusApiModel` to surface `LastSeenDate` + `LastSeenStatus`.
- `src/Dorc.Api/Controllers/` — extend `DaemonStatusController` or add a new `DaemonObservationController` for paged history read.
- `src/dorc-web/` — `application-daemons.ts` and/or `page-daemons-list.ts` get a "Last seen" column; status cell shows relative time for staleness; new "Observation history" action on daemon row (optional — per UX decision).
- TS regen.

**Out of scope:**
- **Retention policy / rolling purge** of observation rows. Observations accumulate unboundedly for now; a cleanup job is a follow-up once we see real-world row growth.
- **Scheduled/background probe** — observations are recorded only when the UI triggers a status read. No cron.
- **Alerting on stale daemons** — the UI surfaces the data; alerts are a separate feature.
- **Server-scope observations** — reusing the same history for "last seen the server at all" is out of scope. Observations are daemon-on-server tuples.
- **Audit on observation writes** — probe-path writes do not produce `DaemonAudit` rows. Observations are telemetry; audit is user-intent. Separate concerns.

---

## 3. Goals and Success Criteria

| ID    | Success Criterion |
|-------|------------------|
| OBS-01 | `deploy.DaemonObservation` exists with columns for `ServerId`, `DaemonId`, `ObservedAt`, `ObservedStatus`, `ErrorMessage`. Each successful `ServiceStatus.ProbeDaemonStatuses` call appends one row per probed daemon. |
| OBS-02 | Failed probes (ping failure, service-controller exception) also append observation rows with `ErrorMessage` populated and `ObservedStatus` = null or a canonical error marker. The observation history is a faithful probe log, not a filtered "successes only" log. |
| OBS-03 | The daemon list UI (`page-daemons-list.ts`) has a "Last seen" column showing the most recent observation date+time across all servers the daemon is attached to, or "Never" if no observations exist. |
| OBS-04 | A per-(daemon, server) "observation history" is queryable via `GET /DaemonObservation?daemonId=…&serverId=…` or equivalent — paged, descending by date. UI access is a follow-up row action on the daemons list (TBD in SPEC). |
| OBS-05 | No audit rows are written for observations (observations are probe telemetry; audits are user-intent). |
| OBS-06 | All pre-existing daemon tests continue to pass. `Dorc.Database.IntegrationTests` (via `Tests.Acceptance/DatabaseTests/`) has one new test for the observation table's creation and an observation-write path. |

---

## 4. Constraints

- **C-01** New writes on the probe path. The original reason for removing the probe side effect (DF-7 in HLPS-daemons-modernisation) was that it silently mutated **mapping intent** (`ServerDaemon` attachments). Observation writes are different — they record **telemetry**, not intent. This is an acceptable write-on-read because it's adding history rows, not modifying mapping semantics. The distinction must be preserved in the design: observation writes never alter `ServerDaemon` mappings or `Daemon` fields.
- **C-02** The probe runs in parallel (`Parallel.ForEach` in `ProbeDaemonStatuses`). Observation writes must be safe under parallel insert (INSERT-only; no read-modify-write).
- **C-03** Observation writes must not block the probe. If an observation write fails (DB down, permission error), the probe still returns its result to the UI — observation is best-effort.
- **C-04** No retention/quota management in this work. Flagging that the table will grow; follow-up cleanup is explicitly deferred.
- **C-05** Wire additions only. `DaemonStatusApiModel` gets new optional fields; no renames or removals. Consistent with HLPS-daemons-modernisation C-02.

---

## 5. Proposed Solution Directions

### SD-1 — New `deploy.DaemonObservation` table
- Columns: `Id BIGINT IDENTITY PK`, `ServerId INT NOT NULL`, `DaemonId INT NOT NULL`, `ObservedAt DATETIME NOT NULL`, `ObservedStatus NVARCHAR(50) NULL`, `ErrorMessage NVARCHAR(MAX) NULL`.
- Indexes: `IX_DaemonObservation_DaemonId_ObservedAt` (for "latest observation per daemon" queries), `IX_DaemonObservation_ServerId_ObservedAt` (for server-scoped queries).
- FKs: `ServerId → dbo.SERVER(Server_ID)` ON DELETE NO ACTION; `DaemonId → deploy.Daemon(Id)` ON DELETE **NO ACTION** (observations survive daemon deletion — audit-history precedent from DaemonAudit).
- No unique constraint — multiple rows per (daemon, server) pair by design; each probe produces a new row.

### SD-2 — `DaemonObservation` entity + source
- Entity in `Dorc.PersistentData.Model/DaemonObservation.cs`.
- EntityTypeConfiguration with fluent `.ToTable(...)`.
- `IDaemonObservationPersistentSource` with:
  - `void InsertObservation(int serverId, int daemonId, DateTime observedAt, string? status, string? errorMessage)`
  - `IEnumerable<DaemonObservationApiModel> GetObservations(int daemonId, int? serverId, int limit, int page)`
  - `DaemonObservationApiModel? GetLastSeen(int daemonId)` — returns the most recent observation across all servers, or null.
- Registered in `PersistentDataRegistry.cs`.

### SD-3 — Wire into `ServiceStatus.ProbeDaemonStatuses`
- After each probe result lands in `resultsDict`, also call `_daemonObservationPersistentSource.InsertObservation(serverId, daemonId, DateTime.Now, status, errorMessage)`.
- Serverid / daemonid need resolution — the current probe works with `ServerName` / `DaemonName` strings from the enumeration. Two options:
  - **(a)** Resolve Ids at the top of `ProbeDaemonStatuses` by pre-querying daemons + servers (one extra query).
  - **(b)** Add Ids to the `DaemonStatus` model (internal only — pre-populated in `BuildDaemonList`).
- Option (b) is cleaner and avoids the extra query. Will be locked in the SPEC.
- Observation writes wrapped in try/catch; failures logged at `LogWarning` and swallowed per C-03.

### SD-4 — Controller + read endpoint
- New `DaemonObservationController` with `GET /DaemonObservation?daemonId=…&serverId=…&page=…&limit=…` returning paged history.
- Alternative: fold into `DaemonStatusController` as `GET /DaemonStatus/history`. Cleaner to give it its own controller (parallel to `DaemonAuditController`).
- Authenticated-only read, no role gate (same as `DaemonAuditController`).

### SD-5 — UI surfacing
- `application-daemons.ts` (used by `page-daemons-list.ts`) adds a "Last seen" column showing the most-recent observation date across all attached servers, populated via a new bulk endpoint or by including the value in the existing `refDataDaemonsGet` response. Likely the latter — extend `DaemonApiModel` with an optional `LastSeenDate` / `LastSeenStatus` computed by the persistent source's `GetDaemons()` via a subquery / JOIN.
- Visual: relative time ("5 min ago", "3 days ago", "Never") with tooltip showing the exact timestamp.
- Per-row action "Observation history" that opens a dialog similar to `daemon-audit-view.ts` but showing the observation grid. Optional — can be deferred.

### SD-6 — Database integration test
- Extend `Tests.Acceptance/DatabaseTests/DaemonSchemaMigrationTests.cs` with a test proving `deploy.DaemonObservation` exists after publish, and a round-trip through the persistent source inserts and reads an observation.

---

## 6. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | Should daemons with zero attached servers also appear with "Never seen"? | User | Non-blocking | **RESOLVED 2026-04-24 — yes**, informative. |
| U-2 | Should `LastSeenDate` be computed on every daemon-list-GET or cached? | Agent | Non-blocking | **RESOLVED 2026-04-24 — on-demand first**; materialisation follow-up only if perf demands. |
| U-3 | Observation history row action — include in this PR, or defer? | User | Non-blocking | **RESOLVED 2026-04-24 — defer** to keep PR smaller. Primary win is the list-page column. |
| U-4 | Should the observation table retain rows indefinitely? | User | Non-blocking | **RESOLVED 2026-04-24 — unbounded for now**; rolling-purge policy is a follow-up once real-world row growth is observed. |
| U-5 | Does the UI need a "no daemons seen in N days" filter? | User | Non-blocking | **RESOLVED 2026-04-24 — column-only** (sortable Last Seen column is enough for this pass). |

---

## 7. Out-of-Scope Risks

- **Table growth**: observations accumulate per probe per daemon per server. A busy environment with 50 daemons × 20 servers probed every 5 minutes by an auto-refreshing UI would produce 200 rows/sec. Unlikely at current UX (user-triggered button), but flag for monitoring. Follow-up: retention policy (U-4).
- **Probe-time latency**: adding an INSERT per probed daemon is on the probe path. Parallel writes help; still, on a DB under load, this could slow the probe noticeably. Mitigation: write in `try/catch` with swallowed failure per C-03.
- **"Last seen" column on list is potentially misleading** if the user hasn't clicked "Load Daemons" recently — shows stale last-seen even when the daemon is actually running. The UI should clarify that "last seen" reflects the last UI-triggered probe, not a live check.
