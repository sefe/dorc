# S-010 — DOrc Kafka Cutover Runbook

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval (operator dry-run pending) |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Governing** | `SPEC-S-010-Cutover-Runbook.md` (APPROVED — Pending user approval); IS §3 S-010 / S-011; HLPS SC-8 / C-2 / R-3 |
| **Cutover artefact** | `feat/kafka-migration` HEAD (post-S-009) |
| **Rollback target** | annotated tag `release/pre-kafka-cutover` (tag object `0d5146c8` → commit `481f4830`) |
| **Hard ceiling** | 240 minutes from "go" to "verified" (binding per IS §3 S-010) |

---

## 1. How to use this runbook

Read top to bottom. Sections in order:

1. §2 Pre-cutover smoke-test catalogue — execute before §3 begins.
2. §3 Wall-clock-budgeted cutover sequence — gate-driven; stop on any gate fail.
3. §4 Rollback triggers — observable conditions that fire §5.
4. §5 Rollback procedure — execute on any trigger fire; ≤60-minute target.
5. §6 Post-cutover monitoring — operator checks at T+1h / +4h / +12h / +24h.
6. §7 Communication + authority plan — comm channel, schedule, decision authority.
7. §8 Operator-facing tuning notes carried forward.
8. §9 Dry-run evidence pointer.

The same §2 smoke-test catalogue is re-run at four points: pre-cutover,
the post-deploy gate, post-cutover-verified, and post-rollback (if §5
fires).

---

## 2. Smoke-test catalogue (R-1)

Run from operator workstation against the deployed environment under
test. Each test logs PASS / FAIL and wall-clock duration to the
evidence transcript.

| # | Name | Verifies | Pass criteria | Expected duration | Blocker? |
|---|---|---|---|---|---|
| ST-1 | API health | `GET /api/health` returns 200 with JSON containing the deployed build number | HTTP 200; JSON `version` field matches expected build | ≤5 s | Yes |
| ST-2 | Monitor service running | `Get-Service DeploymentActionService{Prod\|NonProd}` shows `Running` on each Monitor host | Service status `Running` on every replica | ≤10 s per replica | Yes |
| ST-3 | Kafka lock coordinator joined | Monitor log shows `KafkaLockCoordinator subscribed: topic=dorc.locks group=dorc.monitor.locks partitions=12` followed by a `partitions incrementally assigned` line within `SessionTimeoutMs` (default 10 s) | Both log lines present in the post-startup window | ≤30 s after Monitor start | Yes |
| ST-4 | Synthetic deployment end-to-end | Submit a deployment request to a smoke-test environment with a no-op script; observe `Pending → Requesting → Running → Complete` transitions | Final status `Complete` reached within the budget | ≤5 min | Yes |
| ST-5 | SignalR live UI | Connect the web UI; submit ST-4 in another tab; observe the deployment row's status update live (without page refresh) | Live status update observed within 2 s of the Kafka commit log line | ≤30 s | Yes |
| ST-6 | Avro schema resolution observability | Producer + consumer logs show `avro-schema-resolved subject={Subject} type={Type} kind={serializer\|deserializer}` for every in-scope subject (`dorc.results.status-value`, `dorc.requests.new-value`, `dorc.requests.status-value`) | All six lines present (3 subjects × 2 kinds) within ST-4's window | ≤10 s | No (warn only) |
| ST-7 | Kafka error log empty | `SELECT COUNT(*) FROM KAFKA_ERROR_LOG WHERE OccurredAt >= '<cutover window start>'` | Zero rows, OR every row dispositioned by operator with note in evidence transcript | ≤5 s | Yes |

Total smoke-test wall clock at full pass: ~7 minutes including the
ST-4 deployment.

---

## 3. Wall-clock-budgeted cutover sequence (R-3)

