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
| **Folder**  | docs/deployment-privilege-containment/                       |
| **Branch**  | `feat/dpc-s001-terraform-approval-predicates`                |
| **Governing constraints** | C-07 (naming), C-08 (no secrets in logs or error messages) |

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

**Fail closed, explicitly, on three conditions — the chosen predicate does not do it for you.** `CanModifyEnvironment` returns `IsAdmin(user)` when the environment security object cannot be found, so a request whose `EnvironmentName` is null, blank, or names a renamed environment resolves to **allow for every administrator** rather than to a refusal. The endpoint must therefore refuse independently of the checker when:

1. the request cannot be resolved from the result;
2. `EnvironmentName` is null or whitespace;
3. resolution throws.

Condition 3 needs care: the controller's blanket `catch` returns **500**, which is not a refusal and is indistinguishable from a genuine fault. Resolution failures must be caught and converted to 403 before that handler sees them.

### R2 — View permission

Gate `GetTerraformPlan` on **`CanModifyEnvironment(user, environmentName)`** — the same predicate as confirm.

**Explicitly do not use `CanReadSecrets`.** An earlier draft of this spec recommended it, on the reasoning that plan content embeds resolved variable values (W-12) and there is an existing privilege meaning "may see secrets for this environment". That reasoning was wrong in context, and the correction is worth recording so a later reviewer does not helpfully re-tighten it:

- `CanReadSecrets` was designed for **machine access to secrets**, not as a human read-level permission. Binding a human-facing view endpoint to it would widen a privilege whose scope has already drifted, and make it harder to reclaim.
- It is already doing double duty — gating secret decryption in `PropertyValuesService` and gating ACL visibility in `AccessControlController`. A third, differently-shaped use would entrench the ambiguity about what the privilege means.

`CanModifyEnvironment` is the right gate for this step because the objective is to close a login-level hole, and it does that by an enormous margin: from "any authenticated user" to "users with write authority over this specific environment". It is also the gate confirm uses, so view and approve stay coherent.

*Note for reviewers, so this is not re-litigated:* the chosen gate resolves to `Write ∪ Owner` with an administrator fallback, so environment owners hold it implicitly too. Implicit-owner reach is therefore **not** what distinguishes the two predicates — the distinguishing reason is the one above: `CanReadSecrets` is reserved for machine principals and must not be widened.

**Residual, stated rather than hidden.** A user with modify rights on an environment can see plan output that may contain secret values they could not obtain through `PropertyValuesService`. That gap is real and this step does not close it.

**The gap's proper fix is not a stricter view gate — it is that plan content should not contain cleartext secrets in the first place.** Redacting secret-valued variables at plan generation or before the blob is stored would remove the disclosure at source, after which an ordinary environment-read permission is sufficient for viewing and the blob's own retention (W-12) stops being a secret-handling problem. That is registered as a follow-on rather than smuggled into this step, which is meant to be the cheap one with no dependencies.

### R3 — Confirm permission

Gate `ConfirmTerraformPlan` on **`CanModifyEnvironment(user, environmentName)`** as the minimum.

**Segregation of duties — ADOPTED.** Confirm additionally refuses when the confirming user is the request's own submitter. Configurable, **defaulting on for production-tier environments and off elsewhere**.

Rationale: confirm is the only human gate between a generated plan and a production apply. A gate the requester can pass themselves records intent; it does not control anything.

**Why this requirement carries the whole step for production.** Request submission is gated on `CanModifyEnvironment` — the same predicate as confirm — as are restart, cancel, pause and resume. Anyone who can confirm could have submitted the identical deployment themselves, so the confirm gate constrains *which deployer* approves but adds no authority boundary over the environment. R2's gate closes the login-level hole; **R3 is the only part of this step that adds a control between a plan and a production apply.** That is why it is not optional for production-tier.

Implementation requirements, each of which is a way to get this wrong:

- **Tier determination** comes from the request's production flag, derived server-side from the environment row at submission. It is not caller-supplied, so it cannot be bypassed by mis-declaring the target. *(Verified.)*

- **Compare against an immutable submitter, not the request's mutable user column.** That column is **overwritten** by the restart endpoint with the restarter's name, and restart is gated on the same `CanModifyEnvironment` and drives the request back through plan generation to `WaitingConfirmation`. As specified the control would be both defeatable and invertible: an author whose request is restarted by anyone else becomes a non-submitter and may then approve their own change, while a user who restarts someone else's request is locked out of approving it and the actual author is not. Compare instead against the earliest attempt's recorded user, or a new column that is never overwritten. **Restart must not launder submitter identity** — an audit defect independent of this control.

