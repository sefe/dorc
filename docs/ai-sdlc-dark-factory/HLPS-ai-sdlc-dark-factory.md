# HLPS — AI SDLC / Dark Factory on DOrc: Harness Comparison and Starting Point

| Field       | Value                                        |
|-------------|----------------------------------------------|
| **Status**  | DRAFT — discussion document, no implementation proposed yet |
| **Author**  | Agent                                        |
| **Date**    | 2026-09-07                                   |
| **Folder**  | docs/ai-sdlc-dark-factory/                   |

---

## 1. Problem Statement

Harness has repositioned from "CI/CD platform" to "AI platform for the autonomous SDLC". Its 2026
releases — Autonomous Worker Agents (June), Agent DLC (July), agent-ready Code Repository with AI
code review (August) — all rest on one architectural claim: **an agent is only safe to run
unattended if it runs as a governed step inside a pipeline that already has RBAC, secrets
brokerage, policy gates, verification and an audit trail.** The pipeline is deterministic; the
agent inside it is not; the governance around the step is what makes the non-determinism
tolerable.

DOrc has most of the same primitives, arrived at independently, and has two the Harness model does
not: a first-class estate model (servers, databases, daemons, ports, environment parent/child
chains) and environment lifecycle operations (clone, MakeLikeProd, CopyEnvBuild). It is missing
the three things that make unattended execution survivable: a **declarative deployment
definition**, a **policy gate**, and **verification with rollback**.

It is also, today, missing an enforced trust boundary — see
`docs/deployment-privilege-containment/HLPS-deployment-privilege-containment.md`. That is the
governing constraint on everything below, not a parallel workstream.

The repository is already running the *authoring* half of an AI SDLC: `docs/<topic>/HLPS-*.md`,
`IS-*.md`, `SPEC-S-NNN-*.md`, adversarial panel review, `.github/agents/absolute-agent.md`.
The *delivery* half — a machine-drivable, policy-gated deployment interface — does not exist.
The dark factory is the join of the two, and only the second half is missing.

---

## 2. Side-by-side comparison

Verdicts are relative to an unattended, agent-driven delivery loop, not to general CD maturity.

| # | Concept | Harness | DOrc | Verdict |
|---|---------|---------|------|---------|
| 1 | **Unit of delivery** | Service + artifact + Environment + Infrastructure Definition | Project + Component tree + Environment + Servers/Databases/Daemons | Parity |
| 2 | **Estate model** | Thin — infra is a connector target | `Server`, `Database`, `Daemon`, `SqlPort`, `EnvironmentComponentStatus`, env parent/child chain | **DOrc ahead** |
| 3 | **Environment lifecycle** | Nothing comparable | Clone, MakeLikeProd, CopyEnvBuild, `EnvironmentHistory`, `RestoredFromBackup` | **DOrc ahead** |
| 4 | **Pipeline definition** | Declarative YAML in Git, template library, input sets, triggers | No artifact. Order is derived at runtime by walking the component tree and sorting **alphabetically by name** (`Dorc.PersistentData/Sources/ManageProjectsPersistentSource.cs:237-257`) | **Gap — largest** |
| 5 | **Execution agent** | Delegate (outbound-polling, installed in customer infra) | Runner — Monitor launches an impersonated process, payload over a named pipe | Parity of role; DOrc is Windows/PowerShell-native, which suits the estate |
| 6 | **Config / variables** | Variables with service+env overrides, secret manager connectors | `PropertyValues` scoped to environment/server/daemon/database, AES-GCM at rest, `fn:` expressions | DOrc ahead on scoping; `fn:` is an RCE (W-1) |
| 7 | **Approvals & policy** | Manual approval steps, Jira/ServiceNow, OPA policy sets on save and on run, deployment freeze windows | One gate: Terraform plan `WaitingConfirmation` → `Confirmed`/declined. Its three authorization predicates are `return true;` stubs (W-10, `SPEC-S-001`) | **Gap — and currently a security defect** |
| 8 | **Deployment strategy** | Rolling, canary, blue/green | None. Components run sequentially; `StopOnFailure` is a per-component bool | Gap |
| 9 | **Verification & rollback** | Continuous Verification (ML anomaly detection over APM/logs) with automatic rollback | None. No health check, no last-known-good, no rollback path | **Gap — gates unattended operation** |
| 10 | **Metrics** | Dashboards, DORA, SEI, Software Delivery Knowledge Graph | `AnalyticsDuration` (P50/P90/P95), `AnalyticsMonthlyOutcome`, `AnalyticsComponentReliability`, `AnalyticsRecoveryTime`, `AnalyticsEnvironmentWait` | **Closer than expected** — the DORA raw material is already captured |
| 11 | **API / eventing** | REST + webhooks + triggers + MCP server | 54 REST controllers, generated TS/C# clients, SignalR, Kafka request/result lifecycle topics, `Tools.RequestCLI`, PowerShell cmdlet | Strong foundation; no MCP surface |
| 12 | **RBAC & audit** | Account/org/project scopes, roles, resource groups, audit trail | `AccessControl` allow/deny bitmask (`Write=1, ReadSecrets=2, Owner=4`) on `SecurityObject`; audit tables for refdata, scripts, servers, databases, daemons, property values | Model is sound; enforcement is 66 hand-written `Status403Forbidden` sites with no `IAuthorizationHandler` |
| 13 | **Agents in the loop** | Worker Agents as pipeline steps, AI Evals as quality gates, Agent Security (model/skill scanning, prompt-injection firewall), agent BOM | None in product. Agent-authored SDLC already practised in `docs/` and `.github/agents/` | Gap in product, ahead in practice |

