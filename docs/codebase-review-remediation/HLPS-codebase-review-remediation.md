# HLPS: Codebase Review Remediation — Security, Correctness & Robustness Hardening

| Field       | Value                                   |
|-------------|-----------------------------------------|
| **Status**  | REVISION (round-1 adversarial review applied) |
| **Author**  | Agent                                   |
| **Date**    | 2026-07-05                              |
| **Folder**  | docs/codebase-review-remediation/       |

> **Review record.** Round 1: three independent reviewers (coverage/completeness, sequencing/risk, solution-correctness). All 54 findings confirmed to map to steps with no orphans. Accepted corrections applied to this HLPS and the IS: SC-03 reworded to the retained-and-hardened `fn:` outcome; U-6 mechanism corrected to environment-lock probing; U-7 promoted to blocking S-014; U-9 added (A-4 server-side-enforcement spike); D-1 audit and D-2 on-disk closure notes added; prioritisation revised (G-1 XSS and C-7 hotfix promoted to Tier 1). No findings were rejected. Awaiting user approval at the HLPS checkpoint (only the adversarial panel + user may move this to APPROVED).

---

## 1. Problem Statement

A full-codebase review (API, Core/PersistentData, Monitor/Runners, web frontend, and a cross-cutting security sweep) identified a cluster of defects concentrated in three areas: **authorization gaps**, the **runner/monitor execution engine**, and **injection / code execution**. Several are exploitable by any authenticated user and can lead to code execution as the privileged deployment account, unauthorized control of production deployments, or disclosure of decrypted secrets. A second tier of defects silently mis-marks deployment outcomes (failed/unknown/cancelled work reported as complete), and a third tier covers information disclosure, data-access performance, and frontend robustness.

This HLPS scopes the remediation of **all** confirmed findings into an ordered, testable programme of work. It does not attempt to fix everything in one change; it establishes the problem set, the constraints under which fixes must be made, the success criteria, and the unknowns that must be resolved before specific fixes can proceed.

The consequence of not acting: the CRITICAL and HIGH findings are individually sufficient for privilege escalation or production-deployment tampering; the correctness findings erode trust in deployment status; and the disclosure findings widen the attack surface for the above.

---

## 2. Findings Inventory

Findings are grouped by remediation workstream. Severity is the reviewer's assessment (CRITICAL / HIGH / MEDIUM / LOW). Each ID is referenced by the Implementation Sequence.

### Workstream A — Authorization

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| A-1  | CRITICAL | `Dorc.Api/Controllers/TerraformController.cs:210-224` | `HasViewPermission`/`HasConfirmPermission`/`HasDeclinePermission` all hardcode `return true`; any authenticated user can confirm, decline, or read any Terraform plan. |
| A-2  | HIGH | `Dorc.Api/Controllers/RequestStatusesController.cs:68,112-127` | `Patch`, `Post("RawLog")`, and `GetLog` perform no ownership/role check; any user can tamper with any deployment's log or repoint its log path to a UNC share. |
| A-3  | MEDIUM | `DaemonStatusController.cs:38,58`; `RefDataEnvironmentsUsersController.cs:44-79`; `RefDataDatabasesController.cs:49-92` | Read endpoints return per-environment data (daemon status, user/owner lists, DB inventory) to any authenticated user; write paths check `CanModifyEnvironment` but these reads do not. `RefDataEnvironmentsUsersController.SearchUsers` also bypasses directory-search input validation. |
| A-4  | MEDIUM | `dorc-web` `env-control-center.ts:37-41,244-278` | Destructive actions (delete environment, reset SQL password) are gated only client-side via `isAdmin`; requires confirmed server-side enforcement. |
| A-5  | LOW | `DeploymentV2Controller.cs:8` | No `[Authorize]`; harmless today (action is a no-op) but unprotected under WinAuth/Both modes if an action is added. |

