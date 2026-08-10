# SPEC-S-001: Implement the Terraform Approval Predicates

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | DRAFT                                                        |
| **Step**    | S-001                                                        |
| **Author**  | Agent                                                        |
| **Date**    | 2026-08-10                                                   |
| **IS**      | IS-deployment-privilege-containment.md (DRAFT)               |
| **HLPS**    | HLPS-deployment-privilege-containment.md (APPROVED)          |
| **Addresses** | SD-10, W-10 (rank 1), SC-05a                               |

---

## 1. Context

`TerraformController` carries `[Authorize]` and nothing else. Its three authorization predicates — view, confirm, decline — have bodies that return `true` unconditionally. They *are* called, at the top of each of the three endpoints, so the checks are wired and always pass.

The controller injects both `ISecurityPrivilegesChecker` and `IClaimsPrincipalReader` and uses neither for these decisions. The intent was present; the implementation never landed.

Consequences, both reachable from a plain DOrc login:

- **Confirm** transitions the deployment result to `Confirmed`, which the Monitor's state processor picks up and maps to `ApplyPlan`, dispatched under the production deployment credential. Any authenticated user can approve a production `terraform apply`.
- **View** returns plan content, which embeds resolved variable values, for any deployment.

This is the highest-ranked weakness in the HLPS and among the cheapest to close. It is also the one step that depends on nothing else in the sequence.

---

## 2. Requirements

### R1 — Resolve the environment before deciding

The predicates cannot decide from their current argument. `DeploymentResultApiModel` carries `Id`, `ComponentId`, `RequestId`, status and timings — **no environment**. The resolution chain is:

```
deploymentResultId → GetDeploymentResults(id) → RequestId
                   → GetRequest(RequestId)    → EnvironmentName, UserName
```

Each endpoint already fetches the deployment result. The request lookup is the new work.

**Change the predicate signatures to accept what they actually need** — the resolved environment name, and for confirm/decline the requesting user — rather than a model that cannot supply it. Resolve once per endpoint and pass the result in; do not have three predicates each perform their own lookup.

A missing request for a valid result is a data-integrity failure, not an authorization outcome. It must not fall through to a permissive default: treat it as a refusal and log it.

### R2 — View permission

Gate `GetTerraformPlan` on **`CanModifyEnvironment(user, environmentName)`** — the same predicate as confirm.

**Explicitly do not use `CanReadSecrets`.** An earlier draft of this spec recommended it, on the reasoning that plan content embeds resolved variable values (W-12) and there is an existing privilege meaning "may see secrets for this environment". That reasoning was wrong in context, and the correction is worth recording so a later reviewer does not helpfully re-tighten it:

- `CanReadSecrets` was designed for **machine access to secrets**, not as a human read-level permission. Binding a human-facing view endpoint to it would widen a privilege whose scope has already drifted, and make it harder to reclaim.
- The drift has a concrete mechanism in code, not merely in practice: the predicate resolves to `AccessLevel.ReadSecrets | AccessLevel.Owner`, so **every environment owner holds it implicitly**, without it ever being granted. Any endpoint gated on it inherits that population automatically.
- It is already doing double duty — gating secret decryption in `PropertyValuesService` and gating ACL visibility in `AccessControlController`. A third, differently-shaped use would entrench the ambiguity about what the privilege means.

`CanModifyEnvironment` is the right gate for this step because the objective is to close a login-level hole, and it does that by an enormous margin: from "any authenticated user" to "users with write authority over this specific environment". It is also the gate confirm uses, so view and approve stay coherent.

**Residual, stated rather than hidden.** A user with modify rights on an environment can see plan output that may contain secret values they could not obtain through `PropertyValuesService`. That gap is real and this step does not close it.

**The gap's proper fix is not a stricter view gate — it is that plan content should not contain cleartext secrets in the first place.** Redacting secret-valued variables at plan generation or before the blob is stored would remove the disclosure at source, after which an ordinary environment-read permission is sufficient for viewing and the blob's own retention (W-12) stops being a secret-handling problem. That is registered as a follow-on rather than smuggled into this step, which is meant to be the cheap one with no dependencies.

### R3 — Confirm permission

Gate `ConfirmTerraformPlan` on **`CanModifyEnvironment(user, environmentName)`** as the minimum.

**Segregation of duties — ADOPTED.** Confirm additionally refuses when the confirming user is the request's own submitter. Configurable, **defaulting on for production-tier environments and off elsewhere**.

Rationale: confirm is the only human gate between a generated plan and a production apply. A gate the requester can pass themselves records intent; it does not control anything.

Implementation requirements:

- **Tier determination** comes from the request's production flag, which is derived server-side from the environment rather than supplied by the caller. It must not be taken from anything the requester controls, or the check is bypassable by mis-declaring the target.
- **Identity comparison** uses the same normalised form the codebase already uses for user comparison — the full domain name from the claims reader — compared against the request's stored submitter. The two must be normalised identically before comparison; a mismatch in form fails *open* if written carelessly, which is the failure direction that matters. Write the comparison so an unparseable or absent submitter refuses rather than permits.
- **Machine-to-machine callers** are named by client id rather than user name in this codebase. A request submitted by one service principal and confirmed by the same one must be treated as self-approval; the comparison therefore operates on whatever the claims reader returns as the caller's canonical name, not on a user-only field.
- **No administrator override.** An override that lets an admin approve their own request reintroduces exactly what the control removes, and the population that most often submits production changes is the population holding admin. If an emergency path is genuinely required it should be a deliberate, logged, separately-named capability — not a quiet exemption inside this predicate. Recorded as out of scope here.
- **The refusal must be distinguishable.** A user refused for self-approval should not receive the same message as a user refused for lacking environment rights; the first is a workflow instruction ("someone else must approve this"), the second is an access problem. Same status code, different message.

