# S-022 — Attribution reached and not reached

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | DELIVERED (documentation step)                               |
| **Step**    | S-022                                                        |
| **Address** | W-9 — deployment actions are not attributable below DOrc     |
| **HLPS**    | HLPS-deployment-privilege-containment.md (APPROVED), SD-8     |
| **Folder**  | docs/deployment-privilege-containment/                        |

This step is documentation rather than code, and it is deliberately exempt from a testable criterion. Its purpose is to state precisely what S-021 buys for attribution and what it does not, so that the residual is recorded rather than assumed closed. A weakness that everyone believes was fixed is worse than one everyone knows is open.

---

## 1. What W-9 says

W-9 is an absence, not a vulnerability. It requires no attacker capability at all.

Because every production deployment authenticated as one account and every non-production deployment as another, the logon and process-creation telemetry on a target server could not attribute an action to a request, a project, or a person. Every entry said the same thing. DOrc's audit trail ended where the Runner started: inside DOrc, a deployment is a request raised by a named user against a named environment; on the target server, it is `CORP\svc-dorc-prod` again.

---

## 2. What S-021 delivers

**Account-scoped OS-level attribution — reaching a single environment only where that environment's resolved underlying OS account is exclusive to it, and reaching a bounded sensitivity tier or environment group only where the resolved underlying OS account is exclusive to that group.**

An environment may now name an `ExecutionIdentity` reference, and every deployment into that environment authenticates to its target servers under the resolved account. When that resolved underlying OS account is provisioned exclusively for one environment (not shared with any other named `ExecutionIdentity` reference or legacy tier-default fallback account), target-server telemetry (such as Windows Security Events `4624` and `4688`) distinguishes that environment's deployments from every other environment's and from the fallback population without DOrc-side correlation. When multiple environments of the same sensitivity tier share a dedicated underlying OS account — which HLPS §6 (SD-4) and the IS explicitly permit and recommend across a 1,285-environment estate — OS-level telemetry attributes the deployment to that bounded sensitivity tier or group of environments (provided that OS account is not also resolved outside the group or by a tier fallback account), not to a single environment within it.

Two qualifications matter and are easy to lose.

**Attribution is a property of migration state and account exclusivity, not of the release.** The identity reference is null on every existing environment, and a null reference resolves the legacy `IsProd` tier default exactly as before. Shipping S-021 changed the attribution of nothing. What changes it is an operator binding an `ExecutionIdentity` on an environment and provisioning that account on the target servers — and whether the resulting OS-level attribution reaches one environment or a bounded group depends on whether the resolved underlying OS account is exclusive to that environment or bounded group (and not shared outside the group or with legacy tier-default fallback accounts). Until an environment is migrated onto a distinct underlying OS account, its deployments remain indistinguishable in target telemetry from whatever other environments or fallback resolutions share that OS account.

The per-request `bound` versus `fallback` resolution counts (`RequestExecutionIdentityAdoption`, logged by `PendingRequestProcessor` and summarised as `bound / (bound + fallback)`) measure **execution-identity binding adoption** — the share of credential resolutions using an explicit `ExecutionIdentity` reference rather than the legacy tier-default fallback — **not** underlying OS account exclusivity. `RequestExecutionIdentityAdoption` increments `bound` whenever a non-empty `ExecutionIdentityReference` resolves to a credential, without inspecting whether other environments, other named `ExecutionIdentity` references, or a legacy tier-default fallback resolve to the same underlying OS account. An explicit `ExecutionIdentity` binding therefore does not automatically guarantee OS-level separation from the fallback population or from other tiers if multiple named references (or a named reference and a tier fallback) resolve to the same underlying OS account — those resolutions still count as `bound`. Moving `bound / (bound + fallback)` to 100% proves that deployments have migrated off the legacy fallback path, not that every environment or bounded group has an exclusive OS account.

