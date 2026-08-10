# HLPS: Deployment Privilege Containment — Establishing the Monitor as an Enforced Trust Boundary

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | REVISION (adversarial round 1 applied)       |
| **Author**  | Agent                                        |
| **Date**    | 2026-08-09                                   |
| **Folder**  | docs/deployment-privilege-containment/       |

---

## 1. Problem Statement

DOrc maintains a detailed authorization model — per-environment and per-project access control (`AccessControl`, `SecurityObject`, `Permission`), owner semantics, a distinct `ReadSecrets` privilege, and PowerUser/Admin roles. That model governs **who may submit a deployment request**. Nothing enforces a boundary after that point.

The Monitor process is where every trust assumption in DOrc concentrates. It decrypts every secure config value, holds both the production and non-production deployment credentials in memory, calls `LogonUser`, and launches Runner processes. It is the most privileged component in the system. It is also the component that evaluates user-supplied strings as C#, creates child processes with a NULL DACL, and hands its resolved secret bundle to whichever local process connects to a predictably-named pipe first.

The original framing of this document was that execution identity is undifferentiated — one production account for the whole estate. That remains true and is retained below as W-6. Adversarial review established that it is not the most urgent problem, and not even the most direct one. Three paths reach arbitrary code execution with substantially less attacker capability than compromising the shared account requires, and the lowest of them needs only the ability to submit a deployment request.

This HLPS therefore addresses a broader statement of the same underlying defect: **the Monitor is a trust boundary that is not enforced at any of its edges** — not at its input (request properties), not at its process-creation boundary, not at its IPC boundary, and not at its script-loading boundary. The API-side enforcement gap (66 hand-written `Status403Forbidden` sites in `Dorc.Api/Controllers/` with no `IAuthorizationHandler`) is a separate, separable problem and remains out of scope — see §3 and §8.

---

## 2. Weaknesses, Ranked by Attacker Capability Required

Ranked by what an attacker must already have, not by subsystem. The lowest bar is first.

### W-1 — A user who can submit a deployment request can execute arbitrary code inside the Monitor process

**Capability required: submit a deployment request (the lowest privilege DOrc grants).**

`PropertyExpressionEvaluator.Evaluate` (`src/Dorc.Core/VariableResolution/PropertyExpressionEvaluator.cs:20,30`) passes any string containing `fn:` to `CSharpScript.EvaluateAsync(exp).Result` — unsandboxed Roslyn C# scripting, no allowlist, no restricted `ScriptOptions`.

The path from user input is direct and confirmed end to end:

```
CreateRequest.Properties                      (API request body, user-supplied)
  → RequestsManager.cs:292-294                 requestDetail.Properties.Add(new PropertyPair(...))
  → DeploymentRequest.RequestDetails           (serialized, stored)
  → PendingRequestProcessor.cs:460-471         SetUpRequestDetailsPropertiesAsProperties
  → VariableResolver.SetPropertyValue          (into localProperties)
  → VariableResolver.cs:80                     _expressionEvaluator.Evaluate(propertyValue.Value)
  → CSharpScript.EvaluateAsync(exp).Result
```

This executes **in the Monitor process** — the process holding both decrypted credential pairs. A user with deploy rights on a single development environment obtains the production deployment credential, and everything downstream of it. Every other weakness in this document is reachable from here, which is why it ranks first.

The behaviour is intentional and covered by tests (`src/Dorc.Core.Tests/VariableResolverTests.cs:55-70`), so it cannot simply be deleted without knowing what depends on it (**U-1**).

Two secondary defects in the same class:

- Results are cached in a process-wide `static ConcurrentDictionary` keyed on expression text and never invalidated (`PropertyExpressionEvaluator.cs:9`). A value first computed during a non-production deployment is returned to a subsequent production one. The plan must not assume resolution is per-request; it is not.
- `s.Contains("fn:")` followed by `s.Substring(3)` assumes the marker is at offset 0. Any other position yields a mis-sliced expression. A correctness bug, noted so the IS does not preserve it.

### W-2 — The Runner process is created with a NULL DACL

**Capability required: any local account on a Monitor host.**

`ProcessSecurityContextBuilder.cs:98` calls `SetSecurityDescriptorDacl(ref securityDescriptor, true, IntPtr.Zero, false)` — DACL present and NULL, which grants full access to everyone. That descriptor is stored on `ProcessAttributes` (`:119-121`, with `bInheritHandle = true`) and passed as `lpProcessAttributes` to `CreateProcessAsUser` (`RunnerProcessStarter.cs:69-73`).

