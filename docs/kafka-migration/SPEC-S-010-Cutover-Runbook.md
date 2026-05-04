# JIT Spec — S-010: Cutover Runbook + Staging Dry-Run

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Step ID** | S-010 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-010 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` SC-8, C-2, R-3 |
| **Prerequisites** | S-005a/b, S-006, S-007, S-008, S-009 closed; tag `release/pre-kafka-cutover` exists at `481f4830`. |

---

## 1. Purpose & Scope

Author the production-cutover runbook per HLPS SC-8 and execute it
end-to-end against staging at least once, including a full rollback
rehearsal. The runbook is the canonical artefact S-011 will execute
verbatim in production; the staging dry-run is the proof that the
runbook is correct and time-bounded within the 4h hard ceiling
(IS §3 S-010).

### In scope

- A new doc `docs/kafka-migration/S-010-Cutover-Runbook.md` containing:
  - Pre-cutover smoke-test catalogue (deployable + automatable).
  - Explicit go/no-go gate sequence with named role-owners and decision
    criteria.
  - Wall-clock-budgeted cutover sequence with a hard-ceiling of 4
    hours from "go" to "verified".
  - Rollback **trigger** criteria (error-rate threshold, smoke-test
    failure, HA thrash signal) — observable, not subjective.
  - Rollback **procedure**: redeploy `release/pre-kafka-cutover`
    (from S-009) into the Monitor + API roles; smoke-test verification.
  - Cutover communication + authority plan (R-8a) — comm channel,
    notification schedule, rollback-decision authority chain.
  - Post-cutover smoke-test suite (operator runbook for the first 24h).
- Operator-facing tuning notes carried forward from earlier-step
  audits (S-005b Caller Survey "operational tuning note", S-009 R-7
  installer notes for Kafka:Locks defaults).
- A staging dry-run evidence directory `docs/kafka-migration/S-010-DryRun-evidence/<timestamp>/`
  with: pre-cutover smoke-test transcript, cutover wall-clock log,
  rollback-rehearsal transcript, post-rollback smoke-test transcript.

### Out of scope

- Production cutover execution itself — that's S-011.
- Decommissioning the cold-standby RabbitMQ infrastructure — S-012
  per HLPS C-9 (T+14d).
- Removal of the SignalR hub or post-cutover follow-up cleanups
  (request-lifecycle Kafka→SignalR projection, DB-poll removal) —
  post-cutover.
- Modification of any production code — the runbook **only** orchestrates
  deploys; if a code change is needed during cutover the runbook
  routes to S-011 abort + post-mortem.

---

## 2. Requirements

### R-1 — Pre-cutover smoke-test catalogue

The runbook enumerates a smoke-test catalogue executable from the
operator's workstation against the deployed environment. Each test
states: name, what it verifies, expected pass criteria, expected
duration, and whether failure is a blocker. Coverage at minimum:

- DOrc API health endpoint returns 200 + JSON includes the deployed
  build number.
- Monitor process is running (Windows service status check).
- **Config-shape post-deploy check (per S-014):** on each installed
  node, `appsettings.json` exposes `Kafka.BootstrapServers` at the
  **JSON root** (i.e. the `Kafka` block is a top-level sibling of
  `AppSettings`, not a child of it). If the value appears under
  `AppSettings.Kafka` the deploy has not taken effect as expected
  and must be investigated before proceeding. Upgrade-installs may
  leave a cosmetic orphan subtree at `$.AppSettings.Kafka.*` — the
  root-level values are authoritative; the orphan is harmless but
  should be noted in the transcript.
- `KafkaLockCoordinator` partitions-assigned log line is observed
  within the configured `SessionTimeoutMs`.
- A synthetic deployment request (smoke-test environment) reaches
  `Complete` state within a runbook-stated wall-clock budget.
- SignalR hub reachable from the UI; UI receives the smoke-test's
  status events live.
- `KafkaErrorLogEntry` table is empty for the cutover window (or
  every entry is dispositioned by the operator).

The catalogue is reusable: same suite runs at pre-cutover, mid-cutover
gate, post-cutover, and post-rollback.

### R-2 — Go/no-go gate sequence with named owners

The runbook lists the gates in execution order. Each gate carries:

- Gate name and the immediately-preceding step it gates.
- Named owner role (e.g. "DOrc tech lead", "release engineer",
  "on-call SRE") — names are filled in at S-011 time, not S-010 time;
  the role is what's spec'd here.
- Pass/fail decision criteria — observable, not subjective.
- Action on fail: either retry-up-to-N-times-then-abort or immediate
  rollback (per gate).

At minimum: pre-deploy gate, post-deploy-API gate, post-deploy-Monitor
gate, smoke-test gate, T+30min gate, T+4h hard-ceiling gate (auto-rollback
trigger).

### R-3 — Wall-clock-budgeted cutover sequence

The runbook lists the cutover steps with each step's expected wall-clock
duration. The sum from "go" to "verified" MUST be ≤ 240 minutes (4h
hard ceiling per IS §3 S-010). The sequence at minimum covers:

1. Operator readiness checks (T-30 to T0).
2. API replicas rolled to Kafka build (T0).
3. Monitor replicas rolled to Kafka build (after API confirmation).
4. Smoke-test gate.
5. Soak window (T+30min to T+4h) with health monitoring.
6. T+4h hard-ceiling decision.

Any step that exceeds its budget triggers the gate it precedes;
cumulative slip beyond 4h triggers automatic rollback.

### R-4 — Rollback trigger criteria

The runbook defines observable rollback triggers. Each trigger states:
the metric / log line / smoke-test failure that fires it, the
threshold, the time window, and the rollback decision authority.
Examples (final values per dry-run calibration):

- Smoke-test failure during cutover gate → immediate rollback.
- KafkaErrorLogEntry insert rate > N entries / 5min → operator
  decision; threshold N tuned during dry-run.
- Lock-rebalance churn > X events / minute sustained for Y minutes
  → operator decision.
- Monitor process restart rate > 0 in the cutover window → immediate
  rollback (Monitor crashes during cutover are auto-trigger).

### R-5 — Rollback procedure

Step-by-step redeploy of the `release/pre-kafka-cutover` tag (S-009
R-1 cut at `481f4830`). The procedure:

1. Pause new deployment requests at the API tier (configurable
   pause flag or maintenance mode — runbook documents the mechanism).
2. Drain in-flight Kafka publishes (≤30s based on producer linger).
3. Redeploy API replicas from the `release/pre-kafka-cutover`
   tag via the standard MSI pipeline.
4. Redeploy Monitor replicas from the same tag.
5. Restore SignalR-only and RabbitMQ-lock substrate (the rollback
   tag carries the substrate-flag wiring + `RabbitMqDistributedLockService`
   intact).
6. Resume new deployment requests.
7. Post-rollback smoke-test suite (R-1) — every test must pass before
   declaring rollback complete.

The procedure MUST execute within 60 minutes from "rollback decision"
to "rollback complete" — this is the rehearsal-validation target.

### R-6 — Post-cutover smoke-test suite

A separate 24h post-cutover monitoring section. Operator checks at
T+1h, T+4h, T+12h, T+24h. Each check re-runs the R-1 catalogue plus:

- KafkaErrorLogEntry table review (every entry dispositioned).
- Lock-rebalance event count (compared against pre-cutover baseline).
- Per-Monitor-replica memory + CPU trend (no anomalous step-change).
- API request latency p95 / p99 (no anomalous step-change).

### R-7 — Staging dry-run evidence

Run the runbook end-to-end against staging Aiven at least once.
Capture under `docs/kafka-migration/S-010-DryRun-evidence/<timestamp>/`:

- `pre-cutover-smoke.txt` — R-1 transcript.
- `cutover-wallclock.txt` — actual wall-clock per R-3 step; final
  T+verified time.
- `rollback-rehearsal.txt` — full R-5 procedure transcript including
  decision-time, redeploy time, post-rollback-smoke time.
- `post-rollback-smoke.txt` — R-1 transcript after rollback.
- `summary.md` — verdict, durations vs budgets, any deviations from
  the runbook with their dispositions.

The dry-run is the test of the runbook; runbook revisions made during
dry-run are committed and the dry-run is re-run against the revised
runbook (no S-010 closure on a runbook that wasn't actually exercised
end-to-end).

### R-8a — Cutover communication + authority plan (GPT-F1)

The runbook documents a communication and decision-authority plan
covering the cutover window. Required content:

- **Primary comm channel** for the cutover team (chat bridge, conference
  bridge, or both). Spec'd as a role-based field; the actual URL /
  bridge number is filled in at S-011 time.
- **Stakeholder notification schedule** at minimum: T−24h
  (announcement), T−1h (final readiness), T0 (cutover started),
  T+verified (cutover complete) or rollback notification at the
  trigger time. Notification-distribution role is named (e.g.
  "release engineer notifies the deployment-ops distribution list").
- **Decision-authority chain** for rollback triggers:
  - "Auto-trigger" rollbacks (R-4 immediate-rollback class): the
    on-call operator observes the trigger, executes R-5 on observation,
    and notifies the comm channel — no prior authorisation gate. The
    runbook MUST clarify "immediate" = operator-initiated-on-observation,
    not pipeline-automated.
  - "Operator-decision" rollbacks (R-4 thresholded triggers): tech-lead
    role decides; if unavailable, an explicit tie-break role
    (e.g. release engineer or on-call SRE) is named.
- **Escalation / paging roster** by role (not name). Names filled in
  at S-011 time.

### R-8 — Operational-context references

The runbook explicitly cross-references and ratifies the operator-
facing notes carried forward:

- S-005b Caller Survey §3 "Operational note" — `EnvironmentLockLeaseTimeMs`
  tuning recommendation (~5–30 s) under the Kafka substrate.
- S-009 SPEC §R-7 GPT-F5 — `Kafka:Locks:*` defaults shipped in
  appsettings.json (PartitionCount=12, ReplicationFactor=3) are
  fixed per cluster lifetime; partition-count is immutable
  post-cutover per ADR-S-005 §4 #2.

If a future earlier-step audit produces additional operator-facing
notes after S-010 is authored, the runbook is amended in a follow-up
commit; the cross-reference list above is open, not closed.

---

## 3. Acceptance Criteria

### AT-1 — Runbook deliverable exists

`docs/kafka-migration/S-010-Cutover-Runbook.md` exists and contains
all sections required by R-1..R-6 and R-8. Reviewer can read it
without context-switching to other docs to find any cutover step.

### AT-2 — 4h hard ceiling honoured

The R-3 cutover sequence's wall-clock sum is ≤ 240 minutes.

### AT-3 — Rollback procedure verified deployable

The R-5 rollback procedure references the actual `release/pre-kafka-cutover`
tag (verified to exist at `481f4830`); the redeploy mechanism cited
matches the production deploy pipeline (MSI / WiX from `Setup.Dorc`).
The R-5 step-1 "pause new requests" mechanism cited MUST exist in the
codebase at runbook-authoring time (operator verifies; if it does not
exist, runbook authoring lists the missing mechanism as a Delivery
finding rather than committing the runbook against vapourware).

### AT-4 — Triggers observable, not subjective

Each R-4 rollback trigger names a specific metric / log line / smoke
result with a numeric threshold (after dry-run calibration). No
trigger reads "if it feels wrong".

### AT-5 — Dry-run evidence captured

`docs/kafka-migration/S-010-DryRun-evidence/<timestamp>/` contains
the four transcripts + summary per R-7. Summary's verdict is one of
{Pass, Pass-with-runbook-revision, Fail-rerun-required}.

### AT-6 — Dry-run completes within 4h

The cutover-wallclock transcript shows total elapsed time ≤ 240
minutes from "go" to "verified".

### AT-7 — Rollback rehearsal completes within 60 minutes

The rollback-rehearsal transcript shows total elapsed time ≤ 60
minutes from "rollback decision" to "rollback complete + smoke
tests green".

### AT-8 — Post-rollback smoke-tests green

Every R-1 smoke test passes after the rollback rehearsal.

---

## 4. Accepted Risks

| Risk | Disposition |
|---|---|
| Dry-run runs in staging Aiven, not production Aiven; production may surface issues staging doesn't. | Accepted — staging is the closest available substrate; production-only issues are S-011 risk and the runbook's rollback path is the safety net. |
| 4h hard ceiling may be unachievable on first dry-run; runbook revision + re-run is the documented response. | Accepted — IS §3 S-010 explicitly authorises re-runs; the 4h is binding for S-011 readiness. |
| Rollback-rehearsal requires staging access + deploy pipeline access not available to the AI assistant. | Accepted — operator runs the dry-run; AI authors the runbook + the evidence-capture template. The dry-run evidence may be filled in by the operator post-merge. |
| Post-cutover monitoring window (24h) extends beyond the 4h cutover ceiling. | Accepted — the 4h is "go to verified", not "go to fully soaked"; the 24h monitoring is the comfort-window after S-011 closes. |
| Rollback redeploys SignalR-only + RabbitMQ-lock — staging RabbitMQ infrastructure must remain available for the rehearsal. | Accepted — IS §3 S-011 "RabbitMQ infrastructure moved to cold standby (not decommissioned)" applies post-cutover; pre-cutover the infrastructure is still hot. |

---

## 5. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Files added:** `docs/kafka-migration/S-010-Cutover-Runbook.md` (the runbook itself); `docs/kafka-migration/S-010-DryRun-evidence/<timestamp>/{pre-cutover-smoke.txt, cutover-wallclock.txt, rollback-rehearsal.txt, post-rollback-smoke.txt, summary.md}` populated by the operator.
- **No production code changes.** The runbook only orchestrates deploy artefacts that already exist in the integration branch.
- **AI's authoring scope:** R-1..R-6 + R-8 (the runbook content). The dry-run evidence (R-7 / AT-5..AT-8) is operator-executed and operator-captured; the AI provides the evidence-directory template and the summary.md scaffold.

---

## 6. Review Scope Notes

Reviewers should evaluate:

- Whether R-1..R-6 collectively cover the IS §3 S-010 verification intent.
- Whether the 4h ceiling is realistic given the cutover sequence in R-3.
- Whether rollback triggers in R-4 are observable.
- Whether the operator-facing references in R-8 are complete (no
  forgotten audit notes from earlier steps).

Reviewers should NOT:

- Execute the dry-run in this review — it's a Delivery activity.
- Demand specific named owners for gates — the runbook spec'd is
  role-based; names are filled in at S-011 time per the spec.
- Re-litigate IS settled decisions (4h binding ceiling, S-009 tag
  as rollback target, RabbitMQ cold-standby in S-011).

---

## 7. Review History

### R1 (2026-04-15) — single-reviewer light pass — APPROVE WITH MINOR

Reviewer: GPT-5.3-codex. Verdict APPROVE WITH MINOR; one MEDIUM + four LOWs.

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| GPT-F1 | MEDIUM | Cutover comm plan + decision-authority chain absent | **Accepted** — new R-8a added: comm channel, stakeholder schedule, immediate-vs-decision rollback authority distinction, escalation roster role-list. |
| GPT-F2 | LOW | R-4 "rollback decision authority" subsumed | Subsumed by R-8a fix. |
| GPT-F3 | LOW | AT-3 should assert R-5 step-1 pause mechanism exists in codebase | **Accepted** — AT-3 wording extended to require the mechanism exists at runbook-authoring time. |
| GPT-F4 | LOW | R-3 sub-budget allocation for steps 1–4 vs soak window | Defer to Delivery — runbook authoring + dry-run calibration. |
| GPT-F5 | LOW | R-8 cross-reference list closed | **Accepted** — paragraph appended explicitly opening the list to future audit notes. |

Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