**The data model binds per environment, while OS telemetry granularity follows underlying OS account sharing — and never reaches the request.** Although `Environment.ExecutionIdentityReference` is stored per environment, HLPS SD-4 notes that provisioning one account per sensitivity tier (`prod-critical`, `prod-standard`, `non-prod`) is the operationally viable norm, with exclusive per-environment accounts reserved for environments that warrant them. Underlying OS accounts exclusive to one environment yield single-environment OS telemetry; underlying OS accounts shared exclusively within a bounded sensitivity tier yield tier/group OS telemetry that must be narrowed further via DOrc's correlated deployment timestamps, logs, and execution receipts. What no configuration of S-021 yields is OS-level attribution finer than a single environment.

---

## 3. What S-021 does not deliver

**Per-request attribution.** Many requests target one environment. Two deployments into the same environment, from the same Monitor host, minutes apart, are the same principal on the target server. Nothing in the target's record separates them.

**Per-user attribution.** The user who raised the request never reaches the target server in any form. The Runner is started with `CreateProcessAsUser` under the deployment credential; the requesting user's identity is not carried, impersonated, or asserted anywhere past the API.

**Per-project or per-component attribution.** Same reason. The target sees a logon and a process, not what DOrc was doing.

**Attribution for anything the deployment script does under its own credentials.** A script that authenticates onward with a credential from the property bag attributes to *that* credential, not to the deployment identity. That is a property of what deployment scripts do, not of execution identity, and no change to execution identity reaches it.

---

## 4. Where the seam actually is

It is worth being concrete about what each side retains after a deployment finishes, because the join between them is the only post-incident attribution available today and its ambiguity is specific rather than general.

**DOrc's side of the seam is well populated, provided transient live state is distinguished from retained forensic evidence.**

- **Durable post-incident forensic records:** For each deployment DOrc persists the request and who raised it, the target environment, the project and component, the deployment result timestamps, status, and result log, the persisted execution receipts and artefact/script digests (`PowerShellExecutionReceipt` and `TerraformDeploymentReceipt` / `TerraformDeploymentOutcome`), the Monitor service logs, and the UNC path of the Runner's own log file. At the operating-system audit layer, Windows Security Event `4688` (process creation) and `4624` (logon) audit trails on the Monitor host and target servers provide the corresponding OS-side records. Within DOrc's retained records, a deployment is fully attributable.
- **Transient live cancellation state (not retained for forensics):** While a Runner process is executing, `ScriptDispatcher` and `TerraformDispatcher` record `(ProcessId, RequestId)` in `DeploymentRequestProcesses` via `AssociateProcessWithRequest`, and both dispatchers delete that row via `RemoveProcess` in `finally` as soon as the Runner exits (`DeploymentRequestStateProcessor` likewise deletes it after terminating processes on cancellation or restart); the Runner PID is not persisted on execution receipts either. `AssociateProcessWithRequest` / `RemoveProcess` exists strictly for active cancellation tracking while the Runner is alive; post-incident forensics cannot rely on that table and must use the persisted execution receipts, deployment result logs, Monitor/Runner logs, and Windows Security Event `4688`/`4624` audit trails instead.

**The target server's side holds the logon account, the source Monitor host, and the time (`4624`/`4688`).**

So the post-incident join across the seam is `(account, source host, time window)` against DOrc's persisted deployment timestamps, logs, and execution receipts. That join identifies a deployment uniquely **only when a single deployment under that account was in flight from that Monitor host in that window** — and DOrc runs deployments concurrently, so that condition frequently fails on a shared account:

