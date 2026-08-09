# HLPS: Deployment Privilege Containment — Binding Execution Identity to Authorization

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | DRAFT                                        |
| **Author**  | Agent                                        |
| **Date**    | 2026-08-09                                   |
| **Folder**  | docs/deployment-privilege-containment/       |

---

## 1. Problem Statement

DOrc maintains a detailed authorization model — per-environment and per-project access control (`AccessControl`, `SecurityObject`, `Permission`), owner semantics, a distinct `ReadSecrets` privilege, and PowerUser/Admin roles. That model governs **who may submit a deployment request**. It does not govern **what the resulting execution may do**.

At dispatch time the entire model collapses onto a single boolean. `ScriptDispatcher.GetProcessCredentials` (`src/Dorc.Monitor/ScriptDispatcher.cs:258-267`) takes `(isProduction, environmentName)` and ignores `environmentName`, returning one of exactly two credential pairs held as config values (`ScriptDispatcher.cs:19-22`):

```
DORC_ProdDeployUsername    / DORC_ProdDeployPassword
DORC_NonProdDeployUsername / DORC_NonProdDeployPassword
```

Every production deployment, for every project and every environment in the estate, is launched via `CreateProcessAsUser` as the same domain account. That account must therefore hold the union of every privilege any production deployment has ever needed. The blast radius of any single deployment path is the whole estate.

The execution that account performs is unconstrained: `PowerShellScriptRunner.Run` opens a default `InitialSessionState` with the permissive `AuthorizationManager("Microsoft.PowerShell")` and executes `File.ReadAllText(scriptName)` in FullLanguage mode (`src/Dorc.PowerShell/PowerShellScriptRunner.cs:32,46`; the same shape in `src/Dorc.NetFramework.PowerShell/`). DOrc does not hold script content — it holds a *path* beneath a single global `ScriptRoot` config value (`src/Dorc.Monitor/RequestProcessors/PendingRequestProcessor.cs:70`) and reads the file at deploy time.

The consequence is that DOrc's access-control model is not enforceable at the point where enforcement matters. There is no privilege gradient between deploying a low-risk application to a development environment and running as the account that can reach everything in production.

This HLPS addresses the **execution side** of that gap: containing the privilege a deployment runs with, and preventing a deployment from harvesting privilege it was not granted. The API-side enforcement gap (66 hand-written `Status403Forbidden` sites across the controllers with no `IAuthorizationHandler` or domain policy) is a separate, separable problem and is explicitly out of scope — see §3 and §8.

---

## 2. Observed Weaknesses

### W-1 — Execution identity is a boolean, not an authorization decision

`GetProcessCredentials(isProduction, environmentName)` discards `environmentName` (`ScriptDispatcher.cs:258-267`). Two identities serve the whole estate. Nothing in the execution path consults `AccessControl`, the environment's owner, the project, or the requesting user.

**Consequence:** privilege granted for the most demanding deployment in the estate is privilege available to the least demanding one. Environment isolation exists in the API and nowhere below it.

### W-2 — Deployment scripts execute in a full-trust runspace over content DOrc does not control

`PowerShellScriptRunner.cs:32,46` — default `AuthorizationManager`, FullLanguage, no signature or integrity check, no cmdlet allowlist. Script bodies live on the `ScriptRoot` file share; DOrc stores only the path. Write access to that share is therefore equivalent to code execution as the deployment account, and such a change leaves no trace in DOrc's audit tables (`Audit`, `AuditScript`, `RefDataAudit` record DOrc-mediated mutations only).

The single execution-time control that exists today is `ComponentProcessor.cs:128` — production deployments skip scripts flagged `NonProdOnly`, recording a `Warning` result. That is a component-level gate, not an identity gate, but it establishes the precedent and the natural insertion point for one.

### W-3 — Decrypted secure config values are injected into the script variable scope

`PendingRequestProcessor.SetUpConfigValuesAsProperties` (`:478-493`) calls `GetAllConfigValues(true)`, which decrypts **every** secure config value (`ConfigValuesPersistentSource.cs:49-62`), then sets each one as a resolver property where the `IsForProd` flag matches the target environment's prod-ness and the key is not already set. There is no exclusion list.