Any local principal therefore obtains `PROCESS_ALL_ACCESS` over a Runner: read the decrypted property bag out of its memory, or inject code and execute as the deployment account. This also bounds what SC-04 can mean — transport confidentiality is worth little when the consuming process object is world-writable.

Related, same file: `LOGON32_LOGON_NETWORK_CLEARTEXT` at `:76`, the password held in a managed `string` (non-zeroable, GC-copied), and the `LogonUser` token at `:78` never closed — only its duplicate is returned.

### W-3 — The script-group transport is unauthenticated in both directions

**Capability required: any local account on a Monitor host.**

- **Server side:** `ScriptGroupPipeServer.cs:41-46` constructs `NamedPipeServerStream(name, Out, 1, Byte, Asynchronous)` with no `PipeSecurity`, so the pipe carries the Win32 default DACL. With `maxNumberOfServerInstances: 1`, the **first local connector wins** and receives the entire cleartext bundle.
- **Client side:** `ScriptGroupPipeClient.cs:25-33` (and the `Dorc.NetFramework.Runner` and `Dorc.TerraformRunner` equivalents) connects with no `TokenImpersonationLevel` and no server-SID check. It will accept a `ScriptGroup` from any process that owns the name.
- **The name is fully predictable**, not merely derived: `DOrcMonitor-{HostInstanceId}-{requestId}` (`ScriptDispatcher.cs:92`), where `HostInstanceId` resolves to the machine name and is *required* to be stable across restarts (`Dorc.Kafka.Events.Tests/Publisher/HostInstanceIdTests.cs:51-61`). It is also placed on the Runner command line (`RunnerProcessStarter.cs:53`), readable locally.
- **The server is fire-and-forget:** `Task.Factory.StartNew` at `ScriptDispatcher.cs:93` is never awaited before `processStarter.Start` at `:130`, leaving a create race.

A squatter that wins the race supplies an attacker-authored `ScriptGroup`, controlling `ScriptsLocation` and `ScriptPath` — arbitrary code as the deployment account. Confidentiality and integrity both fail, in opposite directions.

### W-4 — Every decrypted secure config value is injected into script variable scope, unconditionally

**Capability required: land any script or component in any deployment.**

`SetUpConfigValuesAsProperties` (`PendingRequestProcessor.cs:478-493`) calls `GetAllConfigValues(true)`, which decrypts every secure value (`ConfigValuesPersistentSource.cs:49-62`), then sets each into the resolver subject only to `IsForProd`. There is no exclusion list, and `Secure` has no bearing on whether a value is injected — only on whether it is decrypted on the way.

**There is no `IsForProd` value that withholds a row from every deployment:**

| `IsForProd` | Injected into |
|-------------|---------------|
| `null`      | **every** deployment, production and non-production (`:485` short-circuits) |
| `true`      | every production deployment |
| `false`     | every non-production deployment |

The deployment credentials are themselves config values (`ScriptDispatcher.cs:19-22`), so one of those three rows is true of them. The exposure is unconditional; only the exposed *population* varies. `IsForProd = false` is the worst case, not the safe one — it hands the production deployment password to the lower-trust non-production population.

The "key not already set" guard suppresses nothing in practice: `SetUpConfigValuesAsProperties` runs at `:103`, **before** `SetUpEnvironmentAsProperty` (`:104`) and `SetUpRequestDetailsPropertiesAsProperties` (`:109`).

Delivery into the runspace is confirmed end to end: `LoadProperties()` (`:449`) → `commonProperties` → `DeployComponent` (`:213`) → `Dispatch` (`ComponentProcessor.cs:158`) → `SessionStateProxy.SetVariable` per key (`PowerShellScriptRunner.cs:210`).

This is the one weakness where the codebase has already reasoned correctly elsewhere and this path was missed: `ConfigValuesController` is admin-only and calls `GetNonSecureConfigValue`, which *throws* rather than disclose a secure value; `RefDataConfigController.Get` is admin-only and passes `false`. The deployment path is the only caller passing `true`.

### W-5 — Script bytes are neither confined, verified, nor signature-checked, and enforcement is actively disabled

**Capability required: write to the `Scripts` table, or to the script share.**