### Workstream B — Injection & Code Execution

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| B-1  | CRITICAL | `Dorc.Core/SqlUserPasswordReset.cs:23,33` | Username concatenated into T-SQL (`ALTER LOGIN`); validation regex permits `'`, allowing string-literal breakout. Password is set equal to the username (predictable); `targetDbServer` unvalidated into the connection string. |
| B-2  | HIGH | `Dorc.Core/VariableResolution/PropertyExpressionEvaluator.cs:20-32` | Values beginning `fn:` are executed as C# via `CSharpScript.EvaluateAsync(...).Result` — arbitrary code execution during variable resolution; also blocks on `.Result` and caches in a process-global static keyed only on expression text. |
| B-3  | LOW | `Dorc.Core/ActiveDirectorySearcher.cs:178` | `objectSid` LDAP filter built from unescaped `Encoding.ASCII.GetString(sidBytes)`; constrained today but fragile/incorrect. |

### Workstream C — Runner / Monitor Execution Engine

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| C-1  | CRITICAL | `Dorc.Monitor/Pipes/ScriptGroupPipeServer.cs:41-46`; `ScriptDispatcher.cs:91-96` | Named pipe created with no `PipeSecurity` ACL and a predictable name; server task is fire-and-forget and unobserved. Enables pipe-squatting (RCE as deploy account via attacker-supplied `ScriptGroup`) and secret theft (any local user reads the serialized `ScriptGroup` containing decrypted tokens). |
| C-2  | HIGH | `Dorc.Monitor/RunnerProcess/ProcessSecurityContextBuilder.cs:98,119-121` | `SetSecurityDescriptorDacl(..., IntPtr.Zero, ...)` sets a NULL DACL with `bInheritHandle = true` on the runner process — local privilege escalation to the deploy account. |
| C-3  | HIGH | `Dorc.TerraformRunner/TerraformProcessor.cs:254` | Only `ExitCode == 1` is treated as failure; any other nonzero exit (crash/OOM/disk-full) is reported success and the deployment is marked Complete. |
| C-4  | HIGH | `Dorc.TerraformRunner/Program.cs:107-110`; `Dorc.Monitor/TerraformDispatcher.cs:193` | Cancellation passes `CancellationToken.None` and the dispatcher registers no kill-on-cancel; a cancelled Terraform deploy orphans a live `terraform apply` that keeps mutating infrastructure. |
| C-5  | HIGH | `Dorc.Monitor/MonitorService.cs:73-77`; `DeploymentEngine.cs:89-93` | Only `SqlException` is tolerated in the monitor loop; retry-on-failure surfaces `RetryLimitExceededException`, which propagates and stops the whole service (default `StopHost`). |
| C-6  | HIGH | `Dorc.Runner/Pipes/ScriptGroupPipeClient.cs:33`; `RunnerProcess.cs:43-45`; `ScriptDispatcher.cs:155` | No timeouts on pipe connect or process wait; a wedged runner blocks its environment ~48h, and once `MaxConcurrentDeployments` hang, `DeploymentEngine.cs:70` backpressure stops cancellations being processed — unrecoverable without restart. |
| C-7  | HIGH | `Dorc.Monitor/MonitorConfiguration.cs:24-32` | `int.TryParse` overwrites the default delay with 0 on failure and the key is absent from shipped `appsettings.json`, producing a 0-delay busy loop with a forced full GC each iteration. |
| C-8  | MEDIUM | `Dorc.Monitor/DeploymentRequestStateProcessor.cs:128-171,696-730` | Startup `CancelStaleRequests` flips **all** Running requests to Pending (double-deploy in HA); process kill matches on raw PID only (kills unrelated process after PID reuse). |
| C-9  | MEDIUM | `Dorc.Monitor/ComponentProcessor.cs:177-178,213` | Unknown `ComponentType` falls through the switch, leaves status `StatusNotSet`, and the request completes as success with nothing deployed. |
| C-10 | MEDIUM | `TerraformDispatcher.cs:176-180,220-224`; `Pipes/ScriptGroupPipeServer.cs:87-91` | Stale `Marshal.GetLastWin32Error()` checked after successful managed calls; a leftover error code marks a successful deployment Failed. |
| C-11 | MEDIUM | `Dorc.TerraformRunner/TerraformProcessor.cs:221-225,251` | Terraform output read before stdout draining completes; the human-readable plan shown for approval can be truncated. |
| C-12 | LOW | `ProcessSecurityContextBuilder.cs:72-124`; `RunnerProcess.cs:48-56` | Win32 handle/memory leaks per dispatch (unclosed logon token, unfreed security descriptor, double-close of process handle); execution continues on a zero token after `DuplicateTokenEx` failure. |
| C-13 | LOW | `Dorc.Runner/Program.cs:104-109`; `RunnerLogger.cs:36` | Fixed 10s `Thread.Sleep` on every runner invocation; logger timer due-time appears to be milliseconds where seconds were intended, and the final log line can be lost on `Environment.Exit`. |
| C-14 | LOW | `PendingRequestProcessor.cs` / `ComponentProcessor.cs` (multiple) | Deployment status events fired-and-forgotten with a throwing publisher → unobserved task exceptions and dropped UI updates; `SignalRDeploymentEventPublisher.cs:105` re-registers the callback on every publish. |

