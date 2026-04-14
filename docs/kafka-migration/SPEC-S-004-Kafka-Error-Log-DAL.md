# JIT Spec — S-004: Kafka Error-Log Table + Data-Access Layer

| Field | Value |
|---|---|
| **Status** | APPROVED (user-approved 2026-04-14) |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-004 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-004 (APPROVED R3) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` C-8, R-9 (APPROVED R3) |

---

## 1. Purpose & Scope

Establish a SQL Server table + a narrow data-access layer for **Kafka consumer error events** ("poison messages": deserialisation failures, unrecoverable handler exceptions, schema-mismatches that escape S-003's guards). This is the persistent destination for HLPS C-8's "no DLQ → DB error-log" decision and supplies HLPS R-9's auditability requirement.

This step delivers **only** the storage substrate. Consumer-side wiring — the code path that converts a `ConsumeResult<T>` failure plus the structured-log fallback into an `InsertAsync` call — lands per pub/sub flow in S-006 / S-007 / S-008, against the S-002 consumer contract those steps will already understand. S-004 is broker-agnostic and parallelisable with S-002 / S-003 (no Kafka-runtime dependency, no Avro, no schema registry).

### In scope

- A new SQL Server table `dbo.KAFKA_ERROR_LOG` defined via the existing `Dorc.Database` SSDT project (matches DOrc's table-definition convention).
- An EF Core entity (`KafkaErrorLogEntry`) + `DbSet` on `DeploymentContext` + entity type configuration matching the SSDT shape.
- A narrow async DAL interface `IKafkaErrorLog` with three methods: `InsertAsync`, `QueryAsync`, `PurgeAsync`.
- A configurable `KafkaErrorLogOptions` carrying retention-window-days, max-payload-bytes, and purge-batch-size.
- DI registration via an extension method `AddDorcKafkaErrorLog(IServiceCollection, IConfiguration)`.
- Unit tests against an in-memory or SQLite EF provider for the DAL behaviour.
- Integration tests against a real SQL Server (the existing DOrc test SQL Server, if available; otherwise a containerised one) for schema correctness, insert round-trip, and purge cadence.

### Out of scope

- Consumer wiring (S-006 / S-007 / S-008 per flow).
- Poison-message detection logic in the S-002 client layer (each migration step decides what counts as poison for its flow).
- A UI for browsing the error log (operator concern; not in this initiative).
- Cross-region replication / archiving of the error log (DOrc's existing SQL backup story applies).
- Any DLQ-style replay mechanism — HLPS C-8 explicitly chose error-log over DLQ.

---

## 2. Requirements

### R-1 — Table schema (HLPS C-8 fields)

`dbo.KAFKA_ERROR_LOG` must capture every C-8 field plus the audit identifiers DOrc tables conventionally carry:

| Column | SQL type | Nullable | Notes |
|---|---|---|---|
| `Id` | `BIGINT IDENTITY(1,1)` | NO | PK |
| `Topic` | `NVARCHAR(255)` | NO | Kafka topic name |
| `Partition` | `INT` | NO | Kafka partition number |
| `Offset` | `BIGINT` | NO | Kafka offset within the partition |
| `ConsumerGroup` | `NVARCHAR(255)` | NO | Consumer group id at time of failure |
| `MessageKey` | `NVARCHAR(512)` | YES | Key as text; null if the message had no key |
| `RawPayload` | `VARBINARY(MAX)` | YES | Raw bytes-on-wire; truncated per R-3 if oversize; null if the failure was upstream of the value bytes (e.g. registry fetch) |
| `PayloadTruncated` | `BIT` | NO | `1` if `RawPayload` was truncated to fit; `0` otherwise |
| `Error` | `NVARCHAR(2000)` | NO | Top-level exception message |
| `Stack` | `NVARCHAR(MAX)` | YES | Full stack trace; null if synthesised from a non-exception failure |
| `OccurredAt` | `DATETIMEOFFSET` | NO | Timestamp of the failure event, normalised to UTC (offset = 00:00). |
| `LoggedAt` | `DATETIMEOFFSET` | NO | Timestamp of the row insert, normalised to UTC (offset = 00:00). Separate from `OccurredAt` so logging-pipeline delays are visible. |

Both timestamp columns and the DAL surface use `DateTimeOffset` end-to-end — no EF-side coercion to a `DateTime` (Kind=Utc) and no offset-stripping at the SQL boundary. This avoids the silent timezone-loss path SSDT/EF coercion would otherwise introduce.

Indexing per R-2.

### R-2 — Indexes for the operator-likely query patterns

- Primary key on `Id`.
- Non-clustered index on `(Topic, OccurredAt DESC)` — operators triage by topic + recency.
- Non-clustered index on `(ConsumerGroup, OccurredAt DESC)` — consumer-group failure-rate inspection.
- Non-clustered index on `OccurredAt` — supports R-3 retention purge.

### R-3 — Retention + size cap

`KafkaErrorLogOptions` carries:

- `RetentionDays` (default **30**) — rows are purged when `OccurredAt < UtcNow - RetentionDays`. The cutoff is evaluated against `OccurredAt` (operator intent: "when did the failure occur"), not `LoggedAt`. Clock-skew between insert host and purge host within typical DOrc fleet drift (≤ a few minutes) is accepted; a back-dated `OccurredAt` will purge sooner than its row age implies, which is acceptable for an operator-tunable retention.
- `MaxPayloadBytes` (default **65_536** = 64 KiB) — payloads **strictly greater than** `MaxPayloadBytes` are truncated; payloads of length **exactly** `MaxPayloadBytes` are stored intact with `PayloadTruncated = 0`. Truncated rows have `PayloadTruncated = 1` and the kept bytes are the **leading prefix** of the original payload.
- `PurgeBatchSize` (default **5_000**) — `PurgeAsync` deletes rows in batches up to this size to avoid long-running transactions on large backlogs.

**Defaults rationale.** A pathological poison-topic outbreak at 10 failures/second across one topic is roughly 26 M rows over 30 days. With each row capped at ~65 KiB payload + ~2 KiB metadata, that's ~1.7 TiB worst-case — too large for indefinite retention but well within a 30-day rolling window for a SQL Server with normal backup cadence. Steady-state DOrc error rates are far lower (single-digit per day historically); the 30-day default keeps a comfortable post-incident audit window. The 64 KiB payload cap covers the canonical Avro-encoded DOrc events (~120-200 bytes) by ~3 orders of magnitude, so non-pathological payloads are never truncated. The 5_000-row batch keeps individual `DELETE` transactions short enough to avoid index-lock escalation.

**Validation (R-5 hook):** `RetentionDays`, `MaxPayloadBytes`, and `PurgeBatchSize` must all be `> 0`; the options validator rejects zero or negative values at host build time per AT-6.

`PurgeAsync` is idempotent and re-entrant; calling it on an empty table is a cheap no-op; calling it twice in succession on a full table converges to "no rows older than the cutoff."

The actual scheduling of `PurgeAsync` (cron, hosted-service, manual ops job) is **out of scope** for S-004; the DAL exposes the operation, and the consumer-wiring steps or a separate operational job decide cadence.

### R-4 — DAL surface

```text
interface IKafkaErrorLog
{
    Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken ct);

    Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
        string? topic, string? consumerGroup,
        DateTimeOffset? sinceUtc, int maxRows,
        CancellationToken ct);

    Task<int> PurgeAsync(CancellationToken ct);
}
```

(Pseudocode; exact namespaces, method overloads, and parameter packaging are Delivery-phase choices.)

`InsertAsync`:
- Applies R-3 truncation to the payload before persisting.
- Sets `LoggedAt = DateTimeOffset.UtcNow` (DAL-populated, not caller-populated).
- Sets `Id` (DAL-populated via SQL `IDENTITY`) and `PayloadTruncated` (DAL-populated based on the truncation decision).
- Does **not** participate in the calling consumer's ambient transaction — error-logging must not roll back with the consumer's commit cycle. The DAL uses its own `SaveChanges` scope.
- Honours the supplied `CancellationToken`. Bounding the wait under SQL-slow-but-not-down conditions is the **caller's** responsibility (S-006 / S-007 / S-008 will set a timeout via `CancellationTokenSource(timeout)` so a degraded DB doesn't stall the consumer indefinitely; the DB-unavailable structured-log fallback then triggers).

**Caller-vs-DAL-populated fields on `KafkaErrorLogEntry` (for S-006/S-007/S-008 wiring):**
- Caller-populated: `Topic`, `Partition`, `Offset`, `ConsumerGroup`, `MessageKey`, `RawPayload`, `Error`, `Stack`, `OccurredAt`.
- DAL-populated server-side: `Id`, `LoggedAt`, `PayloadTruncated` (the latter set by the truncation decision in `InsertAsync`).

`QueryAsync`:
- All filter parameters are optional; nulls mean "no filter on this dimension".
- `maxRows` is required; the DAL enforces an upper bound (default-cap **10_000**) to protect SQL Server.
- Results ordered by `OccurredAt DESC` then `Id DESC`.

`PurgeAsync`:
- Returns the total count of rows deleted across all batches in the call.

### R-5 — DI registration

A single extension `AddDorcKafkaErrorLog(this IServiceCollection, IConfiguration)`:

- Binds the `Kafka:ErrorLog` configuration section into `KafkaErrorLogOptions` with `ValidateOnStart()` semantics (per S-002 R-1 fail-fast pattern).
- Registers the DAL as a scoped service against `IKafkaErrorLog`.
- Idempotent (marker-singleton + `TryAdd*` per S-002 / S-003 pattern).

Reuses the existing `IDeploymentContext` / EF Core wiring — no new connection-string config keys.

### R-6 — Schema bootstrap consistency

Two parallel definitions of the table exist by DOrc convention:

1. The SSDT `.sql` file under `src/Dorc.Database/dbo/Tables/KAFKA_ERROR_LOG.sql` (authoritative for ops).
2. The EF entity + EntityTypeConfiguration under `src/Dorc.PersistentData/`.

Both must produce a structurally-equivalent table. AT-3 verifies this on a real SQL Server (column names, types, nullability, index set).

### R-7 — Consumer-side wiring placeholder (informational)

S-004 ships **only** the storage substrate. A short README note under `docs/kafka-migration/` documents the intended consumer-side handshake for S-006 / S-007 / S-008:

> On a `ConsumeResult<T>` failure, the consumer wraps the failure into a `KafkaErrorLogEntry` and calls `IKafkaErrorLog.InsertAsync`. If the insert itself fails (DB unavailable), the consumer falls back to a structured `LogError` entry carrying the same fields. The consumer commits its offset only **after** the log path completes (success or DB-unavailable fallback); the offset never advances on a silent failure.

This is documentation, not code. The contract becomes binding when S-006 lands.

---

## 3. Out of Scope (explicit)

- Consumer wiring → S-006 / S-007 / S-008 per flow.
- A purge scheduler / hosted service → operational follow-up, not in this initiative.
- Replay tooling (DLQ-style) — HLPS C-8 forbids.
- Schema versioning / migrations — DOrc uses `EnsureCreated()`; S-004 follows that pattern.
- Cross-region log replication — out of scope for migration; existing SQL backups apply.

---

## 4. Acceptance Criteria

### AT-1 — Table created with R-1 shape on a real SQL Server

- Running `Dorc.Database` SSDT publish (or `EnsureCreated()` against a fresh DB) materialises `dbo.KAFKA_ERROR_LOG` with every R-1 column at the specified type + nullability.
- Indexes per R-2 exist post-publish.
- Column shape verification is automated (a query against `INFORMATION_SCHEMA.COLUMNS` and `sys.indexes`).

### AT-2 — Insert round-trip persists every R-1 field correctly

Unit-test against an in-memory or SQLite EF provider:

- `InsertAsync` of an entry with all fields populated round-trips byte-equal on `RawPayload`, character-equal on text fields, `OccurredAt` preserved (UTC), `LoggedAt` populated server-side.
- `InsertAsync` of an entry with only the mandatory fields succeeds; nullable fields land as null.
- An entry whose payload **strictly exceeds** `MaxPayloadBytes` is truncated to exactly `MaxPayloadBytes` bytes; `PayloadTruncated` is `1`; the truncated content is the leading prefix of the original.
- An entry whose payload is **exactly** `MaxPayloadBytes` is stored intact with `PayloadTruncated = 0` (boundary case).

### AT-3 — Schema parity between SSDT and EF

Integration test against a real SQL Server (test instance or container). The check is for **structural equivalence**, not byte-identical DDL:

- Column set is equal: same names, same normalised type strings (e.g. `nvarchar(255)`, `datetimeoffset`, `varbinary(max)`), same nullability.
- Index key-column sets are equal across the two definitions. **Auto-generated index names**, **filegroup / storage / `WITH (...)` clauses**, and other SQL-Server-defaulted artefacts are explicitly **ignored** by the comparison — they routinely differ between SSDT-published DDL and EF `EnsureCreated()` DDL without being semantically different, and a strict 1:1 comparison would produce false-positive failures.

### AT-4 — Query returns rows by filter

Unit test (in-memory EF):

- After inserting a mix of rows across topics, consumer groups, and timestamps:
  - `QueryAsync(topic: "X", ...)` returns only rows for topic X.
  - `QueryAsync(consumerGroup: "G", ...)` returns only rows for group G.
  - `QueryAsync(sinceUtc: t, ...)` returns only rows with `OccurredAt >= t`.
  - `maxRows` caps the result count.
  - Composite filter (topic + group + since) intersects correctly.
  - Results are ordered by `OccurredAt DESC` then `Id DESC`.

### AT-5 — Purge deletes only rows beyond the retention window

Unit test (in-memory EF):

- Insert N rows across a range of `OccurredAt` values straddling `UtcNow - RetentionDays`.
- `PurgeAsync()` returns the count of rows older than the cutoff; remaining rows in the table are exactly those within the window.
- Calling `PurgeAsync()` a second time returns `0`.
- A run with > `PurgeBatchSize` deletable rows completes correctly across multiple batches.

### AT-6 — DI extension composes cleanly

Unit test:

- `AddDorcKafkaErrorLog` resolves an `IKafkaErrorLog` against a host with `IDeploymentContext` registered.
- Calling the extension twice is idempotent (descriptor count unchanged on second call).
- Missing mandatory config (none today — `RetentionDays`, `MaxPayloadBytes`, `PurgeBatchSize` all have defaults) — instead, **invalid** values (e.g. negative `RetentionDays`) cause `ValidateOnStart()` to throw at host build with the failing key in the message.

### AT-7 — DAL contract is callable from the S-002 consumer shape

A compile-only test that wires a fake `ConsumeResult<TKey, TValue>` failure into an `InsertAsync` call. **Success signal:** the test project builds and a named test contains a call site that constructs `KafkaErrorLogEntry` from a `ConsumeResult<TKey, TValue>` plus an `Exception` and calls `IKafkaErrorLog.InsertAsync(entry, ct)`. The test does not need to execute against any backend — its job is to fail compilation if the DAL surface drifts away from what S-006/S-007/S-008 will need.

This AT is **architectural smoke**, not behavioural — proves the surface fits, not the wiring. Behavioural proof of the R-7 handshake (offset-commit ordering, fallback-to-structured-log on DB failure) lives in S-006.

---

## 5. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| Two parallel table definitions (SSDT + EF) can drift | R-6 | Accepted — AT-3 verifies parity at integration time; future drift is caught by the same test. |
| `EnsureCreated()` does not version-migrate; once `KAFKA_ERROR_LOG` exists, schema changes need either a manual migration or a `DROP + EnsureCreated` cycle | DOrc convention | Accepted — matches every other DOrc table; S-004 does not introduce migrations. |
| Purge runs on a single SQL Server thread; very large backlogs may take noticeable time | R-3 batching | Accepted — `PurgeBatchSize` default of 5_000 keeps individual transactions short; operational cadence keeps backlogs small. |
| Insert during a SQL Server outage will throw and the consumer-side fallback to structured log captures the entry; no in-process queue / retry | R-7 | Accepted — explicit per HLPS C-8; the structured-log fallback is the documented degraded mode. |
| Test project for this DAL needs Dorc.PersistentData / Dorc.Core refs and therefore lives **outside** Dorc.Kafka.Client.Tests for the same EF-9.x reason as Dorc.Kafka.Events.Tests | S-003 architecture note | Accepted — new project `Dorc.Kafka.ErrorLog.Tests` (or live in an existing PersistentData-aware test project, Delivery's choice). |
| Containerised SQL Server availability for AT-3 | AT-3 | Accepted — if a SQL container is impractical, AT-3 may run against a live developer-machine SQL Server with the test marked as requiring `DORC_SQL_TEST_CONNECTION` env var; CI runs of AT-3 are deferred until the trading runner gains a SQL test instance. |
| Dedup of duplicate poison-message log rows is **not** an S-004 concern. The same `(Topic, Partition, Offset)` failing twice will produce two rows, distinguishable only by `LoggedAt` / `Id`. | R-2 | Accepted — adding a unique `(Topic, Partition, Offset)` constraint would force callers to handle a unique-violation path, complicating S-006 wiring. Operator queries can group by `(Topic, Partition, Offset)` to count repeats. If true dedup is later wanted, S-006 can request an additive index without S-004 reopening. |
| Clock skew between insert host and purge host can purge a back-dated `OccurredAt` row sooner than its `LoggedAt` age implies. | R-3 | Accepted — operator intent is "when did the failure occur", and DOrc fleet drift is bounded; consequence is at most a row purged minutes earlier than naive expectation. |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Project layout suggestion (Delivery's call):** keep the DAL implementation either in `Dorc.PersistentData` (closest to the EF context) or in a new `Dorc.Kafka.ErrorLog` project that references `Dorc.PersistentData` if separation is preferred. Either is consistent with prior step boundaries.
- **Tests:** AT-2 / AT-4 / AT-5 / AT-6 / AT-7 use the EF in-memory or SQLite provider (faster, hermetic). AT-1 / AT-3 are the only ones that need a real SQL Server.
- **Migration of existing data:** none. Table is new; first deployment leaves it empty.
- **Docs:** add a short `docs/kafka-migration/error-log-runbook.md` covering operator-facing concerns (querying recent failures by topic/group, purge cadence expectations, structured-log fallback signal). Not part of acceptance — light documentation only.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Clarity + completeness of R-1 (every C-8 field accounted for; sane SQL types) and R-2 (indexes for the likely operator queries).
- R-3 retention/size cap defensibility: defaults reasonable for DOrc's expected error-rate volume? Truncation semantics deterministic?
- R-4 DAL surface — minimal, sufficient for S-006 / S-007 / S-008 wiring?
- AT-3 schema-parity strategy — viable given DOrc's two-track schema definition?
- AT-7 architectural-smoke as a stand-in for full consumer wiring — sufficient discipline against future drift?
- Accepted-risk coverage.

Reviewers should **NOT**:

- Demand exact namespace placement, method signatures, EF configuration mechanics, or test-project boundaries — these are Delivery-phase concerns.
- Re-litigate HLPS / IS decisions: no DLQ, no replay, error-log-only, structured-log fallback on DB-unavailable, `EnsureCreated()`-not-migrations, two-track SSDT + EF schema model.
- Demand a purge scheduler — out of scope by §3.

---

## 8. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Sonnet-F1 / Gemini-G1 / GPT-F4 | All three | MEDIUM | Timestamp-type mismatch: columns were `DATETIME2` but DAL surface used `DateTimeOffset` — silent EF coercion / timezone-loss risk | **Accepted** — both columns now `DATETIMEOFFSET` and DAL uses `DateTimeOffset` end-to-end. R-1 has an explicit one-paragraph note. |
| Sonnet-F2 / GPT-F2 | Sonnet, GPT | MEDIUM | Truncation boundary at `len == MaxPayloadBytes` underspecified | **Accepted** — R-3 now explicit: strictly greater triggers truncation; equal preserves intact. AT-2 adds the boundary case. |
| Gemini-G2 | Gemini | MEDIUM | Retention defaults (30d / 64KiB / 5_000 batch) lacked rationale | **Accepted** — R-3 now carries a "Defaults rationale" paragraph (worst-case envelope + steady-state characterisation). |
| Gemini-G3 | Gemini | MEDIUM | `InsertAsync` silent on slow-but-not-down DB / cancellation contract | **Accepted** — R-4 now states `InsertAsync` honours the `CancellationToken` and bounding the wait is the caller's responsibility (consumer sets a timeout). |
| GPT-F1 | GPT | MEDIUM | Retention column choice (`OccurredAt` vs `LoggedAt`) under clock skew not stated | **Accepted** — R-3 now states `OccurredAt` is the cutoff; clock-skew within fleet drift is documented as accepted. |
| Sonnet-F3 | Sonnet | LOW | R-5 didn't enumerate validation positivity rules | **Accepted** — R-3 now lists the `> 0` constraints alongside the defaults rationale. |
| Sonnet-F4 | Sonnet | LOW | R-4 "unless explicitly supplied" qualifier on transactions was vacuous given the signature | **Accepted** — R-4 now drops the qualifier and states the DAL uses its own `SaveChanges` scope. |
| Sonnet-F5 | Sonnet | LOW | AT-7 "compile-only test" success signal not stated | **Accepted** — AT-7 now defines success as "test project builds + named test contains the call site". |
| Gemini-G4 | Gemini | LOW | `(Topic, Partition, Offset)` index for dedup not present | **Accepted as risk** — §5 documents that dedup is not S-004's concern; S-006 can request an additive index later. |
| Gemini-G5 | Gemini | LOW | AT-3 "1:1 match" wording too strict; SSDT vs EF DDL routinely differs | **Accepted** — AT-3 now compares structural equivalence (column names + normalised type strings + nullability + index key-column sets), explicitly ignoring auto-generated index names + storage clauses. |
| Gemini-G6 | Gemini | LOW | R-7 doesn't enumerate caller-populated vs DAL-populated fields | **Accepted** — R-4 now carries the field bullet list directly. |
| GPT-F3 | GPT | (non-finding) | AT-7 architectural-smoke is correct scope for S-004 | Acknowledged — S-006 owns behavioural proof of the R-7 handshake. |

All 5 MEDIUM findings accepted and resolved via surgical edits. 4 LOWs accepted; 1 LOW recorded as accepted risk in §5; 1 GPT non-finding acknowledged. No re-litigation of HLPS/IS decisions. Per CLAUDE.md §4: three APPROVE-tier verdicts with all MEDIUMs resolved = **unanimous approval**. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
