# HLPS — Connectivity Monitoring for Servers and Databases

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Origin | Issues [#362](https://github.com/sefe/dorc/issues/362) and [#593](https://github.com/sefe/dorc/issues/593); PR [#374](https://github.com/sefe/dorc/pull/374) |
| Topic slug | `connectivity-monitoring` |
| Retrospective? | **Yes (partially)** — the connectivity-check implementation predates this HLPS. The DB creation-date addition (folded in from #593) is forward work. The HLPS captures the problem statement, scope, and accepted-vs-pending decisions so the in-flight code in #374 plus the new addition can be adversarially reviewed against an explicit contract. |

---

## 1. Problem Statement

DOrc tracks ~hundreds of servers and databases across the SEFE estate
in the `SERVER` and `DATABASE` tables. There is currently no way to
distinguish a record for a server / database that **still exists and
is healthy** from one that has been decommissioned, renamed, fire-walled
off, or is otherwise unreachable. Operators discover stale entries
ad-hoc — typically when a deployment fails because the target host is
gone — and clean-up is manual and lossy.

The user-visible cost: stale entries crowd the Servers and Databases
list pages, deployments fail late instead of early, and dashboards
overstate the health of the estate. The maintenance cost: nobody owns
the cleanup, so the lists drift further from reality each quarter.

The originating ask, from issue #362 (2025-10-18):

> Create a way to periodically test for the existence of servers and
> databases in the datacentre. If they cannot be reached for a window
> of a week then they should be marked as such and the UI should
> display some kind of indicator showing that the system is unable to
> contact them.

A secondary ask from @dmitryrext on the same issue, also tracked as
issue #593 (`Add Database Create time`):

> Also it would nice to have column in DORC DB showing the DB's
> creation date (we'll use it in our scripts).

This HLPS folds **both** asks together. The connectivity check is
already opening an authenticated SQL connection to each Database
record on the cadence; reading `sys.databases.create_date` at the
same time and persisting it to a new DOrc-side column is a marginal
addition rather than separate machinery. Issue #593 will be closed
as superseded once this HLPS is APPROVED.

## 2. Goals

- **G1 — Periodic reachability check.** A background process on the
  Monitor service tests every Server and Database record at a
  configurable cadence and records a per-record reachability state
  in the database.
- **G2 — Persistent state.** The result of each check is durable —
  the UI can show the current state and the time it was determined
  without re-running checks.
- **G3 — Long-term unreachability surfacing.** A record that has been
  unreachable continuously for ≥ 7 days is visually surfaced in the
  UI as a candidate for cleanup. Shorter unreachability is shown but
  not flagged for cleanup. "Continuously" is interpreted relative to
  the persisted `UnreachableSince` timestamp — any successful check
  resets that timestamp to `null` (per SC-4), so a single successful
  blip restarts the 7-day clock from zero on the next failure.
- **G4 — Operator override.** The check can be disabled entirely via
  configuration without redeploying — useful for environments
  (CI/local dev) where the Monitor cannot reach the targets.
- **G5 — Predictable resource usage.** Checks are batched (default
  100 records per batch) and rate-limited so a tracker of N records
  does not produce N concurrent network calls. The Monitor process
  must remain responsive to deployment requests during the check
  cycle.
- **G6 — Persist DB creation date.** Whenever the connectivity check
  successfully connects to a Database record, it also reads the
  server's view of that database's creation timestamp
  (`sys.databases.create_date` in SQL Server) and persists it on the
  DOrc-side `DATABASE` row. Subsequent reads (API + scripts +
  optional UI surfacing) use the persisted value rather than
  re-querying. The value is read-only from the user's perspective —
  DOrc records the upstream creation date, never sets it.

## 3. Non-Goals

- **N1.** Auto-deletion of stale records. The system surfaces them;
  removal remains a deliberate operator action.
- **N2.** Real-time alerting (PagerDuty, email, MS Teams). Out of
  scope; can be a follow-up issue once the persistent state exists.
- **N3.** Health-checks beyond reachability — e.g. SQL Server version,
  free disk, replication lag. Connectivity is binary: reachable or
  not. Anything richer is a separate feature.
- **N4.** "Active" probing during a deployment request. The check is
  a background sweep; deployment requests retain their existing
  failure semantics for unreachable targets.
- **N5.** Tracking creation date for **Servers**. The operator
  use-case behind issue #593 is DB-specific (creation date is used
  by DB-management scripts). Server records have no comparable
  use-case driver, and a server's "creation date" is ambiguous in
  practice (OS install date? domain-join date? hostname-assignment
  date?) — picking one without a use-case to anchor it would be
  arbitrary. Server records therefore keep their existing shape.
- **N6.** Tracking the *modified* / *last accessed* time of databases.
  The ask was specifically creation date.

## 4. Constraints

- **C1.** No new runtime dependencies (no Polly, no Hangfire, no
  external scheduler). Use `Microsoft.Extensions.Hosting` primitives
  already present.
- **C2.** No external network endpoints. The check must run inside
  the Monitor process and target the same network paths Monitor uses
  for deployments.
- **C3.** No change to the existing `SERVER` / `DATABASE` schema
  beyond additive columns; existing reads must remain valid.
- **C4.** No regression on Monitor's primary responsibility
  (deployment request processing). Connectivity checks must yield
  to request processing under any contention.
- **C5.** Authentication for the DB check uses the Monitor service
  account's existing identity (Integrated Security). No new
  credential surface.
- **C6.** Server reachability is determined by ICMP `Ping` first; if
  the ping fails, a TCP connect to port **445** (SMB) is attempted
  as a fallback. SMB is the protocol DOrc actually uses to push
  deployments, so its reachability is the operational signal that
  matters. The host is `IsReachable=true` if either probe succeeds.
  Each individual probe (ping or TCP-connect) must complete or fail
  within a per-probe timeout no greater than **5 seconds** so that
  the per-host worst case is bounded at ≤ 10 seconds (ping timeout
  + TCP-connect timeout). This protects SC-6's responsiveness budget
  against firewalled hosts that would otherwise hang a half-open
  SYN for tens of seconds. See U-1 (resolved) for the rationale.
- **C7.** All log statements that include user-controllable values
  (server name, database name) must be sanitised against log
  forging (CR/LF stripping). CodeQL log-forging alerts are an
  accepted gating condition.
- **C8.** All connection strings must be built via
  `SqlConnectionStringBuilder` rather than string concatenation.
  CodeQL resource-injection alerts are an accepted gating condition.
- **C9.** The `sys.databases.create_date` query must use a
  parameterised `WHERE name = @name` form (not concatenation) and
  must run against the connection already opened for the
  reachability check. No additional connection per database.
  Permission requirement is only the default catalog-view access
  (`VIEW ANY DATABASE` is granted to public on SQL Server) — if the
  Monitor account lacks even that, the value is left unchanged
  rather than nulled.

## 5. Success Criteria

A change is successful when, on the affected surfaces:

1. **SC-1 — schema delta lands cleanly.** `SERVER` and `DATABASE`
   tables gain `LastChecked` (DATETIME2 NULL), `IsReachable`
   (BIT NULL), `UnreachableSince` (DATETIME2 NULL). `DATABASE`
   additionally gains `CreateDate` (DATETIME2 NULL) for the
   upstream `sys.databases.create_date` value. Upgrade against a
   current production-shape database succeeds with no manual data
   fixup. Existing reads on these tables are unchanged.
2. **SC-2 — periodic check runs.** With
   `EnableConnectivityCheck=true`, the Monitor service runs the
   check at the configured cadence (default 60 minutes), batches
   records (default 100 per batch), and writes results back to
   the persistent store.
3. **SC-3 — operator disable works.** With
   `EnableConnectivityCheck=false`, the Monitor logs that
   connectivity checks are disabled at startup and never executes
   any check; restart toggles cleanly. No spurious cancellation /
   exception-on-shutdown noise in the log.
4. **SC-4 — UnreachableSince correctly tracks transitions.**
   - `IsReachable` transitions `null` → `true`: `UnreachableSince`
     is `null`.
   - `IsReachable` transitions `true` → `false`: `UnreachableSince`
     is set to "now".
   - `IsReachable` transitions `false` → `true`: `UnreachableSince`
     is cleared back to `null`.
   - `IsReachable` stays `false` across cycles: `UnreachableSince`
     keeps its original "first became unreachable" timestamp.
5. **SC-5 — UI surfaces state correctly.** The Servers and
   Databases list pages show, per row, exactly one of:
   - **"Online"** (green) when `IsReachable=true`.
   - **"Unreachable"** (yellow) when `IsReachable=false` AND
     `UnreachableSince` is **less than 7 days ago** (i.e. recent
     unreachability — show but don't flag for cleanup).
   - **"Unreachable (7+ days)"** (orange / warning) when
     `IsReachable=false` AND `UnreachableSince` is **7 or more days
     ago** (cleanup-candidate per G3).
   - **"Not checked"** (gray) when `LastChecked=null`.
   Tooltips show the relevant timestamp ("Last checked at …",
   "Unreachable since …").
6. **SC-6 — Monitor responsiveness preserved.** Under controlled
   load (a synthetic deployment-request workload run twice — once
   with `EnableConnectivityCheck=false` as the baseline, once with
   it enabled and a check cycle straddling the test window),
   deployment-request p95 latency must not increase by more than
   **5%** between the two runs. The synthetic workload, its
   duration, and the measurement tool are pinned by the JIT spec
   for the relevant IS step; this HLPS only commits to the relative
   threshold and the side-by-side methodology.
7. **SC-7 — Monitor restart resilience.** Stopping and restarting
   the Monitor service mid-check cycle does NOT leave the
   connectivity worker in a wedged state where it cannot start on
   the next launch. Confirmed by triggering 5 consecutive
   stop/start cycles in non-prod with the check enabled.
8. **SC-8 — security gates green.** CodeQL scans for
   resource-injection and log-forging on the touched files return
   no new HIGH/CRITICAL findings.
9. **SC-9 — false-negative behaviour is documented.** The README in
   `docs/connectivity-monitoring/` (or an inline doc) describes
   exactly what "unreachable" means under the chosen probe
   strategy, and which legitimate-but-unreachable cases will
   surface as warnings (e.g. ICMP-blocked hosts, DBs the Monitor
   account cannot authenticate against).
10. **SC-10 — DB creation date captured.** When a connectivity
    check successfully authenticates against a Database, the
    `CreateDate` column on the corresponding DOrc-side row is
    populated from `sys.databases.create_date` (filtered by
    parameterised `name = @dbName`). Subsequent successful checks
    keep the value stable; if the upstream value differs (e.g. a
    DB was dropped and recreated), the new value overwrites and
    a log entry is emitted at **`Information`** level (the
    minimum level required to ensure the entry is captured by
    the standard production log configuration) noting the
    transition (DB name, old `CreateDate`, new `CreateDate`).
    Failed checks leave the column unchanged.
11. **SC-11 — creation date exposed via the API.** The
    `DatabaseApiModel` includes the new `CreateDate` field. The
    OpenAPI surface regenerates cleanly. Existing consumers
    (Tools.RequestCLI, dorc-web) build with no changes — the
    field is additive and nullable.

## 6. Verification Intent

- Manual verification of SC-1 by deploying the schema-delta to a
  baseline-shape non-prod DB and confirming no fixup needed.
- Manual verification of SC-2/SC-3/SC-4 by toggling configuration
  and observing the persistent store + log output across at least
  two check cycles.
- Manual verification of SC-5 by visiting both list pages with
  the four representative state combinations (one row each).
- Manual verification of SC-6 by running a load test against the
  Monitor's request-processing path with the check enabled and
  comparing p95 against a baseline run with the check disabled.
- Manual verification of SC-7 by 5× stop/start cycle on a
  non-prod Monitor.
- Automated verification of SC-8 by GitHub Advanced Security /
  CodeQL on the PR.
- Documentation verification of SC-9 by reviewer reading the
  documented behaviour against the implementation.

## 7. Unknowns Register

Each unknown must be resolved before the corresponding step in the
Implementation Sequence (IS) can be APPROVED.

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| U-1 | **Probe strategy for server reachability.** ICMP `Ping` alone gives false negatives on ICMP-blocked hosts. | User | **Resolved 2026-04-28**: option **(b)** — ICMP first, with a TCP-connect fallback to port **445 (SMB)** when ICMP fails. SMB is the protocol DOrc actually uses to push deployments, so its reachability is the operational signal that matters; if either probe succeeds, the host is `IsReachable=true`. Per-port-or-protocol configurability is **not** required at ship time. |
| U-2 | **Hosted-service shape — `BackgroundService` vs `IHostedService` + Timer.** PR #374 has flip-flopped between the two; the `BackgroundService` form has a documented cancellation-on-startup bug that the Timer form sidesteps. | User | **Resolved 2026-04-28**: restore the `IHostedService` + `System.Threading.Timer` implementation (the 2026-03-19 Claude version). Independent of host lifecycle tokens, no reliance on `Task.Yield()` placement in `MonitorService.ExecuteAsync()`. The IS step that lands this must include a regression test exercising the "MonitorService throws synchronously on startup" scenario. |
| U-3 | **DB authentication semantics.** `CheckDatabaseConnectivityAsync` uses `IntegratedSecurity=true`. A DB the Monitor service account cannot authenticate against will report as `IsReachable=false` even if it's healthy. Options: (a) accept and document; (b) treat auth failures as "unknown" rather than "unreachable" (extra column or tri-state); (c) use a different identity (would require C5 amendment). | User | **Resolved 2026-04-28**: option **(a)** — accept and document. Auth failures continue to surface as `IsReachable=false`; SC-9 documentation enumerates this as a known false-negative case. Option (b) was rejected to avoid a tri-state schema change that adds API surface complexity for a corner case; (c) was rejected to keep C5 (no new credential surface) intact. |
| U-4 | **Check cadence default.** Current default is 60 minutes. For an estate of ~hundreds of records this is once-an-hour traffic, which is light. For an estate growing into the low thousands the cadence might need to be longer. The current implementation is configurable (`ConnectivityCheckIntervalMinutes`); confirm 60 is the right shipped default. | User | No |
| U-5 | **Initial delay default.** Current default is 30 seconds. Originally introduced to let the Monitor settle into request-processing before adding checking traffic. Confirm 30 is appropriate for production startup conditions. | User | No |
| U-6 | **Batch size default.** Current default is 100. Sized so a typical estate completes a full cycle within the cadence window without saturating the DB or network. Confirm. | User | No |
| U-7 | **Schema migration mechanism.** The repo uses an SQLProj which auto-generates ALTER scripts on deploy. Confirm that an upgrade from a pre-#374 production-shape database to the new schema succeeds in a dry-deploy test (SC-1). | Agent | **Yes** — gates SC-1. |
| U-8 | **CreateDate UI surfacing.** The originator only said "we'll use it in our scripts" — i.e. API access is the explicit ask. Adding a column to the Databases list page is optional; deciding now whether to ship the UI surfacing in the same step or defer to a follow-up issue avoids retrofit. | User | **Resolved 2026-04-28**: ship the UI column on the Databases list page in the same step. |
| U-9 | **CreateDate-changed behaviour.** SC-10 specifies the new value overwrites and a log entry is emitted on change. Decide whether DOrc should treat a creation-date change as a "DB rebirth" (resetting other fields like `UnreachableSince`) or just as a value update (other fields follow their normal transition rules). | User | **Resolved 2026-04-28**: treat as a value update. `CreateDate` is overwritten and the change is logged; `LastChecked`, `IsReachable`, `UnreachableSince` follow their normal transition rules. Operators can investigate via the log line if they want to. |
| U-10 | **Cold-start UX during initial rollout.** When the schema delta lands (SC-1) and the service starts for the first time, every existing `SERVER` and `DATABASE` row has `LastChecked=null` and will surface as "Not checked" in the UI per SC-5. For an estate where one full check cycle takes the cadence interval (default 60 minutes), this is up to an hour of "everything is unknown" until the first cycle completes. This is acceptable behaviour but should be explicitly noted to operators in the deployment runbook. | Agent | No — accept the cold-start window as documented behaviour; the deployment runbook produced as part of the IS must mention it. |

Items marked **Yes** under "Blocking" must be resolved before the
corresponding IS step is APPROVED.

## 8. Out-of-scope clarifications

- **Auto-deletion / record cleanup tooling** — out of scope per N1.
  A separate issue can track the cleanup workflow.
- **Real-time alerting / notification** — out of scope per N2.
- **Richer health checks** (version, disk, replication) — out of
  scope per N3.
- **Creation-date column on Servers** — out of scope per N5 (only DB creation date is in scope).
- **Modified / last-accessed date** — out of scope per N6.
- **Tests for the BackgroundService and persistent-source query
  path** — only the `ConnectivityChecker` has unit tests today.
  The IS will reintroduce this as required-or-deferred per
  step-level decision.
- **Treating PR #374 as the implementation contract** — the PR is
  context for this HLPS, not its product. Once this HLPS and its
  IS are APPROVED, PR #374's existing diff will be reviewed
  against the contract and either ratified, revised, or
  superseded. The IS will state which.

### Drift posture

PR #374's branch tip is approximately **251 commits behind `main`**
as of 2026-04-28. The IS for this work must include — as an
explicit early step — a drift assessment that surfaces at minimum:

- the daemons modernisation (PR #649, merged) which renamed
  `Server.cs`'s collection from `Services` to `Daemons`. PR #374's
  diff still references the old name;
- any Monitor-side DI / hosted-service registration changes
  between #374's tip and current `main` that affect where the
  connectivity service plugs in;
- the schema state — what the `SERVER` / `DATABASE` tables look
  like on `main` today, since the delta in SC-1 must land cleanly
  on top of any other intervening schema changes.

The drift assessment's output decides whether the IS proceeds by
**ratifying** PR #374's diff (resolve conflicts, no rework),
**revising** it (selective rework against the HLPS contract), or
**superseding** it (close PR #374 and reimplement on a fresh
branch from current `main`). The HLPS does not pre-judge that
choice.

### Ring-fencing the #593 (DB creation date) work

Although G6 / SC-10 / SC-11 / U-8 / U-9 fold the DB creation-date
addition into this HLPS, the IS must keep that work in its own
step(s) sequenced **after** the connectivity-check skeleton lands.
The connectivity check is a foundation that the creation-date
read piggybacks on; landing both in a single step would produce a
non-atomic commit and erode the IS's "atomic, independently
valuable" requirement (CLAUDE.local.md §2). The two work-streams
share an open SQL connection at runtime but do not share a
delivery commit.

## 9. Risks

| Risk | Mitigation |
|---|---|
| ICMP-block false negatives produce noise that erodes trust in the indicator. | Resolve U-1 with a probe strategy that minimises this OR document the limitation prominently per SC-9. |
| BackgroundService cancellation regression resurfaces on a future Monitor refactor. | Resolve U-2 in a way that includes a regression test exercising the "Monitor sync-startup throws" path. |
| Connectivity check cycle interferes with deployment-request processing under sustained high load. | SC-6 quantifies the regression budget. If the budget is breached, batch size and cadence are tunable; if still breached, reconsider running checks in a separate process. |
| Schema delta breaks existing migration on production-shape DB. | Resolve U-7 via dry-deploy verification before SC-1 is signed off. |
| New DB columns (3 connectivity columns × 2 tables, plus 1 `CreateDate` column on `DATABASE`) leak through to APIs / OpenAPI surface and break consumers (Tools.RequestCLI, dorc-web). | API model classes are additive (nullable). The OpenAPI generator will produce additive field changes; existing consumers remain valid. Verify via build of dorc-web after the API regenerate. |
| `sys.databases.create_date` query returns nothing because the Monitor service account lacks `VIEW ANY DATABASE` (or its catalog-view equivalent on a custom server). | Per C9, treat as a no-op: leave the existing `CreateDate` value unchanged and log at debug level. This avoids ping-pong nulling on every cycle. |
| A DB is dropped and recreated under the same name — DOrc previously held the old creation date. | Per SC-10, the new value overwrites and a log line records the transition. Operators see the change in the UI on the next cycle. |
| Operators ignore the warning indicators and the lists fill up with stale records anyway. | Out of scope of the HLPS — would be addressed by the auto-cleanup work behind N1. |

## 10. Open questions for the Adversarial Review panel

(Substantive questions only — meta-questions about the review
process itself were trimmed in R2 per reviewer feedback.)

- Are the success criteria SC-1..SC-11 collectively sufficient to
  judge the feature delivered, or are there observable behaviours
  we expect but haven't enumerated?
- Are U-1 through U-10 the right unknowns? Anything load-bearing
  that's been quietly assumed?
- Are SC-6's "5%" relative-threshold methodology and SC-7's
  "5 consecutive stop/start cycles" the right shape for
  measurable acceptance, or are they cargo numbers?

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (problem framing, operational/falsifiability,
process/scope) returned APPROVE_WITH_FIXES. Findings and
dispositions:

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| **SC-5 colour/threshold inversion** — yellow vs orange swapped vs G3's threshold; an implementer building from this verbatim would ship the wrong UI | R1-framing F-1 (HIGH), R1-falsif F-01 (MED) | Accept (HIGH) | SC-5 rewritten so the (7+ days) warning fires when `UnreachableSince` is ≥ 7 days; "Unreachable" without warning suffix fires when < 7 days. |
| **U-3 dispositioned "No" but actually unresolved** — the "(a)/(c) decision" was never picked | R1-framing F-2 (HIGH) | Accept | U-3 resolved 2026-04-28 with option (a) — accept and document. SC-9 documentation enumerates DB-auth-failure as a known false-negative. |
| **G3 "continuously" undefined** — single successful blip behaviour ambiguous | R1-framing F-3 | Accept | G3 extended with one-line clarification: "continuously" = relative to `UnreachableSince`; any successful check resets the clock per SC-4. |
| **TCP-445 fallback timeout missing** — half-open SYN can hang for tens of seconds; threatens SC-6 budget | R1-framing F-4 | Accept | C6 extended to specify per-probe timeout ≤ 5 seconds, per-host worst-case ≤ 10 seconds (ping + TCP). |
| **Cold-start UX missing from unknowns** — every row "Not checked" until first full cycle completes | R1-framing F-5 | Accept | New U-10 added covering rollout/cold-start UX; deployment runbook produced by the IS must mention. |
| **N5 rationale shaky** — "OS doesn't expose creation date" is contestable | R1-framing F-6 | Accept | N5 rewritten to lead with the operator use-case (DB-specific scripts) and acknowledge "server creation date" is itself ambiguous (install / domain-join / hostname-assignment). |
| **SC-10 log-level not constrained** — could be `Debug`, suppressed in prod | R1-falsif F-02 | Accept | SC-10 specifies the creation-date-change log line is emitted at `Information` level minimum. |
| **#593 ring-fence directive missing** — risk of monolithic IS steps | R1-process F-1 | Accept | New "Ring-fencing the #593 work" subsection in §8 instructing the IS to keep creation-date in its own step(s) after the connectivity-check skeleton lands. |
| **Drift posture not surfaced** — 251 commits behind `main`, daemons rename | R1-process F-2 | Accept | New "Drift posture" subsection in §8 mandating an early IS-level drift assessment (daemons rename, Monitor DI changes, schema state) before deciding ratify/revise/supersede. |
| **SC-6 4-hour wording contradiction** | R1-falsif F-03 | Accept | SC-6 reworded to commit only to the relative threshold and side-by-side methodology; the synthetic workload, duration, and tooling are pinned by the relevant JIT spec. |
| **U-3 Owner format** — "User decision" → "User" | R1-process F-5 | Accept | Normalised. |
| **§10 meta-question on retrospective bias** | R1-process F-3 | Accept | Removed; retained genuinely substantive panel-input questions. |
| **C-7 / C-8 too specific (CodeQL alert categories)** | R1-process F-4 | Defer | CodeQL log-forging and resource-injection were real gating in PR #374 — keeping the specifics ensures they aren't dropped at JIT-spec time. The abstract restatement adds no value here. |
| **SC-6 5% threshold defensibility** | R1-framing F-7 | Defer to Delivery | Synthetic-baseline methodology in §6 anchors the threshold; the JIT spec for the relevant IS step pins workload + tooling. |
| **Retrospective self-check** | R1-framing F-8 | Defer | Current framing in §0 is sufficient self-aware acknowledgement; reviewer panel's job to verify. |

After this revision, status returns to `IN REVIEW` for R2. R2
reviewers must verify R1 fixes, check for regressions, and (per
CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on
R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Problem framing | **APPROVE** — All eight R1 findings verified (six accepted-and-applied, two correctly deferred). No regressions. SC-5 inversion fixed; U-3 resolved with option (a); cold-start UX captured as U-10. |
| Reviewer B (Sonnet) | Operational / falsifiability | **APPROVE** — All three R1 findings verified resolved. Falsifiability re-audit on SC-5 explicitly re-confirmed; SC-10 log-level constraint unambiguous; SC-6 wording contradiction gone. |
| Reviewer C (default) | Process / scope | **APPROVE** — All five R1 findings verified. Drift-posture and ring-fence subsections judged adequate for IS author guidance; meta-question correctly trimmed; C-7/C-8 specificity rationale recorded. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
