# JIT Spec — S-011: Production Cutover Execution

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Step ID** | S-011 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-011 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` SC-6, C-2, C-9 |
| **Prerequisites** | S-010 closed (runbook authored AND staging dry-run summary verdict = `Pass`); operator team scheduled; Aiven production cluster ready. |

---

## 1. Purpose & Scope

Execute the cutover from RabbitMQ + SignalR-only to Kafka in
**production**, using the runbook authored at S-010 verbatim.

This step is **operator-executed end-to-end**. The AI assistant's
deliverable for S-011 is this spec + the post-cutover evidence
template; the actual cutover is a human-coordinated activity gated
on a successful S-010 dry-run.

### In scope

- Execute `docs/kafka-migration/S-010-Cutover-Runbook.md` against
  production verbatim, with the operator team as named in the
  runbook §7.4 roster.
- Capture cutover evidence under
  `docs/kafka-migration/S-011-Cutover-evidence/<YYYY-MM-DD>/`
  mirroring the S-010 dry-run evidence layout.
- Move RabbitMQ infrastructure to **cold standby** post-cutover (do
  not decommission — S-012 does that at T+14d).
- Verify the `release/pre-kafka-cutover` tag remains deployable
  throughout the C-9 window (spot-check at T+1d, T+7d, T+14d).

### Out of scope

- Decommissioning RabbitMQ infrastructure — S-012 (unconditional at T+14d).
- SC-7 monitoring + baseline report — S-013 (parallel, runs through ≥30d / ≥200 deployments).
- Any production code change during cutover — if the runbook calls
  for a code change, abort the cutover and rebuild S-010.
- Removal of SignalR hub or DB-poll path — both are post-cutover
  follow-ups beyond the IS scope.

---

## 2. Requirements

### R-1 — Runbook execution discipline

The runbook is executed verbatim. Any deviation during cutover MUST
be:

- Logged in `cutover-wallclock.txt` with timestamp + reason.
- Decided per the runbook §7.3 authority chain.
- Either resolved within the gate's slack OR triggers §5 rollback.

Runbook revisions discovered necessary during S-011 execution are
**out of scope for in-flight cutover**. The cutover proceeds on the
runbook as-is or rolls back; revisions are a post-S-011 amendment if
the cutover lands.

### R-2 — Pre-cutover readiness gate (T-24h)

Before T0, the operator team confirms in the §7.1 comm channel:

- S-010 dry-run summary verdict = `Pass` (per S-010 §9 / AT-2 / AT-6 / AT-7 / AT-8).
- Production Aiven cluster healthy (broker count = expected RF ceiling, schema-registry reachable).
- RabbitMQ infrastructure healthy (so rollback target is deployable until S-012).
- §7.4 roster filled in with named individuals; conference bridge + chat channel populated.
- T-24h notification per S-010 §7.2 sent.

Failure of any item aborts the cutover; reschedule per change-management process.

### R-3 — Cutover execution

The runbook's §3 wall-clock-budgeted sequence is executed by the
named operator team. The 240-minute hard ceiling is binding (IS §3 S-010).

Cutover-window evidence captured live in
`docs/kafka-migration/S-011-Cutover-evidence/<YYYY-MM-DD>/cutover-wallclock.txt`.

### R-4 — Smoke-test verification

The runbook's §2 ST-1..ST-7 catalogue is run at every gate per
S-010 §3. Transcripts captured in:

- `S-011-Cutover-evidence/<YYYY-MM-DD>/pre-cutover-smoke.txt` (T-30 to T0).
- `S-011-Cutover-evidence/<YYYY-MM-DD>/cutover-smoke-gateD.txt` (GATE D at T+85).
- `S-011-Cutover-evidence/<YYYY-MM-DD>/post-cutover-smoke.txt` (after GATE E declares "verified").

All ST tests must PASS at every gate. ST-6 may WARN per the runbook;
all others are blockers.

### R-5 — Rollback path remains armed

For the entire cutover window AND the C-9 14-day rollback window
following GATE E:

- The `release/pre-kafka-cutover` tag remains pushed to origin and immutable.
- RabbitMQ infrastructure remains in cold-standby state (powered, credentialed, network-reachable from a redeployed Monitor + API).
- The S-010 §5 rollback procedure is the authoritative recovery; the on-call SRE may execute it on a R-4 immediate-trigger or a §7.3-authorised decision-trigger throughout the window.
- Tag deployability spot-checks at T+1d, T+7d, T+14d (ahead of S-012). Spot-check = a non-disruptive `git rev-parse release/pre-kafka-cutover^{commit}` returning `481f4830` PLUS confirmation that the deploy pipeline can build an MSI from that tag (the build itself is a CI job, no production deploy needed).

### R-6 — Post-cutover monitoring (operator)

Per S-010 §6, the operator runs the smoke catalogue + monitoring
checks at T+1h, T+4h, T+12h, T+24h. Each checkpoint logged in
`S-011-Cutover-evidence/<YYYY-MM-DD>/post-cutover-monitoring.txt`.
S-013 is the structured baseline-comparison activity (parallel,
≥30d / ≥200 deployments); R-6 here is the operator-runbook reaction
window only.

### R-7 — Cold-standby state declaration

Within 1 hour of GATE E "verified", the operator team explicitly
moves RabbitMQ infrastructure to cold-standby state and records the
declaration in
`S-011-Cutover-evidence/<YYYY-MM-DD>/rabbit-cold-standby.txt` with:

- RabbitMQ broker process state at the time of declaration (running / stopped / drained).
- Network reachability state (firewall rules in place; credentials still valid).
- IaC state (config still applied, no destructive changes).
- Confirmation that no Monitor or API replica is connected to RabbitMQ post-cutover (from the cutover-deployed builds the Rabbit code is gone — this is a verification not an action).

This document is the "what state was Rabbit in at T+verified" record S-012 will reference when the decommission step fires.

### R-8 — Evidence summary

`S-011-Cutover-evidence/<YYYY-MM-DD>/summary.md` mirrors the S-010
template with verdict ∈ {`Pass`, `Pass-with-deviation`, `Rolled-back`}.

If `Rolled-back`: the cutover is treated as not-executed for
S-012/S-013 dependency purposes; the IS schedule slips and S-011 is
re-attempted post-runbook-revision.

---

## 3. Acceptance Criteria

### AT-1 — Pre-cutover gate passed

§2 R-2 readiness gate items all confirmed in the comm channel
transcript before T0.

### AT-2 — Cutover completed within hard ceiling

`cutover-wallclock.txt` shows total elapsed ≤ 240 minutes from "go" to
"verified" (or `Rolled-back` per R-5/§7.3).

### AT-3 — All gates passed

GATE A → E each show PASS in the transcript with named role-owner
and decision time.

### AT-4 — All smoke tests passed (or rolled back)

`pre-cutover-smoke.txt`, `cutover-smoke-gateD.txt`,
`post-cutover-smoke.txt` all show PASS for every blocking ST. ST-6
may show WARN.

### AT-5 — Cold-standby declared

`rabbit-cold-standby.txt` exists with all four R-7 items recorded.

### AT-6 — Tag deployability spot-checks

`spot-check-T+1d.txt`, `spot-check-T+7d.txt`, `spot-check-T+14d.txt`
each record the rev-parse + CI build outcome for
`release/pre-kafka-cutover^{commit}`. All three pass before S-012 fires.

### AT-7 — Summary captured

`summary.md` exists with verdict + sign-offs from tech lead +
release engineer + on-call SRE per the runbook §7.4 roster.

---

## 4. Accepted Risks

| Risk | Disposition |
|---|---|
| Production-only issues that staging dry-run did not surface. | Accepted — S-010 §5 rollback is the safety net; the C-9 14-day window provides recovery time. |
| In-flight deployments terminated at T0 per HLPS C-2. | Accepted — explicit HLPS constraint; T-24h notification gives users a window to delay submissions. |
| Cutover may slip beyond 4h ceiling. | Triggers §5 rollback per S-010 §3 GATE E / hard-ceiling. |
| Cold-standby Rabbit may degrade during the 14-day C-9 window (broker upgrades, credential expiry). | Accepted — operator monitors; if degradation detected, escalate to "rebuild rollback target" before S-012 fires. |
| `release/pre-kafka-cutover` tag could be force-moved by mistake. | Mitigated — branch protection on the tag (operator action, recorded but outside the AI's authority); spot-checks at T+1d/+7d/+14d catch any drift. |

---

## 5. Delivery Notes

- **Branch:** `feat/kafka-migration` → `main` merge happens between S-010 closure and S-011 execution per the team's standard release process.
- **No code changes** in this step; only evidence-capture commits.
- **Operator-executed:** AI assistant authors this spec; the operator team executes S-011 against production.
- **Evidence captured under:** `docs/kafka-migration/S-011-Cutover-evidence/<YYYY-MM-DD>/` mirroring S-010 template structure.

---

## 6. Review Scope Notes

Reviewers should evaluate:

- Whether R-1..R-7 collectively cover IS §3 S-011 verification intent.
- Whether R-5 spot-check cadence + the cold-standby state declaration in R-7 are concrete enough.
- Whether the `Rolled-back` verdict path in R-8 correctly preserves the S-012 gating semantics (decommission only after a successful S-011, not a rolled-back one).

Reviewers should NOT:

- Demand specific cutover dates / times.
- Demand specific named individuals for the §7.4 roster.
- Re-litigate IS / HLPS settled decisions.

---

## 7. Review History

### R1 (2026-04-15) — single-reviewer light pass — APPROVE

Reviewer: GPT-5.3-codex. Verdict APPROVE; only LOW/INFO notes. R-1..R-7 cover IS §3 S-011 verification intent; R-5 correctly extends S-010 §5 rollback across the C-9 14-day window; R-7 captures all four cold-standby state aspects; R-8 `Rolled-back` verdict correctly cascades into S-012 R-1's gating predicate. LOWs (T+14d spot-check ordering vs S-012 R-1; CI build pipeline-slot consumption) deferred to Delivery.

Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