### Workstream D — Secret Handling & Cryptography

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| D-1  | HIGH | `Dorc.Core/VariableResolution/PropertyEncryptor.cs:12-22` | Legacy encryptor swallows IV/key parse failure and falls back to a random ephemeral `Aes.Create()` (silent secret corruption); fixed reused IV under CBC (equality leak); no MAC (padding-oracle-prone). Still the decrypt path for pre-migration values. |
| D-2  | MEDIUM | `Dorc.TerraformRunner/TerraformProcessor.cs:53,137-175,261,307` | Decrypted secrets written to `terraform.tfvars` are left on disk on every failure path (cleanup only on success); full terraform output (which can echo secrets) logged to OpenSearch. |
| D-3  | MEDIUM | `Dorc.PowerShell/PowerShellScriptRunner.cs:214-218`; `Dorc.NetFramework.PowerShell/PowerShellScriptRunner.cs:229-233` | Decrypted property values JSON-serialized into runner log and stdout when `SetVariable` throws. |
| D-4  | MEDIUM | `Dorc.Monitor/HighAvailability/RabbitMqDistributedLockService.cs:514-515,620-634` | AMQP `AcceptablePolicyErrors` accepts name mismatch + chain errors; OAuth token-endpoint cert callback returns `true` for any error — MITM of the broker session and OAuth client secret. |
| D-5  | LOW | `Dorc.Api/deploymentportal.pfx`, `Dorc.Api/DorcNonProdSSLCert.pfx` | TLS private-key files committed to source (referenced in `Dorc.Api.csproj:44-47`). |
| D-6  | LOW | `Dorc.Api/Security/OnePassword/OnePasswordClient.cs:21` | Raw `new HttpClient()` per instance, never disposed, no timeout; bypasses `IHttpClientFactory`. |
| D-7  | LOW | `SqlUserPasswordReset.cs:24`; `Tools.PostRestoreEndurCLI/RefreshEndur.cs:79,177` | `TrustServerCertificate=True` disables SQL server-cert validation. |
| D-8  | LOW | `Tools.RabbitMqOAuthTest/Program.cs:185` | Diagnostic CLI disables cert validation while sending the real OAuth client secret. |

