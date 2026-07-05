# IS: Codebase Review Remediation — Implementation Sequence

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | DRAFT                                            |
| **Author**  | Agent                                            |
| **Date**    | 2026-07-05                                        |
| **HLPS**    | HLPS-codebase-review-remediation.md (DRAFT)      |
| **Folder**  | docs/codebase-review-remediation/                |

This IS orders the remediation into atomic, independently-reviewable steps. Steps are grouped into four release tiers by risk. Within a tier, steps may proceed in parallel unless a dependency is noted. Each step gets a JIT Spec (`SPEC-S-00X-<title>.md`) authored immediately before execution — no method signatures or line numbers are fixed here.

Two steps (S-002 for B-2, S-016 for D-5 rotation, S-013 for C-8) are **gated on blocking unknowns** (U-2, U-3, U-6 in the HLPS) and cannot start until those are resolved.

---

## Step Index

| ID    | Title | Addresses | Tier | Depends On / Gate |
|-------|-------|-----------|------|-------------------|
| S-001 | Enforce Terraform plan authorization | A-1 | 1 | Gate: U-1 |
| S-002 | Remove or sandbox the `fn:` C# evaluator | B-2 | 1 | Gate: U-2 |
| S-003 | Harden SQL login reset (random password, safe DDL) | B-1 | 1 | — |
| S-004 | Secure the runner named-pipe channel | C-1 | 1 | — |
| S-005 | Restrictive DACL on the runner process | C-2 | 1 | — |
| S-006 | Authorize log ingest/read endpoints | A-2 | 1 | — |
| S-007 | Stop leaking exception detail in HTTP responses | E-1, E-5 | 1 | — |
| S-008 | Keep decrypted secrets out of logs/stdout/OpenSearch | D-2, D-3 | 1 | — |
| S-009 | Legacy encryptor fails loudly (no random-key fallback) | D-1 | 1 | — |
| S-010 | Correct Terraform failure & cancellation semantics | C-3, C-4, C-11 | 2 | — |
| S-011 | Keep the monitor service alive on transient errors | C-5 | 2 | — |
| S-012 | Timeouts and cancellation-liveness in the runner pipeline | C-6, C-7 | 2 | — |
| S-013 | Fail-closed component dispatch & correct Win32 error checks | C-9, C-10 | 2 | — |
| S-014 | HA-safe startup recovery & identity-checked process kill | C-8 | 2 | Gate: U-6 |
| S-015 | Convert swallow-and-succeed catches into logged failures | E-2, E-3 | 2 | — |
| S-016 | Authorize sensitive read endpoints & confirm client-gate backing | A-3, A-4, A-5 | 3 | — |
| S-017 | Fix certificate-validation bypasses | D-4, D-7, D-8 | 3 | Gate: U-4 |
| S-018 | Remove sync-over-async and async-void on request paths | E-4, E-6 | 3 | — |
| S-019 | Server-side data-access correctness (deny logic, no whole-table loads) | F-1, F-2 | 3 | — |
| S-020 | Set-based permission queries & remove `EnsureCreated` | F-3, F-4 | 3 | Gate: U-5 |
| S-021 | Eliminate stored XSS in web renderers | G-1 | 3 | — |
| S-022 | Correct SignalR connection lifecycle | G-2 | 3 | — |
| S-023 | Restore generated-client workflow & align generator versions | G-3, U-8 | 3 | — |
| S-024 | Remove committed private keys; re-source certs at deploy | D-5 | 4 | Gate: U-3 for rotation |
| S-025 | LDAP SID filter correctness | B-3 | 4 | — |
| S-026 | Runner lifecycle & logging hygiene | C-12, C-13, C-14 | 4 | — |
| S-027 | OnePassword HttpClient via factory; concurrent-safe WinAuth logging | D-6, X-1 | 4 | — |
| S-028 | Frontend hygiene (guard, SRI, parsing, error surfacing) | G-4..G-10 | 4 | — |
| S-029 | DB migration & lookup hygiene | F-5, F-6 | 4 | — |

---

## Tier 1 — Stop active exploitation paths

