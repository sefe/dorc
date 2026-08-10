# HLPS: Deployment Privilege Containment — Establishing the Monitor as an Enforced Trust Boundary

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | APPROVED - panel APPROVE WITH CHANGES (applied); round-3 additions accepted by the user at escalation |
| **Author**  | Agent                                        |
| **Date**    | 2026-08-09                                   |
| **Folder**  | docs/deployment-privilege-containment/       |

---

## 1. Problem Statement

DOrc maintains a detailed authorization model — per-environment and per-project access control (`AccessControl`, `SecurityObject`, `Permission`), owner semantics, a distinct `ReadSecrets` privilege, and PowerUser/Admin roles. That model governs **who may submit a deployment request**. Nothing enforces a boundary after that point.

Two processes hold every trust assumption in DOrc. The **Monitor** decrypts every secure config value, holds both deployment credential pairs in memory, calls `LogonUser`, and launches Runner processes. The **API host** does the same: `DaemonStatusProbe` (`src/Dorc.Core/DaemonStatusProbe.cs:22-25`) declares a third verbatim copy of the four `DORC_*Deploy*` keys, selects the pair on `EnvironmentIsProd` at `:170-184`, and calls `LogonUser` + `RunImpersonated` at `:95`, `:141` and `:398` — and despite living in `Dorc.Core` it is consumed only by `Dorc.Api`. `ResetAppPasswordController:89-115` is a fourth site, hardcoded to the non-prod pair even for production environments.

*Earlier revisions asserted the Monitor was "the most privileged component in the system". Round 3 corrected that: the API host is a co-equal holder of the production deployment credential, reached from a plain DOrc login.* It is also the component that evaluates user-supplied strings as C#, creates child processes with a NULL DACL, and hands its resolved secret bundle to whichever local process connects to a predictably-named pipe first.

The original framing of this document was that execution identity is undifferentiated — one production account for the whole estate. That remains true and is retained below as W-6. Two rounds of adversarial review established that it is not the most urgent problem, and not even close to the most direct one. Several paths reach arbitrary code execution or production approval with substantially less attacker capability than compromising the shared account requires — and the lowest of them needs only a DOrc login, because the Terraform approval gate's three authorization predicates are `return true;` stubs (W-10).

This HLPS therefore addresses a broader statement of the same underlying defect: **the Monitor is a trust boundary that is not enforced at any of its edges** — not at its input (request properties), not at its process-creation boundary, not at its IPC boundary, and not at its script-loading boundary. The API-side enforcement gap (66 hand-written `Status403Forbidden` sites in `Dorc.Api/Controllers/` with no `IAuthorizationHandler`) is a separate, separable problem and remains out of scope — see §3 and §8.

---

## 2. Weaknesses, Ranked by Attacker Capability Required

Ranked by what an attacker must already have, not by subsystem. Identifiers are stable labels, not ordering — the rank column is the order that matters.

| Rank | ID | Capability required | Outcome |
|------|----|--------------------|---------|
| **1=** | **W-10** | **A DOrc login — nothing more** | **Approve a production `terraform apply`; read any deployment's plan content** |
| **1=** | **W-15** | **A DOrc login — nothing more** | **Drive a production-credential `LogonUser` in the API process via an endpoint with no authorization check** |
| 2= | W-1 | Submit a deployment request | Arbitrary code in the Monitor process, which holds both credential pairs |
| 2= | W-14 | Submit a deployment request | Arbitrary code as the deployment account via an unvalidated build drop location |
| 3= | W-5 | Hold **Modify on any one project** | Arbitrary code as the deployment account via script path, bypassing the gated share |
| 3= | W-11 | Hold **Modify on any one project** | Arbitrary code as the deployment account via Terraform repo URL, plus PAT and bearer-token exfiltration |
| 3= | W-16 | Hold **Modify on any one project** | Entra token *or the API service account's Windows credentials* offered to an attacker-chosen host via `ArtefactsUrl` |
| 4 | W-17 | Hold any token for the API audience | In `OAuth+WinAuth` mode the global API scope policy is never applied |
| 5 | W-20 | `Modify` on the environment (post S-001) | Terraform plan artefacts embed cleartext secrets at rest, in storage and in transit |
| — | W-19 | n/a — privilege-model defect | `CanReadSecrets` is held implicitly by every environment owner and answers two different questions; it widens whatever it gates |
| 5 | W-2 | Any local account on a Monitor host | Full control of a Runner process; read its decrypted memory or inject |
| 6 | W-3 | Any local account on a Monitor host | Receive the cleartext bundle, or feed the Runner an attacker-authored one |
| 7 | W-12 | Any local account on a Monitor host — **no race to win** | Read the whole resolved property bag from `terraform.tfvars` under `%ProgramData%\dorc` |
| 8 | W-4 | Land any script or component in any deployment | Harvest every decrypted secure config value, including deployment credentials |
| 9 | W-13 | Same as W-3 / W-8 | Three first-class secrets on the wire that config-value classification cannot reach |
| 10 | W-7 | Read the Runner log share | Same values as W-4, through further channels |
| 11 | W-8 | Read the Monitor host filesystem | Historic bundles, indefinitely retained |
| 12 | W-6 | Already hold the shared account | Blast radius across the whole estate — a property, not an entry point |
| 13 | W-9 | None | Absence of attribution, not a vulnerability |

Identifiers W-1..W-9 are retained from earlier rounds for traceability with §9; W-10..W-13 were added in round 2 and W-14..W-17 in round 3. The rank column, not the identifier, carries the ordering.

**The Terraform dispatch path is where four of the top nine sit.** Earlier rounds treated it as a variant of the PowerShell path — the round-1 finding was that `TerraformDispatcher` duplicates credential resolution (W-6). Round 2 establishes it is not a variant: it has its own approval gate, its own source providers, its own secrets, its own working directory and its own execution model, and each of those is less defended than the PowerShell equivalent.

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
- **The server is fire-and-forget:** the call to `scriptGroupPipeServer.Start` at `ScriptDispatcher.cs:93` returns a `Task` (the pipe server runs under `Task.Factory.StartNew` at `ScriptGroupPipeServer.cs:25`) which is never awaited before `processStarter.Start` at `:130`, leaving a create race.

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

The deployment credentials are themselves config values (`ScriptDispatcher.cs:19-22`), so one of those three rows is true of them. The exposure is unconditional; only the exposed *population* varies.

**Confirmed against the production estate — the worst case is the actual case.** Both instances were inventoried. System Test was measured first; the **production** instance was then queried directly and returns the same configuration. The figures below are production's.

| Key | Secure | IsForProd | Injected into |
|-----|--------|-----------|---------------|
| `DORC_ProdDeployPassword` | 1 | **NULL** | **every deployment, prod and non-prod** |
| `DORC_NonProdDeployPassword` | 1 | NULL | every deployment |
| `DORC_WebDeployPassword` | 1 | NULL | every deployment |
| `DorcApiAccessPassword` | 1 | NULL | every deployment |
| `DORC_ProdDeployUsername` | 0 | NULL | every deployment |
| `DORC_NonProdDeployUsername` | 0 | NULL | every deployment |
| `DORC_WebDeployUsername` | 0 | NULL | every deployment |

