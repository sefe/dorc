# Kafka Migration — Adversarial Review Remediation (2026-07)

**Status:** IN REVIEW
**Scope:** PR #611 (`feat/kafka-migration`), full-diff review vs `main`.

An eight-angle adversarial review (three correctness angles, reuse /
simplification / efficiency, altitude, conventions) produced 45 candidate
findings; each was independently verified (CONFIRMED / PLAUSIBLE / REFUTED
with code-cited justification). 29 findings survived. This document maps every
confirmed finding to its remediation or its explicit deferral rationale, per
the repo's review process (findings triaged as Accept / Downgrade / Defer /
Reject). The 29 findings appear across 27 rows: row 10 merges the two
replica-identity findings, and the commit-pattern LOW bullet merges the
sync-commit and log-level findings.

## Confirmed findings → fixes

### CRITICAL

| # | Finding | Fix |
|---|---------|-----|
| 1 | Prod and NonProd Monitor services install with the same lock consumer group (`dorc.monitor.locks.nonprod`); the runbook referenced a `Kafka:Locks:ConsumerGroupId` MSI parameter that did not exist. Lock partitions split across tiers → deployments to environments hashing to the other tier's partitions stall forever. | `KAFKA.LOCKS.CONSUMERGROUPID.PROD` / `.NONPROD` MSI parameters with tier-distinct defaults, wired through `Install.Orchestrator.bat`, `Setup.Dorc.msi.json`, `DeploySettings.template.json`, and both Monitor `ActionService.wxs` files. Runbook updated to describe the real mechanism. |
| 2 | Lock-coordinator liveness watchdog false-trips on a single benign `ERR__TRANSPORT` (routine idle-connection reap): the locks topic is idle by design so nothing ever cleared `_connectivitySuspect`, and every held lock was cancelled ~15s later on a healthy cluster — killing in-flight deployments, repeating every idle-reap cycle. | Active connectivity probe: while suspect, the coordinator issues a `Committed()` group-coordinator round-trip (max 1/5s, 3s timeout). Success clears suspicion and records contact; failure leaves the split-brain guard armed. Unit test pins the benign-disconnect no-trip behaviour. |

### HIGH

| # | Finding | Fix |
|---|---------|-----|
| 3 | Kafka startup-gate fallback registered `NoOpDistributedLockService` with only `Console.WriteLine` warnings — discarded under Windows-service hosting. Two misconfigured replicas would deploy concurrently (split-brain) with no log trail. The removed RabbitMQ path failed safe-and-logged. | Gate reporting and the locking-is-OFF warning go through Serilog (`Log.Warning` / `Log.Error`) in both hosts; `Log.Logger` is configured before the gate runs. |
| 4 | RabbitMQ's lock service rode out broker outages with a 150s re-acquisition window calibrated to observed broker recovery; the Kafka coordinator hard-cancelled all locks after ~15s with no grace path. | `Kafka:Locks:SessionTimeoutMs` (lock consumer only; shipped 150000 in Monitor appsettings). Watchdog derives ~75s liveness from it. Trade-off documented on the option: crashed-node failover latency rises to ≤150s — the same window the RabbitMQ design accepted. |
| 5 | Any peer joining/leaving the lock consumer group (scale-out, rolling restart) migrated partitions and synchronously cancelled healthy holders' locks — routine fleet operations aborted in-flight deployments on other nodes. | Static group membership (`group.instance.id`, `KafkaLocksOptions.UseStaticGroupMembership`, default on): a Monitor restarting within the session timeout reclaims its partitions with zero rebalance. Scale-out migration remains and is documented as accepted; the HA harness opts out (multiple coordinators per process would fence each other). |

### MEDIUM