The deployment account credentials are themselves config values (W-1). If the `DORC_ProdDeployPassword` row carries `IsForProd = true` or `IsForProd = null`, then every production deployment script receives the shared production deployment password as an ordinary PowerShell variable, readable with a single `Write-Output`. That converts "may deploy one component to one environment" into "holds the estate's most privileged credential".

Whether this is live exposure or latent depends on the `IsForProd`/`Secure` values of those specific rows — data, not code. **U-1 (blocking).** The code path imposes no barrier either way, which is the defect regardless of the current row values.

### W-4 — The resolved secret bundle crosses the process boundary in cleartext

The `ScriptGroup` handed to the Runner carries the fully-resolved property bag. `ScriptGroupFileWriter.cs:29-33` documents its contents explicitly: `GitHubToken`, `AzureBearerToken`, `TerraformGitPat`. It is serialized as plain JSON by both transports:

- **Release builds** — over a named pipe. `ScriptGroupPipeServer.cs:41-46` constructs `NamedPipeServerStream` with no `PipeSecurity` argument, so the pipe carries the process default DACL (**U-5**). The pipe name is derived, not random: `DOrcMonitor-{HostInstanceId}-{requestId}` (`ScriptDispatcher.cs:92`).
- **Debug builds** — to `c:\Log\DOrc\Deploy\Services\ScriptGroupsPipeFiles\{pipeName}.json`, a constant in the shared `Dorc.ApiModel` assembly (`Constants/RunnerConstants.cs:6`). The directory DACL is hardened and re-asserted on every start, but **the file is never deleted** — there is no `File.Delete` in `Dorc.Monitor`, `Dorc.Runner`, or `Dorc.NetFramework.Runner`. Developer machines accumulate cleartext secret bundles indefinitely.

Transport selection is compile-time on the Monitor side (`Program.cs:189-193`, `RunnerProcessStarter.cs:56-58` appends `--useFile=true` only under `#if DEBUG`) and runtime on the Runner side (`Options.UseFile`). The two are consistent today, but it means the production transport is exercised only in Release builds and the file transport only in Debug.

### W-5 — Deployment actions are not attributable below DOrc

Because every production deployment authenticates as the same account, host and domain telemetry on target servers (logon and process-creation events) cannot attribute an action to a DOrc request, a project, or a user. DOrc's own audit trail ends at the point the Runner starts. Post-incident analysis of "what changed this server and on whose authority" cannot be answered from the target server's own records.

---

## 3. Scope

**In scope:**

- Deployment execution identity resolution — `src/Dorc.Monitor/ScriptDispatcher.cs`, `RunnerProcess/`
- Property and config-value resolution into script scope — `src/Dorc.Monitor/RequestProcessors/PendingRequestProcessor.cs`, `src/Dorc.Core/VariableResolution/`
- Config-value classification and access — `src/Dorc.PersistentData/Sources/ConfigValuesPersistentSource.cs`, `Model/ConfigValue.cs`
- Script-group transport confidentiality and lifecycle — `src/Dorc.Monitor/Pipes/`, `src/Dorc.Runner/Pipes/`, `src/Dorc.NetFramework.Runner/Pipes/`
- Script integrity verification at dispatch time
- Additive schema changes to `ConfigValues` and `Environments` required by the above

**Out of scope:**