- **`ScriptRoot` does not confine anything.** `ExtractPath` uses `Path.Combine(scriptsLocation, scriptApiModel.Path)` in both branches (`ScriptDispatcher.cs:310,322`). .NET `Path.Combine` discards the first argument when the second is rooted, so a `Script.Path` of `C:\...` or `\\attacker\share\x.ps1` executes from outside `ScriptRoot` entirely; `..\` traversal likewise. `Script` (`Dorc.PersistentData/Model/Script.cs`) carries no validation and no hash. Write access to the `Scripts` table is therefore an entry point equivalent to write access to the share — and unlike the share, it *is* audited (`AuditScript`).
- **Signature enforcement is actively removed, not merely absent.** `PowerShellScriptRunner.cs:32` (and `Dorc.NetFramework.PowerShell/PowerShellScriptRunner.cs:44`) *replaces* the default `PSAuthorizationManager` — which enforces execution policy and Authenticode — with the base `System.Management.Automation.AuthorizationManager`, whose `ShouldRun` permits unconditionally. FullLanguage is the implicit default of `InitialSessionState.CreateDefault()` (`:30`); there is no `LanguageMode` anywhere in `src/`.
- **`AddScript(File.ReadAllText(scriptName))`** (`:46`) runs the file as an in-memory scriptblock, which would not be subject to file signature checks even if enforcement were restored.

### W-6 — Execution identity is a boolean, not an authorization decision

**Capability required: already hold the shared account. This is a blast-radius property, not an entry point.**

`ScriptDispatcher.GetProcessCredentials(isProduction, environmentName)` (`:258-268`) accepts `environmentName` and never references it, returning one of two pairs. Every production deployment across the estate runs as the same domain account, which must therefore hold the union of every privilege any production deployment has ever needed.

**The same logic is duplicated verbatim in `TerraformDispatcher.cs:23-26` and `:273-283`** (same four constants, same discarded parameter, called at `:86`). Any identity work delivered against `ScriptDispatcher` alone leaves the entire Terraform path on the shared account. This is a scope defect the original draft missed.

The only execution-time gate that exists anywhere is `ComponentProcessor.cs:128` — production deployments skip `NonProdOnly` scripts with a `Warning`. Component-level, not identity-level, but it establishes the insertion point. (Note for the IS: `:128` tests `isProductionEnvironment` while `Dispatch` at `:158` is passed `isProductionRequest`. Both derive server-side from `Environments.IsProd` (`RequestsPersistentSource.cs:508-512`), so there is no exploit, but they are a snapshot-versus-live pair that must not be assumed identical.)

### W-7 — The resolved property bag is disclosed through Runner logs, including on the production path

**Capability required: read the Runner log share.**

Every Runner serializes per-script properties into its log:

| File | Path | Ships in |
|------|------|----------|
| `Dorc.Runner/Pipes/ScriptGroupFileReader.cs:48` | file transport | Debug only |
| `Dorc.NetFramework.Runner/Pipes/ScriptGroupFileReader.cs:50` | file transport | Debug only |
| `Dorc.TerraformRunner/Pipes/ScriptGroupFileReader.cs:47` | file transport | Debug only |
| **`Dorc.NetFramework.Runner/Pipes/ScriptGroupPipeClient.cs:73`** | **pipe transport** | **Release** |
| **`Dorc.TerraformRunner/Pipes/ScriptGroupPipeClient.cs:73`** | **pipe transport** | **Release** |

Only `Dorc.Runner`'s pipe client omits it. The .NET Framework runner is selected whenever `PowerShellVersionNumber` is empty or `5.1` (`ScriptDispatcher.cs:271-285`), so this is the default production path for any script without an explicit version. Additionally, `PowerShellScriptRunner.cs:214-218` writes `JsonConvert.SerializeObject(property.Value)` to the log **and to `Console`** on any `SetVariable` failure.

The log path is then published: `ScriptDispatcher.cs:106-113` composes a UNC path and calls `UpdateUncLogPath`, surfaced on the request model (`RequestsStatusPersistentSource.cs:211`). This is a second disclosure channel for exactly the values W-4 concerns, and it is not closed by anything that fixes W-4.

### W-8 — Script-group artefacts persist indefinitely

**Capability required: read the Monitor host filesystem.**

In the file transport the bundle is written to `c:\Log\DOrc\Deploy\Services\ScriptGroupsPipeFiles\{pipeName}.json` — a `const` in the shared `Dorc.ApiModel` assembly (`Constants/RunnerConstants.cs:6`) — and **never deleted**. Repo-wide, the only `File.Delete`/`Directory.Delete` calls in the Monitor and Runner projects are Terraform temp-zip cleanup. The directory DACL is hardened and re-asserted on start, but files accumulate for the life of the host.

The hardening also has a construction defect: `PrivilegedIdentities()` (`ScriptGroupFileWriter.cs:91-99`) grants only `WindowsIdentity.GetCurrent().User` (the **Monitor** account), SYSTEM and Administrators, with `SetAccessRuleProtection(true, false)`. The Runner runs as the **deployment** account and reads that file (`Dorc.Runner/Pipes/ScriptGroupFileReader.cs:34`). The in-code comment "typically the same account the Runner executes under" (`:93-94`) is false by construction. Either the file transport only works because the deployment account is a local Administrator on Monitor hosts — itself a finding, and a sharper argument for W-6 than the one W-6 makes — or it works by accident (**U-5**).

### W-9 — Deployment actions are not attributable below DOrc

**Capability required: none. This is an absence, not a vulnerability.**

Because every production deployment authenticates as one account, target-server logon and process-creation telemetry cannot attribute an action to a request, project, or user. DOrc's audit trail ends where the Runner starts.

---

## 3. Scope

**In scope:**

- Expression evaluation of user-supplied property values — `src/Dorc.Core/VariableResolution/PropertyExpressionEvaluator.cs`, `VariableResolver.cs`
- Runner process creation and security context — `src/Dorc.Monitor/RunnerProcess/`
- Script-group transport, both directions and both variants — `src/Dorc.Monitor/Pipes/`, `src/Dorc.Runner/Pipes/`, `src/Dorc.NetFramework.Runner/Pipes/`, **`src/Dorc.TerraformRunner/Pipes/`**
- Deployment execution identity resolution — `src/Dorc.Monitor/ScriptDispatcher.cs` **and `src/Dorc.Monitor/TerraformDispatcher.cs`**
- Config-value classification and resolution into script scope — `PendingRequestProcessor.cs`, `ConfigValuesPersistentSource.cs`, `Model/ConfigValue.cs`
- Script path confinement and content verification — `ScriptDispatcher.ExtractPath`, `Model/Script.cs`, both `PowerShellScriptRunner` implementations
- Secret disclosure through Runner logs — all three runners' pipe clients and file readers
- Additive schema changes required by the above

**Out of scope:**

- **API-side authorization enforcement.** 66 hand-written `Status403Forbidden` checks in `Dorc.Api/Controllers/` (69 across `Dorc.Api`), no `IAuthorizationHandler` anywhere, 44 of 54 controllers reaching `Dorc.PersistentData` directly. Real, related, deliberately deferred to a sibling HLPS: different risk profile, different test strategy, and combining them yields an unreviewable IS.
- What a deployment identity may do **once on a target server** — a domain privilege question. DOrc can only ensure the correct identity is used.
- Secrets in `PropertyValues` and their `ReadSecrets` gating, beyond preventing their disclosure through the channels named above.
- Replacing PowerShell as the execution substrate; migrating `Dorc.NetFramework.Runner` off .NET Framework 4.8.
- Domain account provisioning. This HLPS defines what DOrc must consume (**U-4**).
- The `#if DEBUG` transport split itself.

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|-------------------|
| SC-01 | User-supplied property values cannot cause arbitrary code execution in the Monitor process. Verified by a test submitting a request whose property value contains an `fn:` expression with an observable side effect, asserting the effect does not occur. |
| SC-02 | Expression evaluation results do not leak between requests or between production and non-production populations. Verified by a test that evaluates the same expression text under two different request contexts and asserts independent resolution. |
| SC-03 | Every `ConfigValue` classified operator-scope is absent from the resolved property bag for every environment, expressed as a positive invariant over the classified set rather than as a fixed key list. Coverage at `ScriptGroup` serialization **and** at `PowerShellScriptRunner.AddProperties`, not only at `Dispatch`. |
| SC-04 | A local principal on a Monitor host that is not the Monitor service account or the deployment identity can neither open a Runner process for write access, nor read a script-group bundle in transit, nor impersonate the pipe server. |
| SC-05 | The bytes executed by a Runner are verified against a hash recorded by DOrc, at the point of read inside the Runner, not at dispatch in the Monitor. A script path that resolves outside the configured `ScriptRoot` is rejected before execution. |
| SC-06 | The resolved property bag does not appear in any Runner log, on any transport, in any build configuration. |
| SC-07 | Execution identity is a function of the target environment rather than of `IsProd`, on **both** the PowerShell and Terraform dispatch paths. No code path resolves a credential for an environment it is not bound to. Environments with no configured identity use the documented fallback, and the count still on fallback is reportable. |
| SC-08 | Script-group artefacts written to disk do not outlive the deployment that produced them. |
| SC-09 | No regression where nothing is adopted: with no per-environment identity configured and no config value reclassified, the resolved property bag and the selected credential pair are equivalent to today's. Equivalence is over those two values, not over the full `ScriptGroup` — which carries a fresh `Guid` and per-request identifiers and cannot be compared byte-for-byte. |
| SC-10 | No breaking change to the API contract, the Runner command-line protocol, or the `ScriptGroup` wire format consumed by the .NET Framework 4.8 runner. Database changes are additive only. |
| SC-11 | Every behavioural change is covered by automated tests at the level at which the behaviour is decided. Transport ACLs, process ACLs, and file lifecycle require integration-level coverage; unit tests alone are insufficient for those three. |