- **Define the canonical identity comparison; do not assume one exists.** An earlier draft claimed the codebase has a normalised form used for user comparison. It does not — every existing call site of the full-domain-name accessor writes an audit field, none compares. The stored value is also scheme-dependent: Windows authentication yields `DOMAIN\\user`, OAuth yields a fallback chain of email, then SAM account name, then identity name. Both schemes are registered and selected per request, so the same human submitting under Windows and confirming under OAuth produces two different strings, they compare unequal, and **self-approval is permitted** — the failure direction that matters. Requirements: a single canonicalisation exposed on the claims-reader interface; `OrdinalIgnoreCase` comparison, since AD and email identifiers are case-insensitive; and a **refusal when the stored submitter cannot be canonicalised into the same space as the caller**. That is a wider condition than "absent or unparseable" — the common case is a value that parses fine and is simply shaped differently.

- **Machine-to-machine callers already work.** The full-domain-name accessor returns the client id for m2m callers, and that is exactly what is stored at submission, so a service principal confirming its own request compares equal without special handling. Stated so no parallel m2m path is invented.

- **No administrator override.** An override that lets an admin approve their own request reintroduces exactly what the control removes, for precisely the population that most often submits production changes. If an emergency path is genuinely required it should be a deliberate, logged, separately-named capability — not a quiet exemption inside this predicate.

- **The refusal must be distinguishable.** Self-approval refusal is a workflow instruction ("someone else must approve this"); a rights refusal is an access problem. Same status code, different message.

**Configuration — name it, and beware the house default.** The setting must be named explicitly in the implementation, and `TerraformController` does not inject configuration today. The codebase's dominant pattern for boolean settings evaluates to **false when the key is absent**, which would ship this control silently *off* — the exact inversion of the intent. Requirement: **an absent or unparseable value means enabled whenever the request is production-tier**, disabled otherwise. Turning it off for a production-tier environment must be a deliberate act, and one worth recording wherever such decisions are recorded.

### R4 — Decline permission

Gate `DeclineTerraformPlan` on the **environment-authority check only**. A user who cannot approve should not be able to veto either, so the authority requirement is the same as confirm.

**The segregation-of-duties rule must NOT apply to decline.** The current implementation delegates decline to the confirm predicate wholesale. Retaining that delegation once R3 lands would mean the person who submitted a request cannot cancel their own pending plan — a workflow deadlock, and one that protects nothing: declining stops a deployment rather than starting one, so there is no self-approval to prevent.

Therefore split the decision into two parts:

- an **environment-authority check**, shared by view, confirm and decline;
- an **SoD check**, applied to confirm alone.

Confirm requires both. View and decline require only the first. Do not express this by having decline call the confirm predicate.

### R5 — Fix the refusal path

**The three refusal branches are currently broken, and implementing the predicates is what activates them.**

Each refusal calls `Forbid("You do not have permission…")`. `ControllerBase.Forbid` takes **authentication scheme names**, not a message. Passing a message causes the framework to attempt resolution of an authentication scheme by that name, which fails at runtime — the caller receives a 500, not a 403.

This has never fired because the predicates always return true, so all three branches are dead. Every one becomes live in this step.

Two details worth knowing when writing the test: the throw happens at **result-execution** time, outside the action's own `try/catch`, so the controller's blanket handler does not swallow it; it surfaces through the application's exception handler as a **500 with an empty body**. A test asserting "not 403" would pass on a 500 — assert the status is *exactly* 403.

Replace all three with the codebase's dominant pattern: return a 403 status result carrying the message. These are the only three uses of `Forbid(string)` in the solution; other controllers use either that status-code pattern or a bare `Forbid()`.

### R8 — Pin the row between the decision and the transition

The confirm endpoint validates the result's status from a model read earlier, then writes the new status filtered on identity alone with no from-status predicate. Two consequences: concurrent confirms both succeed, and a confirm racing the Monitor can push a result that has already moved to `Running` back to `Confirmed`, producing a **second apply dispatch**.

Make the confirm write conditional on the result still being in `WaitingConfirmation`, and treat zero rows affected as a conflict rather than a success. The codebase already has this pattern — the request status switch guards on a from-status — so this is applying an existing idiom, not inventing one.

