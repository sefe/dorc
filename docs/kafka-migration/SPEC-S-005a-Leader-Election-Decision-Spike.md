# JIT Spec — S-005a: Leader-Election Decision Spike + ADR

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-005a (the decision-spike sub-phase of IS S-005) |
| **Time-box** | ≤ 2 weeks from start |
| **Slip trigger** | **2026-07-15** — if no ADR has converged by this date, option (iii) SQL-advisory-lock fallback is adopted by default and S-005b begins with that mechanism. |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-005 (APPROVED R3) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.1 (Safety Property), R-1, R-2, U-11 (APPROVED R3) |

---

## 1. Purpose & Scope

Decide which mechanism replaces `RabbitMqDistributedLockService`. Produce an Architecture Decision Record (ADR) selecting **one** of:

- **(i) Kafka consumer-group single-partition leader election + Request-level idempotency** — partition assignment is the leader signal; per-resource ordering relies on `RequestId`-keyed messages going to the same partition; idempotent handlers tolerate the cooperative-rebalance two-leader window.
- **(ii) (i) + fencing tokens** — same as (i) but every state-transition write to the database carries a monotonically-increasing token (drawn from the offset stream itself or a sidecar counter); writes from a stale leader are rejected at the DB by token comparison.
- **(iii) SQL Server advisory-lock fallback** — keep per-resource locking on `sp_getapplock`; Kafka carries event flow only and does not mediate leader election.