**Residual, not a criterion:** compromise of one environment's identity conferring no rights over another depends on domain privilege reduction that is out of scope (§3). SC-07 delivers the code-level half; the operational half is recorded in §8.

---

## 5. Constraints

- **C-01** — Three Runner implementations consume the `ScriptGroup` contract: `Dorc.Runner` (net8.0), `Dorc.NetFramework.Runner` (net48), `Dorc.TerraformRunner` (net8.0). Changes must be additive and backward-compatible, or all three ship in lockstep. Two dispatchers produce it: `ScriptDispatcher` and `TerraformDispatcher`.
- **C-02** — Migration must be incremental and reversible. An environment with no configured identity deploys exactly as today. No flag day.
- **C-03** — Database changes additive only.
- **C-04** — Nothing may depend on Kafka. The master switch is `Kafka:Enabled` (`Dorc.Monitor/appsettings.json:36-37`, read via `KafkaStartupGate.IsKafkaEnabled` at `Program.cs:129-131`, gating `AddDorcKafkaDistributedLock` at `:148`). *There is no `HighAvailabilityEnabled` setting; the earlier draft named one that does not exist.*
- **C-05** — The Monitor launches Runners via `LogonUser` (`ProcessSecurityContextBuilder.cs:72`) + `CreateProcessAsUser` (`RunnerProcessStarter.cs:69`). Any identity model must yield credentials usable by that mechanism. gMSA is rejected — see U-6.
- **C-06** — No secret may become retrievable through the API as a side effect. Existing AES-GCM encryption at rest and its `v1:` legacy fallback (`AesGcmPropertyEncryptor.cs:62-68`) are retained unchanged.
- **C-07** — Naming per `CLAUDE.md`: no `Manager`, `Helper`, `Service`, `Util`, `Common`, `Shared`; namespaces follow `Dorc.[Component].[Feature]`. Note that `OnePasswordClient`'s existing namespace is `OnePassword.Connect.Client`, already outside that pattern — relocating it under SD-2 must fix this rather than propagate it.
- **C-08** — No secret may be written to logs, exception messages, audit records, or `Console`. This constrains **existing** sites as well as new ones: W-7 lists five current violations that must be closed, not merely not-added-to.