### S-001 — Enforce Terraform plan authorization
**What changes.** Replace the three `return true` stubs in `TerraformController` with real checks using the existing security infrastructure: view requires read access to the deployment's environment; confirm/decline require modify rights on that environment (per U-1).
**Why.** A-1 — any authenticated user can currently confirm/decline/read any Terraform plan and trigger infrastructure changes.
**Gate.** U-1 (permission model) must be confirmed before authoring the JIT Spec.
**Verification intent.** A user without rights on the target environment receives 403 on view/confirm/decline; a user with rights succeeds; existing confirm→Monitor-execute flow is unchanged for authorised users.

### S-002 — Remove or sandbox the `fn:` C# evaluator
**What changes.** Per U-2: if the feature is unused, remove the `fn:` branch and the `CSharpScript` dependency; if used, gate it behind explicit configuration and a restricted `ScriptOptions` with no imports and no host object, and replace the blocking `.Result` and the process-global cache.
**Why.** B-2 — arbitrary code execution during variable resolution by anyone able to set a property value.
**Gate.** U-2.
**Verification intent.** A property value beginning `fn:` does not execute arbitrary code (either rejected, or evaluated only within the sandbox with no host/file/process access); resolution of ordinary values is unchanged.

### S-003 — Harden SQL login reset
**What changes.** Generate a strong random password (not the username); construct the `ALTER LOGIN` via a quoted-identifier-safe path (`QUOTENAME`-based stored procedure or equivalent) and pass the password as a parameter where the provider allows; tighten the identifier allow-list to exclude quote characters; validate `targetDbServer` against a known-server list rather than concatenating it into the connection string.
**Why.** B-1 — T-SQL injection via a quote-permitting regex plus a predictable credential.
**Verification intent.** A username containing `'` is rejected; the resulting login password is random and returned/stored via the existing secure channel, never equal to the username; an unknown `targetDbServer` is rejected.

### S-004 — Secure the runner named-pipe channel
**What changes.** Create the `NamedPipeServerStream` with a `PipeSecurity` ACL granting access only to the deploy account and SYSTEM; observe the pipe-server task so its failure fails the dispatch instead of silently proceeding; consider a non-guessable pipe name component.
**Why.** C-1 — pipe-squatting RCE as the deploy account and secret theft of the serialized `ScriptGroup`.
**Verification intent.** A process outside the allowed accounts cannot connect to or pre-create the pipe; if pipe creation fails, the dispatch fails rather than launching the runner against a foreign pipe; the legitimate runner handshake still succeeds.

### S-005 — Restrictive DACL on the runner process
**What changes.** Replace the NULL DACL (`IntPtr.Zero`) in `ProcessSecurityContextBuilder` with an explicit DACL granting only the necessary principals; stop marking the handle inheritable unless required.
**Why.** C-2 — local privilege escalation to the deploy account via the fully-accessible process object.
**Verification intent.** The created runner process object denies `PROCESS_ALL_ACCESS` to non-privileged local users; deployment still runs as the correct account.

### S-006 — Authorize log ingest/read endpoints
**What changes.** Add ownership/role checks to `RequestStatusesController.Patch`, `Post("RawLog")`, and `GetLog` using the same `GetRequestForUser`/`CanModifyEnvironment` pattern the neighbouring `Get` already uses; validate/normalise the UNC path target.
**Why.** A-2 — unauthenticated-in-effect log tampering and UNC path injection.
**Verification intent.** A user cannot append to, repoint, or read the log of a request they lack rights to; the runner's legitimate status callbacks still succeed (confirm the runner authenticates as a principal that passes the check).

### S-007 — Stop leaking exception detail in HTTP responses
**What changes.** Remove the `Exception` `JsonConverter` registration (or restrict it to the Development environment); replace per-action `StatusCode(500, e)` returns with sanitized `ProblemDetails` via the existing `DefaultExceptionHandler`; return 400 for the validation failures currently returned as 500/`HttpResponseMessage`; guard the NRE sources (null project, null query param, null inner exception).
**Why.** E-1, E-5 — stack traces, internal types, and possibly connection strings returned to callers.
**Verification intent.** A handled exception returns a sanitized message with no stack/type/inner-chain; validation failures return 400; the previously NRE-throwing inputs return a clean 4xx.