| # | Finding | Fix |
|---|---------|-----|
| 6 | Startup gate checked only BootstrapServers + SchemaRegistry:Url while the installers force `AuthMode=SaslSsl` with empty credential defaults — the resulting half-configured install passed the gate then crashed both hosts at `ValidateOnStart`. | Gate requires `Kafka:Sasl:Username`/`Password`/`Mechanism` when `Kafka:AuthMode=SaslSsl` (enum parsed with binder-equivalent tolerance, incl. numeric values); gate preconditions now explicitly track the validator's. The gate-decision tests call the production `KafkaStartupGate` (retiring that half of the test file's hand-copy TODO; the fallback DI-wiring mirror in `BuildFallbackRegistrations` remains a documented known limitation). |
| 7 | Both production appsettings shipped `Kafka:Avro:AllowAutomaticSchemaRegistration=true`, contradicting the option's own documentation and silently voiding the PR-time schema gate in every installed environment. | Flipped to `false` in both appsettings; local-dev README documents the compose-stack override (`Kafka__Avro__AllowAutomaticSchemaRegistration=true` or pre-seed via `tools/snapshot-schemas`). |
| 8 | With Azure SignalR Service enabled (a supported install-time option), hub sends are delivered service-wide, so the per-replica fan-out consumer design broadcast every results-status event N× per client. | `AddDorcKafkaResultsStatusSubstrate(..., useSharedConsumerGroup)` wired from `Azure:SignalR:IsUseAzureSignalR`: in that mode all API replicas join one competing consumer group and exactly one projects each event. Broadcaster doc states both modes' invariants. |
| 9 | Results + DLQ topic provisioners awaited `CreateTopicsAsync` with no timeout or cancellation (sequential, in `IHostedService.StartAsync`): an unreachable broker delayed Monitor startup ~5 minutes and the API ~4 before the DB-poll fallback could run. | Shared `IdempotentTopicProvisioner` core (Dorc.Kafka.Client): every admin call bounded by `WaitAsync(30s, cancellationToken)`; the results provisioner's three topics batch into one `CreateTopicsAsync`. The locks provisioner keeps its documented fail-fast mismatch policy but gains the same timeout bound. Includes a public-interface addition: `IKafkaConnectionProvider.GetAdminConfig()` (default interface method + provider override through `ApplySecurity`) replaces the 6-field `AdminClientConfig` hand-copy in all three provisioners, single-sourcing admin security config. |
| 10 | Per-replica consumer-group identity: co-hosted Prod/NonProd services shared `{MachineName}` groups (wake-signals partition-split between tiers); the doc-recommended K8s pod UID contradicted the class's own stable-across-restarts requirement (orphan groups per rollout); no installer surface could set `DORC_REPLICA_ID`. | Config-bound `Kafka:ReplicaId` (`{MachineName}-{ReplicaId}` suffix; MSI writes `prod`/`nonprod` for the Monitor services and API components). `HostInstanceId` docs now require rollout-stable identity (e.g. StatefulSet pod name) and explicitly proscribe the pod UID. |
| 11 | `KafkaConsumeFailureRecorder` exists so "the consumers cannot drift", yet the ~95-line `KafkaErrorLogEntry` mapping lived outside it, duplicated per consumer, and had already drifted (`TopicName` vs `""` fallback). | Entry construction moved into recorder factory methods; consumers reduced to one-line calls. *(Cleanup batch A)* |
| 12 | `KafkaSubstrateOptions` survived the removed substrate-selector mode carrying one misleadingly-named property (`ResultsStatusReplicationFactor` actually sets RF for all three topics); its `Kafka:Substrate` section exists in no appsettings. | Folded into `KafkaTopicsOptions.ReplicationFactor`; class + validator + duplicate registrations deleted. *(Cleanup batch B)* |
| 13 | `KafkaClientOptions.EnableAutoCommit` / `AutoOffsetReset` / `ConsumerGroupId` were dead operator surface — every consumer deliberately overrides them; an operator setting them sees no change. | Removed; `GetConsumerConfig` takes a required group id, per-consumer policy is explicit at each call site. *(Cleanup batch B)* |
| 14 | The Envelope feature (`KafkaEnvelope`, extensions, header names) and the `IKafkaConsumerBuilder` DI registration are referenced by no production code — scaffolding from S-002 the final wiring never adopted, silently promising correlation-header tracing that doesn't exist. | Deleted (tests migrated to direct construction where they used the builder). *(Cleanup batch B)* |
| 15 | The 13-key Kafka config surface spans 9 hand-synchronized sites across 7 installer/config files with no consistency guard — a missed WiX line silently drops that install into fallback mode. | `InstallerKafkaConfigConsistencyTests`: forward direction (every WiX-referenced `[KAFKA.*]` property has a bat default, an msi.json mapping, and a DeploySettings seed; Prod↔NonProd ElementPath parity) plus the reverse direction (every Kafka key shipped in either appsettings template is WiX-written or carries a documented exemption with its reason) — the reverse guard is what catches a forgotten JsonFile line. *(Cleanup batch C + round 2)* |

