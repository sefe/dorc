# IS: Codebase Review Remediation — Implementation Sequence

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | REVISION (round-1 adversarial review applied)    |
| **Author**  | Agent                                            |
| **Date**    | 2026-07-05                                        |
| **HLPS**    | HLPS-codebase-review-remediation.md (REVISION)   |
| **Folder**  | docs/codebase-review-remediation/                |

This IS orders the remediation into atomic, independently-reviewable steps. Steps are grouped into four release tiers by risk. Within a tier, steps may proceed in parallel unless a dependency is noted. Each step gets a JIT Spec (`SPEC-S-00X-<title>.md`) authored immediately before execution — no method signatures or line numbers are fixed here.

**Blocking gates — single source of truth is the "Depends On / Gate" column of the Step Index below.** As of this revision the gated steps are: **S-014** (U-6 HA topology, U-7 idempotency), **S-017** (U-4 cert provisioning), **S-020** (U-5 EnsureCreated dependency), and **S-024 rotation only** (U-3 live-key status). S-001 (U-1) and S-002 (U-2) are now ungated — their unknowns are RESOLVED. JIT-spec authoring for a gated step must not begin until its unknown is closed.

> **Revision note (2026-07-05, post round-1 adversarial gate).** This IS was revised after a three-reviewer panel (coverage, sequencing/risk, solution-correctness). Material corrections: S-010 exit-code handling made command-aware (exit 2 under `-detailed-exitcode` is success-with-changes, not failure); S-010+S-012 declared a paired release; S-014 re-scoped around environment-lock probing and reconciled with the already-delivered monitor-robustness S-004; S-004 account model corrected (env-specific deploy account + Monitor's real service identity, not literal SYSTEM); S-002 sandbox mechanism corrected and a safe-expression-evaluator option added; S-006 reframed (runner writes status directly to the DB — it does **not** call these endpoints; `DeployApiClient` is dead code); S-011 transient-exception set enumerated; C-7 busy-loop split into an expedited hotfix (S-012a); G-1 XSS promoted; A-4 given a Tier-1 verification spike (S-000).

---

## Step Index

| ID    | Title | Addresses | Tier | Depends On / Gate |
|-------|-------|-----------|------|-------------------|
| S-000 | **Spike:** verify server-side enforcement behind client-gated destructive actions | A-4 | 1 | — (spike; may spawn a Tier-1 fix) |
| S-001 | Enforce Terraform plan authorization | A-1 | 1 | U-1 RESOLVED |
| S-002 | Sandbox/replace & permission-gate the `fn:` C# evaluator | B-2 | 1 | U-2 RESOLVED; Q-2a audit gates design |
| S-003 | Harden SQL login reset (random password, safe DDL) | B-1 | 1 | — |
| S-004 | Secure the runner named-pipe channel | C-1 | 1 | — |
| S-005 | Restrictive DACL on the runner process | C-2 | 1 | Sibling of S-004 (same handshake path) |
| S-006 | Authorize log ingest/read endpoints | A-2 | 1 | — |
| S-007 | Stop leaking exception detail in HTTP responses | E-1, E-5 | 1 | — |
| S-008 | Keep decrypted secrets out of logs/stdout/OpenSearch | D-2 (logs), D-3 | 1 | D-2 on-disk part closed by S-010 |
| S-009 | Legacy encryptor fails loudly + undecryptable-value audit | D-1 | 1 | — |
| S-012a | **Hotfix:** iteration-delay floor + add key to `appsettings.json` | C-7 | 1 | — (split from S-012) |
| S-021 | Eliminate stored XSS in web renderers | G-1 | 1 | — (promoted from Tier 3) |
| S-010 | Correct Terraform failure & cancellation semantics | C-3, C-4, C-11 | 2 | **Paired release with S-012** |
| S-011 | Keep the monitor service alive on transient errors | C-5 | 2 | — |
| S-012 | Timeouts & cancellation-liveness in the runner pipeline | C-6 | 2 | **Paired release with S-010** |
| S-013 | Fail-closed component dispatch & correct Win32 error checks | C-9, C-10 | 2 | — |
| S-014 | HA-safe startup recovery & identity-checked process kill | C-8 | 2 | Gate: U-6, U-7 (both blocking) |
| S-015 | Convert swallow-and-succeed catches into logged failures | E-2, E-3 | 2 | — |
| S-016 | Authorize sensitive read endpoints & confirm client-gate backing | A-3, A-5 (+A-4 fix if S-000 finds a gap) | 3 | Informed by S-000 |
| S-017 | Fix certificate-validation bypasses | D-4, D-7, D-8 | 3 | Gate: U-4 |
| S-018 | Remove sync-over-async and async-void on request paths | E-4, E-6 | 3 | — |
| S-019 | Server-side data-access correctness (deny logic, no whole-table loads) | F-1, F-2 | 3 | — |
| S-020 | Set-based permission queries & remove `EnsureCreated` | F-3, F-4 | 3 | Gate: U-5 |
| S-022 | Correct SignalR connection lifecycle | G-2 | 3 | — |
| S-023 | Restore generated-client workflow & align generator versions | G-3, U-8 | 3 | — |
| S-024 | Remove committed private keys; re-source certs at deploy | D-5 | 4 | Gate: U-3 for rotation only |
| S-025 | LDAP SID filter correctness | B-3 | 4 | — |
| S-026 | Runner lifecycle & logging hygiene | C-12, C-13, C-14 | 4 | — |
| S-027 | OnePassword HttpClient via factory; concurrent-safe WinAuth logging | D-6, X-1 | 4 | — |
| S-028 | Frontend hygiene (per-finding checks G-4..G-10) | G-4..G-10 | 4 | — |
| S-029 | DB migration & lookup hygiene | F-5, F-6 | 4 | — |

---

## Tier 1 — Stop active exploitation paths

### S-000 — Spike: verify server-side enforcement behind client-gated destructive actions
**What changes.** Time-boxed investigation (not a code change unless it finds a gap): confirm whether `refDataEnvironmentsDelete` and the SQL password-reset endpoints enforce authorization **server-side**, or whether the only gate is the client-side `isAdmin`/`isEnvOwner` disabling in `env-control-center.ts`. 
**Why.** A-4 — if server-side enforcement is absent, any authenticated user can delete environments or reset SQL passwords by calling the API directly; that is a Tier-1-grade authz hole, not the Tier-3 MEDIUM it is currently filed as. This spike closes the unknown before it is mis-prioritised.
**Outcome.** If enforcement exists → record it, A-4 stays a Tier-3 confirmation under S-016. If it is missing → raise a new blocking finding and pull the fix into Tier 1 immediately.
**Verification intent.** A definitive yes/no, per endpoint, on server-side authorization, with the enforcing code cited.

### S-001 — Enforce Terraform plan authorization
**What changes.** Replace the three `return true` stubs in `TerraformController` with real checks using the existing security infrastructure (`ISecurityPrivilegesChecker` / `CanModifyEnvironment`, as used by the request-lifecycle endpoints): view requires read access to the deployment's environment; confirm and decline require modify rights on that environment. (U-1 RESOLVED — mirror `CanModifyEnvironment`; no separate owner-only tier.)
**Why.** A-1 — any authenticated user can currently confirm/decline/read any Terraform plan and trigger infrastructure changes.
**Verification intent.** A user without rights on the target environment receives 403 on view/confirm/decline; a user with modify rights succeeds; existing confirm→Monitor-execute flow is unchanged for authorised users.

> **Implementation note (2026-07-05).** Confirm/decline gate on `CanModifyEnvironment` (env resolved from the deployment result's owning request via `GetRequestForUser`). **View is gated at the same modify level** because the ACL model exposes no distinct read tier (`AccessLevel` = None/Write/ReadSecrets/Owner) and the plan content describes infrastructure changes — gating view at modify fails closed for sensitive content. Resolves fail-closed when the owning environment cannot be determined. Also fixed a latent bug the stubs masked: the denial paths used `Forbid("message")`, which ASP.NET Core treats as an auth *scheme* name (would throw); switched to `StatusCode(403, message)`. Delivered with `TerraformControllerTests` (5 cases).

### S-002 — Sandbox/replace & permission-gate the `fn:` C# evaluator
**What changes.** The capability stays (U-2 RESOLVED — actively used, required), but the implementation is reworked. **The design decision is gated on the Q-2a usage audit** (see below), because the two viable directions differ fundamentally:

- **Direction A (preferred if the audit permits) — replace `CSharpScript` with a safe expression evaluator.** If the audit shows real `fn:` values are arithmetic/string/date-shaped, replace the C# scripting engine with a dedicated expression evaluator that *cannot resolve arbitrary .NET types at all*. This **eliminates the RCE class by construction** rather than mitigating it. Note `CLAUDE.md` restricts functional-programming/language-extension libraries without dev-manager approval; a plain arithmetic/string expression evaluator is not such a library, but confirm the specific library choice with the dev manager.
- **Direction B (only if the audit shows genuine C# is required) — contain `CSharpScript`.** **Correction to the prior draft:** restricting `ScriptOptions.Imports`/`WithReferences` is **not** a security boundary — a script with zero imports can still call fully-qualified `System.IO.File.ReadAllText(...)` or reach any type via reflection off `System.Private.CoreLib`, which cannot be removed. Imports-restriction is only a usability/typo guard. Genuine containment for Direction B requires running the evaluator **out-of-process under a low-privilege/restricted token** with a hard timeout. (`.NET` AppDomain isolation and CAS were removed in .NET Core/5+ and are **not** a boundary — do not rely on them.)

Common to both directions:
1. **Permission-gate authorship** — only users with the appropriate property-edit permission may create/modify a value containing `fn:` (reuse existing property ACLs; clear error otherwise). Under Direction B this is the *primary* control, since containment is imperfect.
2. **Hard evaluation timeout** — bound runaway/infinite expressions (a no-timeout `.Result` today is also a DoS vector).
3. **Fix `.Result` and the cache** — replace blocking `.Result` with proper async, and replace the process-global static cache keyed only on expression text with a correctly-scoped cache (or remove caching if cheap), so a value computed in one context is not returned in another.

**Residual risk (Direction B only).** `CSharpScript` in-process is not a boundary against a determined authorised author; the permission gate is the real control and must be documented as such. Direction A has no such residual RCE risk.
**Open sub-questions (JIT spec):** Q-2a — audit existing `fn:` property values in the DB: are they arithmetic/string/date-shaped (→ Direction A) or do any require arbitrary C# (→ Direction B)? **This audit gates the design.** Q-2b — which property permission gates authorship, and is there a migration concern for existing `fn:` values authored by users who would fail the new check? Q-2c — for Direction B, confirm out-of-process host + token restriction is acceptable operationally.
**Why.** B-2 — arbitrary code execution during variable resolution; no-timeout blocking `.Result` (DoS); cross-context cache bleed.
**Verification intent.** An `fn:` expression attempting file/process/network/reflection access fails (Direction A: cannot even express it; Direction B: blocked by the out-of-process token and returns an error); a legitimate arithmetic/string `fn:` expression still evaluates correctly; an unauthorised user cannot save an `fn:` value; a runaway expression is terminated by the timeout; identical expression text in two contexts no longer returns a stale cached result.

### S-003 — Harden SQL login reset
**What changes.** Primary guarantee: generate a strong random password from a **quote-free alphabet** (never the username) — this removes the string-literal injection surface regardless of how the DDL is built, because there is no quote to break out with. Belt-and-suspenders: build the `ALTER LOGIN` login *name* safely via `QUOTENAME`/`]`-doubling (the login name can **never** be a bound parameter, so it must be quoted, not parameterised) and tighten the identifier allow-list to exclude quote characters. **Do not depend on parameterising the PASSWORD clause** — `ALTER LOGIN … WITH PASSWORD = @p` is grammar-sensitive and not reliably accepted as a bound parameter; the generated quote-free password is what makes it safe. Construct the connection with `SqlConnectionStringBuilder` (not concatenation) and validate `targetDbServer` against a known-server source.
**Open sub-question (JIT spec):** does a canonical known-server list/config source exist to validate `targetDbServer` against, or must one be introduced?
**Why.** B-1 — T-SQL injection via a quote-permitting regex plus a predictable credential (password == username).

> **Deviation recorded during implementation (2026-07-05).** The "generate a random password" direction was **dropped** after discovering the UI (`reset-app-password-behalf.ts:154`) explicitly tells the user the password is deliberately reset to equal their login name ("it is now set as the same as your … login name, you will need to login without encryption the first time"). Password==username is therefore *intended, documented reset-to-default* behaviour, not the defect — the defect is the injection. Implemented fix: tightened allow-list excludes `'` and `[`/`]`; login name embedded via an escaped quoted identifier and escaped literal; connection built with `SqlConnectionStringBuilder`. `targetDbServer` originates from the databases table (`db.ServerName`), not direct user input, and the builder closes the connection-string-injection vector. `TrustServerCertificate=true` retained here (its removal is S-017, gated on U-4). Delivered with unit tests (`SqlUserPasswordResetTests`).
**Verification intent.** A username containing `'` is rejected by the tightened allow-list; the resulting login password is random, quote-free, and returned/stored via the existing secure channel, never equal to the username; an unknown `targetDbServer` is rejected; the connection string is built via the builder.

### S-004 — Secure the runner named-pipe channel
**What changes.** Create the `NamedPipeServerStream` with a `PipeSecurity` ACL granting access to only two principals: **the specific environment deploy account that launched this runner** (Prod vs NonProd — the same `processAccountName`/`domainName` used for the `LogonUser`/`DuplicateTokenEx` in `ProcessSecurityContextBuilder`, *not* a generic account) and **the Monitor service's actual run-as identity** (set at install time — commonly a domain service account, **not** necessarily `SYSTEM`; do not hardcode SYSTEM). Observe the pipe-server task so its failure fails the dispatch instead of silently proceeding.
**Pipe-name change (if adopted).** A non-guessable name is **not** self-contained: three runner entrypoints parse the `requestId` back out of the pipe name (`Dorc.Runner/Program.cs`, `Dorc.NetFramework.Runner/Program.cs`, `Dorc.TerraformRunner/Program.cs`) and the log filename is derived from it (`ScriptDispatcher`, `TerraformDispatcher`). If randomising, keep a parseable suffix (e.g. `DOrc-{guid}-{requestId}`) and update every parser/log-path site in the same step. Given the ACL already closes the squatting vector, treat the rename as optional defence-in-depth.
**Why.** C-1 — pipe-squatting RCE as the deploy account and secret theft of the serialized `ScriptGroup`.
**Testing.** Integration-level (real pipe + real account tokens) — mis-specifying the account model would break every deployment handshake, so this must be exercised end-to-end.
**Verification intent.** A process outside the allowed principals cannot connect to or pre-create the pipe; the legitimate runner (as its env deploy account) and the Monitor still complete the handshake; if pipe creation fails, the dispatch fails rather than launching the runner against a foreign pipe.

### S-005 — Restrictive DACL on the runner process
**What changes.** Replace the NULL DACL (`IntPtr.Zero`) in `ProcessSecurityContextBuilder` with an explicit DACL granting only the necessary principals; stop marking the handle inheritable unless required.
**Why.** C-2 — local privilege escalation to the deploy account via the fully-accessible process object.
**Verification intent.** The created runner process object denies `PROCESS_ALL_ACCESS` to non-privileged local users; deployment still runs as the correct account.

### S-006 — Authorize log ingest/read endpoints
**What changes.** Add ownership/role checks to `RequestStatusesController.Patch`, `Post("RawLog")`, and `GetLog`, keyed on the resource being mutated: `Patch` mutates by `deploymentResultId` (it currently **ignores** the `requestId` route value), so authorization must be resolved from the **`deploymentResultId`'s owning request → environment**, not from the caller-supplied `requestId` — otherwise a caller pairs a `requestId` they own with a `deploymentResultId` they don't and the check passes. Reuse the `GetRequestForUser`/`CanModifyEnvironment` pattern the neighbouring `Get` uses. Validate/normalise the UNC path target in `Post("RawLog")`.
**Note (correction to prior draft).** These endpoints are **not** on the runner→API path: the Monitor/runner write status transitions and the UNC log path **directly to the database** (`DeploymentRequestStateProcessor`, `ScriptDispatcher`, `TerraformDispatcher`) and logs go to OpenSearch. `Dorc.Api.Client/DeployApiClient` looks like a callback client but is **dead code** (no `.csproj` references it, zero callers). So there is no runner-principal to preserve — the consumers to protect are **browser/UI users**. This de-risks the step (no machine-caller break), but the JIT spec should still confirm no other caller depends on the current open behaviour.
**Why.** A-2 — log tampering and UNC path injection by any authenticated user; plus a cross-request IDOR if the check is bolted onto the wrong id.
**Verification intent.** A user cannot append to, repoint, or read the log of a request they lack rights to; a caller supplying a mismatched (owned `requestId`, foreign `deploymentResultId`) pair is rejected; a legitimate UI user with rights still succeeds.

### S-007 — Stop leaking exception detail in HTTP responses
**What changes.** Remove the `Exception` `JsonConverter` registration (or restrict it to the Development environment); replace per-action `StatusCode(500, e)` returns with sanitized `ProblemDetails` via the existing `DefaultExceptionHandler`; return 400 for the validation failures currently returned as 500/`HttpResponseMessage`; guard the NRE sources (null project, null query param, null inner exception).
**Why.** E-1, E-5 — stack traces, internal types, and possibly connection strings returned to callers.
**Verification intent.** A handled exception returns a sanitized message with no stack/type/inner-chain; validation failures return 400; the previously NRE-throwing inputs return a clean 4xx.

### S-008 — Keep decrypted secrets out of logs/stdout/OpenSearch
**What changes.** In both PowerShell runners, stop serializing property values on `SetVariable` failure (log the property name/type only); in the Terraform runner, avoid logging raw plan/apply output that can echo secrets (mask known secret values or suppress), and ensure secret handling in tfvars is addressed together with cleanup in S-010.
**Why.** D-2, D-3 — decrypted secrets written to runner logs, stdout, and OpenSearch.
**Verification intent.** A forced `SetVariable` failure logs no secret value; Terraform output containing a known secret is masked/omitted in the log sink.

### S-009 — Legacy encryptor fails loudly + undecryptable-value audit
**What changes.** Remove the `catch (Exception) { _provider = Aes.Create(); }` random-key fallback in `PropertyEncryptor`; a bad **global** key/IV must throw a clear configuration error **at DI construction / startup** (fail-fast), not mid-deployment. Do not change the legacy algorithm itself (C-03) — only the fallback. **Companion action (promoted from out-of-scope):** because this change turns values previously "working" via the random-key fallback into hard read-time failures, extend `Tools.EncryptionMigrationCLI` with a **dry-run scan** that reports undecryptable values so operators can remediate *before* they surface during a deployment. Ensure a single corrupt legacy *value* fails its own request cleanly and does not propagate into the monitor loop (interacts with S-011). Document the fixed-IV/CBC limitation and confirm AES-GCM v2 is the write path.
**Why.** D-1 — silent random-key substitution corrupts secrets and masks misconfiguration; fail-loud must not become a surprise mid-deploy outage.
**Verification intent.** A malformed global key/IV throws at startup, not mid-run; a single undecryptable value fails only its own request; the CLI dry-run lists affected values; genuinely-valid legacy data still decrypts; new writes use v2.

### S-012a — Hotfix: iteration-delay floor + config key
**What changes.** Small, low-risk, expedited fix split out of S-012: guard the `int.TryParse` in `MonitorConfiguration` so a parse failure does not overwrite the default with 0 (the `out` parameter is set to 0 on failure — clamp to a safe minimum floor instead), and add the `requestProcessingIterationDelayMs` key to the shipped `appsettings.json`.
**Why.** C-7 — this is an **active** production degradation *today* (0-delay busy loop with a forced full GC every iteration), not latent; it should not wait behind the larger, riskier timeout rework in S-012.
**Verification intent.** A missing/invalid delay key yields the floor value, not 0; the shipped config contains the key.

### S-021 — Eliminate stored XSS in web renderers *(promoted to Tier 1)*
**What changes.** Replace the `root.innerHTML = ...` renderers (listed in G-1) with `render(html\`...\`, root)` using Lit's auto-escaping `html` — the correct pattern for these Vaadin imperative-root renderers, which must mutate `root` and cannot return a template. **Explicitly forbid `unsafeHTML`.** Preserve any follow-on imperative DOM work these renderers do after setting content (e.g. `page-scripts-list.ts` calls `root.querySelector('hegs-json-viewer').expand('**')` after rendering — `render()` is synchronous so the query still resolves).
**Why.** G-1 — stored XSS executing in an authenticated **admin** session is an active exploitation path to full admin capability, the same category Tier 1 targets; it should not sit ~20 steps deep.
**Verification intent.** A value containing `<img src=x onerror=...>` renders as text, not executable markup, across every affected grid/combo-box; the post-render imperative work (e.g. json-viewer expand) still functions.

---

## Tier 2 — Deployment-integrity correctness

> **S-010 and S-012 are a paired release.** They edit the same dispatcher region and are complementary: kill-on-cancel (S-010) without a wait timeout (S-012) still hangs forever if the runner *hangs* (the token never fires); a wait timeout (S-012) without process-tree kill (S-010) abandons the wait and orphans a live `terraform apply`. Ship them together, mirroring the monitor-robustness S-003/S-004 coupling.

### S-010 — Correct Terraform failure & cancellation semantics
**What changes.**
1. **Command-aware exit codes (correction to prior draft).** The `plan` command runs with `-detailed-exitcode`, under which **0 = no changes, 1 = error, 2 = success *with* changes** — exit 2 is the normal case for any real deployment. Do **not** treat "any nonzero" as failure (that would mark essentially every changed plan Failed — a self-inflicted outage). Interpret per command: for `plan -detailed-exitcode`, success = {0, 2}, failure = {1}; for `init`/`apply`/`show` (no `-detailed-exitcode`), success = 0, failure = any nonzero. The current bug (C-3) is specifically that the **apply** path only fails on exit 1.
2. **Real cancellation + process-tree kill (scope correction).** There is **no job object anywhere in the codebase today** — `ScriptDispatcher` uses `cancellationToken.Register(() => process.Kill())`, and `RunnerProcess.Kill()` calls `TerminateProcess` on the **runner handle only**, which does not kill the spawned `terraform.exe` child. So this is **new work**, not reuse: thread a real `CancellationToken` from the dispatcher through the runner (today it passes `CancellationToken.None`) and introduce genuine process-tree termination (Windows Job Object or equivalent) so cancelling kills `terraform` itself, not just the runner.
3. Drain stdout / `WaitForExit` before reading captured output (C-11 truncated approval plan).
4. Delete the temp tfvars working directory in a `finally` on every exit path (closes the on-disk half of D-2 — currently cleaned only on apply-success).
**Why.** C-3, C-4, C-11, D-2(on-disk).
**Testing.** Integration-level; must include an **exit-2 plan-with-changes** case and a **cancel-mid-apply** case that asserts `terraform` is actually gone.
**Verification intent.** A plan with pending changes (exit 2) is **not** marked Failed; an apply crash (exit ≠ 0) **is** marked Failed; cancelling mid-apply terminates the `terraform` process tree and removes the temp dir; the confirmation plan is complete; no tfvars remain after any exit path.

### S-011 — Keep the monitor service alive on transient errors
**What changes.** Do **not** implement a blanket `catch (Exception) { log; continue; }` (that would swallow NREs from real defects, OOM, and misconfiguration and spin forever). Instead: (a) enumerate the **exact transient set** to tolerate — `SqlException`, `RetryLimitExceededException` wrapping a transient `SqlException`, and transient EF/timeout exceptions — logging and continuing for those; (b) keep unknown exceptions fatal, **or** add a consecutive-failure circuit breaker plus an unhealthy health signal so sustained failure triggers an orchestrated restart rather than a silent spin; (c) specify the backoff (capped/exponential), not just "bounded". Place per-iteration transient handling in the **inner** loop (`DeploymentEngine.ProcessDeploymentRequestsAsync`) so in-flight `_runningTasks` state is not lost.
**Why.** C-5 — a single non-`SqlException` (e.g. retry-exhausted wrapper) stops the whole deployment monitor.
**Verification intent.** A simulated retry-exhausted DB error logs and the loop continues with backoff; an unexpected exception either stays fatal or trips the circuit breaker and health signal; no tight spin on repeated failure.

### S-012 — Timeouts & cancellation-liveness in the runner pipeline
**What changes.** Add finite timeouts to pipe `Connect`, `WaitOne`, and process wait paths; when a wait times out, fail the request cleanly and release the slot; decouple cancellation processing from the `Task.WhenAny` concurrency backpressure so cancellations are honoured even when all `MaxConcurrentDeployments` slots are occupied. (The iteration-delay floor / config key formerly bundled here is now the expedited S-012a.)
**Why.** C-6 — wedged runners block environments ~48h and, once all slots hang, lock out cancellation processing entirely.
**Paired with S-010** (see note above).
**Verification intent.** A non-connecting/hanging runner times out and frees its slot; a cancellation is processed while all slots are busy.

### S-013 — Fail-closed component dispatch & correct Win32 error checks
**What changes.** Make the `ComponentProcessor` switch fail closed — an unknown `ComponentType` sets Failed, not `StatusNotSet`; reset `Marshal.GetLastWin32Error()` context so success paths are not misread as failures (call `SetLastError(0)` before the P/Invoke or check the managed return value instead of the stale last-error).
**Why.** C-9, C-10 — unknown component silently "succeeds"; stale last-error marks successful deployments Failed.
**Verification intent.** An unknown component type marks the request Failed; a successful runner start is not misreported as Failed due to a leftover error code.

### S-014 — HA-safe startup recovery & identity-checked process kill
**What changes.** **This step modifies behaviour that monitor-robustness S-004 deliberately delivered and that passed a prior adversarial gate** (the unconditional `Running → Pending` resume, justified by idempotency U-5 and a single-active assumption). It must be reconciled with that design, not simply reverted — the `DeploymentEngine` graceful-shutdown path explicitly relies on startup resume recovering Running requests.
- **Mechanism (correction to prior draft).** The only lock granularity is per-**environment** (`env:{name}`); there is **no persisted per-request ownership**, and at startup a node holds **no** locks. So "requests this node owns via the lock" is the wrong framing. The sound mechanism: before resuming a Running request for environment E, **probe (TryAcquire) E's environment lock**. The lock is held for the full deployment duration, so a successful acquire proves no live peer is mid-deployment on E → safe to resume; a failed acquire means a peer is live → leave the request alone. This is a **structural change** (async, lock-service-dependent, moved into the lock path), not a filter tweak on the existing synchronous `CancelStaleRequests`.
- **Crashed-node recovery must be preserved.** A crashed node's requests are owned by a now-dead node; the environment lock will be unheld/expired, so the probe acquires and the request resumes — this keeps the S-004 crash-recovery guarantee. Do **not** implement a scheme that only resumes "this node's" requests (that would strand crashed-node work in Running forever — the exact regression S-004 fixed).
- **Startup lock-not-yet-released race.** After a same-node crash the broker may still hold the dead consumer's lock until the consumer-timeout elapses, so a one-shot startup probe may skip resuming the node's own stale request (fails safe — no double-deploy — but strands it). Resume must therefore be **retried on later loop cycles**, not only once at startup, and must handle **multiple** Running requests per environment.
- **PID kill.** Before killing a persisted PID, verify process identity (image name + start time) to avoid killing a reused PID.
**Why.** C-8 — double-deploy in HA and killing an unrelated reused-PID process.
**Gate.** **U-6 (HA topology) and U-7 (idempotency of re-run) are both blocking** — U-7 is load-bearing because resume re-runs an interrupted deployment; re-confirm it before shipping.
**Testing.** Integration-level, including a **crashed-node-owner** case (dead peer's request still resumes) and a **live-peer** case (request is left alone).
**Verification intent.** A second node restarting while a peer deploys does not re-queue that request; a request whose owning node has crashed is resumed on this or a later cycle; a stale PID reused by another process is not killed.

### S-015 — Convert swallow-and-succeed catches into logged failures
**What changes.** `EnvironmentsPersistentSource` attach/detach return a real success/failure signal instead of an empty non-null model; `PropertyValues`/`ConfigValues` removes distinguish "not found" from "error" and log the exception; `PropertyEvaluator`/`VariableResolver` surface resolution errors instead of returning the unresolved token or null (and guard the null-value NRE).
**Why.** E-2, E-3 — failures reported as success; unresolved `$(token)` deployed as config.
**Verification intent.** A forced DB error on attach/detach/remove returns failure and logs; a resolution error fails the request rather than deploying a literal placeholder.

---

## Tier 3 — Disclosure, data access, remaining medium

### S-016 — Authorize sensitive read endpoints & confirm client-gate backing
**What changes.** Add per-environment authorization to daemon-status reads, `RefDataEnvironmentsUsersController` user/owner/search reads, and `RefDataDatabasesController` inventory reads; route the controller's directory search through the validated `DirectorySearchController` path; add `[Authorize]` to `DeploymentV2Controller`. The client-gated delete/reset-password backing (A-4) is investigated up-front by the Tier-1 spike **S-000** — if S-000 found server-side enforcement missing, that fix was already pulled into Tier 1; this step only *confirms* it.
**Why.** A-3, A-5 (+A-4 confirmation) — sensitive reads insufficiently protected.
**Verification intent.** Each read returns 403 for an unauthorised environment; the destructive actions are confirmed rejected server-side regardless of client state.

### S-017 — Fix certificate-validation bypasses
**What changes.** Preferred path: install the internal CA into the trust store and remove the bypasses entirely. Where pinning is required (per U-4): implement it with the **correct mechanism** — for AMQP, `SslOption.AcceptablePolicyErrors` only toggles error *categories* and **cannot pin a certificate**; pinning requires a real per-certificate `SslOption.CertificateValidationCallback` (`RemoteCertificateValidationCallback`). For the OAuth token endpoint, replace the `ServerCertificateCustomValidationCallback` that returns `true` with a thumbprint check. Support **multiple** thumbprints (current + next) so certificate renewal is zero-downtime; the AMQP cert and the OAuth-endpoint cert must be pinned/rotated together. All validators fail closed. Remove `TrustServerCertificate=True` where a trusted cert is available; fix the diagnostic CLI to validate.
**Why.** D-4, D-7, D-8 — MITM of the broker session, OAuth client secret, and SQL sessions.
**Gate.** U-4 (cert provisioning) determines trusted-CA vs pinned-thumbprint.
**Verification intent.** A wrong/untrusted certificate is rejected on both AMQP and the OAuth endpoint; a correct/pinned cert is accepted; a renewal to the "next" pinned thumbprint does not break the connection.

### S-018 — Remove sync-over-async and async-void on request paths
**What changes.** Convert `.Result` call sites (`RequestController`, `AzureDevOpsDeployableBuild`) to awaited async; await the fire-and-forget event publishes or route them through an observed, non-throwing wrapper. **Note:** `Dorc.Api.Client/DeployApiClient` (the `async void` `PostToDorc`/`PatchToDorc`) is **dead code** (unreferenced by any `.csproj`, zero callers) — prefer **deleting it** over fixing it; if kept for a future consumer, convert to `async Task`.
**Why.** E-4, E-6 — thread-pool starvation/deadlock risk and swallowed publish failures.
**Verification intent.** No `.Result`/`async void` remains on the request path; a publish failure is observed/logged; behaviour under load is unchanged functionally.

### S-019 — Server-side data-access correctness
**What changes.** Remove the premature `IEnumerable` cast in `AccessControlPersistentSource` so filtering/ordering execute in SQL; correct `SecurityObjectFilter` so a deny bit only blocks the level it denies and the requested level is evaluated correctly (replace the misleading `IsBitSet` mask test with clear intent).
**Why.** F-1, F-2 — whole-table loads and over-broad deny logic.
**Verification intent.** The name filter is translated to SQL (no full-table load); a Deny of Write does not block ReadSecrets/Owner; existing allow/deny cases still pass.

### S-020 — Set-based permission queries & remove `EnsureCreated`
**What changes.** Rewrite the correlated per-row permission subqueries in the paged property queries as set-based joins/EXISTS that translate efficiently; remove `Database.EnsureCreated()` and its racy static gate (schema is DACPAC-managed), providing a documented test-DB setup if U-5 shows a bootstrap depends on it.
**Why.** F-3, F-4 — poor scaling and EF/DACPAC schema divergence.
**Gate.** U-5 (EnsureCreated dependency).
**Verification intent.** Paged property queries produce set-based SQL; the app starts without `EnsureCreated`; tests provision their schema explicitly.

### S-021 — Eliminate stored XSS in web renderers
*Promoted to Tier 1 — see the full step under Tier 1 above.*

### S-022 — Correct SignalR connection lifecycle
**What changes.** Give the monitor views a correct connection lifecycle: dispose the `register` `Disposable` in `disconnectedCallback`, and do not `stop()` a shared connection that another mounted view depends on (reference-count or per-view connections).
**Why.** G-2 — receiver leaks and cross-page disconnects.
**Verification intent.** Navigating between monitor pages does not stop live updates for a still-mounted view and does not accumulate zombie receivers/detached elements.

### S-023 — Restore generated-client workflow & align generator versions
**What changes.** Per U-8, either restore a generator-plus-patch workflow (custom auth logic applied post-generation) or formally adopt `runtime.ts` as maintained source with the "do not edit" banner removed; align the two `openapitools.json` generator versions.
**Why.** G-3 — regeneration silently drops auth logic; divergent generator pins.
**Verification intent.** Regenerating the client does not remove auth/token/redirect logic; a single generator version is pinned.

---

## Tier 4 — Low hardening & hygiene

> **JIT-spec note (all steps):** cited file paths in the HLPS inventory were captured during review and one is known to have drifted (`addUserOrGroupTemplateHelper.ts` is under `components/add-user-or-group/utilities/`). Each JIT spec must re-resolve its target paths against the current tree rather than trusting the inventory verbatim.

### S-024 — Remove committed private keys; re-source certs at deploy
**What changes.** Remove `deploymentportal.pfx` and `DorcNonProdSSLCert.pfx` from source and the `.csproj` references; source them from a non-committed location at deploy/local-run (C-05). Rotation of the keys is an operational action gated on U-3.
**Why.** D-5 — TLS private keys in the repository.
**Gate.** U-3 for rotation (removal proceeds regardless).
**Verification intent.** The build succeeds without the committed `.pfx`; no private key remains tracked.

### S-025 — LDAP SID filter correctness
**What changes.** Build the `objectSid` filter using the proper escaped byte-to-hex encoding rather than `Encoding.ASCII.GetString(sidBytes)`.
**Why.** B-3 — fragile/incorrect SID filter construction.
**Verification intent.** SID lookups return correct results and the filter is properly escaped.

### S-026 — Runner lifecycle & logging hygiene
**What changes.** Remove/parameterise the fixed 10s `Thread.Sleep` per runner invocation; correct the `RunnerLogger` timer due-time units and ensure the final log line flushes before exit; fix the fire-and-forget throwing event publishes and the per-publish callback re-registration; close/free the leaked logon token, security descriptor, and avoid the double process-handle close.
**Why.** C-12, C-13, C-14 — latency, lost final logs, unobserved task exceptions, handle/memory leaks.
**Verification intent.** No fixed per-invocation delay; the final log line is persisted; no unobserved publish exceptions; no per-dispatch handle/memory growth.

### S-027 — OnePassword HttpClient via factory; concurrent-safe WinAuth logging
**What changes.** Route `OnePasswordClient` through `IHttpClientFactory` with a timeout; replace the unsynchronized `static HashSet<string>` in `WinAuthLoggingMiddleware` with a concurrent, bounded structure.
**Why.** D-6, X-1 — socket exhaustion/no timeout; concurrent HashSet corruption and unbounded growth.
**Verification intent.** Secret reads reuse pooled handlers with a timeout; concurrent requests do not corrupt or unboundedly grow the logged-users set.

### S-028 — Frontend hygiene
**What changes (each with its own discrete verification — do not collapse into one green check).**
- G-4: fix the `number !== object` de-dupe guard in `deploy-env.ts` → *verify build-defs/components do not reload on an already-loaded project.*
- G-5: add SRI (or self-host) the CDN assets → *verify a tampered asset is rejected.*
- G-7: guard `JSON.parse` of localStorage in the auth callbacks → *verify corrupt localStorage does not abort the callback.*
- G-8: remove/clean up the anonymous `visibilitychange` listener → *verify no residual listener after teardown.*
- G-6: harden token retrieval so early requests carry a token → *verify a request fired immediately after load carries a token (no sign-in bounce).*
- G-9: surface load errors instead of console-only swallowing → *verify a failed role/env load is visible to the user, not silently hiding admin UI.*
- **G-10 (consider splitting out — deploy-safety relevant):** make the `appconfig.json` `https://localhost:7159` default safe/obvious → *verify a forgotten override fails loudly rather than silently calling localhost.*
**Why.** G-4..G-10 — redundant reloads, CDN-compromise exposure, callback aborts, listener leak, unauthenticated early request, hidden failures, environment coupling.

### S-029 — DB migration & lookup hygiene
**What changes.** Scope the `Props-to-Configuration.sql` orphan-filter delete to the migrated properties only; standardise entity name lookups on the case-insensitive collation across all paths.
**Why.** F-5, F-6 — over-broad migration delete; inconsistent case-sensitivity.
**Verification intent.** The migration removes only the migrated properties' orphaned filters; a differently-cased name resolves identically across code paths.

---

## Release & Review Notes

- **Per-step quality gate.** Each step passes the Adversarial Review panel (2–4 sub-agents) on its diff before release, per `CLAUDE.md`.
- **Test-first.** Each step is authored test-first; Tier 1/2 execution-engine steps (S-004, S-005, S-010, S-012, S-014) require integration-level tests (pipe/process/broker behaviour), not unit mocks alone — mirroring SC-04 of the monitor-robustness precedent.
- **Independent release.** All steps are independently releasable except where a gate or dependency is noted; no step regresses the `HighAvailabilityEnabled = false` path (C-07 baseline).
- **Blocking gates.** S-001 (U-1), S-002 (U-2), S-014 (U-6), S-017 (U-4), S-020 (U-5), S-024-rotation (U-3) must not begin JIT-spec authoring until their unknown is resolved.
