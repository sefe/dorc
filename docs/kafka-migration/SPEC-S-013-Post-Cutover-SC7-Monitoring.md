# JIT Spec — S-013: Post-Cutover SC-7 Monitoring + Baseline Report

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Step ID** | S-013 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-013 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` SC-7 |
| **Prerequisites** | S-011 closed with verdict `Pass` (or `Pass-with-deviation`). **Runs in parallel with S-012; does not depend on or gate decommission.** |

---

## 1. Purpose & Scope

Establish a structured post-cutover observation window to verify
the Kafka substrate produces no regression in deployment reliability
versus the pre-HA-disablement baseline, and publish a baseline
comparison report for SC-7 closure.

**Operator-executed observation work.** AI authors this spec + the
report template; data collection, analysis, and report sign-off are
operator activities.

### In scope

- A monitoring window starting at S-011 GATE E "verified" timestamp.
- Window length: **30 calendar days OR ≥200 production deployments,
  whichever is later** (per IS §3 S-013).
- Comparison data set: pre-HA-disablement 30-day window
  **2026-03-14 to 2026-04-13** (baseline source per HLPS SC-7).
- A baseline comparison report
  `docs/kafka-migration/S-013-SC7-Report.md` published at window
  closure with: baseline metrics, post-cutover metrics, delta,
  pass/regression verdict, methodology + queries.
- If the report surfaces a regression: a remediation-plan stub is
  authored as a separate IS-extension or ticket; the actual
  remediation work is not in S-013's scope.

### Out of scope

- Any change to the production Kafka substrate or DOrc code in
  response to monitoring findings — that is forward-fix work outside
  this IS.
- The SignalR re-broadcast removal or DB-poll path removal —
  post-cutover follow-ups beyond IS scope.
- RabbitMQ decommission — S-012 (parallel; S-013 does not gate it
  per IS §3 S-013).

---

## 2. Requirements

### R-1 — Baseline data capture (one-time)

Before the post-cutover window begins, capture the SC-7 baseline:

- Time window: **2026-03-14 to 2026-04-13** (UTC; 30 calendar days).
- Source: DOrc production database (`DeploymentRequests` /
  `DeploymentResults` tables), filtered to production-environment
  deployments only.
- Metrics:
  - Total production deployments completed (RequestStatus reaches a
    terminal state: `Complete`, `Failed`, `Cancelled`, `Errored`,
    `Abandoned`).
  - Success rate = `Complete` / total terminal.
  - Median + p95 deployment duration (request-create to terminal).
  - Failure-mode breakdown: counts of `Failed`, `Cancelled`,
    `Errored`, `Abandoned`.
- Query SQL captured verbatim in the report (so it can be re-run on
  the post-cutover window for an apples-to-apples comparison).

If the baseline window's data is degraded (gaps, mid-window
DB migrations, etc.), document the degradation in the report and
either widen the window or note the comparison limitation.

### R-2 — Post-cutover window definition

The post-cutover window:

- **Starts at** the S-011 GATE E "verified" timestamp recorded in
  `S-011-Cutover-evidence/<S-011 date>/cutover-wallclock.txt`.
  Timestamp normalised to **UTC** for apples-to-apples comparison
  with the R-1 UTC baseline window.
- **Ends at** `max(T+30 calendar days, T+200th production deployment)`.

If at T+30d the production-deployment count is < 200, the window
extends until the 200th. If volume is very low, the window may
extend into Q1 2027 — the IS explicitly authorises this extension.

### R-3 — Same-query post-cutover data capture

At window closure, re-run the **identical** R-1 SQL queries against
the post-cutover window to produce comparable metrics. Same units,
same filters, same terminal-state definition.

### R-3a — Cancellation-rate disambiguation (GPT-F1)

`Cancelled` deployments are user-initiated and not a true reliability
signal. The report MUST surface cancellation-rate delta separately
from the headline success rate so a drop in success rate driven by a
shift in user-cancellation behaviour (e.g. more deliberate cancels
under improved UX) is not mis-flagged as a Kafka regression.

### R-4 — Baseline comparison report

Author `docs/kafka-migration/S-013-SC7-Report.md` containing at minimum:

- Window dates (baseline + post-cutover).
- Total-deployment counts for each window (with the volume-extension
  note if the window extended past T+30d).
- Per-metric side-by-side: baseline value, post-cutover value,
  absolute delta, percentage delta.
- The R-1 SQL query verbatim (so the report is reproducible).
- Verdict: one of:
  - **`No regression`** — all metrics at or above baseline;
    failure-mode shifts are explained or absent.
  - **`Regression — remediation required`** — at least one metric is
    materially worse than baseline beyond statistical noise.
- If verdict is `Regression`: a remediation-plan stub appended,
  citing the regressed metric, hypothesised cause(s), and a pointer
  to the separate forward-fix initiative (separate IS / ticket).

### R-5 — "Materiality" threshold

To avoid false-positive regression calls on small samples, the
report uses these materiality thresholds (operator may tighten):

- **Success rate**: regression if post-cutover < baseline − 1.0
  percentage point.
- **Median duration**: regression if post-cutover > baseline × 1.10
  (10% longer).
- **p95 duration**: regression if post-cutover > baseline × 1.20
  (20% longer).
- **Failure-mode shift**: regression if any single failure mode
  count grows by > 50% AND total terminal count is comparable
  between windows (within ±20%). When raw counts are not comparable
  (e.g. post-cutover volume materially higher), use volume-normalised
  rates (per-100-deployments) for the comparison instead.

The operator may override these thresholds at report time provided
the override + rationale is captured in the report.

### R-6 — Interim monitoring (optional but recommended)

The operator may publish interim updates at T+7d / T+14d / T+30d
under `docs/kafka-migration/S-013-Interim/<YYYY-MM-DD>/` showing
running totals. These are not gates; they exist so a regression
trend is visible early enough to escalate before window closure.

### R-7 — Parallel-with-S-012 invariant

S-013 runs in parallel with S-012 (T+14d decommission). S-013 does
NOT gate S-012 (per IS §3 S-012 / S-013). If S-013 surfaces a
regression after S-012 has fired, the response is forward-fix on
the Kafka substrate — Rabbit revival is no longer cheap or in scope.

This invariant is recorded in the report's methodology section so
future readers understand the gating semantics.

---

## 3. Acceptance Criteria

### AT-1 — Baseline captured

R-1 baseline data set captured and stored (raw query result + the
SQL itself) before the post-cutover window opens.

### AT-2 — Window correctly bounded

Post-cutover window's start timestamp matches S-011 GATE E
"verified" within ±5 minutes; end timestamp = `max(T+30d,
200th-deployment-timestamp)`.

### AT-3 — Same-query post-cutover data captured

R-1 SQL queries re-run verbatim against the post-cutover window;
result set captured.

### AT-4 — Report published

`docs/kafka-migration/S-013-SC7-Report.md` exists with all R-4
sections populated. Verdict explicitly `No regression` or
`Regression — remediation required`.

### AT-5 — Regression handling

If verdict is `Regression`: the report includes a remediation-plan
stub with a pointer to the separate forward-fix initiative.

### AT-6 — Audit reproducibility

A future reader can re-run the R-1 SQL queries against the same
windows and reproduce the same metrics within rounding.

---

## 4. Accepted Risks

| Risk | Disposition |
|---|---|
| Production volume during the window may be low; the 200-deployment threshold may push closure into Q1 2027. | Accepted — IS §3 S-013 explicitly authorises the extension. |
| Baseline window (2026-03-14 to 2026-04-13) may include atypical conditions (incidents, holidays) inflating or deflating baseline metrics. | Accepted — the report documents any baseline-window anomalies; operator may widen the baseline if material distortion is observed. |
| Interpretation of "regression" can be subjective. | Mitigated — R-5 materiality thresholds make the call objective; operator override requires rationale. |
| Reporting lag: window closure → report publication. | Accepted — operator commits to ≤7 calendar days from window-close to report publication. |
| If S-013 finds a regression after S-012 fires, Rabbit-revival is no longer cheap. | Accepted per IS §3 S-013 — forward-fix on Kafka is the contracted response. |

---

## 5. Delivery Notes

- **Branch:** `main` post-S-011 merge. S-013 makes only docs commits (baseline capture, interim, final report).
- **Operator-executed:** AI authors this spec + the report template; the operator runs the queries, authors the report content, and signs off.
- **Timing:** T+0 (S-011 verified) to `max(T+30d, 200th deployment)`. Report published within 7 calendar days of window closure.
- **Evidence under:** `docs/kafka-migration/S-013-Interim/` (optional) and `docs/kafka-migration/S-013-SC7-Report.md` (final).

---

## 6. Review Scope Notes

Reviewers should evaluate:

- Whether R-1 baseline capture is comprehensive enough for a
  meaningful comparison.
- Whether R-5 materiality thresholds are calibrated (not too tight,
  not too loose).
- Whether the parallel-with-S-012 invariant in R-7 correctly
  preserves IS gating semantics.

Reviewers should NOT:

- Demand specific metric values (those are post-cutover data).
- Re-litigate IS / HLPS SC-7 window definition.
- Demand AI-executed monitoring (data collection is operator work).

---

## 7. Review History

### R1 (2026-04-15) — single-reviewer light pass — APPROVE

Reviewer: GPT-5.3-codex. Verdict APPROVE; only LOW/INFO notes.

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| GPT-F1 | LOW | Cancelled treated as failure could mask reliability signal | **Accepted** — new R-3a requires cancellation-rate delta surfaced separately. |
| GPT-F2 | LOW | Failure-mode volume-comparability gate may hide drift if post-cutover volume is materially higher | **Accepted** — R-5 failure-mode bullet now permits volume-normalised rates when raw counts aren't comparable. |
| GPT-F3 | LOW | UTC normalisation for window-start timestamp not stated | **Accepted** — R-2 now states UTC normalisation explicitly. |
| GPT-F4 | INFO | Mid-window-trend escalation behaviour silent | Defer to operator — escalation is operator discretion; window-closure mechanics unchanged. |

Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