### Workstream E — Error Handling & Information Disclosure

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| E-1  | HIGH | `Dorc.Api/Services/ExceptionJsonConverter.cs:18-37` + `StatusCode(500, e)` sites (`RequestController`, `PropertyValuesController`, `MakeLikeProdController`, `ResetAppPasswordController`, `RequestStatusesController`, …) | Full exception (message + stack + inner chain + type) serialized into HTTP responses, bypassing the sanitized `DefaultExceptionHandler`. |
| E-2  | MEDIUM | `EnvironmentsPersistentSource.cs:255-258,286-289`; `PropertyEvaluator.cs:40-43`; `VariableResolver.cs:73,83-87` | Broad catches convert failures into "success": attach/detach returns an empty non-null model; variable resolution returns the unresolved `$(token)`; resolver returns `null` and can NRE on a null stored value. |
| E-3  | MEDIUM | `PropertyValuesPersistentSource.cs:55-57,81-83`; `ConfigValuesPersistentSource.cs:76-79` | `Remove`/`RemoveByFilterId`/`RemoveConfigValue` swallow all exceptions and return `false`, indistinguishable from "not found", never logged. |
| E-4  | MEDIUM | `RequestController.cs:136`; `AzureDevOpsDeployableBuild.cs:80,94`; `Dorc.Api.Client/DeployApiClient.cs:20,46,65` | Sync-over-async `.Result` on request paths (thread-pool starvation) and `async void` fire-and-forget HTTP that swallows failures and can crash the host. |
| E-5  | MEDIUM | `RequestController.cs:97-104`; `DirectorySearchController.cs:57,109`; `AccountController.cs`; `PropertiesService.cs:90-95` | Unhandled NREs (null project, null query param, null inner exception) surface as serialized 500s; validation failures returned as `StatusCode(500, HttpResponseMessage)` instead of 400. |
| E-6  | LOW | `RequestController.cs:244,401,456,511` | Fire-and-forget `_ = PublishRequestStatusChangedAsync(...)` discards publish failures silently. |

### Workstream F — Data Access Correctness & Performance

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| F-1  | MEDIUM | `PersistentData/Sources/AccessControlPersistentSource.cs:200-220` | Casts `context.Set<TEntity>()` to `IEnumerable` before filtering, loading the entire Environment/Project table into memory per call; name filter never reaches SQL. |
| F-2  | MEDIUM | `PersistentData/SecurityObjectFilter.cs:33-51` | `denied` accumulates every ACE's deny bits and the decision is `denied <= 0 && allow`, so any deny (e.g. Write) blocks every level including ReadSecrets/Owner; `IsBitSet` is a mask test that only works because enum values are powers of two. |
| F-3  | MEDIUM | `PropertyValuesPersistentSource.cs:365-386,439-446,538-558` | Correlated per-row permission subqueries plus `Union` + `NOT EXISTS` in paged property queries; scales poorly with property/ACL count. |
| F-4  | MEDIUM | `PersistentData/Contexts/DeploymentContext.cs:22,27-31` | `Database.EnsureCreated()` gated on an unsynchronized mutable static, shared across connection strings, incompatible with the DACPAC-managed schema. |
| F-5  | LOW | `Dorc.Database/Scripts/Post-Deployment/Props-to-Configuration.sql:110-113` | Deletes **all** orphaned `PropertyFilter` rows inside the per-property cursor, not just those for the migrated properties. |
| F-6  | LOW | `ProjectsPersistentSource.cs:60,105,116`; `EnvironmentsPersistentSource.cs:63,514,607` | Inconsistent case-sensitivity: some lookups force `CaseInsensitiveCollation`, others use collation-dependent `.Equals`; behaviour differs per path for the same entity. |