- **API-side authorization enforcement.** The 66 hand-written `Status403Forbidden` checks, the absence of `IAuthorizationHandler`/policy abstraction, and the 44-of-54 controllers reaching `Dorc.PersistentData` directly are a real and related problem, deliberately deferred to a sibling HLPS. It has a different risk profile, a different test strategy, and combining the two would produce an Implementation Sequence too large to review meaningfully.
- What the deployment identity may do **once it is on a target server** — a domain and infrastructure privilege question, not a DOrc code question. DOrc can only ensure the right identity is used.
- Secrets held in `PropertyValues` (per-environment secure properties). Their encryption at rest and `ReadSecrets` gating are unchanged by this work.
- Replacing PowerShell as the execution substrate, or migrating `Dorc.NetFramework.Runner` off .NET Framework 4.8.
- Provisioning of domain accounts. This HLPS defines what DOrc must consume; account creation is an operator activity (**U-3**).
- The `#if DEBUG` transport split itself. Noted in W-4 as context; changing it is not required by any success criterion here.

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|-------------------|
| SC-01 | A deployment script executing against environment E cannot read the credential used to launch it, nor any other environment's deployment credential, through the resolved property bag. Demonstrated by an automated test asserting that reserved credential keys are absent from the properties handed to `ScriptDispatcher.Dispatch`. |
| SC-02 | Execution identity is resolved as a function of the target environment rather than of `IsProd`. Environments with no configured identity fall back to the current pair; the count of environments still on the fallback is reportable so migration progress is visible. |
| SC-03 | Compromise or misuse of one environment's deployment identity does not confer deployment rights over another environment. Verifiable by inspection once SC-02 is delivered and per-environment accounts are provisioned; the code-level criterion is that no single credential is reachable for more than its bound environments. |
| SC-04 | The serialized `ScriptGroup` is readable only by the Monitor service account and the launched deployment identity. In the file-based transport it does not outlive the deployment that produced it. |
| SC-05 | A modification to a script file beneath `ScriptRoot` between approval and execution is detectable by DOrc at dispatch time, and the detection outcome is recorded against the deployment result. |
| SC-06 | No regression for estates that adopt nothing: with no per-environment identity configured and no config value reclassified, deployment behaviour is byte-for-byte unchanged. Covered by tests at the `ScriptDispatcher` and `PendingRequestProcessor` level. |
| SC-07 | No breaking change to the API contract, to the Runner command-line protocol, or to the `ScriptGroup` wire format consumed by the .NET Framework 4.8 runner. Database changes are additive only. |
| SC-08 | Every behavioural change is covered by automated tests at the level at which the behaviour is decided. Credential-exclusion and identity-resolution logic require unit tests; transport ACL and file lifecycle require integration-level coverage. |

---

## 5. Constraints

- **C-01** — Three Runner implementations consume the `ScriptGroup` contract: `Dorc.Runner` (net8.0), `Dorc.NetFramework.Runner` (net48), and `Dorc.TerraformRunner`. Any change to that contract must be additive and backward-compatible, or all three must be updated in lockstep and shipped together.
- **C-02** — Migration must be incremental and reversible. An environment with no configured identity must continue to deploy exactly as it does today. No flag day.
- **C-03** — Database changes are additive only (new nullable columns, new rows). No column removal, no type change, no destructive migration.
- **C-04** — The `HighAvailabilityEnabled = false` path must continue to function; nothing here may depend on Kafka or the distributed lock.
- **C-05** — The Monitor launches Runners via `LogonUser` + `CreateProcessAsUser` (`RunnerProcess/ProcessSecurityContextBuilder.cs`, `RunnerProcessStarter.cs`). Any identity model must yield credentials usable by that mechanism. Group Managed Service Accounts would remove password handling entirely but do not support interactive-style password logon, so gMSA is an **investigation with a decision point**, not an assumption (**U-6**).
- **C-06** — No secret may become retrievable through the API as a side effect of this work. The existing AES-GCM encryption at rest (`Dorc.Core/VariableResolution/AesGcmPropertyEncryptor.cs`) and its legacy-format fallback are retained unchanged.
- **C-07** — Naming must satisfy the repository standard in `CLAUDE.md`: no `Manager`, `Helper`, `Service`, `Util`, `Common`, or `Shared` in new type names; namespaces follow `Dorc.[Component].[Feature]`.
- **C-08** — Secrets must not be introduced into new log statements, exception messages, or audit records. Credential resolution failures must be reportable without disclosing the value or the resolved account password.

---

## 6. Proposed Solution Directions

Conceptual only. Ordering, decomposition, and dependency analysis belong in the Implementation Sequence.

### SD-1 — Classify config values as operator-scope or script-scope (addresses W-3)