### LOW (fixed)

- `IsCritical(Exception)` copy-pasted 6× → shared helper in Dorc.Kafka.Client. *(A)*
- Avro JSON `Canonicalise` copy-pasted 4× (schema gate, schema generator, both tools) → one public canonicaliser in Dorc.Kafka.Events. *(A)*
- Options-binding + provisioner-ordering guard duplicated between the two DI extensions → shared internal registration helper. *(A)*
- Dead `_errorLogOptions` field + ctor dependency in `DeploymentResultsKafkaConsumer`. *(B)*
- Results consumer used per-message synchronous `Commit` (one broker round-trip per record) where the sibling's `StoreOffset` + auto-commit pattern gives identical at-least-once semantics; also its per-record `broadcast-ok` log at Information vs the sibling's deliberate Debug. *(C)*
- `PollSignalRequestEventHandler` signalled the Monitor poll loop on the Monitor's own `requests.status` publishes (self-wake; each wake costs a forced full GC + 4 DB sweeps in the pre-existing loop) → signal filtered to `requests.new`. *(C)*
- Four `tools/` projects produced hyphenated assembly names violating the `Dorc.<Component>.dll` rule → `<AssemblyName>Dorc.Tools.*</AssemblyName>`. *(C)*
- `S007IntegrationTests` / `S007TestHarness` named after a plan step, not functionality (and spanning four concerns) → renamed descriptively; mangled doc comments repaired. *(C)*

## Deferred (explicit, with rationale)

| Finding | Rationale |
|---------|-----------|
| DLQ routing hard-coded in two places (route map + provisioner array) | Deliberate K-2 design decision, documented in code ("extend by adding more entries here"); exactly one DLQ exists today and the schema-gate subject coupling means a config-driven map wouldn't collapse the full edit surface anyway. Revisit when a second DLQ is added. |
| `MurmurHash2` hand-rolled for broker-partitioner parity | Verified acceptable-with-tests: known-answer vectors from Apache Kafka's reference implementation pin the hash, and the alignment comment self-documents the librdkafka `consistent_random` caveat. Residual risk noted: `KafkaLocksOptions.PartitionCount` divergence between nodes is the more plausible split-brain-by-config vector — flagged in the cutover runbook's immutability warning. |
| New `tools/` console apps target `net8.0`, not the current LTS (.NET 10) | Every CI pipeline pins the .NET 8 SDK and the whole solution is net8.0; retargeting the tools alone would break CI. Tracked as part of the solution-wide net10 migration needed before net8 EOL (Nov 2026). |
| Serializer-cache `lock` on the publish path | REFUTED as material (nanosecond critical section at deployment-scale event rates); left as-is. |

## Round 2 (review of the remediation itself)

A second adversarial panel reviewed the remediation diff. Confirmed round-2
findings and their fixes:

| Finding | Fix |
|---------|-----|
| The `Kafka:Substrate:ResultsStatusReplicationFactor` → `Kafka:Topics:ReplicationFactor` rename had no back-compat binding, and the old claim that "dev compose overrides to 1" was implemented nowhere — an out-of-repo legacy override would silently revert to RF=3 and topic creation would fail warn-only. | Legacy key honoured as a `PostConfigure` fallback (new key wins); option doc states the explicit dev-stack override. |
| `Kafka:ReplicaId` (written unconditionally by the MSI) outranked `DORC_REPLICA_ID`, silently collapsing per-replica identities a site had established via the env var. | Precedence inverted: env var → config → machine name, documented on `HostInstanceId.For`. |
| The lock coordinator derived its static `group.instance.id` from a different identity channel (`HostInstanceId.Value`) than the event consumers — co-hosted tiers would have fenced each other. | Coordinator uses the same `HostInstanceId.For(Kafka:ReplicaId)` resolution; fencing risk for same-tier co-hosting documented (requires `DORC_REPLICA_ID`, same as the fan-out warning). |
| The startup gate string-matched `AuthMode` against the literal name only and skipped `Sasl:Mechanism`, so numeric enum values or a blanked mechanism crashed at `ValidateOnStart` past the gate. | `Enum.TryParse` (name/number, case-insensitive) + Mechanism completeness check; tests cover both. |
| Cutover-Runbook timings (ST-3 "SessionTimeoutMs default 10 s", §8.2 defaults) predated the 150s session / static-membership semantics. | Runbook budgets and defaults updated, including the crashed-incarnation ghost window and decommission cost. |
| The consistency test only checked the forward (wxs → bat/msi/template) direction, so a forgotten WiX write for an appsettings key was invisible. | Reverse-direction guard added with a documented exemption list (see finding 15). |
| Doc-accuracy: finding-count arithmetic, the unlisted `GetAdminConfig` interface addition, and the over-claimed TODO retirement in finding 6. | Corrected in this document. |

## Round 3 (convergence check)

| Finding | Fix |
|---------|-----|
| **Install-breaker:** the four new `$.Kafka.ReplicaId` WiX writes targeted a key that shipped in neither appsettings.json — WixJsonFileExtension's `setValue` fails on a JSONPath with no matches and rolls back the ENTIRE install. | `"ReplicaId": ""` shipped in both appsettings; new forward-direction consistency test (`EveryWxsKafkaElementPath_TargetsAKeyShippedInTheCorrespondingAppSettings`) makes any future WiX-write-without-shipped-key a build failure. |
| The Azure SignalR shared consumer group used the bare prefix with no tier discriminator: Prod and NonProd share one broker and one results topic, so both tiers' APIs would join ONE competing group and each event would reach only one tier's SignalR service. | Shared group is suffixed with `Kafka:ReplicaId` (tier value, not machine name — the group must span a tier's machines): `dorc-api-results-status.prod` / `.nonprod`. |
| The gate checked `Kafka:Sasl:Mechanism` for non-empty only, while the validator also requires membership in the supported-mechanism set — a typo'd deploy property passed the gate and crashed both hosts at `ValidateOnStart`. | `KafkaSaslOptions.SupportedMechanisms` is now the single source shared by the validator and the gate; a typo'd mechanism gates to clean fallback. |

Convergence: candidate volume fell 29 → 7 → 3 across rounds, with two of the
three round-3 findings independently duplicated by both round-3 reviewers and
fixed within the round. All confirmed findings are fixed; nothing is escalated.

## Verification

- Unit suites (Dorc.Kafka.Client/Events/Lock/ErrorLog.Tests, Dorc.Monitor.Tests): green locally on .NET 8 (56/142/60/17/72 after all rounds).
- Compose-based integration + HA suites run in CI (`kafka-integration.yml`); the HA harness explicitly opts out of static membership and pins its own 10s session timeout, so scenario timings are unchanged.
- A stale test asserting the pre-#758 prod-cancellation guard (removed on `main`) was updated to pin the merged behaviour.