| T (min from "go") | Step | Owner role | Budget | Cumulative ceiling |
|---|---|---|---|---|
| T−30 to T0 | Pre-cutover §2 smoke-test against current production (which is the rollback baseline). Run §7 T−1h notification. | Release engineer | 30 min | (pre-cutover) |
| **T0** | **GATE A — Pre-deploy:** every ST-1..ST-5 + ST-7 PASS on current prod; comm channel populated; rollback authority confirmed available. **Pass→ proceed; fail → abort cutover, no rollback needed.** | Tech lead | 5 min | 5 |
| T0 → T+30 | Roll API replicas to Kafka build via standard MSI pipeline. Rolling deploy, one replica at a time; wait for ST-1 PASS on each before next. | Release engineer | 30 min | 35 |
| **T+30** | **GATE B — API rolled:** ST-1 + ST-5 + ST-6 PASS on the upgraded API. **Pass → proceed; fail → fire §5.** | Tech lead | 5 min | 40 |
| T+35 → T+65 | Roll Monitor replicas to Kafka build. Rolling deploy, one host at a time; wait for ST-2 + ST-3 PASS on each before next. | Release engineer | 30 min | 70 |
| **T+65** | **GATE C — Monitor rolled:** ST-2 + ST-3 + ST-4 + ST-7 PASS. **Pass → proceed; fail → fire §5.** | Tech lead | 5 min | 75 |
| T+70 → T+85 | Full §2 smoke catalogue against the cutover-deployed environment. | Release engineer | 15 min | 90 |
| **T+85** | **GATE D — Smoke gate:** every ST-1..ST-7 PASS (ST-6 may warn). **Pass → declare "verified-pending-soak"; fail → fire §5.** | Tech lead | 5 min | 95 |
| T+95 → T+225 | Soak window: passive monitoring per §6 1h, 2h, 4h thresholds. No active operator actions; rollback triggers (§4) remain armed. | On-call SRE | 130 min | 225 |
| **T+225 to T+240** | **GATE E — Soak verified:** §6 T+1h + T+2h + T+4h checkpoints all green; KafkaErrorLogEntry rate within threshold; no rebalance churn. **Pass → declare "verified" + run §7 T+verified notification; fail → fire §5.** | Tech lead | 15 min | 240 |
| **T+240** | **HARD CEILING.** If "verified" not declared, fire §5 unconditionally (auto-rollback authority). | On-call SRE | 0 | 240 |

**Sub-budget invariant:** if any of GATE A→D slips past its budget,
recompute the soak window: it shortens, never lengthens. If the soak
window collapses below 30 minutes, treat as a §4 trigger and fire §5.

---

## 4. Rollback triggers (R-4)

Each trigger fires §5. Two classes:

### 4.1 Immediate-rollback class (operator-initiated on observation, no prior authorisation gate)

| Trigger | Observable | Authority |
|---|---|---|
| Smoke-test failure during GATE A..E | Any ST-1..ST-5 or ST-7 returns FAIL | On-call SRE executes §5 on observation; notifies §7 channel |
| Monitor process restart in cutover window | `Get-Service` `StartTime` increments after T0; OR Windows event-log shows service crash | On-call SRE executes §5 on observation |
| Kafka producer fail-loud at API tier sustained | API log shows `publish-failed` for >5 consecutive requests within 60 s | On-call SRE executes §5 on observation |

### 4.2 Operator-decision class (tech-lead decides; release engineer is tie-break)

| Trigger | Observable | Threshold |
|---|---|---|
| KafkaErrorLog insert rate elevated | `SELECT COUNT(*) FROM KAFKA_ERROR_LOG WHERE OccurredAt >= now()-INTERVAL '5' MINUTE` | > 5 entries / 5 min sustained for 10 min |
| Lock-rebalance churn | Monitor log: `partitions incrementally assigned` + `partitions incrementally revoked` event-pair count | > 10 event-pairs / minute sustained for 5 min |
| API request latency p99 step-change | API metrics endpoint p99 latency vs T-24h baseline | > 2× baseline for 10 min |

If tech lead is unavailable, the release engineer is the tie-break
authority. If both unavailable, the on-call SRE escalates per §7
roster and may execute §5 on a unilateral observation.

---

## 5. Rollback procedure (R-5) — target ≤60 minutes

| T (min from "rollback decision") | Step | Owner role | Budget |
|---|---|---|---|
| T0 | Notify §7 comm channel: "Rollback fired at <reason>; redeploy of `release/pre-kafka-cutover` beginning". | On-call SRE | 1 |
| T+1 | Pause new deployment requests at the API tier: set `AppSettings.PauseDeploymentEnabled = "true"` in the deployed `appsettings.json` and bounce the API process (or hot-reload if the deploy pipeline supports it). Verify by submitting a synthetic request to a non-cutover environment — it must be rejected with `PauseDeploymentEnabled` semantics. | Release engineer | 5 |
| T+6 | Wait 30 s for in-flight Kafka publishes to drain (producer linger budget). | Release engineer | 1 |
| T+7 | Redeploy API replicas from `release/pre-kafka-cutover` via the standard MSI pipeline (annotated tag, dereferences to commit `481f4830`). Rolling, one replica at a time; wait for ST-1 PASS on each. **Verify before deploy:** `git rev-parse release/pre-kafka-cutover^{commit}` returns `481f4830` (commit SHA). The bare `git rev-parse release/pre-kafka-cutover` returns the annotated tag object SHA `0d5146c8` — that is normal for annotated tags and not a defect. | Release engineer | 20 |
| T+27 | Redeploy Monitor replicas from the same tag. Rolling, one host at a time; wait for ST-2 PASS on each. | Release engineer | 20 |
| T+47 | Verify: the rolled-back build registers `RabbitMqDistributedLockService` + the SignalR-only publisher path (rollback build pre-dates S-005b/S-006/S-007 cutover wiring). | Tech lead | 3 |
| T+50 | Lift the pause: set `AppSettings.PauseDeploymentEnabled = "false"`; bounce the API process. | Release engineer | 3 |
| T+53 | Run full §2 smoke catalogue against the rolled-back environment. Every ST-1..ST-7 must PASS. | Release engineer + tech lead | 7 |
| T+60 | Declare "rollback complete"; notify §7 channel; archive transcript. | Tech lead | 0 |