### Workstream G — Web Frontend

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| G-1  | HIGH | `dorc-web` renderers (`attach-server.ts:107`, `attach-database.ts:200`, `add-edit-database.ts:221`, `edit-database-permissions.ts:203,213`, `page-scripts-list.ts:452,460`, `page-project-components.ts:554,566,588`, `page-project-bundles.ts:331,350`, `page-deploy.ts:157`, `add-sql-port.ts:125`, `add-user-or-group/.../addUserOrGroupTemplateHelper.ts:10`) | Backend-controlled strings interpolated raw into `innerHTML` — stored XSS executing in an admin's authenticated session. |
| G-2  | HIGH | `DeploymentHub.ts:6,39` + `page-monitor-requests.ts`, `page-monitor-result.ts`, `env-monitor.ts` | Single `static` SignalR connection is `stop()`-ed and re-registered by every monitor view; register `Disposable` never disposed → receiver leaks and cross-page disconnects. |
| G-3  | MEDIUM | `dorc-web/src/apis/dorc-api/runtime.ts`; `openapitools.json` (6.6.0 vs 7.13.0) | Hand-edited "do not edit" generated client carries all auth logic; regeneration silently drops it, and the two generator pins diverge. |
| G-4  | MEDIUM | `deploy/deploy-env.ts:709,160,725` | De-dupe guard compares `number !== object` (always true); build definitions and component tree reload on every trigger. |
| G-5  | MEDIUM | `index.html:22-36` | Tagify and ace-builds loaded from a public CDN with no Subresource Integrity. |
| G-6  | LOW | `runtime.ts:37-39`; `OAuthService.ts:94-99` | Access token snapshotted at construction (can be stale); `signedInUser` returns null on first read → early unauthenticated request forces a sign-in bounce. |
| G-7  | LOW | `signin-callback.ts:4`; `signout-callback.ts:4` | Unguarded `JSON.parse` of localStorage aborts the callback module on corrupt/tampered data. |
| G-8  | LOW | `dorc-navbar.ts:337` | `visibilitychange` listener added with an anonymous handler and never removed. |
| G-9  | LOW | `dorc-app.ts:548,558,568`; `global-cache.ts:26,35`; `page-monitor-result.ts:175` | Error swallowing on role/email/env loads and `.stop().catch(()=>{})` → admin UI silently hidden with no user feedback. |
| G-10 | LOW | `public/appconfig.json` | Ships a hardcoded `https://localhost:7159` API base; a forgotten override leaves the app calling localhost. |

### Cross-cutting (infrastructure) — LOW

| ID   | Sev | Location | Defect |
|------|-----|----------|--------|
| X-1  | LOW | `Dorc.Api/Security/WinAuthLoggingMiddleware.cs:11,28` | Unbounded, unsynchronized `static HashSet<string>` written from concurrent request threads; corruption risk and unbounded growth. |

---

## 3. Scope

**In scope:** All confirmed findings listed in §2 across `src/Dorc.Api`, `Dorc.Api.Client`, `Dorc.Core`, `Dorc.PersistentData`, `Dorc.Database`, `Dorc.Monitor`, `Dorc.Runner`, `Dorc.TerraformRunner`, `Dorc.PowerShell`, `Dorc.NetFramework.*`, `Tools.RabbitMqOAuthTest`, and `dorc-web`.

**Out of scope:**
- Feature changes or refactors beyond what a fix requires.
- Broker/infrastructure configuration changes (fixes must be achievable in client code, per the monitor-robustness precedent), except where a finding is *only* resolvable operationally (e.g. certificate provisioning for D-4/D-5) — in which case the code change is to remove the insecure fallback and the operational step is documented, not performed here.
- Rotation of any credentials exposed by D-5 (private keys) — flagged for the user to perform out-of-band; code change is limited to removing the files from source control and re-sourcing them at deploy time.
- The `Tools.PostRestoreEndurCLI` raw-SQL construction beyond the `TrustServerCertificate` flag (D-7) — the tool is an operator utility outside the request-serving path; deep hardening is deferred unless the user requests it.

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|-------------------|
| SC-01 | Every CRITICAL and HIGH finding (A-1, A-2, B-1, B-2, C-1..C-7, D-1, E-1, G-1, G-2, C-2) is remediated with an automated test that demonstrates the vulnerable behaviour is no longer reachable (authorization denied, injection rejected, correct failure status, no secret in output). |
| SC-02 | No authenticated user can perform an action or read data for an environment/resource they lack rights to; authorization is enforced server-side for every state-changing and sensitive-read endpoint. Client-side gating (A-4) is confirmed to be backed by server-side checks. |
| SC-03 | No user-supplied value reaches a T-SQL, LDAP, or shell/script execution context without parameterisation or a strict allow-list. For the `fn:` evaluator (B-2, which U-2 confirms must be retained): the evaluator is capability-restricted so an expression cannot reach host/IO/process/reflection/network (by construction under Direction A, or by out-of-process token restriction under Direction B — imports-restriction alone is **not** sufficient) **and** authorship is permission-gated **and** evaluation is timeout-bounded, with the residual-risk note recorded. |
| SC-04 | A deployment (Terraform or PowerShell) that fails, is cancelled, or hits an unknown component type is never reported Complete; cancellation stops the underlying process; and a single wedged runner cannot block cancellation processing for the whole monitor. |
| SC-05 | Decrypted secrets do not appear in logs, stdout, OpenSearch, on-disk temp files after a failed run, or in HTTP error responses. The legacy encryptor no longer silently substitutes a random key. |
| SC-06 | Every change is covered by tests at the level the defect occurs (unit for logic, integration for pipe/broker/process behaviour). No change alters the external API contract, DB schema (beyond additive), or runner protocol without explicit call-out. |
| SC-07 | The `HighAvailabilityEnabled = false` path and existing green tests remain passing after every step; each step is independently releasable except where the IS explicitly pairs steps. |