The production instance manages **1,285 environments — 1,083 non-production and 202 production** (System Test's copy holds 1,286, differing by one). Within production, `DORC_ProdDeployPassword` is delivered in cleartext, as an ordinary PowerShell variable, to the deployment scripts of all **1,083 non-production environments**: the lower-trust population, where the bar to landing a component is lowest.

**This is a live production disclosure, not a design weakness and not an artefact of a test instance.** The production deployment credential is presently readable by anyone who can land a script or component in any non-production deployment. Rotation scope is addressed in SD-3, and **SD-0 is the interim containment to apply first** — it is one reversible row and it removes the credential from 1,083 of the 1,285 environments.

**The exposure is also wider than the deployment credentials.** `DorcCliSecret` and `DeploymentServiceAccountPassword` appear with `IsForProd` 0 and 1 (correctly split), but `DORC_WebDeployPassword` and `DorcApiAccessPassword` are `NULL` and reach everything. Every `Secure = 1` config value is decrypted by `GetAllConfigValues(true)` and injected subject only to this flag — so the reserved-key denylist in SD-3a must be scoped to the estate's actual secure key set, not to the four `DORC_*Deploy*` keys the earlier revisions assumed.

The "key not already set" guard suppresses nothing in practice: `SetUpConfigValuesAsProperties` runs at `:103`, **before** `SetUpEnvironmentAsProperty` (`:104`) and `SetUpRequestDetailsPropertiesAsProperties` (`:109`).

Delivery into the runspace is confirmed end to end: `LoadProperties()` (`:449`) → `commonProperties` → `DeployComponent` (`:213`) → `Dispatch` (`ComponentProcessor.cs:158`) → `SessionStateProxy.SetVariable` per key (`PowerShellScriptRunner.cs:210`).

This is the one weakness where the codebase has already reasoned correctly elsewhere and this path was missed: `ConfigValuesController` is admin-only and calls `GetNonSecureConfigValue`, which *throws* rather than disclose a secure value; `RefDataConfigController.Get` is admin-only and passes `false`. The deployment path is the only caller passing `true`.

### W-5 — Script bytes are neither confined, verified, nor signature-checked, and enforcement is actively disabled

**Capability required: `Modify` on any single project — a per-project permission, not PowerUser or Admin. Rank 3.**

**Already realised in the live estate, organically.** Four registered scripts resolve through rooted paths that bypass `ScriptRoot` confinement; **three of the four are genuinely off-share** (U-11), the fourth resolving to `ScriptRoot` itself. Two of them — components `2323` and `2942` — point at `\\trading1\common\jhai\ABissue\SyncIssue.ps1`, which by its path is an individual's working folder and an incident workaround, not a pipeline-promoted artefact. So the gated promotion process (U-8) is not merely bypassable in theory; it is bypassed in normal operation, and those scripts run as the deployment account. This raises W-5 from a latent design weakness to a demonstrated one, and it means the remediation step in SD-5 is mandatory rather than precautionary.

- **`ScriptRoot` does not confine anything.** `ExtractPath` uses `Path.Combine(scriptsLocation, ...)` in both branches (`ScriptDispatcher.cs:311,323`). .NET `Path.Combine` discards the first argument when the second is rooted, so a `Script.Path` of `C:\...` or `\\attacker\share\x.ps1` executes from outside `ScriptRoot` entirely; `..\` traversal likewise. `Script` (`Dorc.PersistentData/Model/Script.cs`) carries no validation and no hash.
- **The script path is user-settable at a lower privilege than the scripts screen implies.** `ComponentApiModel.ScriptPath` is copied verbatim into `Script.Path` on both create and update (`ManageProjectsPersistentSource.cs:384-386`, `:481-496`). `ValidateComponents` (`:321-337`) checks component names, IDs, project ownership, duplicates and name lengths — **it does not validate `ScriptPath` at all**. The reaching endpoint is `RefDataController.Put` (`:65-68`), gated only by `_securityPrivilegesChecker.CanModifyProject(User, projectId)`.

  **This is the operative finding.** Promotion onto the script share is gated by pipeline or admin (U-8, confirmed), but that control does not cover the gap: a user with `Modify` on one project can point a component at a path outside the share entirely and have it executed as the deployment account. The gated pipeline protects the share; nothing protects the *path*.

  Note also the inconsistency — `RefDataScriptsController.Put("edit")` (`:47-52`) requires PowerUser or Admin to change a script, while the component route above achieves the same effect with per-project `Modify`. Two routes to one outcome at two different privilege levels is a direct symptom of the deferred API-enforcement problem (§8), showing up inside this HLPS's scope.

- Unlike changes to the share, changes through this route *are* audited (`AuditScript`, `InsertRefDataAudit` at `RefDataController.cs:73`) — detection exists where prevention does not.
- **Signature enforcement is actively removed, not merely absent.** `PowerShellScriptRunner.cs:32` (and `Dorc.NetFramework.PowerShell/PowerShellScriptRunner.cs:44`) *replaces* the default `PSAuthorizationManager` — which enforces execution policy and Authenticode — with the base `System.Management.Automation.AuthorizationManager`, whose `ShouldRun` permits unconditionally. FullLanguage is the implicit default of `InitialSessionState.CreateDefault()` (`:30`); there is no `LanguageMode` anywhere in `src/`.
- **`AddScript(File.ReadAllText(scriptName))`** (`:46`) runs the file as an in-memory scriptblock, which would not be subject to file signature checks even if enforcement were restored.

### W-6 — Execution identity is a boolean, not an authorization decision

**Capability required: already hold the shared account. This is a blast-radius property, not an entry point.**

`ScriptDispatcher.GetProcessCredentials(isProduction, environmentName)` (`:258-268`) accepts `environmentName` and never references it, returning one of two pairs. Every production deployment across the estate runs as the same domain account, which must therefore hold the union of every privilege any production deployment has ever needed.

**The same logic is duplicated verbatim in `TerraformDispatcher.cs:23-26` and `:273-283`** (same four constants, same discarded parameter, called at `:86`). Any identity work delivered against `ScriptDispatcher` alone leaves the entire Terraform path on the shared account. This is a scope defect the original draft missed.

The only execution-time gate that exists is `ComponentProcessor.cs:128` — production deployments skip `NonProdOnly` scripts with a `Warning`. Component-level, not identity-level, but it establishes the insertion point.

**Correction from round 2:** that gate sits inside `case ComponentType.PowerShell`. The `case ComponentType.Terraform` branch at `:67-113` reaches `terraformDispatcher.Dispatch` with no equivalent check, and Terraform components whose source type is not `SharedFolder` have no `Script` row at all (`ManageProjectsPersistentSource.cs:379,456`) — so there is nothing for a `NonProdOnly` flag to hang on. The IS cannot treat `:128` as a single insertion point for identity gating; the Terraform branch needs its own, added first.

(Further note for the IS: `:128` tests `isProductionEnvironment` while `Dispatch` at `:158` is passed `isProductionRequest`. Both derive server-side from `Environments.IsProd` (`RequestsPersistentSource.cs:508-512`), so there is no exploit, but they are a snapshot-versus-live pair that must not be assumed identical.)

`TerraformDispatcher` also omits the cancellation kill-registration that `ScriptDispatcher.cs:144-178` performs, so a Terraform Runner continues executing after a lost distributed lock (`TerraformDispatcher.cs:174-232`). The duplication defect is broader than credential resolution alone.

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

### W-10 — The Terraform approval gate has no authorization at all

**Capability required: a DOrc login. Rank 1 — below every other weakness in this document.**

`TerraformController` (`Dorc.Api/Controllers/TerraformController.cs`) carries `[Authorize]` and nothing else. Its three authorization predicates are stubs:

```csharp
private bool HasViewPermission(DeploymentResultApiModel deploymentResult)    => true;   // :210-213
private bool HasConfirmPermission(DeploymentResultApiModel deploymentResult) => true;   // :215-218
private bool HasDeclinePermission(DeploymentResultApiModel deploymentResult)            // :220-224
    => HasConfirmPermission(deploymentResult);   // "Same permission logic as confirm for now"
```

They are called at `:61`, `:108` and `:171` — so the checks are wired in and always pass. The controller **injects `ISecurityPrivilegesChecker` and `IClaimsPrincipalReader`** and uses neither for these decisions: the intent was present and the implementation never landed.

Consequences:

- `ConfirmTerraformPlan` (`:94-147`) sets the result to `Confirmed`; `DeploymentRequestStateProcessor.cs:427` picks up `Confirmed` requests and `ComponentProcessor.cs:73-74` maps `Confirmed` → `ApplyPlan` → `TerraformDispatcher.Dispatch` under the production deployment credential. Any authenticated user can approve a production `terraform apply`.
- `GetTerraformPlan` (`:47-84`) returns plan content — which includes resolved variable values — to any authenticated user, for any deployment.

This is **not** the deferred §3 problem. That concerns 66 authorization checks that are hand-written and inconsistent but *present*. This is an authorization function stubbed open on the one human gate in the Terraform state machine, and it is the single highest-severity finding in this document.

**It should not wait for this HLPS.** Implementing three predicates against the result's environment is a contained change with no dependency on any solution direction here. The IS should carry it as its first step, and the operator should consider acting on it ahead of the plan.

### W-11 — The Terraform repository URL is unvalidated, settable at per-project `Modify`, and cloned then applied

**Capability required: `Modify` on any single project. Rank 3, alongside W-5.**

Write path: `RefDataController.Put` (`:67,80`, gated only by `CanModifyProject`) → `ProjectsPersistentSource.UpdateProject:236` assigns `TerraformGitRepoUrl` verbatim. `ValidateProject` (`:324-331`) runs five validators — ID existence, name uniqueness, name non-empty, URL presence, length restrictions — and **none of them inspects `TerraformGitRepoUrl`**.

Execution path: `TerraformSourceConfigurator.cs:76` copies it into `ScriptGroup` → `GitCodeSourceProvider.cs:53` `Repository.Clone(...)` → `TerraformProcessor.cs:110,116,302` runs `init`, `plan`, and `apply -auto-approve` over the cloned HCL. `local-exec` provisioners, custom providers and module sources make that arbitrary command execution as the deployment account.

**Credential exfiltration leg:** `GitCodeSourceProvider.cs:63-87` supplies `TerraformGitPat` to *any* clone URL, and for URLs containing the substring `dev.azure.com` it also supplies the Entra `AzureBearerToken` (`TerraformSourceConfigurator.cs:79-84`). The substring test runs against attacker-controlled text, so a URL crafted to contain that substring attracts the bearer token to an attacker-controlled host.

The codebase already reasoned correctly about exactly this hazard on the neighbouring branch: `TerraformSourceConfigurator.cs:169` routes GitHub through `_gitHubHostValidator.GetApiBase`, commented "to prevent SSRF/token exfiltration" (`GitHubHostValidator.cs:40-53`). The Git branch has no equivalent. As with W-4, the principle exists in the codebase and one path was missed.

**SD-5 closes nothing here** — there is no `Script.Path` and no `ScriptRoot` on this path. It needs its own direction (SD-9).

### W-12 — `terraform.tfvars` writes the resolved property bag to disk in cleartext and survives failure

**Capability required: any local account on a Monitor host, with no race to win. Rank 7.**

`TerraformProcessor.cs:137-175` writes every `CommonProperties` entry — the entire W-4 population, deployment credentials included — to `Path.Combine(workingDir, "terraform.tfvars")`, where `workingDir` sits under `%ProgramData%\dorc` (`DorcProgramData.cs:5-8`), created with **inherited ACLs**, not the hardened DACL that `ScriptGroupFileWriter` applies to the pipe-file directory.

Cleanup is on the success path only: `PreparePlanAsync:47-61` calls `DeleteTempTerraformFolder` after success, while the `catch` at `:57-61` returns `false` and leaves the directory in place. `ExecuteTerraformPlanAsync:296-315` has the same shape. A failing plan therefore leaves a cleartext secret file behind indefinitely — and failures are the common case during development of a new component.

The binary plan file, which also embeds variable values, is additionally uploaded to blob storage (`TerraformDispatcher.cs:246-248`) and served by the stubbed-open `GetTerraformPlan` of W-10.

### W-13 — `ScriptGroup` carries first-class secrets that config-value classification cannot reach

**Capability required: as W-3 or W-8. Rank 9.**

`ScriptGroup.cs:20,21,27` declares `TerraformGitPat`, `AzureBearerToken` and `GitHubToken` as fields in their own right. `AzureBearerToken` is minted from the Entra client secret (`TerraformSourceConfigurator.cs:198-217`) and `GitHubToken` comes from `AppSettings` — so **neither is a `ConfigValue`**, and neither can be denylisted by SD-3a or flagged by SD-3b.

All three ride the W-3 pipe and land in the W-8 bundle. A conforming implementation of SC-03 as originally worded — an invariant over the resolved *property bag* — would still ship these in cleartext, which is why SC-03 is now expressed over the serialized `ScriptGroup` instead.

**Addressed by SD-2 (transport confidentiality) and SD-7 (expiry), not by SD-3** — these are the one class of secret that config-value classification structurally cannot reach, so they depend entirely on the transport and lifecycle controls.

### W-14 — The build drop location is an execution input and is unvalidated

**Capability required: submit a deployment request. Rank 2, alongside W-1.**

`FileShareDeployableBuild.IsValid` (`src/Dorc.Api/Services/FileShareDeployableBuild.cs:24-35`) checks only that the URI is well-formed and the directory exists. It does **not** check that the path lies under the project's `ArtefactsUrl`. `DeployLibrary.CreateRequestAsync` then copies it verbatim (`src/Dorc.Core/DeployLibrary.cs:264-269`), and it is injected into script scope exactly as W-4's config values are (`PendingRequestProcessor.cs:95` → `:531-548`, as `$DropFolder$`).

DOrc's own shipped deployment script then **executes code from it**: `install-scripts/RunDeployment.ps1:36-39` dot-sources `Join-Path $DropFolder "DeploymentCommon.ps1"`, with `LoadModules($DropFolder)` at `:48`.

Same shape as W-4 and W-11 — the principle is applied on the neighbouring branch and missed here: `AzureDevOpsDeployableBuild` (`:44-84`) validates the build against the project's own definitions; the file-share path validates nothing. `RequestController.Post` (`:549-605`) checks project, environment, component membership and `CanModifyEnvironment`, but never `BuildUrl`. Reachable a second way at per-project `Modify`, since a stored bundle's `BuildUrl` is resubmitted unchanged by Make-Like-Prod (`MakeLikeProdController.cs:145-162`).

### W-15 — The API process also holds and impersonates with the deployment credentials

**Capability required: a DOrc login. Rank 1, alongside W-10.**

`DaemonStatusProbe` (`src/Dorc.Core/DaemonStatusProbe.cs`) is a third verbatim copy of the credential logic: the four keys at `:22-25`, selection on `EnvironmentIsProd` at `:170-184`, and `LogonUser` + `RunImpersonated` at `:95`, `:141`, `:398`. It is registered in `CoreRegistry` but consumed only by `Dorc.Api`, so this executes in the **API host**.

`DaemonStatusController.Get(int id)` (`:38-48`) carries no authorization check beyond the class-level `[Authorize]` — it takes an environment ID and drives a production-credential `LogonUser` for any of them. `ResetAppPasswordController:89-115` is a fourth resolution site, hardcoded to the non-prod pair even for production environments.

Also, as in W-2, `:95-113` never disposes its `safeAccessTokenHandle`, unlike the `using` at `:152`.

This weakness is why §1 was rewritten in round 3 and why SC-07 is now an invariant over every credential-resolution site rather than over the two dispatchers.

### W-16 — `ArtefactsUrl` is W-11's defect on a second field, with a Windows-credential leg

**Capability required: `Modify` on any one project. Rank 3, alongside W-5 and W-11.**

`ValidateProject` (`ProjectsPersistentSource.cs:323-330`) runs five validators, none inspecting the URL's host; `UpdateProject` (`:241-246`) accepts anything passing a bare scheme-prefix test. `AzureDevOpsServerWebClient` then derives an endpoint by string surgery (`:107-116`) and attaches an Entra access token when `azureEndpoint.Contains("dev.azure.com")` (`:41-60`) — a substring test against attacker-controlled text, as in W-11.

**The `else` branch is worse:** it sets `UseDefaultCredentials = true`, offering the API service account's Negotiate/NTLM credentials to whatever host the URL names. No token-scoping fix reaches that leg. Triggered by `RequestController.GetBuildDefinitions`/`GetBuilds` (`:93-155`), `[Authorize]` only.

### W-17 — In `OAuth+WinAuth` mode the global API scope policy is never applied

**Capability required: hold any token issued for the API audience. Rank 4.**

`Program.cs:441` tests `if (authenticationScheme is ConfigAuthScheme.OAuth)`, an exact match, but `ConfigAuthScheme.Both` is the distinct string `"OAuth+WinAuth"`. In Both mode `RequireAuthorization(apiScopeAuthorizationPolicy)` is skipped, so the scope requirement registered at `:208-216` is never enforced. `[Authorize]` still requires an authenticated principal and audience validation still holds — only the `scope` check is lost.

This is not the deferred §3 problem: that concerns hand-written per-endpoint checks that exist. This is a global policy silently not applied by the composition root.

### W-18 — Deployment scripts write the credential to host output

**Capability required: read a runner log. Rank alongside W-7.**

Found by the estate scan rather than by code review, in the deployment scripts themselves rather than in DOrc:

```
48 AIS\AISCommonCode.ps1:344
  Write-Host ([String]::Format("Mapping drive {0} to {1} using account: {2} [{3}]",
              $DriveName, $Path, $DeploymentServiceAccount, $DeploymentServiceAccountPassword))
```

The deployment service account's password is formatted into host output on every drive mapping. That output is captured by the runner's PowerShell host, written to the runner log, and the log path is published on the deployment request (W-7) — so it reaches a share, and the request record points at it.

This is outside DOrc's code and so outside this HLPS's remit to fix, but it is squarely inside its remit to *report*: SD-6 removes DOrc's own logging of resolved values, and a conforming implementation of SC-06 would still leave this line writing the same class of secret to the same log. Whoever owns that script should be told, and the wider question — how many deployment scripts do this — is worth an estate scan of its own.

### W-19 — `CanReadSecrets` has drifted from a machine-access privilege into a general one

**Capability required: none directly. This is a privilege-model defect that widens the population of several other weaknesses.**

`CanReadSecrets` was designed for machine access to secrets. Two mechanisms have widened it:

- **Implicit grant.** It resolves to `AccessLevel.ReadSecrets | AccessLevel.Owner`, so **every environment owner holds it without it ever being granted**. Any endpoint gated on it inherits that population automatically, and an audit of who holds `ReadSecrets` will not show them.
- **Overloaded meaning.** It gates secret decryption in `PropertyValuesService` and, separately, ACL visibility in `AccessControlController` — two different questions answered by one predicate.

The consequence for this HLPS is direct: it is not a safe gate to reach for. SPEC-S-001 initially proposed using it for Terraform plan content and reversed that decision on exactly this ground, because binding a human-facing read path to it would have entrenched the drift while appearing to tighten security.

**Recorded here rather than fixed here.** Reclaiming the privilege means auditing its holders, separating the two meanings, and deciding whether owner-implies-read-secrets is intended — all of which is privilege-model work belonging to the deferred sibling HLPS. What this document owes it is the warning: *do not gate anything new on `CanReadSecrets` until it has been reclaimed.*

### W-20 — Terraform plan content embeds cleartext secrets at rest

**Capability required: as W-10 (before S-001) or `Modify` on the environment (after). Rank alongside W-12.**

Plan output renders resolved variable values, and the property set feeding a Terraform deployment includes decrypted secure configuration values (W-4) and secure property values. The plan blob is uploaded to storage and served by the API.

Gating the *view* — which S-001 does — reduces who can read it. It does not stop the artefact containing secrets, and so does not help with the blob's own retention, its storage-side access control, or anyone reaching it outside the API.

**The fix is redaction at source:** identify secret-valued variables and withhold or mask them when the plan is rendered or before the blob is stored. Once done, an ordinary environment-read permission suffices for viewing and the artefact stops being a secret-handling problem. This is the reason S-001 accepts a residual rather than reaching for a stricter gate: the stricter gate would have treated the symptom and made W-19 worse.

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
- The Terraform approval gate — `src/Dorc.Api/Controllers/TerraformController.cs`
- Terraform source acquisition and execution — `src/Dorc.Monitor/TerraformSourceConfig/`, `src/Dorc.TerraformRunner/CodeSources/`, `TerraformProcessor`, and the working directory under `DorcProgramData`
- Write-path input validation for values that become execution inputs - `ManageProjectsPersistentSource.ValidateComponents`, `ProjectsPersistentSource.ValidateProject` (both `TerraformGitRepoUrl` and `ArtefactsUrl`), `FileShareDeployableBuild.IsValid` (`BuildUrl` becoming `DropLocation`), and the `RefDataController.Put` and `RequestController.Post` paths that reach them
- API-process credential resolution and impersonation - `src/Dorc.Core/DaemonStatusProbe.cs`, `src/Dorc.Api/Controllers/DaemonStatusController.cs`, `src/Dorc.Api/Controllers/ResetAppPasswordController.cs`
- Build-server client credential binding - `src/Dorc.Core/AzureDevOpsServer/`
- Authentication scheme composition - `src/Dorc.Api/Program.cs:441` (the `Both`-mode policy gap)
- The administrative surface for config-value classification — `src/dorc-web` (SD-3b is undeliverable without one)
- Relocation of `OnePasswordClient` / `OnePasswordSecretsReader` out of `Dorc.Api` into a shared assembly (SD-4, C-07)
- Config-value classification and resolution into script scope — `PendingRequestProcessor.cs`, `ConfigValuesPersistentSource.cs`, `Model/ConfigValue.cs`
- Script path confinement and content verification — `ScriptDispatcher.ExtractPath`, `Model/Script.cs`, both `PowerShellScriptRunner` implementations
- Secret disclosure through Runner logs — all three runners' pipe clients and file readers
- Additive schema changes required by the above

**Out of scope:**

- **API-side authorization enforcement.** 66 hand-written `Status403Forbidden` checks in `Dorc.Api/Controllers/` (69 across `Dorc.Api`), no `IAuthorizationHandler` anywhere, 44 of 54 controllers reaching `Dorc.PersistentData` directly. Real, related, deliberately deferred to a sibling HLPS: different risk profile, different test strategy, and combining them yields an unreviewable IS.

  **The boundary, stated precisely:** *input validation on write paths is in scope; who may reach those paths is out.* This resolves an ambiguity in the previous revision, where §8 disclaimed `RefDataController.Put` while SD-5 instructed the implementer to add validation inside it.

  **The carve-out does not extend to authorization predicates that are stubbed open.** W-10's three `return true;` bodies are not an instance of inconsistent-but-present enforcement; they are absent enforcement on a production-triggering gate, and they are in scope here.
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
| SC-03 | Every `ConfigValue` classified operator-scope is absent from the serialized `ScriptGroup` for every environment, expressed as a positive invariant over the classified set rather than as a fixed key list. The invariant is over the **serialized `ScriptGroup`**, not the property bag, because `TerraformGitPat`, `AzureBearerToken` and `GitHubToken` are first-class `ScriptGroup` fields rather than config values (W-13) and a property-bag invariant would not reach them. Those three are permitted to be present — they are required by the Terraform path — but must be covered by SC-04's transport confidentiality and SC-08's expiry. Coverage at serialization **and** at `PowerShellScriptRunner.AddProperties`, not only at `Dispatch`. |
| SC-04 | A local principal on a Monitor host that is not the Monitor service account or the deployment identity can neither open a Runner process for write access, nor read a script-group bundle in transit, nor impersonate the pipe server. |
| SC-05 | **PowerShell path:** the bytes executed by a Runner are verified against a hash recorded by DOrc, at the point of read inside the Runner, not at dispatch in the Monitor. A script path that resolves outside the configured `ScriptRoot` is rejected before execution. **Terraform path:** the hash regime is not applicable — `TerraformDispatcher` never receives `ScriptRoot`, sets `ScriptsLocation = component.ScriptPath` directly with no combine (`:113-119,292-307`), and for Git and artifact source types the executed bytes are fetched from a remote at run time. The Terraform equivalent is therefore source pinning — a commit SHA or artifact digest recorded by DOrc and verified before `init` — plus host allow-listing per SD-9. Stated explicitly so the difference is a decision rather than a gap. |
| SC-05a | No authorization predicate on any path that can trigger or disclose a deployment is a constant expression. Verified by test against `TerraformController`'s three predicates specifically, and by review across the deployment-triggering surface. |
| SC-05b | A Terraform source credential is released only to a host on the validated allow-list. Specifically: `TerraformGitPat` is not supplied to an arbitrary clone URL, and the `AzureBearerToken` attaches on a **parsed-host comparison** rather than the current substring test against attacker-controlled text. Directly testable with a URL crafted to contain `dev.azure.com` as a substring of an attacker-controlled host. |
| SC-06 | The resolved property bag does not appear in any Runner log, on any transport, in any build configuration. Expressed as an **invariant over runner logging**, not as a fixed list of sites — W-7's enumeration was stated as complete at five sites and round 2 found a sixth (`TerraformProcessor.cs:261`, which logs full terraform stdout including `terraform show`, rendering variable values). A criterion tied to an enumeration cannot be verified. |
| SC-07 | Execution identity is a function of the target environment rather than of `IsProd`, at **every credential-resolution site in the solution** — `ScriptDispatcher`, `TerraformDispatcher`, `DaemonStatusProbe` (API process) and `ResetAppPasswordController` — not only the dispatch paths. Stated as an invariant because round 3 found the third and fourth sites after two rounds asserted there were two. No code path resolves a credential for an environment it is not bound to. Environments with no configured identity use the documented fallback, and the count still on fallback is reportable. |
| SC-08 | Script-group artefacts written to disk do not outlive the deployment that produced them. |
| SC-08a | Terraform working directories and `terraform.tfvars` do not survive the deployment that produced them, including on the failure path, and are created under a restricted DACL rather than inherited permissions. |
| SC-09 | No regression where nothing is adopted: with no per-environment identity configured and no config value reclassified beyond the SD-3a reserved keys, the resolved property bag and the selected credential pair are equivalent to today's. **The keys denylisted by SD-3a are explicitly excluded from this equivalence** — defined by reference to that direction rather than enumerated here, because the set has already changed once (from four assumed keys to the estate's actual secure key set) and enumerating it in two places is how the two fell out of step. Their removal is the point of the step, not a regression against it. The usernames SD-3a deliberately retains are *not* excluded: they must still be present. Equivalence is over those two values, not over the full `ScriptGroup` — which carries a fresh `Guid` and per-request identifiers and cannot be compared byte-for-byte. |
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

### SD-0 - Interim containment, no code required (addresses W-4 immediately)

Setting `DORC_ProdDeployPassword.IsForProd` from `NULL` to `1` is a **one-row, reversible, code-free change** that removes the production deployment password from every non-production runspace - in the measured instance, from 1,084 of 1,286 environments. It is not a fix: production deployments still receive it, and SD-3a remains the actual remedy.

Two preconditions: confirm against the three known password consumers (SD-3a) that none relies on the prod password reaching a non-prod deployment - `DeploySSRSPowerBIReports.ps1` is the live example, since it sits in the production folder yet names the *non-prod* password - and confirm production's own configuration (**U-13**). Assess the same move for `DORC_WebDeployPassword` and `DorcApiAccessPassword` at the same time.

This exists because the document otherwise goes straight from "active, maximal disclosure" to a remediation that cannot begin until two directions land. A step that shrinks the exposed population by 84% on the day it is applied should not be left implicit.

### SD-1 - Contain expression evaluation (addresses W-1)

**SD-1a — contain by provenance. This is the primary direction and it is not gated on U-1.**

The weakness is not that a compiler exists; it is that a compiler is reachable *from `CreateRequest.Properties`*. Evaluate `fn:` only for values whose origin is an admin-curated source, and never for values entering through `SetUpRequestDetailsPropertiesAsProperties` (`PendingRequestProcessor.cs:460-471`).

This closes W-1 — rank 2, and the lowest-capability arbitrary-code-execution path in the document — **without knowing the inventory**, because it removes no capability from any existing curated value. `VariableResolver` already discriminates by source at the `localProperties` versus `_propertyValuesPersistentSource` branch (`VariableResolver.cs:60-80`); this is threading provenance through `SetPropertyValue`, not a rewrite. Note that `localProperties` is not itself a usable proxy — every `SetUp*` method writes there — so provenance must be explicit.

The earlier framing of this direction offered only mechanism choices, all of which needed U-1, and so made the document's own top-ranked weakness unstartable behind a user query it did not need to wait for.

**SD-1b — disposition of curated `fn:` usage. U-1 is resolved and the option is decided: replace Roslyn with a fixed grammar.**

The inventory returned 11 distinct expressions over 133 values, every one of the form:

```
fn:"<literal containing $Token$ interpolations>" [ .ToLower() | .ToUpper() | .Replace("<a>","<b>") ]*
```

Three operations, applied left to right to an interpolated string literal. That is a small parser, not a compiler — the `Microsoft.CodeAnalysis.CSharp.Scripting` dependency can be removed from `Dorc.Core` entirely rather than restricted.

Design notes the IS will need, drawn from the actual data:

- Interpolation must handle **adjacent tokens** (`$Coral_SefeLegalEntityAbbrev$$EnvironmentTier$`) and **tokens abutting literal text** (`$EnvironmentTier$1uks`) — both occur in the `Coral_Azure_KeyVaultName` values.
- `Replace` is always called with two double-quoted string literals, including the empty string (`.Replace(" ","")`).
- The grammar must **fail closed**: an expression it cannot parse is an error, never a fallback to compilation. This is what contains the U-1 residual, since secure `ConfigValue`s could not be inventoried and do reach the evaluator.
- Migration is a no-op for all 133 known values by construction; the IS should still verify by evaluating both implementations over the inventory and comparing.

Retained below for the record — the options considered before U-1 was answered, in what was then descending preference:

- **Remove it.** Cleanest, contingent on U-1 returning "unused or trivially replaceable".
- **Replace with a fixed function table.** A closed set of named operations (string manipulation, date arithmetic, whatever U-1 shows is actually used) resolved by lookup, with no compiler in the path. Preserves the useful capability and eliminates the class.
- **Sandbox the Roslyn path.** Restricted `ScriptOptions`, no assembly references, allowlisted imports. Weakest option — it retains a compiler reachable from user input and depends on getting the restriction exactly right — and should be chosen only if U-1 shows genuinely open-ended usage.

Independently of any of the above, and unblocked today: make the result cache per-request rather than a process-wide static, and fix the `Substring(3)` offset assumption.

### SD-2 — Confine the process and IPC boundaries (addresses W-2, W-3)

Three changes that stand alone and depend on nothing else here:

- Replace the NULL DACL with an explicit DACL admitting only the Monitor service account and SYSTEM. Close the `LogonUser` token. Reconsider `LOGON32_LOGON_NETWORK_CLEARTEXT`.
- Give `NamedPipeServerStream` an explicit `PipeSecurity` limited to the Monitor account and the deployment identity, and await server readiness before starting the Runner to close the create race. **Derive the ACL principal from the credential resolution point, not from a constant** — credentials are already in hand at `ScriptDispatcher.cs:67`, before the pipe server starts at `:93`, so taking the principal from there makes SD-4's environment-dependent identity a drop-in rather than a rework (see the back-dependency note in SD-4).
- If an unpredictable pipe name is adopted, it must be **per server instance, not per request**. The name is currently composed once per request (`:92`) while the enclosing loop iterates per script group, reusing one name with `maxNumberOfServerInstances: 1`; a per-request random name would collide with itself on any multi-version request.
- **Add client-side server verification.** Server-side ACLs alone are theatre against the squat: the Runner must verify it is talking to the expected server before accepting a `ScriptGroup`. Either a server-SID check on connect, or an unpredictable name in a hardened namespace, or both.

This direction can ship against today's two shared accounts and does **not** require SD-4. It should not wait for it.

### SD-3 — Classify config values as operator-scope or script-scope (addresses W-4)

Values DOrc needs in order to *operate* — deployment credentials above all — must never reach a runspace.

- **SD-3a:** a reserved-key denylist, **split by key type on the evidence from U-10**. No schema change, cannot be switched off.

  **Round-3 correction, from the completed estate scan.** "Denylist every secure key" was wrong. The scan shows the seven secure keys divide into two classes with opposite requirements:

  | Class | Keys | Consumers in the script estate | Treatment |
  |-------|------|-------------------------------|-----------|
  | **Operator-only** — DOrc needs them; scripts never should | `DORC_ProdDeployPassword`, `DORC_WebDeployPassword`, `DorcApiAccessPassword` | **Zero** | Denylist unconditionally. No migration. |
  | **Deliberately script-facing** — supplied to scripts as deployment inputs | `DeploymentServiceAccountPassword`, `ProgetAccountPassword`, `DorcCliSecret` | ~60 files / ~80 call sites; ~12; 2 | **Must remain available.** Denylisting breaks the estate. |
  | Mixed | `DORC_NonProdDeployPassword` | 3 | Migrate those three, then denylist. |

  `DeploymentServiceAccountPassword` is not an incidental consumer — it is how scripts reach target servers at all (`net use`, `Invoke-RemoteProcess`, `New-Object PSCredential`, NTLM credential construction). It is a deployment primitive, and no amount of sequencing makes removing it viable.

  **This is why SD-3b's per-key classification is the real mechanism and SD-3a is only the head start.** A blanket rule over "secure" cannot express the distinction, because the distinction is not about sensitivity — both classes are genuinely secret — but about whether the script is the *intended recipient*. For the script-facing class the exposure is addressed by transport and lifecycle hardening (SD-2, SD-6, SD-7) and by identity binding (SD-4), not by withholding the value.

  - **Operator-only keys — denylisted unconditionally.** The full set, established by enumerating `Secure = 1` rather than by assumption, is **7 distinct keys**:

  | Key | `IsForProd` | Reaches |
  |-----|-------------|---------|
  | `DORC_ProdDeployPassword` | NULL | every deployment |
  | `DORC_NonProdDeployPassword` | NULL | every deployment |
  | `DORC_WebDeployPassword` | NULL | every deployment |
  | `DorcApiAccessPassword` | NULL | every deployment |
  | `DeploymentServiceAccountPassword` | 0 and 1 | its own population |
  | `DorcCliSecret` | 0 and 1 | its own population |
  | `ProgetAccountPassword` | 0 and 1 | its own population |

  This set has grown twice — from the four `DORC_*Deploy*` keys assumed in rounds 1-2, to six after a partial listing, to seven once `Secure = 1` was enumerated in full. That is the reason SC-09 defines its carve-out by reference to this direction rather than restating a list.

  **Note the estate's own convention:** three of the seven are correctly split into production and non-production pairs. Someone understood the flag and scoped them deliberately. The four NULL rows are exceptions to an existing practice, not the norm — which makes SD-0 a correction toward that convention rather than a new restriction, and lowers its risk accordingly. The U-10 inventory found nothing in either `PropertyValue` or `ConfigValue` referencing any of them, so the DB-visible migration risk is nil. Earlier revisions scoped this to the four `DORC_*Deploy*` keys; the estate data shows that would have left at least two live secrets in every runspace.
  - **Username keys — remain script-visible.** `FOIT_CondaProxyIdentity` interpolates `$DORC_NonProdDeployUsername$` across three values, so denying them breaks a working deployment. The security gain would be minimal in any case: an account *name* is an identity, not a credential, and is already visible in target-server ACLs, process listings and event logs. Withholding it buys recon friction at the cost of a live regression — the wrong trade.

  The original direction denylisted all four. That would have broken `FOIT_CondaProxyIdentity` on the first deployment after release, which is precisely the failure mode U-10 was registered to catch.

  **The share grep has since run, and the password denylist is a migration.** Two scripts in the non-production share assign `$password` from what appears to be an injected credential variable — `139 UploadersDownloaders\ControllApp…:11` and `51 TFS\TFS Refresh\020 - Hosts File.ps1:83`. Preconditions for shipping the password half, in order:

  1. Read both matching lines in full — they were captured truncated, so which key each consumes is inferred, not confirmed.
  2. Migrate both to a supported credential-retrieval path.
  3. **Re-run the share scan against the widened key set.** The completed scan used the original four-key pattern; `DORC_WebDeployPassword`, `DorcApiAccessPassword`, `DorcCliSecret` and `DeploymentServiceAccountPassword` are in SD-3a's denylist but their consumption has never been checked (**U-12**).
  4. Scan the production instance's script folder (**U-13**).

  The username carve-out needs no migration and can ship independently — that is the return on splitting by key type rather than denylisting uniformly.
- **SD-3b:** an additive `IsScriptVisible` flag on `ConfigValue`, defaulting to **visible for existing rows** and **hidden for new secure rows**. Existing deployments cannot break; admins tighten per key at their own pace. This inversion is what removes U-2 from the blocking set.

Note the interaction with SD-4: if per-environment credentials land in `ConfigValues`, a fixed denylist cannot cover keys minted per environment, and SD-3b stops being optional. Either SD-4 uses the Connect-backed provider, or SD-3b is a prerequisite for SD-4.

Because W-4's exposure is unconditional, **credential rotation is a precondition of this work, not a contingency.** Any credential that has been in a runspace must be treated as disclosed. **Applying that principle to the measured data, rotation scope is every secure config value reaching a runspace, not just the two deployment passwords:** `DORC_ProdDeployPassword`, `DORC_NonProdDeployPassword`, `DORC_WebDeployPassword` and `DorcApiAccessPassword` (all `IsForProd = NULL`, so in every runspace), plus `DorcCliSecret` and `DeploymentServiceAccountPassword` in their respective populations. An earlier revision scoped this to two keys, which contradicted the document's own principle. The usernames remain script-visible by design (SD-3a) and are not credentials to rotate.

**Rotation must be gated on SD-1a *and* SD-3a together — not SD-3a alone.** SD-3a removes the credentials from the property bag; it does nothing about W-1, where arbitrary C# executing inside the Monitor process reads them directly through `ScriptDispatcher.GetConfigValue` (`:299-304`) whether or not they ever reach a runspace. A credential rotated after SD-3a but before SD-1a is re-disclosed to the lowest-privileged user in the system the moment anyone submits a request. This was stated incorrectly in the previous revision, and it is the error an operator would have acted on directly while believing they were contained.

For precision on what does *not* gate rotation: W-7's log channels no longer carry the deploy credentials once SD-3a lands (they still carry other secure values, which is SD-6's own justification); W-2 and W-3 are use-of-identity vectors rather than credential-disclosure vectors post-SD-3a; and W-8's historic bundles are invalidated *by* rotation rather than gating it. Note also that rotating before SD-4 migrates credentials to a provider-backed store means rotating twice — expected, not a surprise.

### SD-4 — Bind execution identity to the environment (addresses W-6, partially W-9)

An optional per-environment deployment identity holding a *reference* to a credential, never the credential. `Environment` gains an additive nullable reference (it has no identity column today, so this is clean); credential resolution becomes environment-keyed on **both** `ScriptDispatcher` and `TerraformDispatcher`, with the current pair as documented fallback (C-02).

Resolution should go through a provider abstraction with two implementations — `ConfigValues`-backed and 1Password Connect-backed — selected by configuration. `OnePasswordClient` and `OnePasswordSecretsReader` already exist (`Dorc.Api/Security/OnePassword/`, `Dorc.Api/Services/OnePasswordSecretsReader.cs`), wired only into the API at `Program.cs:97-98`; SD-4 would relocate them to a shared assembly, fixing the C-07 namespace violation en route. The abstraction is what removes U-3 from the blocking set.

**Back-dependency on SD-2 and SD-7.** Both name "the deployment identity" when setting ACLs — SD-2 on the pipe, SD-7 on the bundle file. SD-4 makes that identity environment-dependent, so if SD-4 lands without revisiting them, deployments under a newly bound identity fail to read the pipe or the bundle. The ordering works provided SD-2 and SD-7 derive their ACL principal from the credential resolution point (`ScriptDispatcher.cs:67`, which precedes the pipe server start at `:93`) rather than from a constant. Recorded here because the dependency runs backwards from the later step to the earlier ones and is easy to miss.

On provisioning granularity: an account per environment is unbounded operational cost and maximises the target-server ACL remediation in U-8. Binding by **sensitivity tier** — prod-critical / prod-standard / non-prod — captures most of the available blast-radius reduction for a handful of accounts, while the data model still permits per-environment binding where warranted. The code change is identical; only the provisioning bill differs.

### SD-5 — Confine and verify script loading (addresses W-5)

- **Path confinement, enforced twice.** Reject any `Script.Path` that resolves outside the configured `ScriptRoot` after full canonicalisation. `Path.Combine` cannot express this; the check must be explicit. It must be applied **at write time** — in `ValidateComponents`, which is where an unvalidated path enters the system through per-project `Modify` — *and* at dispatch, because existing `Scripts` rows predate any validation and the write path is not the only way rows arrive. Validating only at write leaves stored rows unchecked; validating only at dispatch leaves the API accepting values it will later refuse, which is a worse operator experience and a weaker audit story.

  Given U-8 — the share is gated by pipeline or admin — path confinement is the single highest-value control in this direction. It converts "any project-Modify user can execute arbitrary code as the deployment account" back into "the gated pipeline is the only route", which is what the operator already believes to be true.
- **Remediate before enforcing.** Dispatch-time rejection of stored paths is a breaking change to every deployment currently relying on one, and C-02 forbids a flag day. The order must be: write-time confinement → inventory the stored population (**U-11**) → remediate offending rows → dispatch-time rejection. The previous revision gave the first and last steps and omitted the middle two.
- **Content verification at the point of read.** Verifying at dispatch is defeated by a TOCTOU the design would itself create: dispatch happens in the Monitor, but the executed bytes are read later, in another process, at `PowerShellScriptRunner.cs:46`. Anyone with share write access — exactly the W-5 threat — swaps the file in the window. The expected hash must travel in `ScriptGroup` and be verified inside the Runner immediately before execution.

  **This step must ship lockstep, not additively.** C-01 permits either, but only lockstep is sound here: `ScriptGroup` is deserialized with `System.Text.Json`, which ignores unknown members, so a runner that has not been upgraded would silently ignore an added `ExpectedHash` and execute unverified — SC-05 unmet with no signal. All three runners and both dispatchers ship in one MSI (`Setup.Dorc`), so lockstep is achievable. The Monitor must also define its behaviour when it cannot confirm the runner performed verification.
- Restoring `PSAuthorizationManager` is worth evaluating but does not substitute: `AddScript(ReadAllText(...))` executes an in-memory scriptblock not subject to file signature checks. Signature enforcement is a defence-in-depth addition, not the control.

Constraining the runspace itself — ConstrainedLanguage, cmdlet allowlisting — should be recorded as investigated and rejected: deployment scripts legitimately do arbitrary things, and over-constraining breaks the estate.

### SD-6 — Close the log disclosure channels (addresses W-7)

Remove property serialization from all five sites in §W-7, including the two on the production pipe path and the `Console` write at `PowerShellScriptRunner.cs:214-218`. Log key *names* where diagnostics require it, never values. This must land with or before SD-3, or SD-3 closes one channel while a second stays open for the same values.

### SD-7 — Expire script-group and Terraform artefacts (addresses W-8, W-12)

Delete the bundle once consumed, or on Runner exit. Correct `PrivilegedIdentities()` to grant the deployment identity explicitly rather than relying on the Monitor and deployment accounts coinciding (**U-5**). As in SD-2, **derive that principal from the credential resolution point rather than a constant**, so SD-4's environment-dependent identity is a drop-in.

Extend the same treatment to the Terraform working directory: move `DeleteTempTerraformFolder` into a `finally` so it runs on the failure path (`TerraformProcessor.cs:47-61`, `:296-315`), and create the directory under `DorcProgramData` with a restricted DACL rather than inherited permissions. Note that deleting the working directory does not reach the plan blob uploaded to storage (`TerraformDispatcher.cs:246-248`); that has its own retention and its own disclosure path through W-10.

### SD-8 — Attributable execution (addresses W-9)

Per-environment identity yields environment-granularity attribution in target-server telemetry. Per-request attribution would require impersonation semantics well beyond this scope and is not proposed. The IS should state plainly what SD-4 does and does not buy so the residual gap is recorded rather than assumed closed.

### SD-9 — Validate and pin Terraform sources (addresses W-11)

**`TerraformGitRepoUrl` and `ArtefactsUrl`** must both be validated against a host allow-list at write time in `ValidateProject` - which currently inspects neither - and again at dispatch, on the same two-point principle as SD-5. Round 3 established that `ArtefactsUrl` (W-16) carries the identical defect and is reached through the same endpoint. Credentials must be bound to the validated host rather than supplied to any URL. **This must explicitly cover `UseDefaultCredentials = true` in `AzureDevOpsServerWebClient` (`:41-60`)** - a Windows-credential leg that offers the API service account to an attacker-chosen host, which no token-scoping fix would catch. On the Terraform side: `GitCodeSourceProvider.cs:63-87` hands `TerraformGitPat` to any clone target, and the Entra `AzureBearerToken` is attached on a substring test against attacker-controlled text (`TerraformSourceConfigurator.cs:79-84`).

The pattern to follow already exists in the same file: `GitHubHostValidator.GetApiBase` (`TerraformSourceConfigurator.cs:169`, `GitHubHostValidator.cs:40-53`) does exactly this for the GitHub branch, with the rationale recorded in a comment. SD-9 is largely extending an established local pattern to the branch that lacks it.

Source pinning — a recorded commit SHA or artifact digest verified before `init` — is the Terraform counterpart of SD-5's hashing, per SC-05.

### SD-10 — Implement the Terraform approval gate (addresses W-10)

Replace the three `return true;` predicates with real checks against the deployment result's environment. `TerraformController` already injects `ISecurityPrivilegesChecker` and `IClaimsPrincipalReader`, so the dependencies are present and unused.

Two decisions the IS must make explicit rather than assume: whether approving an apply requires the same privilege as modifying the environment or a distinct approver privilege (segregation of duties would argue the latter, and would also prevent a requester approving their own plan), and whether plan *content* — which embeds resolved variable values — requires `ReadSecrets` on the environment rather than mere view rights.

This direction depends on nothing else in this document and should be sequenced first.


---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | Which property values in live estates use the `fn:` expression syntax, and what do they do? | User | Was blocking for SD-1b | **RESOLVED.** 11 distinct expressions, 133 occurrences, all `PropertyValue`, all `Secure = 0`, zero non-secure `ConfigValue` hits. Every expression is one shape: a double-quoted literal containing `$Token$` interpolations, followed by a chain of `.ToLower()`, `.ToUpper()` and/or `.Replace("<a>","<b>")`. No loops, reflection, assembly references, type construction or I/O. `EnvironmentNameWithUnderscores` (`fn:"$EnvironmentName$".Replace(" ","_")`) is 101 of the 133. **Decision: SD-1b adopts the fixed-grammar option — the Roslyn dependency is removed outright, not sandboxed.** Residual: secure `ConfigValue`s are encrypted at rest and could not be inspected by query; they *do* reach the evaluator, so the grammar must fail closed on anything it cannot parse rather than falling back to compilation. |
| U-2 | Which secure config values are referenced by scripts on the share? | User | **No longer blocking.** SD-3b's default-visible inversion means no existing deployment can break, so the inventory is useful for tightening but gates nothing. If wanted: enumerate keys from `ConfigValues` and grep the share for `$<key>`. | Resolved as non-blocking by design change. |
| U-3 | Are Monitor hosts able to reach 1Password Connect? | User | **No longer blocking.** SD-4's provider abstraction makes this a deployment-time choice between two implementations rather than a design dependency. | Resolved as non-blocking by design change. |
| U-4 | Is there an existing per-environment service-account convention? Who provisions, what lead time, how many environments? **Environment count answered: 1,286 — 1,084 non-production, 202 production. This settles the granularity question: one service account per environment is not viable at that scale, so SD-4 binds by sensitivity tier and the data model retains per-environment binding for the few cases that warrant it.** The provisioning convention and owner remain open. | User | **Non-blocking.** It gates provisioning cost and adoption pace, not the code: SD-4 has a documented fallback under C-02, so the implementation can land and environments bind over time. The previous revision labelled this "blocking for value realisation", which contradicted §7's summary sentence and would have halted progress under the "blocking unknowns halt progress" rule for no gain. | Open, non-blocking. Recommendation: bind by sensitivity tier rather than per environment (SD-4). |
| U-5 | Is `DORC_*DeployUsername` a local Administrator on Monitor hosts? The file transport's ACL (`ScriptGroupFileWriter.cs:91-99`) excludes the deployment account, yet the Runner reads that file — so either the accounts coincide, the deploy account is a local admin, or the Debug path is broken. Each answer is materially different: the middle one is a privilege finding in its own right and strengthens W-6. | User | Non-blocking — SD-7 corrects the ACL either way | Unresolved. |
| U-6 | Can a gMSA be used with `LogonUser` + `CreateProcessAsUser`? | Agent | Non-blocking | **RESOLVED — no.** `CreateProcessAsUser` requires a token, and the supported way to obtain a gMSA token is to *run as* the account (a service configured with that identity), not to `LogonUser` with a password; gMSA passwords are not retrievable for password-based logon. Adopting gMSA would require per-environment Runner services instead of Monitor-side impersonation — a restructure beyond this scope. Recommendation: retain username/password, take the value from not storing them in the DOrc database (SD-4, Connect-backed) plus rotation. |
| U-7 | Effective DACL on the named pipe as currently constructed. | Agent | Non-blocking | **RESOLVED in effect.** `CreateNamedPipe` with no security attributes receives the documented Win32 default DACL, which grants read to Everyone and to anonymous; the pipe is `PipeDirection.Out`, so read is precisely the access needed to receive the bundle. Confirm on a Monitor host before the IS cites it as exploited rather than exploitable; SD-2's fix is unchanged either way. |
| U-8 | Are scripts on the share Authenticode-signed, and is there change control on it? | User | Non-blocking, but the answer **raised** W-5 rather than lowering it | **RESOLVED.** Promotion onto the share is gated — via a controlled pipeline or an administrator. That is a real control, and it means the share itself is not the soft entry point. It does not close W-5: `Script.Path` is set from `ComponentApiModel.ScriptPath` with no validation, through an endpoint gated only by per-project `Modify`, and `Path.Combine` honours a rooted path by discarding `ScriptRoot`. The gated pipeline is therefore bypassable without touching the share. W-5's capability requirement is revised **down** from "write to the share" to "`Modify` on one project", moving it to rank 2. Signature enforcement remains moot regardless: `AddScript(ReadAllText(...))` executes an in-memory scriptblock not subject to file signature checks. |
| U-9 | Do existing scripts depend on running specifically as the shared account — e.g. target-server ACLs naming it? Principal migration cost driver for SD-4. | User | Non-blocking for design; material for sequencing | Unresolved. |
| U-10 | **Does anything consume the deployment credential config keys?** SD-3a is a denylist with no off switch; if a script re-uses a credential to reach a downstream system, SD-3a breaks it on the first deployment after release. | User | **RESOLVED for non-production; open for production.** Consumption exists, so SD-3a's password half is a migration rather than a drop-in. Non-prod migration is two scripts. Production is unscanned. | **DB slice RESOLVED, and non-empty.** `FOIT_CondaProxyIdentity` interpolates `$DORC_NonProdDeployUsername$` across three property values. **No row in either table references either password.** This splits SD-3a — see the revised direction: passwords are denylisted unconditionally (nothing consumes them), usernames remain script-visible (something does, and a username is an identity rather than a credential). **Share grep COMPLETE for the non-production script folder — 11 matching lines across 9 files in EACH of the two instances, and the two sets are identical** (`…\Scripts\Scripts.ST\` non-production, `…\Scripts\Scripts.PR\` production; the `prod` element of the UNC is DFS namespace, not DOrc prod-ness). The pipeline promotes the same script set to both, so the migration is the same three scripts in each. The distribution vindicates the username/password split:

**Username consumers (unaffected by SD-3a as designed) — 8 lines across 7 files.** Five sit at line 6 in `Test-RequiredP…` calls inside "Setup logon as a service" basebuild scripts (`137 AMP`, `146 TREX`, `147 Power Desk`, `151 FOM`, `152 FOP`), plus two lines in `RecycleEnv.ps1` and the `$userName` declaration at `DeploySSRSPowerBIReports.ps1:8`. Granting a logon right needs the account *name*, not its password. Because SD-3a keeps usernames visible, **none of these require migration** — which is the whole return on making that split rather than denylisting all keys uniformly.

**Confirmed password consumers — 3 lines across 3 files. These gate the password denylist:**

| File | Line | Evidence |
|------|------|----------|
| `00 Generic\DeploySSRSPowerBIReports.ps1` | 9 | `[string]$userPassword = $DORC_NonProdDep…` — direct, key named |
| `139 UploadersDownloaders\ControllApp…` | 11 | `$password = ConvertTo-SecureString -Strin…` |
| `51 TFS\TFS Refresh\020 - Hosts File.ps1` | 83 | `$password = ConvertTo-SecureString -S…` |

The count rose from two to three when the production scan returned fuller lines: `DeploySSRSPowerBIReports.ps1` had been classified as a username consumer on the strength of two truncated `[string]$use…` declarations. Line 8 is `$userName`; **line 9 is `$userPassword`**. A truncation-driven misclassification, corrected here — and a caution against reading truncated evidence as settled.

Worth attention during migration: that script sits in the **production** folder yet consumes `$DORC_NonProdDeployPassword`. Either production SSRS deployments genuinely run under the non-production account, or it is a latent bug. Either way it belongs to whoever owns the script.

**Consequence for SD-3a:** the password denylist requires those two scripts to be migrated to a supported credential-retrieval path first. In non-production that is a two-script migration, not an estate-wide one.

**Two residuals before SD-3a's password half can ship:**
1. The captured lines are truncated, so which key each `$password` assignment matched is inferred rather than confirmed. Both files need their matching line read in full.
2. The production instance's script folder has not been scanned. Scripts are promoted into each instance through the same gated pipeline, so the non-prod result is a reasonable proxy — but it is a proxy, and production is where the `DORC_ProdDeployPassword` denylist actually bites. `Select-String` is read-only, so the scan itself carries no risk to production; the decision to run it is the operator's. |
| U-11 | **How many `Scripts` rows resolve outside the configured `ScriptRoot`?** SD-5's dispatch-time rejection is a breaking change to every deployment relying on such a row. | User | Was blocking for SD-5's dispatch-time half | **RESOLVED — four rooted paths, of which three are genuinely off-share.** Re-triaged after establishing that `\\Itss.global\prod\GLO\APP\DORC\Scripts\Scripts.ST\` is the configured `ScriptRoot` for this instance (the `prod` element is DFS namespace, not DOrc prod-ness):

| Id | Name | Path | Verdict |
|----|------|------|---------|
| 12862 | Create ResourceGroup via Terraform | `\\ltss.global\prod\GLO\APP\DORC\Scripts\Scripts.ST\…` | **Benign in effect** — rooted, but resolves to `ScriptRoot` itself. Still bypasses confinement mechanically. |
| 1647 | 025 - MCB Marketing Processor Certificate Permissions | `\\trading1\Common\DevSupport\Deployv2\Scripts.QA…` | **Genuinely off-share** — different file server |
| 2323 | FOELK 0XX - AB Components | `\\trading1\common\jhai\ABissue\SyncIssue.ps1` | **Genuinely off-share** — personal/incident folder |
| 2942 | MOBOELK 0XX - AB Components | `\\trading1\common\jhai\ABissue\SyncIssue.ps1` | **Genuinely off-share** — same file |

The `Finding` column in query 3 classifies by *pattern* (rooted or traversing), not by comparison against the configured `ScriptRoot`, so it over-flags: any absolute path pointing at the share itself is reported as off-share when its effect is benign. The IS must do the comparison properly, canonicalising both sides.

The `IsPathJSON` rows sampled are relative (`00 Generic\DeployDacpac.ps1`) and stay within the share; the full set still needs review.

**SD-5's dispatch-time half requires the remediation step:** the three `trading1` rows must be relocated into `ScriptRoot` or explicitly exempted before enforcement, or three live deployments break. Row 12862 needs only rewriting as a relative path. |

| U-12 | **Do any scripts consume the secure keys added to SD-3a's denylist after the original scan?** | User | Was blocking for SD-3a's password half | **RESOLVED — and it reversed the design.** The production share was scanned with all seven secure keys. `DORC_ProdDeployPassword`, `DORC_WebDeployPassword` and `DorcApiAccessPassword` have **zero** consumers, so denylisting them is a drop-in and **S-000's remaining precondition is satisfied**. But `DeploymentServiceAccountPassword` appears in roughly 60 files and 80 call sites as the mechanism by which scripts reach target servers, `ProgetAccountPassword` in about 12, and `DorcCliSecret` in 2. Those three cannot be denylisted at all. See the revised SD-3a: the secure keys are two classes, not one. |
| U-13 | *(RESOLVED — see resolution column.)* **Is the production DOrc instance's *database* configured as System Test's is?** All database inventory here came from `DeploymentOrchestrator_ST`. Production's *script folder* has since been scanned and matches ST's exactly, but production's `ConfigValue` table has not: does its `DORC_ProdDeployPassword` also carry `IsForProd = NULL`? The mechanism is identical in both instances; only the data differs, so this decides whether W-4 is a live production incident or an ST-only one. | User | Was decisive for incident severity and for SD-0 | **RESOLVED — production matches System Test.** The user queried the production instance directly. `DORC_ProdDeployPassword`, `DORC_NonProdDeployPassword`, `DORC_WebDeployPassword` and `DorcApiAccessPassword` all carry `IsForProd = NULL` in production, reaching every one of its 1,285 environments. The production script folder was separately scanned and matches System Test's exactly. **W-4 is a live production incident and SD-0 is immediately actionable.** |
| U-14 | **Who records a script's content hash, and how is it re-recorded when the gated pipeline promotes a new version?** Scripts change on the share outside DOrc's knowledge (U-8), so a naive implementation breaks dispatch on every legitimate update — a flag day, contrary to C-02. The adoption path needs an unrecorded-hash mode. | Agent + User | Non-blocking — gates only SD-5's content-verification half, not path confinement | Unresolved. |

**Status after the estate inventory and the round-3 sweep: one unknown blocks one step — U-12, gating SD-3a's password denylist.** U-1 resolved and decided SD-1b's variant. U-10 resolved for both instances against the four-key pattern, establishing a three-script migration. U-11 resolved and made SD-5's remediation step mandatory. U-4's environment count settled SD-4's granularity. U-13 is partially resolved and, while not blocking the plan, decides whether W-4 is a live production incident.

**The inventory converted W-4 from a design weakness into a live production incident.** `DORC_ProdDeployPassword` carries `IsForProd = NULL`, so the production deployment password is currently delivered in cleartext to the deployment scripts of all 1,084 non-production environments. Rotation of the deployment passwords is no longer only a sequencing precondition of this plan — it is an outstanding remediation in its own right, and the plan's value is partly that it stops the rotated credential from being re-disclosed immediately.

**A note on what U-10 implies for the outstanding grep.** `FOIT_CondaProxyIdentity` exists because something needed the deployment account's *identity* to reach a downstream system — a Conda proxy. Whatever authenticates as that identity needs a password too, and no property or config value supplies one. Either it obtains the password some other way, or it reads `$DORC_NonProdDeployPassword` directly from the injected variable scope, which is W-4's exposure being consumed deliberately rather than incidentally. The DB cannot distinguish these; the share grep can, and it is the one that decides whether the password denylist is a drop-in or a migration. This is why the grep should not be skipped on the strength of the SQL result looking benign. U-2, U-3, U-4 and U-8 are non-blocking — downgraded by changing the design rather than by obtaining answers: inverting SD-3b's default, adding SD-4's provider abstraction, relying on SD-4's documented fallback, and moving verification to point-of-read respectively.

**What is unblocked today matters more than what is blocked.** These steps depend on no unknown and no other direction, and between them cover the rank-1 weakness and both local-host weaknesses:

- **SD-10** — implement the three stubbed-open Terraform approval predicates (W-10, rank 1)
- **SD-1a** — contain `fn:` evaluation by provenance (W-1, rank 2), plus the per-request cache and `Substring(3)` fixes
- **SD-2** — explicit process DACL, pipe `PipeSecurity`, client-side server verification, await server readiness (W-2, W-3)
- **SD-6** — remove property serialization from every runner log site (W-7)
- **SD-7** — delete bundles and Terraform working directories, including on the failure path (W-8, W-12)
- **SD-5 (write-time half)** — path confinement in `ValidateComponents`; and **SD-9's** write-time half in `ValidateProject` (W-5, W-11)

Superseded from earlier revisions: the original U-1 (`IsForProd`/`Secure` values of the `DORC_*Deploy*` rows) is **not blocking**. W-4 establishes the exposure is unconditional — every `IsForProd` value exposes the credential to some population — so the query determines *which* population is affected, not *whether* one is. It remains worth running for incident scoping. Credential rotation is a stated precondition gated on SD-1a and SD-3a (see SD-3).

---

## 8. Out-of-Scope Risks

- **API authorization enforcement remains ad hoc.** Deferring the controller refactor leaves authorization a per-endpoint convention with no chokepoint below it. This work reduces what an escalation *yields*; it does not reduce the likelihood of one. Given W-1 — where the entry point is submitting a request, i.e. the privilege the API grants most freely — the sibling HLPS is more urgent than the original draft implied.
- **Target-server privilege is unchanged.** Binding an environment to its own identity does not restrict what that identity can do once on a target server. This is the residual half of SC-07 and requires domain-side privilege reduction outside DOrc.
- **Share write access remains code execution — but is gated.** Per U-8, promotion onto the share runs through a controlled pipeline or an administrator, so this residual risk is owned and accepted. SD-5 confines paths and verifies content; it does not, and need not, control who may write to a share that already has change control.
- **Inconsistent authorization for one outcome is in scope's blast radius but not in its remit.** Setting a script path requires PowerUser/Admin through `RefDataScriptsController` and per-project `Modify` through `RefDataController`. SD-5 closes the *consequence* by confining paths, but the underlying inconsistency is exactly the class of defect the deferred sibling HLPS exists to address, and it demonstrates that the deferral has a real cost.
- **Migration is where the risk concentrates.** Every direction is designed to be a no-op until adopted (C-02), but the adoption steps — reclassifying config values, rotating credentials, binding environments — are exactly the steps that can break live deployments. The IS must treat each adoption step as separately reversible, not merely each code step.
- **`#if DEBUG` transport divergence.** The production pipe path is exercised only in Release builds and the file path only in Debug. SD-2, SD-6 and SD-7 touch both; coverage must reach both, or a change validated in development will not have been validated where it ships.
- **Credential rotation is assumed but not owned here.** SD-3 makes rotation a precondition; executing it is an operator activity this HLPS does not sequence.
- **The sibling HLPS does not yet exist.** `docs/` contains no API-authorization document, so §3 and this section currently defer to a plan with no owner and no raise date — an unowned risk acceptance rather than a deferral. Given W-10 (a stubbed-open authorization predicate) and W-5/W-11 (one outcome reachable at two different privilege levels through two endpoints), the sibling should be raised alongside the Implementation Sequence for this one rather than after it. Recommend it be created as `docs/api-authorization-enforcement/` with a named owner before this HLPS is approved.

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

**Accepted — MEDIUM/LOW:** `Path.Combine` does not confine to `ScriptRoot`, making the `Scripts` table an equivalent entry point; `Dorc.TerraformRunner/Pipes/` missing from scope against C-01; SC-01 was a denylist self-test, restated as a positive invariant; SC-03 and SC-06 were unverifiable as written; the file-transport ACL excludes the account that must read it; existing log sites disclose the property bag that SD-3 exists to protect; the static expression cache crosses environments; SD-3a/SD-4 variant interaction; `LOGON32_LOGON_NETWORK_CLEARTEXT`, password in a managed string, leaked `LogonUser` token; `HighAvailabilityEnabled` does not exist (corrected to `Kafka:Enabled`); FullLanguage citation corrected to implicit-by-`CreateDefault()`; U-6 resolved (gMSA ruled out with reasoning). **U-5 was *not* resolved and remains open, non-blocking** — whether the deployment account is a local Administrator on Monitor hosts is a live-environment fact that source alone cannot settle. The previous revision's triage record overstated this as "U-5 and U-6 resolved"; corrected here.

**Strengthened beyond the finding as reported:** the Runner log disclosure was reported as Debug-only. It is not. `Dorc.NetFramework.Runner/Pipes/ScriptGroupPipeClient.cs:73` and `Dorc.TerraformRunner/Pipes/ScriptGroupPipeClient.cs:73` log properties on the **release** pipe path, and the .NET Framework runner is the default selection for any script without an explicit PowerShell version. Promoted to W-7 as a production exposure.

**Rejected — 2 findings:**

| Finding | Reason for rejection |
|---------|---------------------|
| `"IsSecure": false` on `DORC_NonProdDeployPassword` in `DeploySettings.template.json` shows the shipped default stores the password unencrypted, corroborating W-4 | The flag governs the *installer's own* settings file — whether the value is a DPAPI-protected `SecureString` locally (`DeploymentCommon.ps1:145-152`, `:207-221`). It never reaches `ConfigValue.Secure`. `$getCredentials` in fact **throws** (`DeploymentCommon.ps1:182`) `"Password property is not a secure string"` when it is false, so the shipped value is not a working credential configuration at all. Citing it would put a false claim into the document. |
| The `Status403Forbidden` count is 68, not 66 | 66 is correct for the stated scope, `Dorc.Api/Controllers/` across 22 files; the reviewer's 68 included `Dorc.Api/ConfigValuesController.cs`, which sits outside that directory. Independently confirmed by a second reviewer. §3 now states the scope of the count explicitly (66 in `Controllers/`, 69 across `Dorc.Api`) so it is not re-litigated. |

**Panel disagreement, resolved:** on prioritisation, the security reviewer held that W-4-before-W-6 was correct reasoning but argued below the waterline, since three directly-exploitable paths were absent from the draft entirely. Verified and accepted — the ranking is now by attacker capability required, and the original W-3-versus-W-1 question is settled by both sitting below three paths that need less.

**Round 1 of a maximum of 3.** No finding was deferred.

---

## 10. Adversarial Review — Round 2 Triage

Second panel of three (security completeness on fresh eyes, regression and fidelity against the round-1 record, approval-gate and IS-sequenceability). Round-2 verdict from the approval reviewer was **REVISE**, with six blocking items. All are applied below. Every claim was re-verified against the code before acceptance.

**Accepted — CRITICAL:**

| Finding | Disposition |
|---------|-------------|
| `TerraformController`'s three authorization predicates are `return true;` stubs, making a DOrc login sufficient to approve a production `terraform apply` and read any plan | Accepted. Verified at `:210-224`, called at `:61`, `:108`, `:171`; the controller injects `ISecurityPrivilegesChecker` and uses it for none of them. New **W-10 at rank 1**, above everything previously in the document. New SD-10. §3's out-of-scope carve-out narrowed to exclude stubbed-open predicates. Flagged as not needing to wait for this plan. |

**Accepted — HIGH:** `TerraformGitRepoUrl` is unvalidated by `ValidateProject`, settable at per-project `Modify`, cloned and `apply -auto-approve`-ed, with `TerraformGitPat` supplied to any URL and the Entra bearer token attached on a substring test against attacker-controlled text — new W-11 at rank 3, new SD-9. `terraform.tfvars` writes the whole property bag under `%ProgramData%\dorc` with inherited ACLs and is not deleted on the failure path — new W-12, SD-7 extended, new SC-08a. `ScriptGroup`'s three token fields are not config values and so cannot be reached by SD-3's classification — new W-13; SC-03 restated over the serialized `ScriptGroup` rather than the property bag. SC-05's hash regime is inapplicable to the Terraform path — SC-05 split, with source pinning as the counterpart. Rotation was gated on SD-3a alone when W-1 re-discloses the credential regardless — now gated on SD-1a **and** SD-3a, with the reasoning recorded. SD-1 offered only mechanism options, all needing U-1, making the rank-2 weakness needlessly unstartable — provenance-based containment added as SD-1a, and U-1 re-scoped to gate only SD-1b. SC-09 contradicted SD-3a outright — SC-09 now excludes the reserved keys explicitly. Two unregistered blocking unknowns added: U-10 (SD-3a breaking scripts that consume the deploy credential) and U-11 (the stored out-of-`ScriptRoot` path population).

**Accepted — MEDIUM/LOW:** `NonProdOnly` at `ComponentProcessor.cs:128` is inside the PowerShell branch only, so W-6's "the only execution-time gate" claim was inaccurate and the Terraform branch needs its own insertion point first. `TerraformProcessor.cs:261` is a sixth log-disclosure site, so SC-06 is restated as an invariant over runner logging rather than an enumeration. SD-5 cannot ship additively — `System.Text.Json` ignores unknown members, so an un-upgraded runner would skip an added hash silently; lockstep required. SD-4 carries a back-dependency on SD-2 and SD-7's ACL principals, resolvable by deriving them from the credential resolution point. U-4's "blocking" label contradicted §7's summary — reclassified non-blocking with reasoning. §3 gained the five missing scope entries. §8's scope boundary restated as *input validation on write paths is in scope; who may reach those paths is out*. Pipe-name unpredictability must be per server instance, not per request. `TerraformDispatcher` also lacks `ScriptDispatcher`'s cancellation kill-registration. Citation drift corrected: `Task.Factory.StartNew` is at `ScriptGroupPipeServer.cs:25` not `ScriptDispatcher.cs:93`; `Path.Combine` at `:311,323` not `:310,322`; the installer throw at `DeploymentCommon.ps1:182`.

**Accepted — regression against the round-1 record:** §9 claimed "U-5 and U-6 resolved rather than left open". Only U-6 was resolved; U-5 requires a live-environment fact and correctly remains open. §9 corrected — an overbroad triage record is itself a defect, since it misrepresents what was done.

**Verified, no change:** the regression reviewer independently re-derived every round-1 accepted finding as genuinely applied in the body rather than merely recorded, re-checked roughly 40 citations against source with only the three drifts above, and confirmed **both round-1 rejections were correct** — the installer `IsSecure` flag governs local DPAPI handling and never reaches `ConfigValue.Secure`, and the `Status403Forbidden` count is 66 in `Dorc.Api/Controllers/` versus 69 across `Dorc.Api`. The security reviewer separately confirmed `GetAllConfigValues(true)` has exactly one non-test caller, and found nothing new in the Runner interop, `GitHubArtifactDownloader` (host allow-list, size cap, safe extraction all correct), `DirectoryHelper.ValidateAndNormalizeSubPath`, `RequestController.Post`'s component-belongs-to-project validation, or the server-side `IsProd` derivation.

**Carried, not applied:** W-9 has no dedicated success criterion. This is deliberate — SD-8 records what per-environment identity does and does not buy for attribution, and the residual is stated in §8. W-9/SD-8 is **intentionally criteria-exempt**: a documented improvement, not a testable requirement.

**Round 2 of a maximum of 3.** Blocking approval: answers to U-1, U-10 and U-11 — one query and two greps. The approval reviewer's assessment was that with the six items applied and those answers returned, this should approve on round 3.

---

## 11. Adversarial Review - Round 3 Triage, and Escalation

Third panel of three (approval gate, internal consistency and fidelity, final security sweep over areas the earlier rounds did not cover). Under `CLAUDE.md` this is the **last permitted round**.

### Outcome

The approval gate returned **APPROVE WITH CHANGES** on six pre-approval items, all of which are applied below. The consistency reviewer returned ten items, all applied. **But the security sweep returned three new HIGH findings and one MEDIUM, one of which invalidated the document's central framing.** The approval verdict was formed without them.

**Resolution.** The document was held at REVISION pending user decision, since round 3 is exhausted and `CLAUDE.md` directs escalation rather than a fourth round. **The user declined round 4 and accepted the round-3 additions**, resolving escalation item 1. The status is now APPROVED on that basis: the panel's APPROVE WITH CHANGES verdict with every change applied, plus four late-arriving weaknesses the user has consciously accepted into scope rather than re-panelled. Escalation items 2-4 remain open and are carried into the Implementation Sequence as step preconditions, not as approval blockers.

### Applied - approval gate

SC-09 recited the superseded four-key denylist and is now defined by reference to SD-3a. Rotation scope was two keys against the document's own principle, and now covers every secure value reaching a runspace. SD-3a's stale "conditional on the outstanding share grep" is replaced by the completed result and a four-step precondition. SC-05b added for W-11's credential-binding leg, which had no criterion. U-14 registered for the script-hash lifecycle - unaddressed, and naively implemented it would break dispatch on every legitimate script promotion. **SD-0 added**: the interim `IsForProd` containment, one row and reversible, which the document had omitted entirely while classifying the exposure as active and maximal.

### Applied - consistency

The header overstated U-4 as resolved. W-5 still claimed four off-share scripts after U-11's re-triage found three. SC-08a sat out of numeric sequence. W-13 had no solution-direction owner. `DORC_WebDeployUsername` fell into neither SD-3a bucket. U-10's counts did not reconcile with its own itemisation. The reviewer also named the document's weak seam precisely: **a design change made in section 6 does not reliably reach section 4's criteria** - which is how SC-09 and the rotation scope both went stale.

### Applied - security sweep (new weaknesses)

| Finding | Effect on the document |
|---------|------------------------|
| **W-15** - `DaemonStatusProbe` is a third verbatim copy of the credential logic, calling `LogonUser` in the **API process**, reached through `DaemonStatusController.Get` which has no authorization check | **Invalidated section 1's thesis.** Two rounds asserted the Monitor was "the most privileged component in the system". It is not; the API host is a co-equal holder of the production credential. Section 1 rewritten, SC-07 restated as an invariant over every credential-resolution site, W-6's "two sites" corrected to four across two processes. |
| **W-14** - `BuildUrl` is unvalidated, becomes `$DropFolder$`, and DOrc's own `RunDeployment.ps1` dot-sources code from it | New rank-2 weakness alongside W-1. SD-5's confinement principle extended to `DropLocation`. |
| **W-16** - `ArtefactsUrl` carries W-11's defect on a second field, and its `else` branch sets `UseDefaultCredentials = true` | New rank-3 weakness. SD-9 widened; the Windows-credential leg is called out because no token-scoping fix reaches it. |
| **W-17** - `Program.cs:441` exact-matches `OAuth`, so `OAuth+WinAuth` mode never applies the global scope policy | New rank-4 weakness. Distinguished from the deferred scope: this is a policy silently not applied by the composition root, not a hand-written check. |

Two LOW findings (unguarded status transitions reachable from the API; `CopyEnvBuild` authorising only the target environment) are recorded here and carried to the IS rather than promoted to weaknesses - neither yields an authorization bypass that changes the plan's shape.

### Corrections to previously reported evidence

- **Instance provenance.** All database inventory came from `DeploymentOrchestrator_ST`, the System Test instance - visible in the client status bar and not noted at the time. W-4's blast-radius figures describe ST's configuration. Production's script folder has since been scanned and matches exactly; production's `ConfigValue` table has not (**U-13**).
- **Password-consumer count 2 to 3.** The production scan returned fuller lines and showed `DeploySSRSPowerBIReports.ps1:9` is `$userPassword = $DORC_NonProdDep...`, not a second username declaration. It had been classified from truncated output. Truncated evidence was read as settled; it was not.
- **A widened grep was specified but never ran.** An edit intended to broaden the scan pattern to the full secure key set silently failed to apply, so both completed scans used the original four-key pattern. `DORC_WebDeployPassword`, `DorcApiAccessPassword`, `DorcCliSecret` and `DeploymentServiceAccountPassword` sit in SD-3a's denylist with their consumption unchecked (**U-12**).

### Escalation to the user - required before approval

1. **Whether to accept the document as it stands, or to convene a fourth round.** Round 3 found four new weaknesses, so the discovery curve has not flattened, and a fourth round would probably find more. Against that: the four are all instances of classes already named (unvalidated input reaching an execution path; credential resolution duplicated; a principle applied on one branch and not its neighbour), so their marginal value to the *plan* is lower than their count suggests. The judgement is a governance one and is the user's.
2. **U-12** - re-run the share scan with the widened key set. Blocks SD-3a's password half only.
3. ~~**U-13** - query production's `ConfigValue` table.~~ **CLOSED.** Production matches System Test; W-4 is a live production incident.
4. **The sibling HLPS has no owner.** Section 8 states it should be raised as `docs/api-authorization-enforcement/` with a named owner *before this HLPS is approved*. It does not exist. W-17 and the two LOW findings above would land there. The condition must be honoured or consciously downgraded; only the user can do either.

**Immediate actions that do not wait for any of this:** apply SD-0, rotate the deployment passwords, and implement the three `TerraformController` predicates. None depends on the plan being approved.
