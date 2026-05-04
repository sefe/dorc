# JIT Spec — S-009: Cut Rollback Tag, Remove All RabbitMQ Code

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-15 |
| **Step ID** | S-009 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-009 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` SC-1, R-3 |
| **Prerequisites** | S-005b, S-006, S-007, S-008 all closed (✅ as of `feat/kafka-migration` HEAD `481f4830`). |

---

## 1. Purpose & Scope

Two-phase destructive step on `feat/kafka-migration`:

1. **Cut + archive `release/pre-kafka-cutover`** at the last green pre-removal commit (`481f4830` — the S-008 audit commit). This is the canonical rollback target per HLPS R-3 / C-9.
2. **Remove every RabbitMQ-bearing surface** AND the substrate-selector flags + the inactive Direct branches that were introduced as transitional dual-substrate scaffolding by S-005b / S-006 / S-007.

Verification: the IS §3 S-009 grep suite returns zero hits on the post-removal branch.

### In scope

- Tag creation + push: `release/pre-kafka-cutover` at `481f4830`.
- Deletion of the surfaces enumerated in §3 of the S-008 audit findings (production code + tests + tooling project + NuGet ref).
- Removal of the substrate-selector flags `Kafka:Substrate:DistributedLock`, `Kafka:Substrate:RequestLifecycle`, `Kafka:Substrate:ResultsStatus`, the Direct branches in their DI extensions, and the now-dead Direct-mode publisher chain (`IFallbackDeploymentEventPublisher`, `FallbackDeploymentEventPublisher`, `DirectDeploymentEventPublisher` — the API-side direct-to-SignalR producer; the API-side **consumer** that projects Kafka onto SignalR for UI continuity stays per IS §3 S-007).
- `KafkaSubstrateMode` enum + `KafkaSubstrateOptions` simplification or removal as a consequence of the above.
- `NoOpDistributedLockService` removal (no remaining caller once the substrate flag is gone — Monitor wires the Kafka lock unconditionally).
- `NoOpRequestPollSignal` removal (same reasoning; Monitor wires the latching `RequestPollSignal` unconditionally).
- The `KafkaDeploymentEventPublisher` dual-publish ordering pattern collapses to plain Kafka emit (the SignalR fan-out branch is dead — UI continuity is preserved by the API-side Kafka consumer projection).
- Removal of `S-006 R-2` ordering-invariant fallback wiring (the comments stay as historical breadcrumb in commit messages, not in code).
- **Removal of the legacy DB-poll path is explicitly allowed but optional** in this step — IS §3 S-006 says it is "removed in S-009" but does not mandate removal *during* S-009 if doing so risks regression. The DB-poll is the safety net during cutover; safer to keep it through S-010 / S-011 and remove post-cutover. **This spec elects to RETAIN the DB-poll** through S-009; its removal is deferred to a post-cutover follow-up.

### Out of scope

- The SignalR hub itself (the UI's WebSocket transport) — out of S-009 per IS §3 S-007.
- The API-side Kafka→SignalR consumer (`DeploymentResultsKafkaConsumer` and broadcaster) — that *is* the UI continuity story.
- `IDistributedLockService` / `IDistributedLock` interface shapes — both stay (Kafka impl implements them).
- `KafkaLockCoordinator` / `KafkaDistributedLockService` / `RequestPollSignal` / `KafkaDistributedLockServiceCollectionExtensions` etc. — Kafka-side code stays; only the substrate-selector branching is removed.
- Database schema changes — none.
- Operator runbook for cutover / rollback — that's S-010.

---

## 2. Requirements

### R-1 — Tag the rollback target

`git tag release/pre-kafka-cutover 481f4830` and push to `origin`. Tag must be a lightweight or annotated tag (annotated preferred for the message metadata); MUST be branch-protected post-push so it cannot be force-moved. The tag commit MUST be the S-008-closing commit (`481f4830`), not a later one — that commit is the last green build that still carries every Rabbit surface.

### R-2 — Remove RabbitMQ production surfaces

Delete:

- `src/Dorc.Monitor/HighAvailability/RabbitMqDistributedLockService.cs`
- `src/Tools.RabbitMqOAuthTest/` (entire directory, plus removal from `src/Dorc.sln`)
- The `RabbitMQ.Client` and `RabbitMQ.Client.OAuth2` `<PackageReference>` entries from `src/Dorc.Monitor/Dorc.Monitor.csproj`.
- The Rabbit-only DI registration line in `src/Dorc.Monitor/Program.cs` that registers `RabbitMqDistributedLockService` as `IDistributedLockService` (replaced unconditionally by `KafkaDistributedLockService` via the simplified DI extension).

### R-3 — Remove RabbitMQ test surfaces

Delete:

- `src/Dorc.Monitor.Tests/DistributedLockServiceTests.cs` (Rabbit-impl unit tests).
- `src/Dorc.Monitor.IntegrationTests/HighAvailability/RabbitMqLockIntegrationTests.cs`.

`Dorc.Monitor.Tests` and `Dorc.Monitor.IntegrationTests` projects themselves stay; only the Rabbit-targeted files inside them are deleted.

### R-4 — Remove substrate-selector flags + Direct branches

Collapse the substrate-selector pattern. Each of the three `Kafka:Substrate:*` flags is removed:

- `KafkaSubstrateOptions.DistributedLock` field (added by S-005b).
- `KafkaSubstrateOptions.RequestLifecycle` field (used by S-006).
- `KafkaSubstrateOptions.ResultsStatus` field (used by S-007).

The `KafkaSubstrateOptions` class collapses to whatever non-substrate fields remain (e.g. `ResultsStatusReplicationFactor`); `KafkaSubstrateMode` enum is deleted if unused after this. The `KafkaSubstrateOptionsValidator` simplifies in lockstep — and is **deleted entirely** if `KafkaSubstrateOptions` is reduced to a trivial single-field DTO with no validation predicate worth keeping; that decision is delivery-time and recorded in the commit.

The DI extensions (`AddDorcKafkaDistributedLock`, `AddDorcKafkaRequestLifecycleSubstrate`, `AddDorcKafkaResultsStatusSubstrate`) lose their flag-read + Direct-branch and become unconditional Kafka registration. They retain their idempotency markers so multi-call hosts don't double-register.

The `ReadResultsStatusMode` / `ReadDistributedLockMode` / `ReadRequestLifecycleMode` private helpers are deleted.

### R-5 — Direct-mode publisher chain — RETAINED (UI continuity for request-lifecycle)

**Revised mid-Delivery (2026-04-15):** The original spec called for deletion of `DirectDeploymentEventPublisher`, `FallbackDeploymentEventPublisher`, and `IFallbackDeploymentEventPublisher`. Execution-time discovery showed that `DirectDeploymentEventPublisher` is the **only** path that publishes request-lifecycle events (`OnDeploymentRequestStarted`, `OnDeploymentRequestStatusChanged`) to the SignalR hub the UI subscribes to. Unlike the results-status flow (which has `SignalRDeploymentResultBroadcaster` driven by the API-side Kafka consumer), no request-lifecycle Kafka→SignalR projection exists, and IS §3 S-007 §141 explicitly defers "Removal of the SignalR re-broadcast" to post-cutover follow-up. Deleting the publisher chain in S-009 would therefore silence the UI's real-time request-lifecycle notifications — a regression S-009 is not authorised to introduce.

Resolution: **retain** all three classes. `KafkaDeploymentEventPublisher` keeps its dual-publish ordering (SignalR fan-out attempted first, then Kafka) and its `IFallbackDeploymentEventPublisher` constructor parameter. R-4 still removes the substrate-selector flags so the publisher is registered unconditionally — the post-S-009 publisher always dual-emits to SignalR (UI) + Kafka (Monitor authoritative).

The S-006 R-1 ordering invariant (Sonnet-F1 / Gemini-G1 / GPT-F6) still applies and its tests remain in scope.

R-8's grep suite is updated to exclude these three types from the "must be zero" list.

### R-6 — Remove the No-Op default plumbing

Delete `NoOpDistributedLockService` and `NoOpRequestPollSignal`. Monitor's `Program.cs` no longer registers either as a fallback default; the simplified DI extensions register the Kafka implementations unconditionally.

### R-7 — Configuration cleanup

Remove from every checked-in `appsettings*.json` (and any `loggerSettings.json` or environment-specific config files) under `src/`:

- The `Kafka:Substrate:*` flag entries (any/all of `DistributedLock`, `RequestLifecycle`, `ResultsStatus`).
- Any `RabbitMQ:*` / `HA:RabbitMQ:*` configuration sections **including individual leaf keys under those prefixes** (Monitor's HA settings such as `HA:RABBITMQ:CONSUMERTIMEOUTMS` — the Kafka lock has its own `Kafka:Locks` section).
- Any `RabbitMQ.Client.OAuth2` related keys.

Remove from `install-scripts/` any RabbitMQ provisioning / template files. (S-008 audit confirmed there are no DataService Rabbit configs to clear.)

### R-8 — Verification grep suite (IS §3 S-009 verification intent)

Post-removal, the following MUST return zero hits across `src/` and `install-scripts/` (and any in-tree IaC, though DOrc has none):

- `RabbitMQ\.` (case-insensitive)
- `EasyNetQ` (case-insensitive)
- `RabbitDataService` (case-insensitive)
- `RabbitMqDistributedLock` (case-insensitive)
- `amqp://` and `amqps://`
- `Kafka:Substrate:` (no flag entries left in any config)
- `NoOpDistributedLockService`
- `NoOpRequestPollSignal`