### S-008 — Keep decrypted secrets out of logs/stdout/OpenSearch
**What changes.** In both PowerShell runners, stop serializing property values on `SetVariable` failure (log the property name/type only); in the Terraform runner, avoid logging raw plan/apply output that can echo secrets (mask known secret values or suppress), and ensure secret handling in tfvars is addressed together with cleanup in S-010.
**Why.** D-2, D-3 — decrypted secrets written to runner logs, stdout, and OpenSearch.
**Verification intent.** A forced `SetVariable` failure logs no secret value; Terraform output containing a known secret is masked/omitted in the log sink.

### S-009 — Legacy encryptor fails loudly
**What changes.** Remove the `catch (Exception) { _provider = Aes.Create(); }` random-key fallback in `PropertyEncryptor`; a bad key/IV must throw a clear configuration error. Do not change the legacy algorithm itself (C-03) — only the fallback. Document the fixed-IV/CBC limitation and confirm the AES-GCM v2 path is the write path.
**Why.** D-1 — silent random-key substitution corrupts secrets and masks misconfiguration.
**Verification intent.** A malformed key/IV throws instead of silently producing undecryptable ciphertext; genuinely-valid legacy data still decrypts; new writes use v2.

---

## Tier 2 — Deployment-integrity correctness

### S-010 — Correct Terraform failure & cancellation semantics
**What changes.** Treat any nonzero Terraform exit code as failure (not only `1`); thread a real `CancellationToken` from the dispatcher through `TerraformProcessor` and register kill-on-cancel that terminates the `terraform` process tree (job object), matching `ScriptDispatcher`; drain stdout/`WaitForExit` before reading captured output; delete the temp tfvars working directory in a `finally` (ties off D-2 on-disk secrets).
**Why.** C-3, C-4, C-11, D-2 — failed applies marked Complete, cancellation orphaning live applies, truncated approval plans, secrets left on disk.
**Verification intent.** A nonzero-≠1 exit marks the request Failed; cancelling mid-apply terminates `terraform` and the temp dir is removed; the confirmation plan is complete; no tfvars remain after any exit path.

### S-011 — Keep the monitor service alive on transient errors
**What changes.** Broaden the tolerated-exception handling in `MonitorService`/`DeploymentEngine` so transient data-access failures (including `RetryLimitExceededException` wrapping `SqlException`) are logged and the loop continues, rather than propagating and stopping the host.
**Why.** C-5 — a single non-`SqlException` stops the whole deployment monitor.
**Verification intent.** A simulated retry-exhausted DB error logs and the loop continues; a genuinely fatal condition still surfaces; no busy-spin on repeated failure (bounded backoff).

### S-012 — Timeouts and cancellation-liveness in the runner pipeline
**What changes.** Add finite timeouts to pipe `Connect`, `WaitOne`, and process wait paths; when a wait times out, fail the request cleanly and release the slot; decouple cancellation processing from the `Task.WhenAny` concurrency backpressure so cancellations are honoured even when all slots are occupied; give `MonitorConfiguration` iteration delay a safe minimum floor and add the key to `appsettings.json`.
**Why.** C-6, C-7 — wedged runners block environments ~48h and lock out cancellation; missing config key causes a 0-delay busy loop with forced GC.
**Verification intent.** A non-connecting runner times out and frees its slot; a cancellation is processed while all slots are busy; a missing/invalid delay key yields the floor value, not 0.

### S-013 — Fail-closed component dispatch & correct Win32 error checks
**What changes.** Make the `ComponentProcessor` switch fail closed — an unknown `ComponentType` sets Failed, not `StatusNotSet`; reset `Marshal.GetLastWin32Error()` context so success paths are not misread as failures (call `SetLastError(0)` before the P/Invoke or check the managed return value instead of the stale last-error).
**Why.** C-9, C-10 — unknown component silently "succeeds"; stale last-error marks successful deployments Failed.
**Verification intent.** An unknown component type marks the request Failed; a successful runner start is not misreported as Failed due to a leftover error code.