---

## 6. Proposed Solution Directions

Conceptual only; sequencing belongs in the IS. Directions are listed in the order the weakness ranking implies, not in implementation order.

### SD-1 — Contain expression evaluation (addresses W-1)

The `fn:` mechanism is intentional and tested, so removal is gated on knowing what uses it (**U-1**). Three options, in descending preference:

- **Remove it.** Cleanest, contingent on U-1 returning "unused or trivially replaceable".
- **Replace with a fixed function table.** A closed set of named operations (string manipulation, date arithmetic, whatever U-1 shows is actually used) resolved by lookup, with no compiler in the path. Preserves the useful capability and eliminates the class.
- **Sandbox the Roslyn path.** Restricted `ScriptOptions`, no assembly references, allowlisted imports. Weakest option — it retains a compiler reachable from user input and depends on getting the restriction exactly right — and should be chosen only if U-1 shows genuinely open-ended usage.

Independently of which is chosen: make the result cache per-request rather than a process-wide static, and fix the `Substring(3)` offset assumption.

### SD-2 — Confine the process and IPC boundaries (addresses W-2, W-3)

Three changes that stand alone and depend on nothing else here:

- Replace the NULL DACL with an explicit DACL admitting only the Monitor service account and SYSTEM. Close the `LogonUser` token. Reconsider `LOGON32_LOGON_NETWORK_CLEARTEXT`.
- Give `NamedPipeServerStream` an explicit `PipeSecurity` limited to the Monitor account and the deployment identity, and await server readiness before starting the Runner to close the create race.
- **Add client-side server verification.** Server-side ACLs alone are theatre against the squat: the Runner must verify it is talking to the expected server before accepting a `ScriptGroup`. Either a server-SID check on connect, or an unpredictable name in a hardened namespace, or both.

This direction can ship against today's two shared accounts and does **not** require SD-4. It should not wait for it.

### SD-3 — Classify config values as operator-scope or script-scope (addresses W-4)

Values DOrc needs in order to *operate* — deployment credentials above all — must never reach a runspace.