Whichever option is chosen, the implementation **must preserve a lock-loss signal equivalent to the existing `IDistributedLock.LockLostToken` / `IsValid` contract** so consumers (`Dorc.Monitor`'s `DeploymentEngine`) can react to lost locks immediately. For (i)/(ii) this means modelling partition-revocation as a lock-loss signal; for (iii) this means surfacing SQL connection drop. Any API-parity gap is recorded in the ADR Consequences clause.

The output is an **ADR** plus a **time-boxed proof-of-concept** for the chosen option that demonstrates HLPS §5.1's Safety Property under a deliberately-induced rebalance / failover. No production code changes — the implementation lives in S-005b.

### In scope

- An ADR document under `docs/kafka-migration/ADR-S-005-Leader-Election.md` recording the chosen option, the rationale, the rejected alternatives, and the key design constraints carried into S-005b.
- A throwaway POC (in a `pocs/` or `experiments/` folder, **not** under `src/`) that exercises the chosen mechanism end-to-end against the local compose stack and produces evidence — a structured-log transcript, screenshots, or a short markdown walkthrough — that the §5.1 Safety Property holds under the failure injection described in §4 below.
- An evaluation grid scoring all three options against the §3 criteria, so the ADR isn't a single-option assertion but a justified comparison.

### Out of scope

- Production implementation (`Dorc.Monitor` integration, DI wiring, removal of `RabbitMqDistributedLockService`) — **S-005b**.
- Wiring the chosen mechanism into the Request-lifecycle Kafka substrate (S-006 / S-007 already depends on the **ADR decision**, not on S-005b's completion).
- Any change to `IDistributedLockService` or `IDistributedLock` interface shape — S-005b decides whether to keep, narrow, or replace these.
- Tuning of session-timeout / heartbeat / max-poll values — S-005b's responsibility per HLPS R-2.

---

## 2. Current-State Pivot Point

`RabbitMqDistributedLockService` provides **per-resource** locking on arbitrary string keys (e.g. `request:123`, `env:Production`), not a singleton "monitor leader" election. Granular per-resource locking is what `Dorc.Monitor`'s `DeploymentEngine` calls into today.

This shapes the option space:

- **(iii) SQL advisory-lock** maps directly: `sp_getapplock @Resource = N'request:123'` is a one-line equivalent.
- **(i) / (ii) Kafka consumer-group** require an explicit mapping from arbitrary string-key → partition (consistent hashing on the resource key), and the lock-holder is whichever consumer instance currently owns that partition. Per-resource ordering becomes per-partition ordering. This is a different shape than the current API.

The POC must address this pivot: either prove the partition-mapping approach is workable end-to-end, or accept SQL-fallback as the lower-friction path.

---

## 3. Decision Criteria

The ADR must score each option on:

| Criterion | Weight | What "good" looks like |
|---|---|---|
| **§5.1 Safety Property** — no duplicate execution of deployment side-effects during a rebalance / failover window | **MUST** | Zero duplicate state-transitions across ≥20 induced rebalances (per IS SC-2c). Demonstrated, not asserted. |
| **API parity with `IDistributedLockService`** | HIGH | Per-resource arbitrary-string-key locking. If the option requires reshaping the caller surface, the cost is recorded. |
| **Operational complexity** | HIGH | Number of moving parts at runtime; failure modes the on-call team must understand; observability primitives reused vs new. |
| **Failover latency** | MEDIUM | Time from leader loss to next leader accepting work (IS SC-2a: ≤60 s for partition reassignment, SC-2b: ≤30 s for the new leader to accept a fresh deployment). |
| **HLPS R-1 / R-2 risk surface** | HIGH | (i) inherits cooperative-not-fenced two-leader window; (ii) closes it with fencing; (iii) sidesteps it by not using Kafka for leader election. |
| **Implementation effort for S-005b** | MEDIUM | Estimated weeks to land a production-grade replacement, including HA tests. |
| **Removal-from-Kafka-stack-cost** | LOW | If we later regret the choice, how hard is it to swap? |

A score-table appears in the ADR. The chosen option is the one whose cost/benefit profile best satisfies "MUST" + "HIGH" criteria, not the highest single score.

---

## 4. Proof-of-Concept Requirements

The POC need only validate the **chosen** option (not all three), but the ADR must explain why the rejected options were eliminated on paper before the POC was written.

### POC must demonstrate

1. **Lock acquisition + release** for at least two distinct resource keys, observed end-to-end against the local Kafka + Karapace + (optionally) SQL Server stack.
2. **Single-leader-at-a-time**: with two POC processes started concurrently, only one acquires the lock for a given resource. The other either blocks or returns "could not acquire."
3. **Failover**: kill the current lock-holder process abruptly (`SIGKILL`-equivalent). The other process must acquire the lock within the SC-2a budget (60 s). Observed wall-clock latency is recorded.
4. **§5.1 Safety Property under induced rebalance** — option-specific. The rebalance / failure-injection bar is **≥20** events in line with IS SC-2c (the production-acceptance bar). The POC's job is to show the property holds at the IS bar, not at a smaller sample.
   - **(i)** Run a synthetic deployment-state-transition stream through the chosen partition while inducing ≥20 rebalances (consumer join/leave). Idempotency is the mechanism, so the POC must **deliberately force at least one duplicate handler invocation** (e.g. by replaying an already-consumed offset against a freshly-assigned consumer) and the evidence must distinguish three outcomes: (a) handler invoked twice but the second was a no-op (expected — idempotency working), (b) handler invoked once (also expected for that offset), (c) two distinct DB rows or repeated side-effects (failure). The terminal DB state and the handler-invocation count must both appear in the evidence so a reviewer can tell a *masked* duplicate from a *prevented* one.
   - **(ii)** Same as (i), plus the DB write path rejects a stale-token write. Demonstrate this with a deliberately-stale producer and an assertion that the DB row's token has not regressed.
   - **(iii)** Induce a SQL connection drop (kill the network namespace or the connection mid-acquire). Verify (a) the lock is released and a fresh acquire succeeds within the SC-2a budget (this proves *liveness*) and (b) **during the drop window, no second process executes the guarded side-effect before the lock is re-acquired** (this proves the Safety Property in (iii)'s shape — `sp_getapplock` is session-scoped so there is no two-leader window to test, but the no-double-execution check still applies).
5. **Failure-mode walkthrough**: a one-paragraph markdown summary of "what an operator sees when this goes wrong" — log shape on lock loss, on dead leader, on broker unavailability (i/ii) or DB unavailability (iii).

### POC location

Under `pocs/s-005a-leader-election/` (new top-level folder if it does not exist; never under `src/`). The POC project does not need to be added to `Dorc.sln`. A `README.md` in that folder documents how to run the POC against the compose stack.

### POC artefacts deliverable

The POC need **not** be of production quality. It must, however, produce:

- A structured-log transcript of the §4 #4 induced-failure run, captured to `pocs/s-005a-leader-election/evidence/<timestamp>.log`.
- A short markdown analysis (`evidence/<timestamp>-analysis.md`) explaining what the log shows and pointing at the lines that demonstrate the Safety Property.

These artefacts are the binding evidence the ADR cites; without them the ADR is a rejection-by-default of the option it claimed to validate.

---

## 5. Acceptance Criteria

### AT-1 — ADR exists and is internally complete

`docs/kafka-migration/ADR-S-005-Leader-Election.md` exists and contains:

- **Context** paragraph (current `RabbitMqDistributedLockService` shape, why S-005 is replacing it, the three options under consideration).
- **Score grid** per §3 (all three options scored on every criterion).
- **Decision** clause selecting one option.
- **Consequences** paragraph (including the API-parity gap if option (i) or (ii) was chosen, the carry-over of fencing-token design to S-005b, and — if (i) or (ii) chosen — the **resource-key → partition mapping approach the POC validated** so S-005b does not have to re-open it).
- **References** linking the POC artefacts (§4) and HLPS §5.1 / R-1 / R-2.

### AT-2 — POC artefacts exist and demonstrate the §5.1 Safety Property

- `pocs/s-005a-leader-election/` exists with a runnable POC + `README.md` + `evidence/` subdir.
- Evidence transcript shows the chosen mechanism passes the option-specific §4 #4 test.
- Analysis markdown points at the specific log lines proving zero duplicate state-transitions (i/ii) or zero double-acquire under SQL outage (iii).

### AT-3 — Option (iii) was evaluated even if not chosen

- The ADR's score grid covers (iii) as the explicit fallback path. The HLPS R-4 dated trigger (2026-07-15) already says (iii) is the default if the spike doesn't converge — so the ADR must show that (iii) is at least *viable*, even if a different option is chosen on merit.

### AT-4 — User sign-off captured before S-005b spec authoring

- The ADR carries a "user-approved" line with the date once approved.
- S-005b's JIT Spec **must not** be authored until this AT passes — the spec's R-set depends on the chosen mechanism.

### AT-5 — Schedule discipline

- Calendar elapsed from "S-005a start" to "AT-1 + AT-2 complete" is ≤ 14 days.
- If 14 days elapse without convergence → escalate per HLPS R-4 (write a one-line risk-register entry and continue the spike for up to 14 more days). The +14 extension is **bounded by 2026-07-15** — whichever comes first.
- **Latest acceptable start date: 2026-06-17.** A start later than this compresses the available extension below 14 days; a start later than 2026-07-01 means the +14 extension would push past the 2026-07-15 hard date and therefore does not exist. Document any late start with a one-line risk-register entry.
- If **2026-07-15** elapses without convergence (whether the spike never started, or started but didn't finish) → option (iii) is adopted automatically per the IS slip-trigger; AT-1 closes by recording the trigger event and (iii)'s rationale; the POC for (iii) must still be run before AT-2 closes.
- If a fourth option (iv) (e.g. ZooKeeper / etcd) emerges from POC findings as a candidate, it **must be raised to the user no later than day 10 of the initial 14-day window**. A later (iv) escalation forfeits the time needed to evaluate it before the slip trigger and the spike falls back to (iii) by default.

---

## 6. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| Option (i) two-leader window during cooperative rebalance is well-documented but the POC runs only ≥10 induced rebalances; an unobserved-but-real edge case may surface later | HLPS R-1 | Accepted — the POC is bounded; production hardening is S-005b's job. The fencing-token option (ii) exists exactly for this concern. |
| Option (iii) couples DOrc to SQL Server availability for leader election. SQL Server outage = no new deployments accepted | DOrc current state | Accepted — this is unchanged from today (the existing Rabbit lock similarly couples to broker availability). |
| The POC may produce evidence that **none** of the three options cleanly satisfy the Safety Property at acceptable operational cost | §3 | Accepted — escalate to user; possibly add option (iv) (e.g. external coordination service like ZooKeeper / etcd) as a side-by-side, but only if clearly motivated by POC findings. Adding option (iv) requires user approval. |
| POC code lives outside `src/` and is not maintained beyond the ADR's reference to its evidence; future Kafka upgrades may invalidate the POC's reproducibility | §4 | Accepted — POC is throwaway; S-005b's production tests are the long-lived regression suite. |

---

## 7. Out of Scope (explicit)

- Production implementation, DI wiring, removal of `RabbitMqDistributedLockService` — **S-005b**.
- Tuning of session-timeout / heartbeat / max-poll for the chosen mechanism — **S-005b**.
- Audit-trail query that proves zero-duplicate state-transitions (an IS S-005 deliverable) — **S-005b** (the spike's evidence is sufficient for ADR; the production audit query lives with the production code).
- Operator runbook for failover diagnosis — **S-010** as part of the cutover runbook.

---

## 8. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1. The ADR is committed as part of the spike; the POC code is committed under `pocs/` but is not part of any `dotnet build` invocation.
- **Spike start trigger:** S-005a is unblocked per IS §3 S-005 dependencies (S-002 + S-004). The IS R3 SC-3 interpretation (Kafka authoritative substrate; SignalR as UI wire) does not affect S-005 — leader election is independent of the UI substrate.
- **Reviewer note:** the ADR itself goes through Adversarial Review at AT-1 close — same 3-reviewer panel pattern as JIT Specs. The POC artefacts are not reviewed individually; their existence + the ADR's citation of them is the evidence.

---

## 9. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Whether §3 decision criteria capture the right tradeoffs for the chosen mechanism's downstream impact (S-005b, S-006, S-007).
- Whether §4 POC requirements actually prove the §5.1 Safety Property for the chosen option, or leave a gap a competent attacker / failure mode could exploit.
- Schedule realism (14-day spike vs the 2026-07-15 slip trigger).
- Risk coverage in §6.

Reviewers should **NOT**:

- Demand which option to choose — that's the ADR's job, not the spec's.
- Demand a specific POC code shape or test framework — Delivery's choice.
- Re-litigate HLPS R-1 / R-2 or the §5.1 Safety Property text — those are settled.
- Expect production-quality test coverage from the POC; it's a throwaway artefact.

---

## 10. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Sonnet-F1 / Gemini-F1 | Sonnet, Gemini | MEDIUM | ≥10 vs ≥20 rebalance count drift between §3 and §4 | **Accepted** — §4 #4 raised to ≥20 (matches IS SC-2c). |
| Gemini-F2 / GPT-F2 | Gemini, GPT | MEDIUM | Option (i) idempotency must be observably tested: POC needs forced duplicate invocation + evidence distinguishing handler-count from terminal DB state | **Accepted** — §4 #4 (i) now requires deliberate duplicate-invocation + three-outcome distinction in evidence. |
| GPT-F3 | GPT | MEDIUM | Option (iii) variant proves liveness not safety | **Accepted** — §4 #4 (iii) now also requires "no second process executes the guarded side-effect during the drop window" (Safety Property in (iii)'s shape). |
| Sonnet-F2 / GPT-F1 / Gemini-F3 | Sonnet, GPT, Gemini | MEDIUM | Schedule interaction: elastic 14+14 vs 2026-07-15 hard date; latest start; option (iv) timing | **Accepted** — AT-5 now bounds the +14 extension by 2026-07-15, sets latest-acceptable-start = 2026-06-17, and requires option (iv) escalation by day 10 of the initial window. |
| Gemini-F4 / GPT-F4 | Gemini, GPT | LOW | LockLostToken/IsValid contract preservation; partition-mapping decision in ADR Consequences | **Accepted** — §1 records the lock-loss-signal preservation requirement; AT-1 Consequences now requires the partition-mapping approach to be recorded if (i)/(ii) chosen. |
| Sonnet-F3 | Sonnet | LOW | Asymmetric (iii) test shape rationale | **Accepted** — subsumed by GPT-F3 fix (§4 #4 (iii) now explicit on session-scoped lock and no-double-execution). |
| Sonnet-F4 | Sonnet | LOW | "S-004 still has AT-1/AT-3 deferred" parenthetical drift risk | **Accepted** — §8 now cites IS §3 instead of restating S-004 state. |
| GPT-F5 | GPT | LOW | SC-3 non-impact note belongs in §8, not §6 risks | **Accepted** — moved to §8. |
| Sonnet-F5 | Sonnet | LOW | Timestamp filename convention prescriptive | Defer to Delivery — author discretion. |
| Gemini-F5 | Gemini | LOW | (i) vs (ii) score-grid differentiation thin | Defer — flagged for ADR-author awareness. |
| Gemini-F6 | Gemini | (non-finding) | Schedule headroom is reasonable | Acknowledged. |

All 4 MEDIUM groups accepted and resolved via surgical edits. 4 LOWs accepted; 2 deferred to Delivery; 1 acknowledgment-only. No re-litigation of HLPS / IS settled decisions. Per CLAUDE.md §4: three APPROVE-tier verdicts with all MEDIUMs resolved = **unanimous approval**. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