### 2.1 The single idea worth taking from Harness

Not the module list. This: **the agent is a step, not an operator.** It runs inside the same
execution context that already holds identity, secrets, policy and audit; it never holds a
credential, and it never bypasses a gate. Everything in §3 is sequenced to make a DOrc component
able to be that step.

---

## 3. Proposed starting point

Phases are dependency-ordered, not priority-ordered. Each is independently shippable.

### Phase 0 — Finish the trust boundary (prerequisite, already in flight)

`docs/deployment-privilege-containment/` is the blocking work. Until it lands, "autonomous
deployment" reads as "unauthenticated production apply":

- **SPEC-S-001** — implement the three Terraform approval predicates (W-10, rank 1).
- **W-1** — sandbox or remove `CSharpScript.EvaluateAsync` in `PropertyExpressionEvaluator`;
  note the process-wide static result cache that leaks values across prod/non-prod boundaries.
- **W-15** — authorization on the API-side `LogonUser` path.
- Replace the 66 ad-hoc `Status403Forbidden` sites with `IAuthorizationHandler` policies.

**Nothing in Phases 1–5 should be scheduled ahead of this.** An agent-driven loop multiplies the
rate at which every one of these weaknesses is exercised.

### Phase 1 — Make the deployment unit declarative

Introduce an explicit **deployment definition**: ordered steps, per-step gate, per-step strategy,
per-step verification — exportable and importable as YAML/JSON, with the database remaining the
source of record initially.

Rationale: today the execution order of a production deployment is an emergent property of
component names sorted alphabetically. An agent cannot safely author, and a human cannot usefully
review, a change to something that has no representation. This is what gives an agent an artifact
to *write* and a reviewer a diff to *read*.

### Phase 2 — Generalise the gate

Promote the Terraform `WaitingConfirmation`/`Confirmed` transition into a first-class gate on any
component, evaluated by a policy component against request context: `IsProd`, build provenance and
pinned status, requester identity **including whether the requester is a machine**, target
environment, and time window.

This buys freeze windows, agent-vs-human differentiation, and change-window enforcement from one
mechanism. Harness uses OPA/rego; a .NET rule set over the existing request context is a
legitimate first cut and avoids a new runtime dependency.

### Phase 3 — Verification and rollback

The two hardest gaps, and the ones that decide whether unattended deployment is defensible.

- A `Verification` component type running post-deployment health checks against the estate model
  DOrc already holds (daemon state, SQL ports, endpoints).
- Last-known-good tracking per environment/component. `EnvironmentComponentStatus`
  (`Environment`, `Component`, `Status`, `UpdateDate`, `DeploymentRequest`) is most of the schema
  needed; it needs the successful build reference.
- Rollback as a redeploy of last-known-good, triggered by verification failure.

`AnalyticsRecoveryTime` becomes a measured outcome rather than an observation.

### Phase 4 — The agent control plane

Only now is there something safe to hand an agent.

- An **MCP server over the existing REST API** — the API surface, generated clients and CLI
  already exist; this is a facade, not a new subsystem.
- A **machine identity** with its own `AccessControl` entries, so agent actions are
  distinguishable in `RefDataAudit` and in the Kafka event stream, and constrainable by the
  Phase 2 policy (for example: agents may deploy to non-prod without approval, and may *request*
  prod deployments but never approve them).
- Agent actions inherit the existing audit and event trail for free.

### Phase 5 — Close the loop

- **Evals**: replay a corpus of historical deployment requests against a changed component or
  script as a regression suite, gating promotion. This is the direct analogue of Harness AI Evals,
  and DOrc has the request history to build it from.
- **Objective function**: feed `AnalyticsComponentReliability` and `AnalyticsRecoveryTime` back to
  the agent as the measure it optimises, rather than deployment count.

---

## 4. First slice

One vertical slice, one project, one non-prod environment:

> An agent submits a deployment request through a scoped machine identity, the request passes a
> policy predicate, deploys, is verified, and rolls back to last-known-good on verification
> failure — with every step attributable in the audit trail.

That exercises Phases 1–4 end to end at minimum width and produces the first genuinely
unattended cell. Scaling the dark factory afterwards is replication of that cell, not new
architecture.

---

## 5. What not to copy from Harness

- **CI.** Azure DevOps already builds; `Dorc.AzureDevOps` already consumes the artefacts. There is
  no case for a second build system.
- **A general-purpose pipeline engine.** DOrc's value is the estate and configuration model.
  Rebuilding a generic step-graph runtime spends that advantage rather than compounding it.
- **A module portfolio.** Feature flags, cost management, chaos, IDP are separate products bundled
  under one brand. Adopting the shape of the bundle is not adopting the idea.

---

## 6. Open questions

- **U-1** — What depends on `fn:` expression evaluation today? Phase 0 cannot proceed on W-1
  without the answer; `Dorc.Core.Tests/VariableResolverTests.cs:55-70` proves the behaviour is
  intentional but not who relies on it.
- **U-2** — Does the deployment definition (Phase 1) become the source of record, or stay a
  projection of the database? Affects whether Git becomes the control plane.
- **U-3** — Which health signals exist for the estate today, and which need to be built? Phase 3
  scope depends entirely on this.
- **U-4** — Is a machine identity acceptable to the security model as an `AccessControl`
  principal, or does it need a distinct type?