- **SD-3a:** an unconditional reserved-key denylist covering the four `DORC_*Deploy*` keys. No schema change, no migration risk, cannot be switched off. Ships immediately.
- **SD-3b:** an additive `IsScriptVisible` flag on `ConfigValue`, defaulting to **visible for existing rows** and **hidden for new secure rows**. Existing deployments cannot break; admins tighten per key at their own pace. This inversion is what removes U-2 from the blocking set.

Note the interaction with SD-4: if per-environment credentials land in `ConfigValues`, a fixed denylist cannot cover keys minted per environment, and SD-3b stops being optional. Either SD-4 uses the Connect-backed provider, or SD-3b is a prerequisite for SD-4.

Because W-4's exposure is unconditional, **credential rotation after SD-3a lands is a precondition of this work, not a contingency.** Any credential that has been in a runspace must be treated as disclosed.

### SD-4 — Bind execution identity to the environment (addresses W-6, partially W-9)

An optional per-environment deployment identity holding a *reference* to a credential, never the credential. `Environment` gains an additive nullable reference (it has no identity column today, so this is clean); credential resolution becomes environment-keyed on **both** `ScriptDispatcher` and `TerraformDispatcher`, with the current pair as documented fallback (C-02).

Resolution should go through a provider abstraction with two implementations — `ConfigValues`-backed and 1Password Connect-backed — selected by configuration. `OnePasswordClient` and `OnePasswordSecretsReader` already exist (`Dorc.Api/Security/OnePassword/`, `Dorc.Api/Services/OnePasswordSecretsReader.cs`), wired only into the API at `Program.cs:97-98`; SD-4 would relocate them to a shared assembly, fixing the C-07 namespace violation en route. The abstraction is what removes U-3 from the blocking set.

On provisioning granularity: an account per environment is unbounded operational cost and maximises the target-server ACL remediation in U-8. Binding by **sensitivity tier** — prod-critical / prod-standard / non-prod — captures most of the available blast-radius reduction for a handful of accounts, while the data model still permits per-environment binding where warranted. The code change is identical; only the provisioning bill differs.

### SD-5 — Confine and verify script loading (addresses W-5)

- **Path confinement:** reject any `Script.Path` that resolves outside the configured `ScriptRoot` after full canonicalisation. `Path.Combine` cannot express this; the check must be explicit.
- **Content verification at the point of read.** Verifying at dispatch is defeated by a TOCTOU the design would itself create: dispatch happens in the Monitor, but the executed bytes are read later, in another process, at `PowerShellScriptRunner.cs:46`. Anyone with share write access — exactly the W-5 threat — swaps the file in the window. The expected hash must travel in `ScriptGroup` (additively, per C-01) and be verified inside the Runner immediately before execution.
- Restoring `PSAuthorizationManager` is worth evaluating but does not substitute: `AddScript(ReadAllText(...))` executes an in-memory scriptblock not subject to file signature checks. Signature enforcement is a defence-in-depth addition, not the control.

Constraining the runspace itself — ConstrainedLanguage, cmdlet allowlisting — should be recorded as investigated and rejected: deployment scripts legitimately do arbitrary things, and over-constraining breaks the estate.

### SD-6 — Close the log disclosure channels (addresses W-7)

Remove property serialization from all five sites in §W-7, including the two on the production pipe path and the `Console` write at `PowerShellScriptRunner.cs:214-218`. Log key *names* where diagnostics require it, never values. This must land with or before SD-3, or SD-3 closes one channel while a second stays open for the same values.

### SD-7 — Expire script-group artefacts (addresses W-8)

Delete the bundle once consumed, or on Runner exit. Correct `PrivilegedIdentities()` to grant the deployment identity explicitly rather than relying on the Monitor and deployment accounts coinciding (**U-5**).

### SD-8 — Attributable execution (addresses W-9)