(`IFallbackDeploymentEventPublisher` and `DirectDeploymentEventPublisher` are deliberately retained per the R-5 amendment above — they are the UI's request-lifecycle SignalR transport pending the post-cutover follow-up that adds an API-side request-lifecycle projection.)

The grep results MUST be captured in the commit message (or an attached evidence note) so reviewers can re-run.

Documentation references to RabbitMQ inside `docs/kafka-migration/` are explicitly **out of scope** of this grep — those are migration history.

### R-9 — Build + tests still green

After removal: full solution build succeeds with zero new compile errors, and the Kafka-specific test suites continue to pass:

- `Dorc.Kafka.Lock.Tests` (26/26).
- `Dorc.Kafka.Events.Tests` (46/46 from S-006 — minus tests that referenced the deleted Direct/Fallback types; those tests are deleted, not adapted, since the surface they tested no longer exists).

`Dorc.Monitor.Tests` and `Dorc.Monitor.IntegrationTests` build with the deleted Rabbit test files removed; any other tests in those projects continue to pass.

Pre-existing build errors unrelated to S-009 scope (e.g. `Dorc.NetFramework.PowerShell` target-framework mismatch on this dotnet) are noted but not fixed in this step.

---

## 3. Accepted Risks

| Risk | Disposition |
|---|---|
| Deletion of the substrate-selector flag means rollback Kafka→Direct is no longer possible without redeploying the `release/pre-kafka-cutover` tag. | Accepted — that is exactly the HLPS R-3 rollback model; the tag is the rollback target. |
| `KafkaDeploymentEventPublisher.Dispose` order change as a result of dropping the fallback parameter is a constructor-signature break. | Accepted — internal type, only DI factory + tests use it; updated in lockstep. |
| Some test in `Dorc.Monitor.Tests` may incidentally reference `RabbitMqDistributedLockService` via integration setup — the spec lists the two known files; if a third is found during execution it is deleted under the same authority. | Accepted — execution-time discovery is in scope. |
| Removing the DB-poll path is deferred to post-cutover (this spec does NOT remove it). | Accepted — IS §3 S-006 says "removed in S-009" but the step's risk profile favours leaving the safety-net in until S-011 closes the cutover window cleanly. |
| The S-006 / S-007 ordering invariant tests (Kafka-throw-doesn't-suppress-SignalR etc.) become irrelevant when SignalR-first is removed; those tests are deleted, not adapted. | Accepted — they tested a transitional invariant that no longer applies. |
| `NoOpDistributedLockService` removal eliminates the test-time default fallback. Tests that relied on `IsEnabled=false` semantics must inject a stub explicitly. | Accepted — execution checks for affected tests and adapts them. |

---

## 4. Acceptance Criteria

### AT-1 — Rollback tag exists at the right commit

`git rev-parse release/pre-kafka-cutover` returns `481f4830`. Tag is pushed to `origin`. (Branch protection is an operator action outside the AI's authority; the spec records the requirement.)

### AT-2 — RabbitMQ source surfaces deleted

The four files / one project enumerated in R-2 + R-3 do not exist on the post-removal branch.

### AT-3 — Substrate-flag removal complete

No `Kafka:Substrate:` keys exist in any checked-in config under `src/`. `KafkaSubstrateOptions` either no longer exists or contains only non-substrate fields. `KafkaSubstrateMode` enum is deleted if unreferenced. The three DI extensions are unconditional (no flag-read).

### AT-4 — Direct-mode publisher chain RETAINED (R-5 amendment)

`IFallbackDeploymentEventPublisher`, `FallbackDeploymentEventPublisher`, `DirectDeploymentEventPublisher` continue to exist; `KafkaDeploymentEventPublisher` retains its dual-publish ordering. UI continuity for request-lifecycle SignalR notifications is preserved end-to-end.

### AT-5 — No-Op defaults deleted

`NoOpDistributedLockService` and `NoOpRequestPollSignal` do not exist. Monitor's `Program.cs` does not register them.

### AT-6 — Verification grep suite passes

The eight patterns in R-8 return zero hits across `src/` and `install-scripts/`. Evidence captured in the commit.

### AT-7 — Build + Kafka tests green

`dotnet build` against the Kafka projects + Monitor + Api succeeds; `Dorc.Kafka.Lock.Tests` and `Dorc.Kafka.Events.Tests` run green (with Direct-branch tests removed).

---

## 5. Delivery Notes

- **Branch:** `feat/kafka-migration` per IS §1.
- **Tag:** `release/pre-kafka-cutover` cut at `481f4830` (current HEAD before R-2..R-7 changes).
- **Order of operations:** tag first (R-1), then deletions in dependency order (R-2 RabbitMQ first, R-5 publisher chain second — including the corresponding update to the publisher's DI registration so the `IFallbackDeploymentEventPublisher` argument disappears at the same time the type does, R-4 substrate flags third, R-6 NoOp last), then verification (R-8) and build (R-9), then commit + push.
- **No new docs except the commit message and this spec.**

---

## 6. Review Scope Notes

Reviewers should evaluate:

- Whether R-2..R-7 collectively cover the IS §3 S-009 verification intent.
- Whether the deferral of DB-poll removal to post-cutover is justified (it is, per the risk note).
- Whether the grep suite in R-8 catches every plausible Rabbit / substrate-flag drift.

Reviewers should NOT:

- Demand exact file lists beyond what R-2..R-7 names; execution-time discovery is in scope per the §3 risk note.
- Re-open IS / HLPS settled decisions.
- Demand DB-poll removal in this step.

---

## 7. Review History

### R1 (2026-04-15) — single-reviewer light pass — APPROVE WITH MINOR

Reviewer: GPT-5.3-codex. All findings LOW. Surgical fixes applied:

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| GPT-F1 | LOW | NoOp types not in R-8 grep suite | **Accepted** — added `NoOpDistributedLockService` and `NoOpRequestPollSignal` to R-8. |
| GPT-F2 | LOW | Ambiguity between "simplify" and "delete" for `KafkaSubstrateOptionsValidator` | **Accepted** — R-4 now explicitly says delete-entirely if options collapses to a trivial DTO. |
| GPT-F3 | LOW | §5 deletion ordering needs to call out publisher-DI update belongs in R-5 | **Accepted** — §5 wording tightened. |
| GPT-F4 | INFO | Wildcard config wording should explicitly cover individual leaf keys (e.g. `HA:RABBITMQ:CONSUMERTIMEOUTMS`) | **Accepted** — R-7 wording explicit + cited example. |

Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