**Pre-requisite:** RabbitMQ infrastructure must remain hot through
S-011 + the C-9 14-day window. Cold-standby decommissioning is S-012
per IS §3 S-011.

---

## 6. Post-cutover monitoring (R-6)

After GATE E declares "verified", operator checks at:

| Checkpoint | Actions | Pass criteria |
|---|---|---|
| T+1h | §2 smoke catalogue; review KafkaErrorLog table; check rebalance event count vs T-24h baseline | All ST PASS; KafkaErrorLog rate within §4.2 threshold; rebalance count within 2× baseline |
| T+4h | Same | Same |
| T+12h | Same; plus per-Monitor-replica memory + CPU trend (no anomalous step-change vs T-24h baseline) | Same |
| T+24h | Same; plus API request latency p95 / p99 trend (no anomalous step-change) | Same |

If any checkpoint fails, escalate per §7 roster — possibly fire §5 if
within the C-9 rollback window.

---

## 7. Communication + authority plan (R-8a)

### 7.1 Primary comm channel

A dedicated team chat bridge (e.g. Teams / Slack channel) AND a
conference bridge for synchronous coordination. The chat channel is
the source of truth for cutover-window timeline; the conference
bridge is for live decision-making during gate transitions and
rollback fires.

### 7.2 Stakeholder notification schedule

| Time | Audience | Message contents | Sender role |
|---|---|---|---|
| T−24h | Deployment-ops distribution list | "Cutover scheduled for <T0 timestamp>; in-flight deployments at T0 will be terminated per HLPS C-2; rollback target = `release/pre-kafka-cutover`" | Release engineer |
| T−1h | Same + on-call SRE roster | "Final readiness check — comm channel is `<channel>`; bridge is `<bridge>`" | Release engineer |
| T0 | Same | "Cutover started" | Release engineer |
| T+verified | Same | "Cutover verified at <wall clock>; post-cutover monitoring window begins" | Release engineer |
| Rollback (if §5 fires) | Same + leadership distribution list | "Rollback fired at <T+x> for <trigger>; rolled back to `release/pre-kafka-cutover`; smoke catalogue re-passed at <T+x+y>" | On-call SRE |

### 7.3 Decision-authority chain

- **Auto-trigger rollbacks (§4.1):** on-call SRE observes the trigger,
  executes §5 immediately, then notifies §7.1 channel. No prior
  authorisation gate. "Immediate" = operator-initiated-on-observation,
  not pipeline-automated.
- **Operator-decision rollbacks (§4.2):** tech lead decides on
  trigger. If tech lead is unreachable within 5 minutes, release
  engineer is the tie-break authority. If both are unreachable,
  on-call SRE escalates per §7.4 roster and may execute §5 on a
  unilateral observation.

### 7.4 Escalation / paging roster (role-list)

| Role | Coverage during cutover window |
|---|---|
| Tech lead | T−1h to T+24h (active during cutover; on-call during soak / monitoring) |
| Release engineer | T−1h to T+verified (active throughout; off after verified) |
| On-call SRE | T−1h to T+24h (continuous) |
| Leadership escalation | T0 to T+24h (paged on §5 fire; not paged on green path) |

Names filled in at S-011 time, not in this runbook.

---

## 8. Operator-facing tuning notes carried forward (R-8)

### 8.1 Lock-wait-cap (S-005b Caller Survey §3 operational note)

The Monitor caller `DeploymentRequestStateProcessor.cs:501` passes
`EnvironmentLockLeaseTimeMs` as the wait-cap for `TryAcquireLockAsync`.
Under the Kafka substrate, this is the maximum time the call blocks
waiting for partition ownership before returning null. **Recommended
value: ~5–30 seconds.** Under the original Rabbit substrate this was
"longer than a typical request duration" (TTL semantics); inheriting
that in the Kafka substrate would block API calls for too long on
the null path. Operators verify the deployed value is in the
recommended range during pre-cutover GATE A.