Introduce an explicit classification on `ConfigValue` determining whether a value may be resolved into script variable scope. Values DOrc needs in order to *operate* — deployment credentials above all, and plausibly broker and integration credentials — are operator-scope and must never reach a runspace.

Two variants, with materially different risk:

- **Reserved-key denylist.** A known set of keys (the four `DORC_*Deploy*` keys at minimum) is excluded in `SetUpConfigValuesAsProperties`. Small, immediate, no schema change, no migration risk. Closes the specific documented path; does not generalise.
- **Additive `IsScriptVisible` flag, defaulting to deny for secure values.** Structural and general, but it silently removes variables that existing scripts may depend on. Since DOrc does not own the `ScriptRoot` share, the blast radius of the default cannot be determined from this repository — **U-2 (blocking for this variant)**.

The low-risk reading is to deliver the denylist first and treat the flag as a follow-on gated on U-2. The IS should make this an explicit decision point rather than assuming the general solution is affordable.

### SD-2 — Bind execution identity to the environment (addresses W-1, partially W-5)

Add an optional per-environment deployment identity: a *reference* to a credential, never the credential itself. `Environment` gains an additive nullable identity reference; `GetProcessCredentials` becomes an environment-keyed resolution with the current prod/non-prod pair as the documented fallback (C-02).

Two candidate reference forms:

- A `ConfigValues` key pair per identity, reusing the existing encrypted-at-rest storage. Lowest new machinery; keeps credentials in the DOrc database.
- A 1Password Connect item reference, resolved at dispatch. The repository already contains a working `OnePasswordClient` and `OnePasswordSecretsReader` (`src/Dorc.Api/Security/OnePassword/`, `src/Dorc.Api/Services/OnePasswordSecretsReader.cs`), currently wired only into the API for its own bootstrap secrets. Reusing it removes deployment credentials from the DOrc database entirely, at the cost of a runtime dependency on Connect from the Monitor hosts (**U-4**) and a code move out of `Dorc.Api` into a shared assembly.

The value of SD-2 is realised only as environments are actually bound to distinct accounts, which is operator work outside this repository (**U-3**). The code change is what makes that work possible; the IS should state the reduction in blast radius as a function of adoption rather than as a step outcome.

### SD-3 — Verify script integrity at dispatch (addresses W-2)

Constraining the runspace itself — ConstrainedLanguage, a restricted `InitialSessionState`, cmdlet allowlisting — is very likely infeasible: deployment scripts legitimately do arbitrary things, and the failure mode of over-constraining is a broken estate. It should be recorded as investigated and rejected unless evidence says otherwise, not silently dropped.

The tractable control is **change detection, not capability restriction**. Record a content hash for each script when it is registered or approved in DOrc, and verify it at dispatch. A mismatch is surfaced against the deployment result and, per policy, either warns or fails. This gives DOrc a trustworthy answer to "is the code that ran the code that was reviewed" without constraining what scripts may legitimately do, and reuses the existing `AuditScript` trail. Signature verification is the stronger variant and depends on whether scripts are signed today (**U-7**).

### SD-4 — Confine and expire the script-group transport (addresses W-4)

Construct `NamedPipeServerStream` with an explicit `PipeSecurity` granting access only to the Monitor service account and the resolved deployment identity, replacing reliance on the process default DACL. In the file-based transport, delete the script-group file once the Runner has consumed it — or, failing that, on Runner process exit — so cleartext secret bundles do not persist. Both are contained changes with no contract impact.

### SD-5 — Attributable execution (addresses W-5)