Per-environment identity yields environment-granularity attribution in target-server telemetry. Per-request attribution would require impersonation semantics well beyond this scope and is not proposed. The IS should state plainly what SD-4 does and does not buy so the residual gap is recorded rather than assumed closed.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | Which property values in live estates use the `fn:` expression syntax, and what do they do? Determines whether SD-1 removes the mechanism, replaces it with a fixed function table, or must sandbox it. Answerable with `SELECT ... FROM PropertyValues WHERE Value LIKE '%fn:%'` plus the equivalent over `ConfigValues`. | User | **Blocking for SD-1 variant selection**; not blocking for the rest of the plan | Unresolved. |
| U-2 | Which secure config values are referenced by scripts on the share? | User | **No longer blocking.** SD-3b's default-visible inversion means no existing deployment can break, so the inventory is useful for tightening but gates nothing. If wanted: enumerate keys from `ConfigValues` and grep the share for `$<key>`. | Resolved as non-blocking by design change. |
| U-3 | Are Monitor hosts able to reach 1Password Connect? | User | **No longer blocking.** SD-4's provider abstraction makes this a deployment-time choice between two implementations rather than a design dependency. | Resolved as non-blocking by design change. |
| U-4 | Is there an existing per-environment service-account convention? Who provisions, what lead time, how many environments? | User | **Blocking for SD-4 value realisation**; not for the code change | Unresolved. Recommendation: bind by sensitivity tier rather than per environment (SD-4). |
| U-5 | Is `DORC_*DeployUsername` a local Administrator on Monitor hosts? The file transport's ACL (`ScriptGroupFileWriter.cs:91-99`) excludes the deployment account, yet the Runner reads that file — so either the accounts coincide, the deploy account is a local admin, or the Debug path is broken. Each answer is materially different: the middle one is a privilege finding in its own right and strengthens W-6. | User | Non-blocking — SD-7 corrects the ACL either way | Unresolved. |
| U-6 | Can a gMSA be used with `LogonUser` + `CreateProcessAsUser`? | Agent | Non-blocking | **RESOLVED — no.** `CreateProcessAsUser` requires a token, and the supported way to obtain a gMSA token is to *run as* the account (a service configured with that identity), not to `LogonUser` with a password; gMSA passwords are not retrievable for password-based logon. Adopting gMSA would require per-environment Runner services instead of Monitor-side impersonation — a restructure beyond this scope. Recommendation: retain username/password, take the value from not storing them in the DOrc database (SD-4, Connect-backed) plus rotation. |
| U-7 | Effective DACL on the named pipe as currently constructed. | Agent | Non-blocking | **RESOLVED in effect.** `CreateNamedPipe` with no security attributes receives the documented Win32 default DACL, which grants read to Everyone and to anonymous; the pipe is `PipeDirection.Out`, so read is precisely the access needed to receive the bundle. Confirm on a Monitor host before the IS cites it as exploited rather than exploitable; SD-2's fix is unchanged either way. |
| U-8 | Are scripts on the share Authenticode-signed, and is there change control on it? | User | **No longer gating.** SD-5 verifies content hashes at point of read, which works regardless; and `AddScript(ReadAllText(...))` means signature checks would not apply to this execution path even for signed scripts. Retained for information only. | Resolved as non-blocking. |
| U-9 | Do existing scripts depend on running specifically as the shared account — e.g. target-server ACLs naming it? Principal migration cost driver for SD-4. | User | Non-blocking for design; material for sequencing | Unresolved. |

**One unknown now blocks, and only one direction: U-1 gates the choice among SD-1's three variants. U-2, U-3 and U-8 were downgraded by changing the design rather than by obtaining answers** — inverting SD-3b's default, adding SD-4's provider abstraction, and moving verification to point-of-read respectively. U-4 gates the *value* of SD-4, not the code.

Superseded from the previous draft: the original U-1 (`IsForProd`/`Secure` values of the `DORC_*Deploy*` rows) is **no longer blocking**. W-4 establishes the exposure is unconditional — every `IsForProd` value exposes the credential to some population — so the query determines *which* population is affected, not *whether* one is. It remains worth running for incident scoping, and credential rotation is now a stated precondition (SD-3) rather than a contingency on the answer.

---

## 8. Out-of-Scope Risks

- **API authorization enforcement remains ad hoc.** Deferring the controller refactor leaves authorization a per-endpoint convention with no chokepoint below it. This work reduces what an escalation *yields*; it does not reduce the likelihood of one. Given W-1 — where the entry point is submitting a request, i.e. the privilege the API grants most freely — the sibling HLPS is more urgent than the original draft implied.
- **Target-server privilege is unchanged.** Binding an environment to its own identity does not restrict what that identity can do once on a target server. This is the residual half of SC-07 and requires domain-side privilege reduction outside DOrc.
- **`ScriptRoot` and `Scripts`-table write access remain code execution.** SD-5 detects change and confines paths; it does not control who may write.
- **Migration is where the risk concentrates.** Every direction is designed to be a no-op until adopted (C-02), but the adoption steps — reclassifying config values, rotating credentials, binding environments — are exactly the steps that can break live deployments. The IS must treat each adoption step as separately reversible, not merely each code step.
- **`#if DEBUG` transport divergence.** The production pipe path is exercised only in Release builds and the file path only in Debug. SD-2, SD-6 and SD-7 touch both; coverage must reach both, or a change validated in development will not have been validated where it ships.
- **Credential rotation is assumed but not owned here.** SD-3 makes rotation a precondition; executing it is an operator activity this HLPS does not sequence.