### 8.2 `Kafka:Locks:*` defaults (S-009 R-7 GPT-F5 note)

The `Kafka:Locks:*` block in Monitor's `appsettings.json` carries:

- `Topic` = `dorc.locks`
- `PartitionCount` = `12`
- `ReplicationFactor` = `3`
- `ConsumerGroupId` = `dorc.monitor.locks`

These ship as defaults and are NOT driven by an MSI parameter — they
are baked into the deployed `appsettings.json`. **Partition count is
immutable post-cutover** per ADR-S-005 §4 #2. If a partition-count
change is ever required, it requires a fresh topic + a controlled
re-cutover, not a config flip.

### 8.3 Kafka cluster connectivity (S-009 installer migration)

Cutover deploy passes the following MSI parameters (per
`Setup.Dorc.msi.json` / `Install.Orchestrator.bat`):

- `KAFKA.BOOTSTRAPSERVERS` — comma-separated `host:port` list
  (production: Aiven cluster).
- `KAFKA.SASL.USERNAME` — service-account name (uppercase per Aiven
  convention).
- `KAFKA.SASL.PASSWORD` — service-account password.
- `KAFKA.SSLCA.LOCATION` — local file path to the Kafka CA bundle
  (or empty if SSL is unauthenticated against the platform CA bundle).

Verified routing in `*ActionService.wxs` writes these into
`$.AppSettings.Kafka.{BootstrapServers, Sasl.Username, Sasl.Password, SslCaLocation}`.

If a future earlier-step audit produces additional operator-facing
notes after this runbook is committed, the runbook is amended in a
follow-up commit.

---

## 9. Dry-run evidence (R-7 / AT-5..AT-8)

The staging dry-run is operator-executed (the AI assistant cannot
deploy to staging). When the dry-run is performed, capture under
`docs/kafka-migration/S-010-DryRun-evidence/<timestamp>/`:

- `pre-cutover-smoke.txt` — §2 transcript against staging-pre-cutover.
- `cutover-wallclock.txt` — actual wall-clock per §3 step; final
  T+verified time. **Must show ≤ 240 minutes from "go" to "verified"
  per AT-2 / AT-6.**
- `rollback-rehearsal.txt` — full §5 procedure transcript including
  decision time, redeploy time, post-rollback smoke time. **Must show
  ≤ 60 minutes from "rollback decision" to "rollback complete" per
  AT-7.**
- `post-rollback-smoke.txt` — §2 transcript after the rollback
  rehearsal. **Every ST-1..ST-7 PASS per AT-8.**
- `summary.md` — verdict (`Pass` / `Pass-with-runbook-revision` /
  `Fail-rerun-required`); durations vs budgets; runbook revisions
  required (if any) and their dispositions.

If the dry-run produces a `Pass-with-runbook-revision` verdict, this
runbook is amended in a follow-up commit and the dry-run re-run on
the revised version. S-010 closes only on a `Pass` verdict per the
spec's AT-2 / AT-6 / AT-7 / AT-8.

---

## 10. Review note (R1 — 2026-04-15)

Single-reviewer light pass (GPT-5.3-codex) raised one CRITICAL claim
that the rollback tag SHA was wrong (`0d5146c8` vs `481f4830`). On
inspection: `release/pre-kafka-cutover` is an **annotated** tag —
`git rev-parse release/pre-kafka-cutover` returns the tag-object SHA
(`0d5146c8`), but the underlying commit (the actual rollback target)
is `481f4830`. Both reference the same artefact. The §header,
§5 T+7 row, and §10 summary card now state both SHAs explicitly so
operators have zero ambiguity. The reviewer's two LOW notes
(line-number-citation hygiene; rollback-build content verification
contingent on F-1) are deferred to dry-run validation.

## 11. Summary card (operator quick-reference)

```
Cutover artefact:    feat/kafka-migration HEAD (post-S-009)
Rollback target:     release/pre-kafka-cutover (tag obj 0d5146c8 -> commit 481f4830)
Hard ceiling:        240 minutes (binding, IS S-010)
Rollback target:     ≤60 minutes (S-005b R-8 calibration)
Comm channel:        <fill in at S-011 time>
Conference bridge:   <fill in at S-011 time>
Tech lead:           <fill in at S-011 time>
Release engineer:    <fill in at S-011 time>
On-call SRE:         <fill in at S-011 time>
```