This is in scope despite R7 because R7 forbids *unintended* behavioural change; leaving a known double-dispatch in place while adding the authorization around it would be shipping a step that looks complete and is not. If it is deferred instead, it must be recorded as an explicit accepted risk with a follow-on step, not left silent.

### R6 — Guard against recurrence

The defect was not a missing check — it was a check that existed, was called, and always passed. A test that only exercises the allow path would have passed throughout. Coverage must include the **refuse** path for all three endpoints, which is what makes a constant-true predicate fail.

This is the concrete form of SC-05a.

### R7 — No behavioural change beyond authorization

Status validation, state transitions, plan loading, event publication and error handling are unchanged. This step adds authorization and fixes the refusal mechanism; it does not touch the Terraform workflow.

---

## 3. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | A user without `CanModifyEnvironment` on the deployment's environment receives 403 from the plan-content endpoint, and no plan content is loaded from storage. |
| AC-2 | A user with `CanModifyEnvironment` receives the plan as before. |
| AC-3 | A user without `CanModifyEnvironment` receives 403 from confirm, and the deployment result's status is unchanged. |
| AC-4 | A user with `CanModifyEnvironment` can confirm, and the status transition and downstream dispatch behave exactly as today. |
| AC-5 | Decline enforces the same predicate as confirm, verified independently rather than assumed from the delegation. |
| AC-6 | All three refusals return **403**, not 500. This fails against the current `Forbid(string)` implementation and is the regression test for R5. |
| AC-7 | If R3's segregation-of-duties option is adopted: a user who submitted the request cannot confirm it where the setting applies, and can where it does not. |
| AC-8 | Each of the three fail-closed conditions in R1 produces **exactly 403**, not 500 and not an allow: an unresolvable request, a null or whitespace environment name, and a resolution that throws. The null-environment case must be verified with an administrator principal, since that is the case where the checker's fallback would otherwise permit. |
| AC-9 | No predicate is a constant expression, and each consults the **right** environment. Asserted by exercising the refuse path of each endpoint *and* by verifying the checker was invoked exactly once with the environment name resolved from the request — a suite that merely stubs the checker to false for all arguments would pass against a predicate reading the wrong environment. Refused paths must additionally leave plan storage un-read and the result status un-written. |
| AC-11 | A request that has been restarted by another user cannot then be confirmed by its original author, and can be confirmed by the restarter only if they are not the original author. This is the regression test for the mutable-submitter defect in R3. |
| AC-12 | The submitter of a request **can** decline it. This is the regression test for the R3/R4 collision. |
| AC-13 | The same human submitting under Windows authentication and confirming under OAuth is refused as self-approval. This is the regression test for the identity-normalisation defect; without it the control passes its other tests and fails in production. |
| AC-14 | Two concurrent confirms of the same result produce one state transition and one apply dispatch; a confirm arriving after the result has left `WaitingConfirmation` is rejected as a conflict. (R8.) |
| AC-15 | With the segregation-of-duties setting absent from configuration entirely, the control is **enabled** for a production-tier request. This is the regression test for the house-default inversion in R3. |
| AC-10 | R7 holds: status validation, state transitions, plan loading, event publication and error handling are unchanged. Verified by the existing Terraform workflow regression suite passing without modification, other than mechanical updates for the changed predicate signatures. |

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

## 7. Commit Strategy

One branch, and a commit order that keeps the security fix reviewable in isolation:

1. **The refusal-path fix (R5) first, on its own.** It is a correctness fix to a latent defect and is independently reviewable. Landing it first means the subsequent authorization commits do not carry an unrelated framework fix in the same diff.
2. **The resolution chain and predicate signatures (R1)**, with the predicates still permissive. No behavioural change; pure refactor.
3. **The three predicates (R2, R3, R4)** with their tests. This is the commit that changes behaviour and is the one a reviewer should spend time on.
4. **The segregation-of-duties configuration** and its default, separable so it can be reverted without reverting the authorization itself.

Squash on merge is fine, but the review should see the four steps. Do not combine 1 and 3 — a reviewer confronted with a framework fix and an authorization change in one diff will scrutinise the wrong one.

---

## 8. Out of Scope

- The API-process credential resolution behind `DaemonStatusProbe` — S-002 addresses the endpoint, S-021 the underlying duplication.
- Any change to the Terraform dispatch path, source acquisition, or the plan blob's own retention — SD-9, S-007.
- The broader inconsistency in how the API's controllers express authorization — the deferred sibling HLPS.
- Plan blob storage permissions. This step controls the API's disclosure of plan content; the blob's own access control is a separate concern noted in W-12.
