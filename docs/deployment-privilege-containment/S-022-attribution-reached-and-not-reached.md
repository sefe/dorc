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

**Environment-granularity attribution, for environments that have been migrated.**

An environment may now name its own execution identity, and every deployment into that environment authenticates to its target servers as that identity. Target-server telemetry then distinguishes one environment's deployments from another's without any DOrc-side correlation.

Two qualifications matter and are easy to lose.

**Attribution is a property of migration state, not of the release.** The identity reference is null on every existing environment, and a null reference resolves the tier default exactly as before. Shipping S-021 changed the attribution of nothing. What changes it is an operator naming an identity on an environment and provisioning that account on the target servers — and the estate holds well over a thousand environments. Until an environment is migrated, its deployments are as indistinguishable in target telemetry as they were before.

The bound-versus-fallback count reported once per request exists for exactly this reason: "we have begun binding identities" and "the estate is bound" are different claims, and only the second one means anything for attribution.

**Granularity is by environment, not by tier and not by request.** The HLPS's own note that binding is by sensitivity tier describes the *credential* population that is viable to operate; the data model binds per environment, and an operator who provisions per-environment accounts gets per-environment telemetry. What no configuration of S-021 yields is attribution finer than the environment.

---

## 3. What S-021 does not deliver

**Per-request attribution.** Many requests target one environment. Two deployments into the same environment, from the same Monitor host, minutes apart, are the same principal on the target server. Nothing in the target's record separates them.

**Per-user attribution.** The user who raised the request never reaches the target server in any form. The Runner is started with `CreateProcessAsUser` under the deployment credential; the requesting user's identity is not carried, impersonated, or asserted anywhere past the API.

**Per-project or per-component attribution.** Same reason. The target sees a logon and a process, not what DOrc was doing.

**Attribution for anything the deployment script does under its own credentials.** A script that authenticates onward with a credential from the property bag attributes to *that* credential, not to the deployment identity. That is a property of what deployment scripts do, not of execution identity, and no change to execution identity reaches it.

---

## 4. Where the seam actually is

It is worth being concrete about what each side knows, because the join between them is the only attribution available today and its ambiguity is specific rather than general.

**DOrc's side of the seam is well populated.** For each deployment it holds the request and who raised it, the environment, the project and component, the deployment result, the Monitor host and the operating-system process identifier of the Runner it started (`AssociateProcessWithRequest`), and the path of that Runner's own log. Within DOrc, a deployment is fully attributable.

**The target server's side holds the account, the source host, and the time.**

So the join is `(account, source host, time window)`. That identifies a deployment uniquely **only when a single deployment under that account was in flight from that Monitor host in that window** — and DOrc runs deployments concurrently, so that condition frequently does not hold. Migrating an environment to its own identity narrows the account term, which is what makes the join usable in practice for a migrated environment: concurrency within one environment is far lower than concurrency within a tier.

That is the honest description of what improves. Not "actions become attributable", but "the join becomes narrow enough to be usable for environments that have been migrated".

---

## 5. What per-request attribution would actually require

Recorded so that the decision not to pursue it is visible, and so that nobody re-derives it later under time pressure.

**A principal per request** is the direct reading and is not viable. It means an account lifecycle — creation, provisioning onto target servers, secret management, revocation — running at deployment frequency, and target-server access control lists that name a population instead of an account. The estate cannot absorb this; it is why binding is per environment rather than per request in the first place.

**Impersonation or constrained delegation** — carrying the requesting user's identity to the target — is the technically correct answer for per-user attribution and is well beyond this scope. It would require the requesting user to hold rights on the target servers, which inverts the entire model: DOrc exists so that people can deploy without holding those rights. Attribution gained; the deployment system's reason for existing lost. The HLPS states plainly that this is not proposed.

**Target-side correlation** — an agent or a logging convention on target servers that records a DOrc-supplied correlation identifier alongside its own telemetry — is the only approach that yields per-request attribution without touching the identity model. It is a different project, with a footprint on every target server in the estate, and it belongs to whoever owns target-server tooling rather than to this sequence.

---

## 6. Residual

**W-9 is narrowed, not closed.** After S-021, and after an environment is migrated:

- deployments into a migrated environment are distinguishable, in target telemetry, from deployments into any other migrated environment and from the tier default population;
- deployments into an unmigrated environment are exactly as attributable as before, which is to say not at all;
- no deployment is attributable to a request, a user, a project or a component from the target's own record, in any migration state;
- the `(account, source host, time window)` join to DOrc's records remains the only route to finer attribution, and it is ambiguous under concurrency.

This is recorded as the residual. It should not be reported as W-9 having been addressed without the qualification that the migration, not the release, is what carries the improvement.