**Configuration default.** On for production-tier, off otherwise. The setting exists so that a single-operator non-production environment is not blocked, not so that production can be opted out casually — the IS should note that turning it off for a production-tier environment is a decision worth recording wherever such decisions are recorded.

### R4 — Decline permission

Gate `DeclineTerraformPlan` on the same predicate as confirm. Declining is a safe direction — it stops a deployment rather than starting one — but a user who cannot approve should not be able to veto either, and asymmetry here would be surprising.

The current implementation delegates decline to the confirm predicate. That delegation is correct and should be retained, made explicit rather than left as a "for now" comment.

### R5 — Fix the refusal path

**The three refusal branches are currently broken, and implementing the predicates is what activates them.**

Each refusal calls `Forbid("You do not have permission…")`. `ControllerBase.Forbid` takes **authentication scheme names**, not a message. Passing a message causes the framework to attempt resolution of an authentication scheme by that name, which fails at runtime — the caller receives a 500, not a 403.

This has never fired because the predicates always return true, so all three branches are dead. Every one becomes live in this step.

Replace all three with the codebase's dominant pattern: return a 403 status result carrying the message. These are the only three uses of `Forbid(string)` in the solution; other controllers use either that status-code pattern or a bare `Forbid()`.

### R6 — Guard against recurrence

The defect was not a missing check — it was a check that existed, was called, and always passed. A test that only exercises the allow path would have passed throughout. Coverage must include the **refuse** path for all three endpoints, which is what makes a constant-true predicate fail.

This is the concrete form of SC-05a.

### R7 — No behavioural change beyond authorization

Status validation, state transitions, plan loading, event publication and error handling are unchanged. This step adds authorization and fixes the refusal mechanism; it does not touch the Terraform workflow.

---

## 3. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | A user without `CanReadSecrets` on the deployment's environment receives 403 from the plan-content endpoint, and no plan content is loaded from storage. |
| AC-2 | A user with `CanReadSecrets` receives the plan as before. |
| AC-3 | A user without `CanModifyEnvironment` receives 403 from confirm, and the deployment result's status is unchanged. |
| AC-4 | A user with `CanModifyEnvironment` can confirm, and the status transition and downstream dispatch behave exactly as today. |
| AC-5 | Decline enforces the same predicate as confirm, verified independently rather than assumed from the delegation. |
| AC-6 | All three refusals return **403**, not 500. This fails against the current `Forbid(string)` implementation and is the regression test for R5. |
| AC-7 | If R3's segregation-of-duties option is adopted: a user who submitted the request cannot confirm it where the setting applies, and can where it does not. |
| AC-8 | A deployment result whose request cannot be resolved produces a refusal, not an allow. |
| AC-9 | No predicate is a constant expression — asserted by exercising the refuse path of each endpoint, not by inspection. |

---

## 4. Test Approach

### Unit tests

Controller-level tests with a mocked privileges checker, requests source and claims reader:

- Allow and refuse paths for each of the three endpoints — six cases minimum. **The refuse cases are the point**; the allow cases alone would have passed against the stubs.
- Refusal returns 403 with a message body (AC-6). Worth writing this test first and watching it fail against the current code, to confirm it actually catches the `Forbid` defect.
- Refused view does not reach storage — assert the storage worker is not invoked, rather than only asserting the response, so a future reordering that loads content before checking permission is caught.
- Refused confirm does not update status — same reasoning.
- Unresolvable request refuses (AC-8).
- Self-approval refused where configured, permitted where not (AC-7), if R3 is adopted.

### Integration

Not required for this step. The logic is controller-level and the collaborators are already interfaces. The downstream apply path is unchanged by R7 and is covered by existing tests.

---

## 5. Decisions Required Before Implementation

| # | Decision | Recommendation |
|---|----------|----------------|
| 1 | ~~Does confirm require a distinct approver from the requester?~~ **RESOLVED — adopted.** | Yes, configurable, defaulting on for production-tier environments and off elsewhere. Specified in R3. No administrator override. |
| 2 | ~~Is `CanReadSecrets` the right gate for plan content?~~ **Resolved — no.** | Use `CanModifyEnvironment` (R2). `CanReadSecrets` is a machine-access privilege whose scope has already drifted, partly because it resolves to `ReadSecrets \| Owner` and so is held implicitly by every environment owner. Do not widen it further. The residual — modify-holders seeing secret values in plan output — is fixed by redacting plan content at source, not by a stricter view gate. |

Both are user decisions, not implementer decisions. Neither blocks drafting; both block merge.

---

## 6. Accepted Risks

- **Users who currently rely on unrestricted access will lose it.** That is the intent, but it will surface as support requests from people who were legitimately using a workflow that happened to be ungated. Worth an announcement ahead of release rather than after.
- **A user with modify rights on an environment can view plan content containing secret values they could not retrieve through the properties API.** Accepted for this step. The alternative — gating on `CanReadSecrets` — trades a smaller disclosure for a wider entrenchment of a privilege that is already over-broad, which is the worse trade. The real remedy is plan-content redaction, registered as a follow-on.

---

## 7. Out of Scope

- The API-process credential resolution behind `DaemonStatusProbe` — S-002 addresses the endpoint, S-021 the underlying duplication.
- Any change to the Terraform dispatch path, source acquisition, or the plan blob's own retention — SD-9, S-007.
- The broader inconsistency in how the API's controllers express authorization — the deferred sibling HLPS.
- Plan blob storage permissions. This step controls the API's disclosure of plan content; the blob's own access control is a separate concern noted in W-12.