- For an environment whose bound `ExecutionIdentity` resolves to an **underlying OS account exclusive to that single environment** (not shared with any other environment, named `ExecutionIdentity` reference, or legacy tier-default fallback account), the `account` term already isolates that single environment at the OS level, and low concurrency within one environment usually narrows the correlated timestamp/log/receipt join to a specific request.
- For environments sharing a **sensitivity-tier** `ExecutionIdentity` (or bounded environment group), the `account` term narrows from the legacy `IsProd` fallback population and from other tiers to that bounded group **only when the resolved underlying OS account is exclusive to that group** — not shared with named `ExecutionIdentity` references outside the group or with a tier fallback account (resolutions that still count as `bound` without yielding OS-level separation). Even with a group-exclusive OS account, isolating a specific environment and request within that group still depends on correlating DOrc's persisted deployment timestamps, logs, and receipts, and remains ambiguous whenever two environments sharing that account deploy concurrently from the same Monitor host.

That is the honest description of what improves: not unconditional per-environment or per-request attribution from binding alone, but an `account` term narrowed to a single environment or bounded sensitivity-tier group only where the resolved underlying OS account is exclusive to that environment or group (and not shared outside it or with tier fallback accounts).

---

## 5. What per-request attribution would actually require

Recorded so that the decision not to pursue it is visible, and so that nobody re-derives it later under time pressure.

**A principal per request** is the direct reading and is not viable. It means an account lifecycle — creation, provisioning onto target servers, secret management, revocation — running at deployment frequency, and target-server access control lists that name a population instead of an account. The estate cannot absorb this; it is why binding is at environment or sensitivity-tier granularity rather than per request in the first place.

**Impersonation or constrained delegation** — carrying the requesting user's identity to the target — is the technically correct answer for per-user attribution and is well beyond this scope. It would require the requesting user to hold rights on the target servers, which inverts the entire model: DOrc exists so that people can deploy without holding those rights. Attribution gained; the deployment system's reason for existing lost. The HLPS states plainly that this is not proposed.

**Target-side correlation** — an agent or a logging convention on target servers that records a DOrc-supplied correlation identifier alongside its own telemetry — is the only approach that yields per-request attribution without touching the identity model. It is a different project, with a footprint on every target server in the estate, and it belongs to whoever owns target-server tooling rather than to this sequence.

---

## 6. Residual

**W-9 is narrowed, not closed.** After S-021:

- deployments into a migrated environment achieve OS-level attribution **to that single environment** in target-server telemetry (`4624`/`4688`) — distinguishing them from every other environment and from the unmigrated fallback population — **only when the resolved underlying OS account is exclusive to that environment** (not shared with any other named `ExecutionIdentity` reference or with a legacy tier-default fallback account);
- deployments into migrated environments that **share a sensitivity-tier `ExecutionIdentity`** (per HLPS SD-4) achieve OS-level attribution **to that bounded sensitivity tier or group of environments** — distinguishable from other tiers and from the unmigrated fallback population — **only when the resolved underlying OS account is exclusive to that bounded group** (not also resolved by named references outside the group or by a tier fallback account, both of which still count as `bound` without separating the OS principal), and narrowed further to a specific environment or request only by correlating DOrc's persisted deployment timestamps, logs, and execution receipts;
- deployments into an unmigrated environment (still on fallback) remain on the legacy shared tier default and are as indistinguishable in target telemetry as before;
- the per-request `bound / (bound + fallback)` ratio tracks **execution-identity binding adoption**, not underlying OS account exclusivity (multiple named references, or a named reference and a tier fallback, resolving to the same OS account still count as `bound`);
- no deployment is attributable to a request, a user, a project, or a component from the target server's own OS record in any migration state;
- the `(account, source host, time window)` join against DOrc's persisted forensic records (requests, deployment result logs, execution receipts, and Monitor/Runner logs — excluding the transient `AssociateProcessWithRequest` / `RemoveProcess` live cancellation mapping) remains the only route to request-level attribution, and it remains ambiguous under concurrent deployments sharing the same account and Monitor host.

This is recorded as the residual. It should not be reported as W-9 having been addressed without qualifying both that migration — not the release — carries the improvement, and that environment- or group-level OS attribution holds only where the resolved underlying OS account is exclusive to that environment or bounded group (not shared outside the group or with tier fallback accounts).