---

## 9. Adversarial Review — Round 1 Triage

Panel of three independent reviewers (security architecture, plan quality and process compliance, evidence verification). All findings triaged; every accepted finding is reflected above. Every claim below was re-verified against the code before acceptance or rejection.

**Accepted — CRITICAL (reordered the document):**

| Finding | Disposition |
|---------|-------------|
| `fn:` → unsandboxed `CSharpScript.EvaluateAsync`, reachable from request submission | Accepted. Verified the full chain including `RequestsManager.cs:292-294`. Promoted to W-1; document reframed around it. |
| Runner process created with NULL DACL | Accepted. Verified `SetSecurityDescriptorDacl(..., IntPtr.Zero, ...)` at `ProcessSecurityContextBuilder.cs:98` and its use as `lpProcessAttributes` at `RunnerProcessStarter.cs:69-73`. New W-2, new SD-2. |
| Pipe squat: no client-side server verification; SD-4 as drafted was theatre | Accepted. Verified `ScriptGroupPipeClient.cs:25-33`. W-3 rewritten; SD-2 now requires client-side verification, not just server ACLs. |

**Accepted — HIGH:** W-4 exposure is unconditional, so the original blocking U-1 was downgraded and rotation made a precondition. SD-5 moved from dispatch-time to point-of-read verification (TOCTOU the original design would have created). `PSAuthorizationManager` is *actively replaced*, not passively absent. Weaknesses re-ranked by attacker capability. `TerraformDispatcher` duplicates the credential logic verbatim and was missing from scope — a genuine scope defect.

**Accepted — MEDIUM/LOW:** `Path.Combine` does not confine to `ScriptRoot`, making the `Scripts` table an equivalent entry point; `Dorc.TerraformRunner/Pipes/` missing from scope against C-01; SC-01 was a denylist self-test, restated as a positive invariant; SC-03 and SC-06 were unverifiable as written; the file-transport ACL excludes the account that must read it; existing log sites disclose the property bag that SD-3 exists to protect; the static expression cache crosses environments; SD-3a/SD-4 variant interaction; `LOGON32_LOGON_NETWORK_CLEARTEXT`, password in a managed string, leaked `LogonUser` token; `HighAvailabilityEnabled` does not exist (corrected to `Kafka:Enabled`); FullLanguage citation corrected to implicit-by-`CreateDefault()`; U-5 and U-6 resolved rather than left open.

**Strengthened beyond the finding as reported:** the Runner log disclosure was reported as Debug-only. It is not. `Dorc.NetFramework.Runner/Pipes/ScriptGroupPipeClient.cs:73` and `Dorc.TerraformRunner/Pipes/ScriptGroupPipeClient.cs:73` log properties on the **release** pipe path, and the .NET Framework runner is the default selection for any script without an explicit PowerShell version. Promoted to W-7 as a production exposure.

**Rejected — 2 findings:**

| Finding | Reason for rejection |
|---------|---------------------|
| `"IsSecure": false` on `DORC_NonProdDeployPassword` in `DeploySettings.template.json` shows the shipped default stores the password unencrypted, corroborating W-4 | The flag governs the *installer's own* settings file — whether the value is a DPAPI-protected `SecureString` locally (`DeploymentCommon.ps1:145-152`, `:207-221`). It never reaches `ConfigValue.Secure`. `$getCredentials` (`:174-180`) in fact **throws** `"Password property is not a secure string"` when it is false, so the shipped value is not a working credential configuration at all. Citing it would put a false claim into the document. |
| The `Status403Forbidden` count is 68, not 66 | 66 is correct for the stated scope, `Dorc.Api/Controllers/` across 22 files; the reviewer's 68 included `Dorc.Api/ConfigValuesController.cs`, which sits outside that directory. Independently confirmed by a second reviewer. §3 now states the scope of the count explicitly (66 in `Controllers/`, 69 across `Dorc.Api`) so it is not re-litigated. |

**Panel disagreement, resolved:** on prioritisation, the security reviewer held that W-4-before-W-6 was correct reasoning but argued below the waterline, since three directly-exploitable paths were absent from the draft entirely. Verified and accepted — the ranking is now by attacker capability required, and the original W-3-versus-W-1 question is settled by both sitting below three paths that need less.

**Round 1 of a maximum of 3.** No finding was deferred. Remaining work before this document can leave REVISION is user validation of the design decisions that downgraded U-2, U-3 and U-8, and an answer to U-1.