---

## 5. Constraints

- **C-01** Preserve backward compatibility: existing API contract, runner protocol, and DACPAC-managed DB schema. Schema changes, if any, are additive and delivered via `Dorc.Database`, never via EF `EnsureCreated`.
- **C-02** Preserve the distributed-lock invariant (no two monitors deploying to one environment) when touching Monitor code (C-5, C-6, C-8).
- **C-03** Fixes to secret handling must not break the AES-GCM v2 path already in place; the legacy v1 decrypt path (D-1) must remain able to decrypt genuinely-valid legacy data — the fix removes the *random-key fallback*, not the legacy algorithm.
- **C-04** Authorization fixes must use the existing `ISecurityPrivilegesChecker` / `CanModifyEnvironment` / access-control infrastructure already used correctly elsewhere (e.g. `AccessControlController.Put`) — no new bespoke auth mechanism.
- **C-05** Removing the committed `.pfx` files (D-5) must be coordinated with a working local/dev build; the build must source the certificate from a non-committed location or be made optional for local runs.
- **C-06** No fix may weaken an existing correct behaviour; the review of correct paths (parameterised stored procs, validated JWTs, `WithOrigins` CORS, AES-GCM v2, Lit auto-escaping) is the baseline to preserve.
- **C-07** The `fn:` evaluator decision (B-2) and the private-key rotation (D-5) require user input before implementation — see Unknowns Register.

---

## 6. Proposed Solution Directions

Conceptual approach per workstream; detailed design belongs to the JIT Specs.

