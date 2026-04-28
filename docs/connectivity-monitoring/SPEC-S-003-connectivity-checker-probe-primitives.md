---
name: SPEC-S-003 — ConnectivityChecker probe primitives (U-1 fallback)
description: JIT Specification for S-003 — adds TCP/445 fallback to the server probe so ICMP-blocked but reachable hosts no longer show as unreachable. Pure logic with unit tests; no persistence, no hosting, no DI wiring.
type: spec
status: IN REVIEW
---

# SPEC-S-003 — `ConnectivityChecker` probe primitives (U-1 fallback)

| Field | Value |
|---|---|
| **Status** | IN REVIEW (R2) |
| **Step** | S-003 |
| **Author** | Agent |
| **Date** | 2026-04-28 |
| **HLPS** | `docs/connectivity-monitoring/HLPS-connectivity-monitoring.md` (APPROVED) |
| **IS** | `docs/connectivity-monitoring/IS-connectivity-monitoring.md` (APPROVED) |
| **Governing decision** | `docs/connectivity-monitoring/DECISION-S-001-drift.md` (APPROVED — Pending user approval) |
| **Folder** | `docs/connectivity-monitoring/` |
| **Branch** | `copilot/create-server-db-existence-check` (PR #374's source branch on `sefe/dorc`); current tip `ca6bf954` (S-002 source-edit) |

---

## 1. Context

### What this step addresses

S-003 lands the **probe-primitives half** of the connectivity-monitoring runtime, fulfilling HLPS U-1 (resolved 2026-04-28: "ICMP first, with TCP-connect fallback to port 445"). Today the post-merge tip's `Dorc.Core.Connectivity.ConnectivityChecker.CheckServerConnectivityAsync` is **ICMP-only**, which means:

- A reachable Windows server that has ICMP echo blocked at the firewall (a common production posture) is reported `IsReachable=false`.
- Operators lose trust in the indicator because the false-negative rate is high.
- HLPS SC-9 (false-negative behaviour documented) becomes harder to honour because the failure modes are not bounded.

S-003 fixes this by adding a TCP/445 (SMB) connect attempt that fires when ICMP fails. SMB/445 is the canonical "this Windows host is up and on the network" signal in the DOrc estate; if the host is genuinely down, both probes fail; if ICMP is blocked but the host is up, TCP/445 succeeds and the indicator is correct.

The DB probe (`CheckDatabaseConnectivityAsync`) is **not** in scope for S-003 in any behavioural sense — its current implementation already satisfies HLPS C5 / C6 / C8 (`SqlConnectionStringBuilder`, IntegratedSecurity, 5s timeout). S-003 does add tests around its existing behaviour to make the regression surface explicit and to satisfy the IS verification intent.

### Scope

- **`src/Dorc.Core/Connectivity/ConnectivityChecker.cs`**: extend `CheckServerConnectivityAsync` with the TCP/445 fallback path. May also extract the probe primitives behind small seams to make unit-testing tractable (the implementer chooses the seam shape — see §2.2 for what it must enable, not what it must look like).
- **`src/Dorc.Core/Connectivity/IConnectivityChecker.cs`**: **no change**. Per DECISION-S-001 §7, the fallback is internal to the implementation; the public contract stays.
- **`src/Dorc.Core.Tests/ConnectivityCheckerTests.cs`**: rewrite to cover the full matrix from §3.1, replacing the environment-fragile tests (`WithLocalhost_ReturnsTrue`, `WithInvalidServerName_ReturnsFalse`) with deterministic ones.
- **No new project or assembly**. No DI / hosting / persistence touch points.

Out of scope:
- The connectivity-check service that drives the probe (S-005).
- Any persistent-source change (S-004).
- Any API model, controller, or UI surface change.
- Schema work (S-002).
- Per-host worst-case enforcement that depends on the calling service's cycle scheduler — only **per-probe** timeouts are owned by S-003 (HLPS C6).

### Governing constraints

- **HLPS U-1 (resolved 2026-04-28)**: ICMP first, TCP/445 fallback. Both must be exercised in the resolved order.
- **HLPS C6**: per-probe timeout ≤ 5s; per-host worst-case ≤ 10s. S-003 owns the per-probe ceiling for both ICMP and TCP; the per-host total (worst case ICMP-fail + TCP-fail = 10s) is a natural arithmetic consequence and must be verified.
- **HLPS C5**: no new credential surface for the DB probe — `IntegratedSecurity=true` only.
- **HLPS C7** (log sanitisation): does not apply directly to S-003 — this step has no logging surface; the cycle service in S-005 owns log emission and sanitisation.
- **HLPS C8**: `SqlConnectionStringBuilder` only for the DB probe — already in place at `ca6bf954` per `src/Dorc.Core/Connectivity/ConnectivityChecker.cs:48`.
- **HLPS SC-8**: CodeQL gates green. The TCP fallback uses `System.Net.Sockets.Socket` / `TcpClient` against a hostname provided by configuration — already CodeQL-clean for the existing ICMP path; the spec author has chosen the API surface to keep the CodeQL surface unchanged.
- **CLAUDE.md naming**: any new private type / method name must be cohesive and not a grab-bag (`ServerProbe`, `TcpProbe`, `PingProbe` are acceptable; `ConnectivityHelper`, `NetworkUtility` are not).

---

## 2. Production code change

### 2.1 `CheckServerConnectivityAsync` — add the TCP/445 fallback

**Target**: edit `src/Dorc.Core/Connectivity/ConnectivityChecker.cs`. The canonical pre-edit shape is the file as it exists at PR #374's branch tip `ca6bf954`.

**Behavioural contract after the edit** (in plain-language, not a copy-paste DDL/code block per CLAUDE.local.md JIT-spec abstraction rules):

1. **Empty / null / whitespace input** continues to return `false` immediately, as today.
2. **Otherwise**, attempt an ICMP ping with a per-probe timeout of `PingTimeoutMs` (5000 ms; the constant already exists on the file). If the ping reply status is `IPStatus.Success`, return `true`.
3. **If ICMP did not return success** (any status other than `Success`, including timeout, host-unreachable, or any thrown exception swallowed by the catch-all), attempt a TCP connect to port **445** on the same hostname with a per-probe timeout of **5000 ms** (call it `TcpProbeTimeoutMs` for symmetry). If the TCP connect **establishes** (the OS reports a completed three-way handshake — the connection is `Connected` and ready for I/O) within the timeout, return `true`.
4. **If both probes fail** (ICMP not Success AND the TCP connect did not establish within the timeout — including timeout-elapsed, RST/`SocketException("Connection refused")`, host-unreachable, or any other socket exception thrown), return `false`. A TCP RST means "host received the SYN and rejected it" — that is not "established", so it counts as TCP failure for this contract. Note: a host that RSTs port 445 is *probably* alive but not running SMB; that is an accepted false-negative case per HLPS SC-9 and is documented in S-010, not addressed by adding more probes.
5. **Total per-call worst-case latency is ≤ ~10 s** (ICMP timeout + TCP timeout) plus a small implementation slop. HLPS C6 budget honoured.

**Timeout-bounding ownership (the per-probe ceiling)**: the per-probe ≤ 5 s budget is a **caller-side guarantee**, not something the underlying `Ping` / `Socket` API delivers free. Specifically:

- `Ping.SendPingAsync(host, timeout)` performs DNS resolution **synchronously before** the timeout-bounded ICMP send; on a host with slow DNS the call can run for many seconds before the ICMP timeout starts. The implementer must wrap the ICMP probe such that DNS + send together complete within `PingTimeoutMs` — typical pattern is `Task.WhenAny(probe, Task.Delay(timeout))` with the loser disposed.
- `Socket.ConnectAsync` similarly does not bound DNS; the same wrapping pattern applies.
- The seam from §2.1 (chosen by the implementer) is the surface where this wrapping happens. From the caller's perspective the seam call must always return within its declared timeout. Tests in §3.1 assert this contract on the seam, not on the underlying types.

**Implementation freedom (the seam choice)**: the implementer chooses how to make the two probe paths unit-testable. Acceptable approaches include but are not limited to:

- Constructor-injected `Func<string, int, Task<bool>>` delegates for ICMP-probe and TCP-probe with default real implementations.
- Constructor-injected probe interfaces (e.g. `IServerProbe { Task<bool> PingAsync(...); Task<bool> TcpConnectAsync(...); }`) with a default real implementation registered if no override is provided.
- Internal `protected virtual` methods overridden in test subclasses.
- Any other seam that lets §3.1 unit tests deterministically exercise the four ICMP × TCP outcome combinations without depending on the current host's network state.

The spec does **not** prescribe the seam — only that the §3.1 tests pass with whatever seam is chosen, and that no new public API surface is added (HLPS public contract is `IConnectivityChecker` only).

### 2.2 `CheckDatabaseConnectivityAsync` — no behaviour change

The DB probe at `ca6bf954` already satisfies the contract:
- Uses `SqlConnectionStringBuilder` (HLPS C8) — verified at line 48.
- Uses `IntegratedSecurity = true` (HLPS C5).
- Sets `ConnectTimeout = 5` seconds (HLPS C6, per-probe).
- Returns `false` for empty / null / whitespace inputs.
- Wraps the open in a try / catch and returns `false` on any exception (never throws).

The implementer **may** apply the same seam pattern as §2.1 to make DB tests deterministic without a real SQL Server, but is **not required to** if the unit tests can use a known-bad connection string (e.g. an unroutable IP and a fake DB name) to deterministically observe the connection-refused / timeout path within the 5s budget. Either pattern satisfies the §3.2 test matrix.

### 2.3 Constants and naming

- The existing `PingTimeoutMs = 5000` constant remains.
- A new TCP timeout constant should be introduced (suggested name `TcpProbeTimeoutMs`) at 5000 ms. Naming is the implementer's choice; the value is non-negotiable per HLPS C6.
- Port 445 may be a named constant (`SmbPortNumber` or similar) or inlined; inlining a single use of `445` is acceptable.
- No grab-bag name (`Helper`, `Util`, `Manager`) on any new type or method.

---

## 3. Verification plan

### 3.1 Unit tests for `CheckServerConnectivityAsync`

The implementer adds the following test cases (test names suggestive; the implementer may rename for clarity). Every test must pass deterministically without depending on the host's network state — i.e., they exercise the seam from §2.1, not real ICMP/TCP. Tests live in `src/Dorc.Core.Tests/ConnectivityCheckerTests.cs`, replacing the existing environment-fragile cases.

| # | Scenario | Expected outcome |
|---|---|---|
| 1 | Server name is `null` | Returns `false`; neither probe is invoked. |
| 2 | Server name is empty string | Returns `false`; neither probe is invoked. |
| 3 | Server name is whitespace-only | Returns `false`; neither probe is invoked. |
| 4 | ICMP probe returns `true` | Returns `true`; TCP probe **must not** be invoked. |
| 5 | ICMP probe returns `false`, TCP/445 probe returns `true` | Returns `true`; both probes invoked exactly once. |
| 6 | ICMP probe returns `false`, TCP/445 probe returns `false` | Returns `false`; both probes invoked exactly once. |
| 7 | ICMP probe throws (any exception), TCP/445 probe returns `true` | Returns `true`; the ICMP exception is swallowed, TCP is still attempted. |
| 8 | ICMP probe throws, TCP/445 probe throws | Returns `false`; never propagates either exception. |
| 8b | Hostname pass-through | The seam mock asserts the hostname argument it received equals the hostname argument the public method was called with — verbatim, no normalisation. Locks the contract that no transformation happens between the public surface and the seam. |
| 9 | ICMP probe seam invoked with `PingTimeoutMs` | The seam mock observes the `timeout` argument it was called with; the test asserts the value passed to the seam is exactly `PingTimeoutMs` (5000 ms). This is an **argument-value assertion**, not a wall-clock measurement. |
| 10 | TCP probe seam invoked with `TcpProbeTimeoutMs` and port 445 | Same — the seam mock asserts it was called with `port = 445` and `timeout = TcpProbeTimeoutMs` (5000 ms). Argument-value, not wall-clock. |
| 11 | Both seams invoked at most once per public call | The seams' invocation counts are asserted: ICMP exactly once for any non-empty input; TCP exactly once if and only if ICMP returned `false` or threw. |

These tests assert the *contract* the seam is called against, not real elapsed time. Wall-clock assertions are deliberately avoided because GitHub Actions Windows runners exhibit pause spikes of 1-3 s under load (GC, image pull, antivirus, shared-tenant jitter), which would make any millisecond-bound timing test flaky. The seam's *implementation* (when wired to the real `Ping` / `Socket` types) honours the timeout via the wrapping pattern in §2.1's "Timeout-bounding ownership" paragraph; that wrapping is exercised by S-005 / S-011 runtime verification, not by S-003's unit tests.

### 3.2 Unit tests for `CheckDatabaseConnectivityAsync`

| # | Scenario | Expected outcome |
|---|---|---|
| 1 | Server name is `null` or empty or whitespace | Returns `false`; no connection is opened. |
| 2 | Database name is `null` or empty or whitespace | Returns `false`; no connection is opened. |
| 3 | Connection-refused (e.g. unroutable IP / closed port) | Returns `false` within the 5s timeout; never throws. |
| 4 | Auth-fail (e.g. real SQL Server, wrong identity — only if the test environment has a SQL Server available; otherwise this test is skipped or covered by the seam) | Returns `false`; never throws. |
| 5 | Successful open (only if a real SQL Server is available — typically `localhost` / `master` against the test runner's local DB; otherwise covered by the seam) | Returns `true`; connection is closed by the `using` block. |

Tests 4 and 5 are environment-fragile in the same way the deleted `WithLocalhost_ReturnsTrue` was. The **default and preferred approach** is to **replace them with seam-mocked equivalents** by taking the §2.2 seam (analogous to §2.1's seam pattern). This is the only deterministic option that runs cleanly on CI without any conditional infrastructure.

The fallback option — running against a real local SQL Server — is acceptable **only** when the entire test class is marked `[Ignore("Requires local SQL Server — covered by seam-mocked replacement at <test name>")]`. The repo does not currently wire a `--filter TestCategory!=...` flag in CI, so `[TestCategory(...)]`-style filtering is **not** an option in this spec; using it would either run the test on CI (and hang on the 5 s timeout) or appear to skip silently with no observable signal. `[Ignore]` with a clear message is the only acceptable env-skip pattern.

The implementer's choice between seam-mocked and `[Ignore]` for tests 4-5 must be **documented in the test file** — either via a comment at the top of the test method, or via the seam class name being self-explanatory (e.g. `FakeDbProbe` / `StubSqlConnection`). A reviewer reading the test file alone must be able to tell which path was taken.

The CodeQL re-run on the post-edit tip must remain clean for `Dorc.Core/Connectivity/ConnectivityChecker.cs`. The CodeQL fix at commit `d3005f12` (use `SqlConnectionStringBuilder`) must remain in tree; the spec does not authorise its removal.

### 3.3 Build verification

- `dotnet build src/Dorc.Core/Dorc.Core.csproj` succeeds locally with no warnings or errors.
- `dotnet build src/Dorc.Core.Tests/Dorc.Core.Tests.csproj` succeeds locally with no warnings or errors.
- `dotnet test src/Dorc.Core.Tests/Dorc.Core.Tests.csproj --filter FullyQualifiedName~ConnectivityCheckerTests` passes locally (every test in §3.1 + §3.2 enumeration that is not env-skipped).
- After commit and push, CI on the new branch tip completes green — measured against the all-green baseline at `7bfee34e` recorded in DECISION-S-001 §8 (Test Results, CodeQL, build ×2, Analyze csharp ×2, Analyze js-ts ×2, Analyze actions, Aikido, Dependabot — 11/11 success). Acceptance bar: "no NEW failures vs that baseline".

### 3.4 No runtime / dry-deploy verification needed

S-003 is pure-logic. There is no DB / hosting surface. The unit tests in §3.1 + §3.2 plus the CI build are the entire verification protocol. S-005 (Timer-based hosted service) and S-011 (production verification) own the runtime exercise of the probe path.

---

## 4. Branch and commit strategy

- **Branch**: continue on PR #374's source branch `copilot/create-server-db-existence-check` (currently at `ca6bf954`). No new branch.
- **Commit 1 (production code)**: `connectivity(monitor): add TCP/445 fallback to ConnectivityChecker server probe (S-003)`. Body summarises the U-1 contract change and the seam choice taken.
- **Commit 2 (tests)**: `connectivity(monitor): add ConnectivityChecker unit tests for U-1 fallback matrix (S-003)`. Body lists the 10 server-probe + 5 DB-probe test cases.
- **Splitting** the production code from the tests is acceptable for review cognition; bundling into a single commit is **also** acceptable. Note that splitting means commit 1 lands a new probe path with no test coverage until commit 2 — fine for review, but a reviewer bisecting later sees a transient red commit. The implementer's choice; either is acceptable.

The seam approach taken (delegate / interface / virtual method per §2.1) is recorded in the body of commit 1's message so reviewers and future maintainers can find it without re-reading the spec.
- **No squash, no force-push.** No push to `main`.

---

## 5. Acceptance criteria

A1. `src/Dorc.Core/Connectivity/ConnectivityChecker.cs` at the post-edit tip implements the §2.1 behavioural contract: empty/null/whitespace → false; ICMP-success → true (no TCP); ICMP-fail → TCP/445 attempt; both fail → false; per-probe timeout = 5 s; total per-call worst case ≤ ~10 s.

A2. `src/Dorc.Core/Connectivity/IConnectivityChecker.cs` is **byte-identical** to the pre-edit version — no public API surface change.

A3. In `src/Dorc.Core/Connectivity/ConnectivityChecker.cs` at the post-edit tip, the DB connection string is constructed via `SqlConnectionStringBuilder` only — no string concatenation or interpolation introduces user-supplied values into the connection string (HLPS C8). This is verifiable by `grep` against the file content; the previous fix delivered by commit `d3005f12` is reflected by the file's content, not by a literal commit-presence check.

A4. `src/Dorc.Core.Tests/ConnectivityCheckerTests.cs` contains every scenario enumerated in §3.1 (tests 1–11) and §3.2 (tests 1–3 mandatory; tests 4–5 are present in the file in **one** of these two forms — seam-mocked replacement (default), or class-level `[Ignore("Requires local SQL Server — …")]`). The choice taken for tests 4–5 is documented in the file (comment at the top of the test method, or a self-explanatory seam class name). The previous environment-fragile `WithLocalhost_ReturnsTrue` and `WithInvalidServerName_ReturnsFalse` tests are removed; no other test class in `Dorc.Core.Tests` references these by name (verified at the time of S-003 execution).

A5. `dotnet test src/Dorc.Core.Tests/Dorc.Core.Tests.csproj --filter FullyQualifiedName~ConnectivityCheckerTests` runs locally on the agent's environment with **0 failures**; tests in §3.1 and the mandatory §3.2 tests 1–3 are not skipped.

A6. CI on the post-push tip completes green with **no NEW failures vs the DECISION-S-001 §8 baseline** (11/11 success at `7bfee34e` on Test Results, CodeQL, build ×2, Analyze csharp ×2, Analyze js-ts ×2, Analyze actions, Aikido, Dependabot). CodeQL on `ConnectivityChecker.cs` does not regress: cohort A (resource injection) remains absent (the `SqlConnectionStringBuilder` use per A3 prevents it from re-firing); cohort B (generic `catch`) may either auto-clear via the seam refactor or remain pinned, but **no new categories** of finding must appear.

A7. The spec passes Adversarial Review per CLAUDE.local.md §4 with unanimous approval before execution begins.

---

## 6. Out of scope

- The connectivity-check service that drives the probe (S-005 owns the cycle, the cancellation, the SanitizeForLog helper, and the `IHostedService`+Timer host).
- Persistent-source extensions (S-004 owns batched read + transition writes).
- API model / UI surface (S-006 / S-007).
- Per-host worst-case enforcement at the *cycle* level (S-005's responsibility — the probe enforces per-call ≤ 10s; the cycle scheduler is what bounds per-host within a 60-min cycle).
- DB probe behavioural change (the contract is already correct; only test coverage is added).
- Removal of the environment-fragile `WithLocalhost_ReturnsTrue` and `WithInvalidServerName_ReturnsFalse` tests as a standalone refactor — these are removed *in the same commit* that adds their replacements (A4); no separate "test cleanup" commit.
- **Cancellation semantics**: `OperationCanceledException` propagation behaviour through the probe is **deferred to S-005**, which owns cancellation token wiring through the cycle service. S-003's public surface does not yet accept a `CancellationToken`; if/when S-005 widens the contract, the cancellation-on-probe behaviour is decided there.

---

## 7. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| TCP/445 is firewall-blocked on a host where ICMP is also blocked, even though the host is up. | This is a known false-negative case (HLPS SC-9 documents it). S-003 does not solve it; S-010's docs (operator runbook) name it explicitly. The cost of adding a third probe (e.g. WS-Man / port 5985) is rejected per HLPS C6 (per-host ≤ 10s budget). |
| The seam abstraction (per §2.1) leaks into the public surface and accidentally widens `IConnectivityChecker`. | A2 is the gate — `IConnectivityChecker` must be byte-identical. Adversarial Review (A7) double-checks. |
| `Ping.SendPingAsync` on Windows requires admin in some runtime environments and silently throws. | The §2.1 step-3 catch-all (any exception during ICMP triggers TCP fallback) covers this. Test #7 in §3.1 is the assertion. |
| `Socket.ConnectAsync` with a timeout has known fragility around DNS resolution failures (the timeout may not bound DNS). | The implementer should compose the TCP probe such that DNS resolution *and* connect happen within the 5 s budget — typical pattern is `Task.WhenAny(ConnectAsync, Task.Delay(timeout))` then dispose on race-loss. The seam contract (§2.1) is what tests 9-11 in §3.1 lock down. |
| `Ping.SendPingAsync(host, timeout)` similarly does not bound DNS resolution — `Dns.GetHostAddresses` runs synchronously before the timeout-bounded ICMP send, and on a slow DNS server can run for many seconds (Windows default DNS query timeout is 15 s with retries). | Same wrapping pattern as the TCP probe — the implementer wraps the entire ICMP probe (DNS + send) in `Task.WhenAny(probe, Task.Delay(PingTimeoutMs))` so the seam call is bounded by `PingTimeoutMs` regardless of where the time was spent. §2.1 "Timeout-bounding ownership" paragraph states this contract. |
| Mocking `System.Net.NetworkInformation.Ping` is non-trivial because the type isn't `virtual`-friendly. | The seam pattern from §2.1 (delegate or interface injection at construction) avoids needing to mock `Ping` itself; the implementer mocks the *seam*, not `Ping`. |
| The S-005 step (already-DECISION-S-001-flagged) eventually replaces `ConnectivityCheckService.cs` from BackgroundService to Timer; the probe code in `ConnectivityChecker.cs` is consumed by both. | S-003's contract is shape-stable — `IConnectivityChecker` doesn't change — so S-005's host change doesn't ripple back into S-003. |
| Cohort-B github-code-quality threads on `ConnectivityChecker.cs` (Q6 of DECISION-S-001) flag the existing generic `catch` clauses. The seam refactor may or may not clear them. | If the refactor naturally narrows the catch (e.g. catches only `PingException` / `SocketException`) the threads auto-clear. If not, A6's "no new findings" bar still holds — the existing threads remain pinned. |

---

## 8. Pre-execution self-audit (to be confirmed before code edit)

- [ ] HLPS status APPROVED + user-approved
- [ ] IS status APPROVED + user-approved
- [ ] DECISION-S-001 status APPROVED + user-approved
- [ ] SPEC-S-002 status APPROVED + user-approved (S-002 source-edit landed at `ca6bf954`; dry-deploy may still be pending — that does NOT block S-003 because S-003 has no schema dependency)
- [ ] This SPEC status APPROVED + user-approved
- [ ] PR #374 branch tip is at or after `ca6bf954` (or the agent records the new SHA and verifies items below against that SHA instead)
- [ ] No edits to `src/Dorc.Core/Connectivity/ConnectivityChecker.cs` since `ca6bf954` (verify via `git log ca6bf954..HEAD -- src/Dorc.Core/Connectivity/ConnectivityChecker.cs` returning empty)
- [ ] No edits to `src/Dorc.Core/Connectivity/IConnectivityChecker.cs` since `ca6bf954` (interface-stability precondition)
- [ ] No edits to `src/Dorc.Core.Tests/ConnectivityCheckerTests.cs` since `ca6bf954`
- [ ] CI on the current PR #374 branch tip is green (re-fetch via `gh api` if more than a few hours have elapsed since the last check)
- [ ] No in-flight adversarial reviews on connectivity-monitoring artifacts
- [ ] Working tree clean (no uncommitted tracked changes)
- [ ] Seam approach for §2.1 has been chosen (delegate / interface / virtual method) and will be recorded in commit 1's message body per §4

If any item is unchecked, **STOP** and address it.

---

## 9. Adversarial Review

S-003 is reviewed by an Adversarial panel of 2-3 reviewers per CLAUDE.local.md §4. Suggested lenses:

- **Clarity / completeness**: is the §2.1 behavioural contract unambiguous? Could two implementers produce materially different probe behaviour from the same description? Is the §3 test matrix exhaustive against the §2.1 contract?
- **Risk / feasibility**: is the seam abstraction realistic given the existing `Ping` / `Socket` types? Are the §3.1 timing assertions (tests 9, 10) achievable without flakiness under CI load? Is the env-skipping protocol for §3.2 tests 4-5 realistic?
- **Evidence rigour**: do the §3 acceptance bars map cleanly to A1-A6? Does CodeQL discharge in A6 actually verify the post-edit state?

Reviewers must NOT evaluate pseudocode for syntactic correctness (the spec uses plain-language behavioural contracts per CLAUDE.local.md JIT-spec abstraction rules) and must NOT prescribe the seam shape — that is the implementer's choice per §2.1.

Findings follow the same triage rules as other artifacts (Accept / Downgrade / Defer / Reject; cycle limit 3 rounds).

---

## 10. Review History

### R1 — DRAFT → REVISION

R1 conducted by three reviewers in parallel (clarity/completeness, risk/feasibility, evidence rigour). All three returned `APPROVE_WITH_FIXES`. Combined triage:

| Theme | Reviewers | Severity | Disposition | Resolution |
|---|---|---|---|---|
| `Ping.SendPingAsync` does not bound DNS resolution; total ICMP time can exceed `PingTimeoutMs` on slow DNS | B (F-B1) | HIGH | Accept | §2.1 added "Timeout-bounding ownership" paragraph stating the per-probe ≤ 5 s budget is a caller-side guarantee implemented via `Task.WhenAny(probe, Task.Delay(timeout))` wrapping. New §7 risk row added for ICMP DNS unboundedness mirroring the TCP one. |
| ICMP timeout ownership ambiguous (seam vs caller) | A (F-A1) | MEDIUM | Accept | Resolved alongside F-B1: the seam call is always bounded by its declared timeout; this is the seam contract that tests 9-11 lock down. |
| TCP "completes" semantics — does RST count as reachable? | A (F-A2) | MEDIUM | Accept | §2.1 step 3 clarified: "established (the OS reports a completed three-way handshake)" — RST counts as TCP failure. §2.1 step 4 explicitly enumerates RST/`SocketException("Connection refused")` as a TCP failure. The known false-negative case (host alive but RSTing port 445) is documented per HLPS SC-9 and S-010. |
| Tests 9/10 wall-clock assertions flaky on CI | B (F-B2), A (F-A3) | MEDIUM | Accept | §3.1 tests 9/10 restructured to assert the seam observed the **timeout argument value**, not wall-clock elapsed time. New test 11 added for invocation-count assertions. Wall-clock testing of the actual `Ping`/`Socket` wrapping is the responsibility of S-005/S-011 runtime verification, not S-003 unit tests. |
| `[TestCategory]` env-skip not wired in CI | B (F-B3) | MEDIUM | Accept | §3.2 reworded to make seam-mocked replacement the default for tests 4-5; the only acceptable env-skip pattern is class-level `[Ignore]` with a clear message. `[TestCategory(...)]` is explicitly disallowed because CI does not filter on it. |
| A3 over-claims commit-presence vs effect | C (E-1) | MEDIUM | Accept | A3 reworded to be effect-based ("DB connection string is constructed via `SqlConnectionStringBuilder` only — no string concatenation … verifiable by `grep`"). Commit `d3005f12` referenced as historical context only. |
| A6 baseline phrasing inconsistent with §3.3 | C (E-2) | MEDIUM | Accept | A6 reworded to "no NEW failures vs the DECISION-S-001 §8 baseline (11/11 success at `7bfee34e` …)" matching §3.3. |
| Hostname pass-through not asserted | A (F-A4) | LOW | Accept | New test 8b added in §3.1: seam mock asserts hostname argument equals public-method argument verbatim. |
| §6 server×db null short-circuit ordering | A (F-A5) | LOW | Defer to Delivery | Public contract is satisfied either way; the test author may lock the order if it adds value. Not blocking. |
| §8 self-audit needs "seam choice recorded" item | A (F-A6) | LOW | Accept | New §8 audit item: "Seam approach for §2.1 has been chosen and will be recorded in commit 1's message body per §4". |
| `OperationCanceledException` propagation semantics | A (F-A7) | LOW | Defer to S-005 | §6 Out of scope gains a "Cancellation semantics" bullet stating the question is decided in S-005 when the public surface widens to accept a `CancellationToken`. |
| §4 commit-split cost note | B (F-B4) | LOW | Accept | §4 wording softened: split commit 1 lands new probe path with no test coverage until commit 2 — fine for review, transient red commit for bisecting. Either pattern acceptable. Plus: §4 notes the seam approach is recorded in commit 1's message body. |
| A4 three-way OR with no objective tie-breaker | C (E-4) | LOW | Accept | A4 reworded: tests 4-5 are present in the file in **one** of two forms (seam-mocked default, or class-level `[Ignore]`); the choice is documented in the file. |
| Blast-radius confirmation for env-fragile test removal | B (F-B5) | LOW | Accept | A4 gains "verified at the time of S-003 execution" that no other test class references the deleted test names. |
| A5 has no failure threshold | C (E-3) | LOW | Defer to Delivery | A5 reworded to "0 failures; not skipped" which is the binary threshold. The "≥ N tests executed" lower bound is delivery detail. |

After this revision, status returns to `IN REVIEW` for R2.

### R2 — IN REVIEW → (pending)

(R2 to be added after resubmission)
