# JIT Spec — S-012: RabbitMQ Decommission at T+14d

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Step ID** | S-012 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-012 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` SC-6, C-9 |
| **Prerequisites** | S-011 closed with verdict `Pass` (or `Pass-with-deviation`) AND ≥14 days elapsed since the GATE E "verified" declaration. |

---

## 1. Purpose & Scope

At **T+14 days post-cutover** (the C-9 rollback window has elapsed),
decommission the RabbitMQ infrastructure that has been in cold-standby
state since S-011 R-7. This is an **unconditional** decommission gated
solely on the elapsed C-9 window; **SC-7 monitoring (S-013) is a
separate parallel activity that does not gate this step** (per IS §3
S-012).

This is **operator-executed end-to-end**. The AI assistant's
deliverable for S-012 is this spec + the post-decom evidence template;
the actual decommission is a human-coordinated activity.

### In scope

- Decommission RabbitMQ broker(s) — process shutdown, host/VM tear-down or repurposing.
- Credential revocation (OAuth2 client credentials, SCRAM users if any).
- Network rule removal (firewall, security groups, DNS).
- IaC removal (Terraform / ARM / CloudFormation, whichever applies for the production environment).
- Archive (do **not** delete) the `release/pre-kafka-cutover` tag — retained indefinitely per IS §3 S-012 for audit.
- Capture decommission evidence under `docs/kafka-migration/S-012-Decommission-evidence/<YYYY-MM-DD>/`.

### Out of scope

- SC-7 monitoring + baseline report — S-013 (parallel; runs through ≥30d / ≥200 deployments).
- Removal of SignalR hub or DB-poll path — post-cutover follow-up beyond IS.
- Removal of the rollback tag itself — explicitly retained per IS §3 S-012.
- Any DOrc code change — S-009 already removed all RabbitMQ code; nothing left to delete in source.

---

## 2. Requirements

### R-1 — Pre-decommission readiness gate

Before any destructive action, confirm:

- T+14d elapsed since S-011 GATE E "verified" timestamp (`docs/kafka-migration/S-011-Cutover-evidence/<S-011 date>/cutover-wallclock.txt` shows the timestamp).
- S-011 verdict was `Pass` or `Pass-with-deviation` — NOT `Rolled-back`. If `Rolled-back`, S-012 does not execute and the IS path returns to S-011 re-attempt.
- No production incident in the C-9 window has triggered (or is about to trigger) a rollback. The C-9 window expires at T+14d; if a rollback decision is in flight at that moment, S-012 waits for the decision to resolve.
- The `release/pre-kafka-cutover` tag's most recent spot-check (S-011 R-5 / AT-6 at T+14d) passed.

### R-2 — Decommission sequence

Order matters: revoke credentials before shutting down brokers, then
remove network rules, then remove IaC. This sequence prevents an
accidental partial state (e.g. an orphaned IaC resource attempting
to recreate the broker).

1. **Credential revocation:** OAuth2 clients used by the (now-deleted)
   `RabbitMqDistributedLockService` and `Tools.RabbitMqOAuthTest`
   removed from the OAuth provider. Document the OAuth client IDs
   revoked.
2. **Broker shutdown:** Stop the RabbitMQ broker process(es); confirm
   no active connections (the cold-standby state since S-011 R-7
   should already show zero connections from DOrc).
3. **Host tear-down:** Decommission or repurpose the broker hosts /
   VMs. Document the host identifiers.
4. **Network rule removal:** Remove firewall / security-group rules
   that allowed Monitor + API replicas to reach the broker. Document
   the rule identifiers.
5. **DNS removal (if applicable):** Remove any internal DNS records
   pointing to the broker.
6. **IaC removal:** Delete the RabbitMQ Terraform / ARM / etc.
   modules. Apply the IaC change.
7. **Final confirmation:** Attempt connection from a non-production
   workstation to the (former) broker hostname:port — must fail.

### R-3 — Tag retention

The `release/pre-kafka-cutover` annotated tag (object `0d5146c8` →
commit `481f4830`) remains pushed to origin indefinitely. **Retention
SLA: indefinite, audit-retained.** S-012 explicitly does NOT delete
the tag.

If the team's tag-retention policy ever proposes deleting it, that
deletion requires a separate approval gate documented per the
governance process — it is not within S-012's scope.

### R-4 — Evidence capture

Capture under `docs/kafka-migration/S-012-Decommission-evidence/<YYYY-MM-DD>/`:

- `pre-decom-readiness.txt` — R-1 gate items all green.
- `decom-sequence.txt` — R-2 steps 1–7 with timestamps + identifiers (OAuth client IDs revoked, hosts decommissioned, network rules removed, DNS records deleted, IaC commit SHA for the removal apply).
- `final-connection-attempt.txt` — R-2 step 7 transcript showing the connection failure.
- `summary.md` — verdict (`Decommissioned` / `Partial-decom-rollback-required`) + sign-offs.

### R-5 — Rollback posture for S-012 itself

S-012 has its own micro-rollback in case decom uncovers an issue:
**partial-decom rollback** = re-enable network rules + bring broker
back up. This requires the broker host has not yet been torn down
(R-2 step 3 is the irreversibility threshold — re-evaluate at that
gate). If R-2 steps 1–2 are reversible (re-issue credentials,
restart broker), the team may decide to abort decommission and
investigate before proceeding past step 3.

S-012 itself does NOT require redeploying the
`release/pre-kafka-cutover` tag — that is the C-9 rollback target
which expired at T+14d. Re-deploying the tag at this point would
require fresh credentials + a re-instantiated broker + re-application
of network rules; it is no longer a low-cost path.

If decommission fails irrecoverably (host gone but IaC won't apply,
or some other one-way state issue), S-012 closes with verdict
`Decommissioned-with-deviation` and the deviation is logged in
`summary.md` for audit.

---

## 3. Acceptance Criteria

### AT-1 — Readiness gate passed

`pre-decom-readiness.txt` shows all R-1 gate items confirmed.

### AT-2 — Decom sequence completed

`decom-sequence.txt` shows R-2 steps 1–7 each with a timestamp + the
specific identifier (or "n/a — none applicable" for steps that don't
apply to the production environment, e.g. no internal DNS record).

### AT-3 — Final connection attempt fails

`final-connection-attempt.txt` shows the post-decom connection
attempt to the (former) broker hostname:port returns a failure
(connection refused, hostname unresolvable, or whatever the
specific deletion mode produces).

### AT-4 — Tag retained

`git rev-parse release/pre-kafka-cutover^{commit}` post-S-012 still
returns `481f4830`. The tag is NOT deleted.

### AT-5 — Summary captured

`summary.md` exists with verdict + sign-offs.

---

## 4. Accepted Risks

| Risk | Disposition |
|---|---|
| Decommission-time discovery of a Rabbit dependency the audit (S-008) missed. | Accepted — extremely unlikely given S-008 + S-009 grep verification, but R-5 micro-rollback covers steps 1–2; past step 3 the team accepts the cost of re-instantiation if needed. |
| RabbitMQ host has shared resources (e.g. multi-tenant Aiven instance) — decommission is partial. | Accepted — operator handles per the production environment specifics; only DOrc-specific resources are in scope. |
| Removing IaC modules disrupts a CI/CD pipeline that still references them. | Accepted — operator verifies CI/CD before R-2 step 6. |
| SC-7 monitoring (S-013) detects a regression after S-012 fires; rollback no longer cheap. | Accepted — IS §3 S-012 explicitly states SC-7 does NOT gate decommission; if a regression is found post-S-012, the response is forward-fix on the Kafka substrate, not Rabbit revival. |

---

## 5. Delivery Notes

- **Branch:** `main` post-S-011 merge. S-012 makes no source-code commits — only evidence commits.
- **Operator-executed:** AI assistant authors this spec; operator team executes S-012 against production.
- **Timing:** T+14d after S-011 GATE E "verified" timestamp. Approximately mid-December 2026 or early January 2027 depending on S-011 execution date.
- **Evidence under:** `docs/kafka-migration/S-012-Decommission-evidence/<YYYY-MM-DD>/`.

---

## 6. Review Scope Notes

Reviewers should evaluate:

- Whether R-1 readiness gate correctly disambiguates the `Rolled-back` S-011 verdict (S-012 must not fire on a rolled-back cutover).
- Whether the R-2 decommission sequence ordering (credentials → broker → host → network → DNS → IaC) correctly stages the irreversibility.
- Whether R-5 micro-rollback semantics are clear (steps 1–2 reversible, step 3 the threshold).

Reviewers should NOT:

- Demand specific dates / hosts / IaC modules — those are environment-specific.
- Re-litigate IS / HLPS C-9 timing.
- Demand SC-7 monitoring be a gate (it is not, per IS §3 S-012).

---

## 7. Review History

### R1 (2026-04-15) — single-reviewer light pass — APPROVE

Reviewer: GPT-5.3-codex. Verdict APPROVE; only LOW/INFO notes. R-1 readiness gate correctly excludes `Rolled-back` S-011 verdict; R-2 sequence correctly stages irreversibility (creds → broker → host → network → DNS → IaC → final connect); R-5 step-3 (host tear-down) is the correct irreversibility threshold; R-3 indefinite tag retention matches IS §3 S-012. LOWs (idle-connection error-log on revoked credentials; step-4 IaC reapplicability) deferred to Delivery.

Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
