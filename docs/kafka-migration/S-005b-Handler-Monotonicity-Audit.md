# S-005b Handler Monotonicity Audit

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Created** | 2026-04-14 |
| **Related** | SPEC-S-005b R-6, ADR-S-005 §4 Consequence #3 |

---

## 1. Purpose

Enumerate every `Dorc.Monitor` DB-write path that runs under a distributed
lock today and verify each is either **pure-idempotent** (same input → same
DB state regardless of invocation count) or **monotonic-guarded** (WHERE-clause
predecessor check / version guard that guarantees only the first writer of a
given transition commits). This is the safety property that absorbs the
cooperative-rebalance two-leader window per ADR-S-005 §6.

Scope restricted to the current lock-guarded region in
`Dorc.Monitor.DeploymentRequestStateProcessor` and the helpers it invokes.

---

## 2. Lock-guarded region

`DeploymentRequestStateProcessor.cs:487-582` — the `Task.Run` lambda started
per per-environment request group. The lock spans:

- Acquisition at line 501.
- Re-validation of request status after acquisition (lines 516-524).
- Execution via `ExecuteRequest(requestToExecute, requestCancellationTokenSource.Token)` (line 551).
- Lock-health check (lines 554-560).
- Dispose in `finally` (line 578).

Inside that span the only DB-write path is `ExecuteRequest` → the transition
`Pending|Confirmed → Requesting` for the chosen request.

---

## 3. Write-path enumeration

| # | Path (file:line) | Write shape | Classification | Evidence |
|---|---|---|---|---|
| 1 | `DeploymentRequestStateProcessor.ExecuteRequest` → `RequestsPersistentSource.UpdateNonProcessedRequest` (`Dorc.PersistentData/Sources/RequestsPersistentSource.cs:266-288`) | `UPDATE DeploymentRequests SET Status=@new, RequestedTime=@now WHERE Id=@id AND Status=@expected` via EF `ExecuteUpdate` with a `.Where(r => r.Id==id && r.Status == expected)` predicate | **Monotonic-guarded** — the WHERE clause on prior status means a second executor (two-leader window) issuing the same transition finds 0 rows and the call is a no-op. | Optimistic-concurrency pattern; any out-of-order re-execution observed as "0 rows affected" and treated as a lost-race by the caller. |

### Paths that were in scope but are not lock-guarded (verified harmless)

The following run **outside** the lock region and are therefore outside the
two-leader window by construction, but they are listed for completeness:

| Path | Lock status | Reason it's safe |
|---|---|---|
| Resume stale `Running → Pending` (`DeploymentRequestStateProcessor.cs:155-171`) | Outside lock — startup recovery | Uses `SwitchDeploymentRequestStatuses(..., Running, Pending)` which WHERE-clauses on source status; at most one monitor's call can switch any given row. |
| Cancel stale `Requesting → Cancelled` (`DeploymentRequestStateProcessor.cs:197-225`) | Outside lock — timeout cleanup | Same WHERE-on-status pattern. |
| `AbandonRequests` / `CancelRequests` (`DeploymentRequestStateProcessor.cs:264-316`) | Outside lock — user-initiated | Same WHERE-on-status pattern. |
| Results-status switcher (`DeploymentRequestStateProcessor.cs:330-336`) | Outside lock | WHERE on source status; conditional timestamps keyed on target status. |
| Restart / clear-results (`DeploymentRequestStateProcessor.cs:364-368, 390`) | Outside lock | Same pattern; restart chains only apply when source status matches. |

### SQL-dialect note (AT-5)

All writes in scope use EF Core's `ExecuteUpdate` with a `.Where(...)` predicate
on source status. The Safety Property the HA suite exercises
(`no (RequestId, Version) duplicate rows`) is a property of the predicate
logic, not of the SQL dialect. The AT-5 test harness's SQLite substrate
therefore represents production fidelity for the guard mechanism. If a
production incident ever attributes a duplicate to a SQL-Server-specific
isolation-level edge case, escalate post-cutover per SPEC-S-005b §R-8
note and rerun under a containerised SQL Server.

---

## 4. Remediations

**None required.** Every identified write path is already monotonic-guarded
under the established optimistic-concurrency pattern. No code changes
required for S-005b.

---

## 5. Reviewer checklist

- [x] Every `TryAcquireLockAsync`-guarded write in Dorc.Monitor listed.
- [x] Each classified pure-idempotent or monotonic-guarded.
- [x] Any unclassified write path carries a remediation plan (N/A — all guarded).
- [x] Cited file:line references are resolvable to the shipped HEAD.