- **SD-A (Authorization).** Introduce real permission checks in `TerraformController` mirroring the deployment-confirmation permission model already used for the request lifecycle (`CanModifyEnvironment` on the deployment's environment). Add ownership/role checks to the unprotected log and read endpoints using the existing security infrastructure. Confirm server-side enforcement behind every client-gated destructive action. Add `[Authorize]` uniformly.
- **SD-B (Injection).** Replace the `ALTER LOGIN` string build with a parameter-free but validated approach: generate a strong random password (not the username), and construct the DDL via a quoted-identifier-safe mechanism (`QUOTENAME` in a stored procedure, or `sys.sp_password`/`ALTER LOGIN` with parameterised password where supported), tightening the identifier allow-list to exclude quotes. Resolve B-2 per the Unknowns Register (remove vs sandbox). Fix the LDAP SID filter to use the proper byte-to-escaped-hex encoding.
- **SD-C (Execution engine).** Add a restrictive `PipeSecurity` ACL (deploy account + SYSTEM only) and observe the pipe-server task; add a NULL-DACL replacement with an explicit restrictive DACL on the runner process; correct the Terraform exit-code check (fail on any nonzero) and thread a real `CancellationToken` with kill-on-cancel and a job-object-based process tree kill; broaden the monitor loop's tolerated exceptions and log-and-continue rather than stop-host; add timeouts to every pipe/process wait and decouple cancellation processing from the concurrency backpressure; give the iteration delay a safe floor; make startup recovery HA-aware and PID-kill identity-checked; make the component-type switch fail-closed; reset last-error before Win32 checks; drain stdout before reading Terraform output.
- **SD-D (Secrets/crypto).** Make the legacy encryptor fail loudly on bad key/IV; write tfvars to a securely-permissioned temp dir and delete it in a `finally`; scrub/omit secret values from all log/stdout/OpenSearch paths; remove the cert-validation bypasses in favour of proper trust (or a pinned thumbprint) and document the operational cert provisioning; remove committed `.pfx` files and re-source at deploy; route OnePassword through `IHttpClientFactory` with a timeout.
- **SD-E (Error handling).** Remove the `Exception` `JsonConverter` (or restrict to Development) and replace per-action `StatusCode(500, e)` with sanitized problem responses via the existing `DefaultExceptionHandler`; convert swallow-and-succeed catches into explicit failure results that are logged; return 400 for validation failures; replace `.Result`/`async void` with awaited async on request paths.
- **SD-F (Data access).** Keep queries server-side (`IQueryable`, no premature `IEnumerable` cast); correct the deny-bit logic to evaluate only the requested level; project permission checks into set-based joins; remove `EnsureCreated`; scope the migration delete to the migrated properties; standardise case-insensitive lookups.
- **SD-G (Frontend).** Replace `innerHTML` renderers with Lit templating or explicit escaping; give the SignalR hub a proper connection lifecycle (per-view register/dispose, shared connection not stopped while in use); restore the generated client to a template/patch model and align generator versions; fix the de-dupe guard; add SRI or self-host CDN assets; harden auth token retrieval and localStorage parsing; surface load errors.

---

## 7. Unknowns Register

**Blocking unknowns halt progress on the specific step noted. Non-blocking unknowns do not gate the IS.**

| ID  | Description | Owner | Blocking | Status |
|-----|-------------|-------|----------|--------|
| U-1 | What is the intended permission model for Terraform plan view/confirm/decline (A-1)? | User | **Blocking A-1** | **RESOLVED (2026-07-05).** Mirror `CanModifyEnvironment`: view = read access to the deployment's environment; confirm/decline = modify rights on that environment. No separate owner-only tier for confirm. |
| U-2 | Is the `fn:` C# expression evaluator (B-2) a used, supported feature? | User | **Blocking B-2** | **RESOLVED (2026-07-06).** Actively used, but **only for simple string operations and maths operations** (confirmed by user). This selects **Direction A**: replace the ability to execute arbitrary C# with a fail-closed allow-list, eliminating the RCE by construction. Implemented in S-002 — a Roslyn AST validator permits only literals, arithmetic/comparison/logical operators, and method/property access on string/number literals or on the safe static types `Math`/`Convert`; everything else (bare identifiers, `typeof`, `new`, lambdas, `GetType`, trailing statements) is refused before execution. |
| U-3 | Do the committed `.pfx` files (D-5) contain live private keys currently in use? If so they require rotation, not just removal from source. | User | **Blocking D-5 rotation** (not blocking their removal from source) | Open. |
| U-4 | For D-4/D-7, will RabbitMQ and SQL Server present CA-trusted certificates in production, or must the client pin a specific thumbprint for self-signed certs? | User | **Blocking D-4 final form** | Open. Default: replace "accept all" with a pinned-thumbprint validator, configurable, failing closed. |
| U-5 | Is `Database.EnsureCreated()` (F-4) relied upon by any dev/test bootstrap or first-run flow? | User | Non-blocking (default: remove; provide a documented test-DB setup if needed) | Open. |
| U-6 | Is startup recovery (C-8) expected to run in a true HA (multi-node) configuration today, or is HA single-active? Determines the environment-lock-probing design for resume. | User | **Blocking S-014** | Open. Correct mechanism (per round-1 review): probe/`TryAcquire` the per-environment lock before resuming a Running request — a successful acquire proves no live peer; do **not** blanket-flip, and do **not** restrict to "this node's" requests (would strand crashed-node work). |
| U-7 | Are deployments idempotent enough that a corrected startup-resume (C-8/S-014) may safely re-run an interrupted request? (Mirrors monitor-robustness U-5, previously confirmed idempotent.) | User | **Blocking S-014** (promoted from non-blocking per round-1 review — it is load-bearing for resume correctness) | Prior answer: yes (idempotent). **Re-confirmation required before S-014 ships.** |
| U-8 | Is the `dorc-web` generated API client (G-3) still regenerated from the OpenAPI spec as part of any build, or is it now effectively hand-maintained? Determines whether we restore the generator+patch workflow or formally adopt the file as source. | User | Non-blocking | Open. |
| U-9 | (A-4) Does server-side enforcement exist behind the client-gated delete-environment and reset-SQL-password actions? | Answerable by code inspection — see Tier-1 spike **S-000** | **Blocking A-4 severity classification** | Open — resolved by S-000 up front, not deferred. If enforcement is absent, A-4 is a Tier-1 authz hole, not a Tier-3 MEDIUM. |

---

## 8. Out-of-Scope Risks

- **Credential rotation (D-5):** Removing the `.pfx` files from source does not rotate keys that may already be compromised by their presence in git history. Rotation and history scrubbing (e.g. BFG) are operational actions for the user.
- **Terraform state safety (C-4):** Killing an in-flight `terraform apply` on cancel can itself leave state locked or partially applied. The fix stops the process and marks the request accurately; safe recovery of Terraform state is an operational runbook item, not a code fix.
- **Legacy ciphertext (D-1):** Any secrets already encrypted by the random-key fallback are unrecoverable; the fix prevents recurrence but cannot restore lost data. An audit for undecryptable values may be warranted.
- **Historical exploitation:** This review does not establish whether any finding has already been exploited; if the CRITICAL findings warrant it, an incident/log review is a separate exercise.

---

## 9. Prioritisation Summary

Recommended remediation order (detailed in the IS; revised after the round-1 review):

1. **Tier 1 — Stop active exploitation paths (CRITICAL/HIGH auth, injection, RCE, secret theft, XSS):** A-4 spike (S-000), A-1, B-2, B-1, C-1, C-2, A-2, E-1/E-5, D-2(logs)/D-3, D-1, **C-7 busy-loop hotfix (S-012a)**, **G-1 XSS (promoted)**.
2. **Tier 2 — Deployment-integrity correctness (HIGH/MEDIUM):** C-3/C-4/C-11 (S-010) **paired with** C-6 (S-012), C-5, C-9/C-10, C-8 (S-014, gated), E-2, E-3.
3. **Tier 3 — Disclosure, data-access, and remaining MEDIUM:** A-3/A-5 (+A-4 confirm), D-4/D-7/D-8, E-4/E-6, F-1/F-2, F-3/F-4, G-2, G-3.
4. **Tier 4 — LOW hardening & hygiene:** B-3, C-12..C-14, D-5, D-6, E-6, F-5, F-6, G-4..G-10, X-1.

> **Note on D-2 closure:** the log/OpenSearch half of D-2 is fixed in Tier 1 (S-008), but the on-disk `tfvars` half is only closed with the Terraform cleanup in S-010 (Tier 2). SC-05 is not fully satisfied until S-010 ships.
> **Note on D-1 audit:** because S-009 turns silently-corrupted values into hard read-time failures, the "audit for undecryptable values" (§8) is **promoted from out-of-scope to a companion action** delivered with S-009 (a `Tools.EncryptionMigrationCLI` dry-run scan).