### S-014 — HA-safe startup recovery & identity-checked process kill
**What changes.** Scope `CancelStaleRequests`/resume to requests this node owns (via the distributed lock), never blanket-flipping all Running requests; before killing a persisted PID, verify process identity (name + start time) to avoid killing a reused PID.
**Why.** C-8 — double-deploy in HA and killing an unrelated reused-PID process.
**Gate.** U-6 (HA topology) determines the ownership-scoping design.
**Verification intent.** A second node restarting does not re-queue a request another node is executing; a stale PID reused by another process is not killed.

### S-015 — Convert swallow-and-succeed catches into logged failures
**What changes.** `EnvironmentsPersistentSource` attach/detach return a real success/failure signal instead of an empty non-null model; `PropertyValues`/`ConfigValues` removes distinguish "not found" from "error" and log the exception; `PropertyEvaluator`/`VariableResolver` surface resolution errors instead of returning the unresolved token or null (and guard the null-value NRE).
**Why.** E-2, E-3 — failures reported as success; unresolved `$(token)` deployed as config.
**Verification intent.** A forced DB error on attach/detach/remove returns failure and logs; a resolution error fails the request rather than deploying a literal placeholder.

---

## Tier 3 — Disclosure, data access, remaining medium

### S-016 — Authorize sensitive read endpoints & confirm client-gate backing
**What changes.** Add per-environment authorization to daemon-status reads, `RefDataEnvironmentsUsersController` user/owner/search reads, and `RefDataDatabasesController` inventory reads; route the controller's directory search through the validated `DirectorySearchController` path; verify (and add if missing) server-side enforcement behind the client-gated delete/reset-password actions; add `[Authorize]` to `DeploymentV2Controller`.
**Why.** A-3, A-4, A-5 — sensitive reads and destructive actions insufficiently protected.
**Verification intent.** Each read returns 403 for an unauthorised environment; the destructive actions are rejected server-side regardless of client state.

### S-017 — Fix certificate-validation bypasses
**What changes.** Replace "accept all"/name-mismatch-tolerant callbacks for RabbitMQ AMQP and the OAuth token endpoint with proper trust or a configurable pinned-thumbprint validator that fails closed (per U-4); remove `TrustServerCertificate=True` where a trusted cert is available; fix the diagnostic CLI to validate.
**Why.** D-4, D-7, D-8 — MITM of the broker session, OAuth client secret, and SQL sessions.
**Gate.** U-4 (cert provisioning) determines trusted-CA vs pinned-thumbprint.
**Verification intent.** A wrong/untrusted certificate is rejected; a correct/pinned certificate is accepted.

### S-018 — Remove sync-over-async and async-void on request paths
**What changes.** Convert `.Result` call sites (`RequestController`, `AzureDevOpsDeployableBuild`, `DeployApiClient`) to awaited async; replace `async void` `PostToDorc`/`PatchToDorc` with `async Task` observed by callers; await the fire-and-forget event publishes or route them through an observed, non-throwing wrapper.
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
**What changes.** Replace the `root.innerHTML = ...` renderers (listed in G-1) with Lit `render(html\`...\`, root)` or explicit escaping so backend-controlled values are never parsed as markup.
**Why.** G-1 — stored XSS in admin sessions.
**Verification intent.** A value containing `<img src=x onerror=...>` renders as text, not executable markup, across every affected grid/combo-box.

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
**What changes.** Fix the `number !== object` de-dupe guard in `deploy-env.ts`; add SRI (or self-host) the CDN assets; guard `JSON.parse` of localStorage in the auth callbacks; remove/clean up the anonymous `visibilitychange` listener; harden token retrieval so early requests carry a token; surface load errors instead of console-only swallowing; make the `appconfig.json` localhost default safe/obvious.
**Why.** G-4..G-10 — redundant reloads, CDN-compromise exposure, callback aborts, listener leak, unauthenticated early request, hidden failures, environment coupling.
**Verification intent.** The guard short-circuits correctly; assets carry integrity; corrupt localStorage does not abort the callback; load failures are visible to the user.

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
