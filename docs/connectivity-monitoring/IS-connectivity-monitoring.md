# IS — Connectivity Monitoring for Servers and Databases

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing HLPS | `docs/connectivity-monitoring/HLPS-connectivity-monitoring.md` (APPROVED) |
| Target PR | TBD by S-001 (ratify, revise, or supersede PR #374) |

---

## 1. Strategy

The HLPS folds two related streams into one plan: connectivity
monitoring of Servers and Databases (issue #362), and DB creation
date capture (#593). Per HLPS §8 ring-fence, these must remain on
separate IS steps — the connectivity-check skeleton lands first;
the creation-date piggyback follows once the skeleton is in place.

Per HLPS §8 drift posture, **S-001 is a decision step**: assess the
drift between PR #374's branch tip (~251 commits behind `main`,
authored before the daemons modernisation) and current `main`, and
decide whether the IS proceeds by **ratifying** PR #374's diff
(resolve conflicts only), **revising** it (selective rework against
the HLPS contract), or **superseding** it (close #374 and
reimplement on a fresh branch from `main`). Every downstream step
is described in terms of the **outcome** required against the HLPS
contract — the *route* (ratify / revise / supersede) is an
implementation choice that S-001's decision note pins.

Steps S-002 through S-007 deliver the connectivity-check skeleton
in a sequence where each step is independently shippable. S-008
and S-009 deliver the #593 creation-date addition on top.
S-010 covers the deployment runbook and false-negative
documentation per HLPS SC-9 / U-10. S-011 is the production
verification gate.

**Step boundaries vs commit boundaries.** The eleven steps are
**logical milestones with their own verification intent**, not
mandatory commit boundaries. Under the **revise** or **supersede**
routes, each step naturally maps to its own commit (the route
implies fresh, granular work). Under the **ratify** route — where
PR #374's existing diff is conflict-resolved against current
`main` rather than reimplemented — multiple steps may collapse
into a single conflict-resolution commit (or a small number of
commits) on PR #374's branch. In that case, the PR description
must individually evidence each step's verification intent so an
adversarial reviewer can audit step-by-step compliance even
though the diff is monolithic. The IS author asserts this here
to forestall an interpretation that the route choice in S-001 is
constrained by the IS's step granularity.

## 2. Steps

### S-001 — Drift assessment + ratify/revise/supersede decision

**What changes**
A short investigation, captured as a decision note committed to the
repository at `docs/connectivity-monitoring/DECISION-S-001-drift.md`,
into the gap between PR #374's branch tip and current `main`. The
note must answer:

- What conflicts arise on a `merge main → pr-374` (concrete file
  list, not just the two flagged so far)?
- How does the daemons rename (`Server.Services` → `Server.Daemons`,
  PR #649) affect PR #374's diff?
- What Monitor-side DI, hosting, and configuration changes have
  landed since PR #374's branch tip that affect where the
  connectivity service plugs in?
- What schema state does `main` have today on `SERVER` and
  `DATABASE`, and does it create any obstacle to landing the SC-1
  schema delta?
- What is the status of PR #374's outstanding CodeQL threads
  versus the current-tree fixes (`d3005f12` resource-injection,
  `8413dea6` log-forging)? Under **ratify**, those threads must
  be reconciled (re-run, resolve, or document as fixed-in-tree).
- Of PR #374's existing diff, what (if anything) is salvageable
  versus what would benefit from a fresh implementation?

The note's output is a single decision: **ratify**, **revise**, or
**supersede**, with a paragraph of rationale per option weighed.

**Branch setup output.** Beyond the decision note itself, S-001
also performs the route-conditional branch setup so downstream
steps have a defined starting point:

- **ratify**: rebase / merge PR #374's branch onto current `main`,
  resolving conflicts per the assessment. Output: a branch ready
  for downstream-step verification commits to land on.
- **revise**: same as ratify (rebase first), then identify the
  specific surfaces that will be reworked. Output: same branch
  shape; the rework happens in subsequent steps.
- **supersede**: cut a fresh branch from `main` and close PR #374
  with a comment pointing at the new work. Output: a clean
  branch with no PR #374 commits.

The mechanical rebase / branch-cut is not a separate step ID
because it has no independent verification intent — it is part
of S-001's deliverables.

**Why**
HLPS §8 drift posture mandates this assessment as an explicit
early IS step. Without it, every downstream step would be
guessing at its starting point.

**Dependencies**
None. This is the first concrete step.

**Verification intent**
- The decision note is reviewed by the Adversarial panel as a
  first-class submission. Reviewers verify the conflict list is
  complete (sample: replay the `merge main → pr-374` and
  cross-check), and that the chosen option's rationale is sound.
- The note's APPROVED status gates S-002.

**Out of scope**
Any change to deployment-relevant source code. The route-conditional
branch setup (rebase / fresh-branch) is in scope per the deliverables
above.

---

### S-002 — Schema delta + migration verification

**What changes**
Add the connectivity-monitoring columns to the `SERVER` and
`DATABASE` tables, with the relevant nullability and defaults:

- `SERVER`: `LastChecked`, `IsReachable`, `UnreachableSince`.
- `DATABASE`: `LastChecked`, `IsReachable`, `UnreachableSince`,
  plus `CreateDate` for the #593 piggyback (added in this same
  schema step to keep all schema work atomic; the column is
  populated later in S-008, see §3 sequencing).

All columns are nullable (per HLPS §4 — additive only).

The step also verifies that the SQLProj-generated migration
applies cleanly against a current production-shape DB snapshot
(resolves HLPS U-7).

**Why**
HLPS SC-1. Foundation for every step that reads or writes the
new columns.

**Dependencies**
S-001 APPROVED.

**Verification intent**
- The migration script generated by the SQLProj is captured (as a
  diff or a generated `.sql`) and the JIT spec records the
  manual verification: dry-deploy against a non-prod DB
  snapshotted from production-shape state, confirm clean apply,
  confirm no data fixup required, confirm existing reads still
  return expected results.
- Resolves U-7. Records the resolution in the HLPS Unknowns
  Register addendum.

**Out of scope**
- Persistent-source code that reads or writes these columns
  (that's S-004).
- Any business logic. This step is schema-only.

---

### S-003 — `ConnectivityChecker` (probe primitives)

**What changes**
The `Dorc.Core.Connectivity.IConnectivityChecker` interface and
its implementation, providing two primitives:

- Server reachability: ICMP ping first, with a TCP-connect to
  port 445 (SMB) as fallback. Per-probe timeout ≤ 5s; per-host
  worst case ≤ 10s. (HLPS C6.)
- Database reachability: open a SQL connection using the Monitor
  service account's Integrated Security identity, with a 5s
  connection timeout. Use `SqlConnectionStringBuilder` only —
  no string concatenation (HLPS C8).

The step is pure-logic with unit tests; no persistence, no
hosting, no DI wiring.

**Why**
HLPS U-1 resolution; foundation for the check service in S-005.
Isolated so the probe behaviour can be unit-tested without
dragging in the BackgroundService / Timer machinery.

**Dependencies**
S-001 APPROVED. **Genuinely parallel with S-002 and S-004** — this
step is pure logic with no schema or persistence touch points,
so it can be developed in parallel with the schema-side work.

**Verification intent**
- Unit tests for `ConnectivityChecker` covering: success path
  (mocked successful ping); ICMP-fail / TCP-success path;
  ICMP-fail / TCP-fail path; timeout enforcement (slow probe
  respects the 5s ceiling); empty / null inputs return false.
- DB-reachability tests covering: connection-refused, auth-fail,
  success — all confirming `IsReachable` returns the boolean
  outcome and never throws.
- CodeQL clean on resource-injection (HLPS C8 / SC-8).

**Out of scope**
- The hosted-service shell (S-005).
- Persistent-source query and write paths (S-004).
- Any UI or API model change.

---

### S-004 — Persistent-source: batched read + transition writes

**What changes**
Extend `IServersPersistentSource` and `IDatabasesPersistentSource`
with the read and write paths the connectivity check needs:

- A batched read returning a paged subset of rows for checking.
  The exact paging API shape is the JIT-spec author's choice, but
  it must support the HLPS G5 batch-size constraint (default 100)
  without requiring the caller to load all rows up front.
- An update path that, given a row id, an `IsReachable` outcome,
  and a "now" timestamp, updates `IsReachable` and `LastChecked`
  AND correctly transitions `UnreachableSince` per HLPS SC-4
  (set on `true→false`, clear on `false→true`, preserved across
  consecutive `false` reads).

The transition logic for `UnreachableSince` is centralised here —
the BackgroundService in S-005 must not duplicate it.

**Why**
HLPS G2 / SC-4 / G5. Separating the persistent-source surface
from the hosted service makes the transition logic
unit-testable without orchestration.

**Dependencies**
S-002 APPROVED (schema must exist for the EF mapping).

**Verification intent**
- Unit tests covering **all six** `UnreachableSince` transitions:
  `null→true` (stays null), `null→false` (sets to now),
  `true→false` (sets to now), `false→true` (clears to null),
  `false→false` (preserves the original timestamp across the
  cycle), `true→true` (stays null across consecutive successful
  checks). Each test confirms the timestamp value handed to the
  source is preserved or replaced correctly.
- Integration tests against a real (test) DB confirming the
  batched read returns the expected subset and the update is
  durable.

**Out of scope**
- The hosted service that calls these methods (S-005).
- API-model exposure of the new columns (S-006).

---

### S-005 — Hosted service: `IHostedService` + `Timer`

**What changes**
The `Dorc.Monitor.Connectivity.ConnectivityCheckService` as an
`IHostedService` implementation using a `System.Threading.Timer`,
per HLPS U-2 resolution. Owns the cycle scheduler:

- Honour `EnableConnectivityCheck` configuration (HLPS G4 / SC-3).
- Honour `ConnectivityCheckIntervalMinutes` cadence and the
  initial-delay configuration. Defaults per HLPS U-4 / U-5.
- For each cycle, drive the batched read from S-004's persistent
  source and call S-003's `ConnectivityChecker` per row.
- Sanitise log entries containing user-controlled values (server
  / database name) per HLPS C7 / SC-8.
- Yield to deployment-request processing — i.e. don't hold long
  exclusive resources across cycles (HLPS C4 / SC-6).

The step also ensures the host startup path matches HLPS SC-7:
the service must remain operational across a stop/start cycle
including a stop-during-active-batch event. The original
cancellation regression that drove U-2 must be exercised by an
explicit test.

**Why**
HLPS G1, U-2, SC-7. The surface that the operator interacts with
(via configuration toggle). The three concerns this step bundles
(IHostedService+Timer shell, cycle orchestration, cancellation
regression test) are kept atomic deliberately: the regression
test is meaningless without the shell, and the cycle is
meaningless without the regression-protected shell. Splitting
them would produce sub-steps with no independent shippable
value.

**Dependencies**
S-003 APPROVED, S-004 APPROVED.

**Verification intent**
- Unit / integration tests covering: service-disabled-via-config
  path (no checks run, clean shutdown, no spurious log noise);
  cycle execution drives both servers and databases; the
  cancellation regression — i.e. a host that throws synchronously
  on startup does NOT cancel the connectivity service before its
  initial delay completes.
- 5× stop/start cycle test (HLPS SC-7), at least one of which
  is mid-batch.
- CodeQL clean on log-forging (HLPS C7 / SC-8).
- Initial-delay-vs-first-cycle trade-off acknowledged: HLPS U-10
  notes that all rows surface as "Not checked" until the first
  cycle completes, and U-5 sets a default initial delay. The JIT
  spec author may opt to trigger an immediate first cycle on
  startup (after the initial delay) instead of waiting a full
  cadence interval, provided HLPS C4 (responsiveness) and SC-7
  (restart resilience) remain green. The U-4 / U-5 defaults are
  the floor; tightening them is a JIT-spec decision, not an HLPS
  re-litigation.

**Out of scope**
- API or UI exposure of the data the service writes.
- Any change to deployment-request processing.

---

### S-006 — API-model surfacing of connectivity fields

**What changes**
Extend `ServerApiModel` and `DatabaseApiModel` with the new
connectivity fields (`LastChecked`, `IsReachable`, `UnreachableSince`)
as additive nullable properties; extend the persistent-source
read path that maps DB rows into these models. Regenerate the
OpenAPI surface (`dorc-api`) and verify the generated TypeScript
client builds cleanly.

CreateDate is **not** added in this step — it lives in S-009 per
the #593 ring-fence.

**Why**
HLPS SC-11 (additivity). Lets the UI in S-007 consume the new
fields without round-tripping to a separate query.

**Dependencies**
S-002 APPROVED (schema), S-004 APPROVED (read path).

**Verification intent**
- API-build clean.
- OpenAPI regenerate produces additive change only (no removed
  or renamed fields). Snapshot the diff.
- `dorc-web` builds against the regenerated client without
  changes to existing consumers (Tools.RequestCLI etc. likewise).

**Out of scope**
- UI rendering of the fields (S-007).
- CreateDate (S-009).

---

### S-007 — UI: 4-state status indicator on Servers + Databases lists

**What changes**
Add a `Status` column to `page-servers-list.ts` and
`page-databases-list.ts` rendering the four HLPS SC-5 states
(Online / Unreachable / Unreachable 7+ days / Not checked) with
the corresponding colours and tooltips. The two pages must use
the same renderer logic — parity-locked per HLPS G3 (cleanup
candidate flag is the same across both surfaces).

**Why**
HLPS SC-5. The user-visible product of the feature.

**Dependencies**
S-006 APPROVED.

**Verification intent**
- Manual walkthrough on each page covering all four states
  (one representative row each).
- Light + dark theme parity check.
- HLPS §6 verification intent: "visit both list pages with the
  four representative state combinations".

(Sortability / filterability of the new column is **not** a
SC-5 requirement; the HLPS only mandates correct rendering and
tooltips. If the JIT spec opts to wire sortability for free
because the existing list-page framework supports it, that is a
delivery-level choice — but it is not an acceptance criterion.)

**Out of scope**
- CreateDate column (S-009).
- The MS Teams / pager-style alerting (HLPS N2).

---

### S-008 — DB `CreateDate` read (piggyback on connectivity check)

**What changes**
Extend the connectivity-check step in `ConnectivityCheckService`
(S-005) so that on each successful DB-reachability check, the
service ALSO reads `sys.databases.create_date` for the same
database (parameterised `WHERE name = @dbName`) and persists it
to the new `CreateDate` column on the DOrc-side row.

Behaviour per HLPS SC-10:
- Read uses the same connection already opened for the
  reachability check (HLPS C9).
- On creation-date change, overwrite the column AND emit an
  `Information`-level log entry naming old/new value (HLPS SC-10).
- On query failure (e.g. catalog-view permission denied), leave
  the existing column value unchanged and log at debug level.
- `LastChecked` / `IsReachable` / `UnreachableSince` follow their
  normal transition rules — a creation-date change does **not**
  reset them (HLPS U-9 resolution).

**Why**
HLPS G6 / SC-10. Per ring-fence, lands AFTER the connectivity
skeleton (S-001..S-007) is in place.

**Dependencies**
S-005 APPROVED, S-002 APPROVED (column exists).

**Verification intent**
- Unit / integration test for the per-cycle creation-date read
  using a parameterised query (verify the SQL generated by the
  ORM / data layer is parameter-bound, not concatenated).
- Test for the "DB recreated under same name" transition: log
  emitted at `Information`, column overwritten, other fields
  unaffected.
- Test for the permission-denied path: column unchanged, debug
  log emitted, no exception thrown to caller.

**Out of scope**
- API / UI exposure of the column (S-009).
- Any modification to S-005's cycle-scheduler contract. The
  CreateDate read must be additive **within the per-row check
  call site** that S-005 establishes — not by altering the
  scheduler's loop structure, cadence semantics, or cancellation
  behaviour. If S-008's JIT spec finds that a clean piggyback
  is impossible without contract change, the IS reopens for
  re-sequencing rather than the change happening silently.

---

### S-009 — `CreateDate` API + UI surfacing

**What changes**
Extend `DatabaseApiModel` with the `CreateDate` field (nullable
DateTime); regenerate `dorc-api`; surface the field as a
sortable column on `page-databases-list.ts` per HLPS U-8
resolution. The Servers list is **not** modified — per HLPS N5,
servers do not have a creation-date column.

**Why**
HLPS SC-11 / U-8 resolution.

**Dependencies**
S-008 APPROVED (column gets populated by S-008's logic; this
step is the surface).

**Verification intent**
- OpenAPI regenerate produces additive change only.
- `dorc-web` builds.
- Manual UI walkthrough on the Databases list confirms the
  column renders, sorts, and tooltips appropriately.

**Out of scope**
- Edit / set of `CreateDate` from the UI — read-only; DOrc
  records the upstream value.

---

### S-010 — Deployment runbook + documentation

**What changes**
A `docs/connectivity-monitoring/README.md` (or equivalently
named) covering:

- HLPS SC-9: what "unreachable" means under the chosen probe
  strategy, including the documented false-negative cases
  (ICMP-blocked hosts, DBs the Monitor account cannot
  authenticate against).
- HLPS U-10: cold-start behaviour — every existing row shows
  "Not checked" until the first cycle completes. Operators
  expect ~one cadence-interval window of grey indicators after
  the upgrade lands.
- The configuration knobs (`EnableConnectivityCheck`,
  `ConnectivityCheckIntervalMinutes`, initial-delay, batch size)
  with their defaults and recommended adjustment criteria.
- The log-line catalogue: what is logged at what level, what
  to look for when investigating a creation-date transition or
  a sustained "Unreachable" status.

**Why**
HLPS SC-9, U-10. The deployment runbook is part of the success
criteria, not a nice-to-have.

**Dependencies**
S-007 APPROVED (UI exists to be documented), S-009 APPROVED
(creation-date docs reference the UI).

**Verification intent**
- A reviewer reading the runbook against the implementation can
  trace each documented behaviour to a code path.
- Operator-relevance review: does the doc answer the questions
  an operator would ask on the first deployment?

**Out of scope**
- API reference documentation (auto-generated from OpenAPI).
- Architectural-decision-record content — the HLPS / IS / JIT
  specs already capture those.

---

### S-011 — Production verification

**What changes**
A documented walkthrough of the HLPS §6 verification intent
against a production-like environment, capturing evidence for:

- SC-2 (cycle runs at configured cadence).
- SC-3 (operator-disable works without spurious shutdown noise).
- SC-4 (`UnreachableSince` transitions correctly).
- SC-5 (UI shows all four states).
- SC-6 (≤5% p95 regression on synthetic baseline).
- SC-7 (5 consecutive stop/start cycles, including mid-cycle
  stop, leave the service operational).
- SC-9 (documented behaviour matches observed behaviour).

This step produces an evidence file
(`docs/connectivity-monitoring/S-011-verification-evidence.md`)
recording the timestamps, environments, and observations.

**Why**
The HLPS's success criteria are the contract; this step is
where they are demonstrated to be met.

**Dependencies**
S-010 APPROVED.

**Verification intent**
- Each SC listed above has a corresponding section in the
  evidence file with a pass/fail outcome and supporting data.
- **Near-threshold rule**: if any SC measurement lands within
  20% of the threshold (e.g. SC-6 shows a 4.0–4.99% regression,
  or SC-7 reveals a flaky failure on one of the 5 cycles), the
  measurement is rerun under fresh conditions. Pass requires
  **both** runs under threshold; a single near-threshold pass is
  not sufficient. This guards against happy-path mechanical
  walkthroughs.
- Failures, if any, must be triaged before the IS is considered
  complete: each is either resolved by a follow-up commit
  (re-running the step), accepted as a known limitation
  (with rationale recorded in HLPS Review History), or
  escalated to the user for a binding decision.

**Out of scope**
- Any further code change. S-011 is the verification gate; if
  it fails, the relevant prior step is reopened.

---

## 3. Sequencing rationale

```
              S-001 (drift decision + branch setup; gate for everything)
                                │
                ┌───────────────┴───────────────┐
                ▼                               ▼
        S-002 (schema delta)          S-003 (ConnectivityChecker)
                │                               │
                ▼                               │
        S-004 (persistent source)               │
                │                               │
                └───────────────┬───────────────┘
                                ▼
                       S-005 (IHostedService + Timer)
                                │
                                ▼
                       S-006 (API-model surfacing)
                                │
                                ▼
                       S-007 (UI 4-state indicator)
                                │
                                ▼
                       S-008 (CreateDate read piggyback)
                                │
                                ▼
                       S-009 (CreateDate API + UI)
                                │
                                ▼
                       S-010 (runbook + docs)
                                │
                                ▼
                       S-011 (production verification)
```

- **S-001 → everything** — drift decision sets the route for all
  downstream work and produces the route-conditional branch
  setup.
- **S-001 → S-002 and S-001 → S-003 are parallel** — S-002 is
  schema-only and S-003 is pure-logic with no schema touch
  points. They can be developed concurrently.
- **S-002 → S-004** — persistent source needs the schema to map
  against.
- **S-003 + S-004 → S-005** — the hosted service composes the
  probe primitives and the persistent source.
- **S-005 → S-006** — UI consumes API consumes data. Linear.
- **S-007 → S-008** — ring-fence per HLPS §8: connectivity
  skeleton lands BEFORE the #593 piggyback.
- **S-008 → S-009 → S-010 → S-011** — linear: piggyback writes,
  surface reads, doc covers both, prod verifies the whole.

## 4. Risks acknowledged at IS level

### 4.1 Step-level risks

| Risk | Mitigation |
|---|---|
| S-001 concludes "supersede" — meaning PR #374's diff is largely thrown away. The cost is real but not avoidable: the drift posture in the HLPS forced the question. | Acceptable. The HLPS is the contract; if PR #374 doesn't satisfy it efficiently, supersede is the honest answer. The decision note documents the rationale. |
| S-002 schema migration fails against a production-shape DB despite the dry-deploy. | Acceptable risk. The dry-deploy in non-prod is the standard mitigation; production deployment follows the project's regular database-deploy workflow which is itself transactional and rollback-capable. |
| S-005's cancellation regression test discovers a NEW failure mode (e.g. Timer-based service has its own quirk). | The U-2 resolution explicitly mandates the regression test. If it fails, S-005 reopens; the IS does not advance. |
| S-007 / S-009's UI columns degrade list-page render performance with the additional fields. | The additive-fields nature of the change means the existing pagination model still bounds the row count per render. Performance regression beyond a manual perception threshold becomes a finding to address before S-011 sign-off. |
| S-008's catalog-view query fails on certain older SQL Server configurations the project supports. | HLPS C9 mandates fail-soft behaviour: log at debug level, leave column unchanged. The test plan in S-008 covers this. |
| S-011 production verification reveals an SC failure that requires re-opening multiple prior steps. | Per S-011 verification intent, each failure is triaged individually; the IS does not advance until each is resolved or formally accepted. Cycle-limit escalation per CLAUDE.local.md §4 applies if the triage cycle stalls. |

### 4.2 Cross-step coupling

S-003 (probe primitives) and S-005 (hosted service) share the
`IConnectivityChecker` contract. A change to the contract in
S-003 implies a change to S-005's call site. The JIT specs for
both must be reviewed together if a contract change is needed
mid-flight.

S-002 (schema) and S-006 / S-009 (API model) share the column
shape. A nullability or type change in S-002 implies a model
update. Same review-together rule applies.

S-008 (CreateDate read) is logically coupled to S-005's cycle
loop — adding the read inside the existing cycle is the
ring-fenced path; the JIT spec for S-008 must spell out the
exact insertion point so the change is reviewable. S-008's
"Out of scope" bullet bounds this — the scheduler contract
established in S-005 is fixed; S-008 is additive within the
per-row check call site only.

S-007 and S-009 share the Databases list-page renderer. S-007
adds the connectivity Status column; S-009 adds the CreateDate
column. The renderer must accept both additively without
forking the parity-locked Servers / Databases pages or
introducing per-page divergence (HLPS G3 parity rule applies).
JIT specs for both must explicitly reference each other.

S-010 is coupled to **every step that introduces a configuration
knob, log line, or operator-visible behaviour**. The runbook
must be re-checked against any spec-level change that lands
during S-002..S-009 implementation; if a knob is added in S-005
that S-010 doesn't know about, S-010's verification intent
fails.

## 5. Out-of-scope (re-stated for IS reviewers)

Per HLPS §3 and §8: auto-cleanup tooling; real-time alerting;
richer health-checks; active probing during deployment requests;
Server creation date; modified / last-accessed timestamps;
re-litigating any unknown that the HLPS resolved.

## 6. HLPS coverage map

| HLPS item | Step(s) | Notes |
|---|---|---|
| G1 — periodic reachability check | S-005 | Cycle scheduler + S-003 probes + S-004 writes. |
| G2 — persistent state | S-002 + S-004 | Columns + persistent source. |
| G3 — long-term unreachability surfacing | S-007 | UI 4-state indicator. |
| G4 — operator override | S-005 | `EnableConnectivityCheck` honoured at startup. |
| G5 — predictable resource usage | S-004 + S-005 | Batched read; cycle scheduler enforces cadence. |
| G6 — DB creation date persistence | S-008 + S-009 | Ring-fenced post-skeleton. |
| C1 — no new runtime deps | All steps; primarily verified at S-002 | No new package references introduced; dependency-restore output reviewed during normal CI on each step's PR. |
| C2 — no external network endpoints | S-003 + S-005 | Probes target same paths Monitor uses for deploys. |
| C3 — additive schema only | S-002 | All new columns nullable; verified by S-002's dry-deploy. |
| C4 — no Monitor responsiveness regression | S-005 + S-011 | Cycle yields; SC-6 verifies. |
| C5 — no new credential surface | S-003 | Integrated Security only. |
| C6 — probe strategy + timeouts | S-003 | ICMP + TCP/445; ≤5s/probe. |
| C7 — log sanitisation | S-005 | Sanitiser helper; CodeQL gates. |
| C8 — `SqlConnectionStringBuilder` only | S-003 + S-008 | CodeQL gates. |
| C9 — parameterised catalog query | S-008 | Parameterised `WHERE name = @dbName`. |
| SC-1 — schema delta lands cleanly | S-002 + S-011 |
| SC-2 — periodic check runs | S-005 + S-011 |
| SC-3 — operator-disable works | S-005 + S-011 |
| SC-4 — `UnreachableSince` transitions | S-004 + S-011 |
| SC-5 — UI surfaces state correctly | S-007 + S-011 |
| SC-6 — Monitor responsiveness | S-005 + S-011 |
| SC-7 — restart resilience | S-005 + S-011 |
| SC-8 — security gates green | S-003 + S-005 + S-008 |
| SC-9 — documented behaviour | S-010 |
| SC-10 — DB creation date captured | S-008 + S-011 |
| SC-11 — creation date in API (three sub-requirements: field present on `DatabaseApiModel`, OpenAPI regenerate clean, existing consumers still build) | S-009 (delivers all three sub-requirements via the same regenerate-and-build pattern S-006 establishes) + S-011 (final end-to-end check) |
| U-1 — probe strategy | Resolved per HLPS; landed by S-003. |
| U-2 — hosted-service shape | Resolved per HLPS; landed by S-005. |
| U-3 — DB auth semantics | Resolved per HLPS; documented in S-010. |
| U-4 — cadence default | Configurable from S-005; default tuned in S-005. |
| U-5 — initial-delay default | Configurable from S-005. |
| U-6 — batch-size default | Configurable from S-004. |
| U-7 — schema migration mechanism | Resolved by S-002 verification. |
| U-8 — CreateDate UI surfacing | Resolved per HLPS; landed by S-009. |
| U-9 — CreateDate-changed behaviour | Resolved per HLPS; landed by S-008. |
| U-10 — cold-start UX | Documented in S-010; S-005 verification intent acknowledges initial-delay-vs-first-cycle as a JIT-spec choice. |

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (ordering/atomicity, HLPS coverage, delivery
realism) returned APPROVE_WITH_FIXES. Findings and dispositions:

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| **Ratify route can't honour S-002→S-004 hard split** — IS implicitly assumes revise/supersede shape | R1-realism F-1 (HIGH) | Accept | New paragraph in §1 Strategy: step boundaries are logical milestones, not mandatory commit boundaries. Under ratify, multiple steps may collapse into one PR-#374 conflict-resolution commit; per-step verification intent must still be individually evidenced in the PR description. |
| **SC-11 sub-requirements not enumerated in coverage map** | R1-coverage F-1 (HIGH) | Accept | §6 SC-11 row expanded to enumerate all three sub-requirements (field present, OpenAPI regenerate clean, existing consumers build) and confirm S-009 discharges them. |
| **§3 diagram contradicts S-003 Dependencies** — S-002→S-003 vs "independent" | R1-ordering F-1 (MED) | Accept | Diagram redrawn: S-001 fans out to S-002 and S-003 in parallel; S-002→S-004; S-004 + S-003 converge at S-005. S-003 dependency line clarified to "genuinely parallel with S-002 and S-004". |
| **S-001 Out-of-scope leaves rebase mechanical work ownerless** | R1-realism F-2 (MED) | Accept | S-001 "What changes" expanded with a "Branch setup output" subsection covering ratify/revise/supersede branch-prep deliverables; "Out of scope" reworded so route-conditional setup is explicitly in scope. |
| **S-005 bundles three concerns** — defensible but unjustified | R1-ordering F-2 (MED) | Accept | Added paragraph to S-005 "Why" defending the bundle: regression test is meaningless without the shell; cycle is meaningless without the regression-protected shell. Splitting would produce sub-steps with no independent shippable value. |
| **U-10 cold-start treated as docs-only** — no proactive smoothing | R1-realism F-3 (MED) | Accept | One-line nudge added to S-005 verification intent: initial-delay-vs-first-cycle trade-off is an open JIT-spec choice provided HLPS C4 / SC-7 stay green. |
| **S-008 bleeds into S-005's cycle-scheduler contract** | R1-realism F-4 (MED) | Accept | S-008 "Out of scope" extended: scheduler contract from S-005 is fixed; S-008 is additive within the per-row check call site only. If a clean piggyback is impossible without contract change, the IS reopens. |
| **S-004 verification missing `true→true` transition case** | R1-coverage F-2 (MED) | Accept | S-004 verification intent now lists six transitions explicitly including `true→true` (stays null across consecutive successful checks). |
| **S-001 missing PR #374 CodeQL threads check** | R1-realism F-6 (LOW) | Accept | S-001 question list extended with the CodeQL threads reconciliation item. |
| **S-011 has no near-threshold / failure clause** | R1-realism F-5 (LOW) | Accept | New "near-threshold rule" in S-011 verification intent: any SC measurement within 20% of threshold requires a fresh second run; both must pass. |
| **§6 C1 / C3 / SC-1 rows too loose** | R1-ordering F-3 (LOW) | Accept | C1 tightened to "All steps; primarily verified at S-002"; C3 tightened to "S-002 (verified by dry-deploy)"; SC-1 row already specific. |
| **§4.2 missing S-007↔S-009 + S-010↔every-step couplings** | R1-ordering F-4 (LOW) | Accept | Two paragraphs added to §4.2 covering renderer-parity coupling (S-007/S-009) and runbook-vs-knobs coupling (S-010 vs S-002..S-009). |
| **S-002 "Out of scope" mentions S-003 incorrectly** | R1-ordering F-5 (LOW) | Accept | Trimmed to "S-004" only. |
| **S-007 verification "sortable/filterable" implicit scope expansion** | R1-coverage F-3 (LOW) | Accept | "Sortable/filterable" line removed from S-007 verification intent; replaced with a parenthetical noting this is a delivery-level free-if-supported choice, not an SC-5 requirement. |

After this revision, status returns to `IN REVIEW` for R2. R2
reviewers must verify R1 fixes, check for regressions, and (per
CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on
R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Ordering / atomicity | **APPROVE** — All five R1 findings verified resolved; S-005 bundle defence judged adequate (smallest cohesive unit satisfying U-2 + SC-7); no regressions. |
| Reviewer B (Sonnet) | HLPS coverage | **APPROVE** — All three R1 findings verified resolved; drift-posture and ring-fence still intact; no contradictions introduced. |
| Reviewer C (default) | Delivery realism | **APPROVE** — All six R1 findings verified resolved; cycle-limit re-check downgrades S-001/S-005/S-008 from 3-round to 1–2-round risk; route-agnosticism re-audit clean. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