Per-environment identity (SD-2) yields attribution at environment granularity in target-server telemetry, which is a substantial improvement over none. Per-request or per-user attribution would require impersonation semantics well beyond this scope and is not proposed. The IS should state plainly what level of attribution SD-2 does and does not buy, so the residual gap is recorded rather than assumed closed.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | What are the `Secure` and `IsForProd` values of the `DORC_ProdDeployUsername`, `DORC_ProdDeployPassword`, `DORC_NonProdDeployUsername`, and `DORC_NonProdDeployPassword` rows in `ConfigValues`, in each live estate? This determines whether W-3 is active credential disclosure to every deployment script or a latent path. It also determines whether this work is a routine hardening exercise or an incident requiring credential rotation ahead of any code change. | User | **Blocking** | Unresolved. |
| U-2 | Which secure config values are actually referenced by scripts on the `ScriptRoot` share? DOrc does not own that share, so the answer cannot be derived from this repository. Required before any default-deny classification (SD-1, second variant) can be considered safe. | User | **Blocking for SD-1 default-deny variant only**; the denylist variant proceeds without it | Unresolved. |
| U-3 | Is there an existing per-environment service-account convention in the domain? If not, who provisions accounts, what is the lead time, and how many environments are in scope? SD-2 delivers no privilege reduction until distinct accounts exist to bind to. | User | **Blocking for SD-2 value realisation**; not blocking for the code change | Unresolved. |
| U-4 | Are the Monitor hosts able to reach the 1Password Connect deployment (network path and credentials), or is Connect currently API-host-only? Determines whether the Connect-backed variant of SD-2 is viable or whether identity references must resolve against `ConfigValues`. | User | **Blocking for the Connect variant of SD-2** | Unresolved. |
| U-5 | What is the effective DACL on the `NamedPipeServerStream` as currently constructed, on the Monitor hosts' Windows build? Determines whether W-4's release-build path is a live exposure or a defence-in-depth gap. | Agent (verifiable) | Non-blocking — SD-4 is worth doing either way | Unresolved. |
| U-6 | Can a Group Managed Service Account be used with the `LogonUser` + `CreateProcessAsUser` mechanism in `RunnerProcess/`, or does the password-logon requirement rule it out? Determines whether SD-2 can eliminate stored passwords or must continue to manage them. | Agent (investigation) | Non-blocking — affects SD-2 variant selection, not feasibility | Unresolved. |
| U-7 | Are scripts on the `ScriptRoot` share currently Authenticode-signed, and is there any change control on that share? Determines whether SD-3 can require signatures or must fall back to content hashing. | User | Non-blocking — hashing is viable regardless | Unresolved. |
| U-8 | Do any existing deployment scripts depend on running specifically as the shared account — for example, target-server ACLs granting that named account directly? This is the principal migration cost driver for SD-2 and may require per-environment remediation on target servers. | User | Non-blocking for design; material for sequencing and estimation | Unresolved. |

**Two unknowns are blocking (U-1, U-2) and one is conditionally blocking (U-3, U-4 per variant). Per the development process, blocking unknowns halt progress: U-1 in particular should be answered before this HLPS is approved, because a positive finding changes the nature of the work from planned hardening to credential rotation.**

---

## 8. Out-of-Scope Risks

- **API authorization enforcement remains ad hoc.** Deferring the controller-side refactor means authorization stays a per-endpoint convention: 66 hand-written 403 sites, no policy abstraction, and 44 of 54 controllers bypassing `Dorc.Core` to reach `Dorc.PersistentData` directly, so there is no chokepoint at which a check could be enforced structurally. An omitted check in a new endpoint remains an unnoticed privilege escalation. This work reduces what such an escalation *yields*; it does not reduce the likelihood of one. A sibling HLPS is recommended and should not be deferred indefinitely.
- **Target-server privilege is unchanged.** Binding an environment to its own account does not by itself restrict what that account can do on a target server. Realising the reduction requires corresponding privilege reduction in the domain, outside DOrc.
- **`ScriptRoot` write access remains equivalent to code execution.** SD-3 detects change; it does not prevent it. Preventing it requires access control on the share, which DOrc does not own.
- **Migration is where the risk concentrates.** Every direction here is designed to be a no-op until adopted (C-02), but the adoption steps — reclassifying config values, binding environments to new accounts — are exactly the steps that can break live deployments. The Implementation Sequence must treat each adoption step as separately reversible, not merely each code step.
- **`#if DEBUG` transport divergence.** The production named-pipe path is exercised only in Release builds and the file path only in Debug. SD-4 touches both; test coverage must reach both, or a change validated in development will not have been validated where it ships.
