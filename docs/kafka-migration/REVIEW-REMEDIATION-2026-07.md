# Kafka Migration — Adversarial Review Remediation (2026-07)

**Status:** IN REVIEW
**Scope:** PR #611 (`feat/kafka-migration`), full-diff review vs `main`.

An eight-angle adversarial review (three correctness angles, reuse /
simplification / efficiency, altitude, conventions) produced 45 candidate
findings; each was independently verified (CONFIRMED / PLAUSIBLE / REFUTED
with code-cited justification). 29 findings survived. This document maps every
confirmed finding to its remediation or its explicit deferral rationale, per
the repo's review process (findings triaged as Accept / Downgrade / Defer /
Reject).

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
| 6 | Startup gate checked only BootstrapServers + SchemaRegistry:Url while the installers force `AuthMode=SaslSsl` with empty credential defaults — the resulting half-configured install passed the gate then crashed both hosts at `ValidateOnStart`. | Gate requires `Kafka:Sasl:Username`/`Password` when `Kafka:AuthMode=SaslSsl`; gate preconditions now explicitly track the validator's. Tests call the production gate (removing the hand-copied mirror the test file's own TODO flagged). |
| 7 | Both production appsettings shipped `Kafka:Avro:AllowAutomaticSchemaRegistration=true`, contradicting the option's own documentation and silently voiding the PR-time schema gate in every installed environment. | Flipped to `false` in both appsettings; local-dev README documents the compose-stack override (`Kafka__Avro__AllowAutomaticSchemaRegistration=true` or pre-seed via `tools/snapshot-schemas`). |
| 8 | With Azure SignalR Service enabled (a supported install-time option), hub sends are delivered service-wide, so the per-replica fan-out consumer design broadcast every results-status event N× per client. | `AddDorcKafkaResultsStatusSubstrate(..., useSharedConsumerGroup)` wired from `Azure:SignalR:IsUseAzureSignalR`: in that mode all API replicas join one competing consumer group and exactly one projects each event. Broadcaster doc states both modes' invariants. |
| 9 | Results + DLQ topic provisioners awaited `CreateTopicsAsync` with no timeout or cancellation (sequential, in `IHostedService.StartAsync`): an unreachable broker delayed Monitor startup ~5 minutes and the API ~4 before the DB-poll fallback could run. | Shared `IdempotentTopicProvisioner` core (Dorc.Kafka.Client): every admin call bounded by `WaitAsync(30s, cancellationToken)`; the results provisioner's three topics batch into one `CreateTopicsAsync`. The locks provisioner keeps its documented fail-fast mismatch policy but gains the same timeout bound. |
| 10 | Per-replica consumer-group identity: co-hosted Prod/NonProd services shared `{MachineName}` groups (wake-signals partition-split between tiers); the doc-recommended K8s pod UID contradicted the class's own stable-across-restarts requirement (orphan groups per rollout); no installer surface could set `DORC_REPLICA_ID`. | Config-bound `Kafka:ReplicaId` (`{MachineName}-{ReplicaId}` suffix; MSI writes `prod`/`nonprod` for the Monitor services and API components). `HostInstanceId` docs now require rollout-stable identity (e.g. StatefulSet pod name) and explicitly proscribe the pod UID. |
| 11 | `KafkaConsumeFailureRecorder` exists so "the consumers cannot drift", yet the ~95-line `KafkaErrorLogEntry` mapping lived outside it, duplicated per consumer, and had already drifted (`TopicName` vs `""` fallback). | Entry construction moved into recorder factory methods; consumers reduced to one-line calls. *(Cleanup batch A)* |
| 12 | `KafkaSubstrateOptions` survived the removed substrate-selector mode carrying one misleadingly-named property (`ResultsStatusReplicationFactor` actually sets RF for all three topics); its `Kafka:Substrate` section exists in no appsettings. | Folded into `KafkaTopicsOptions.ReplicationFactor`; class + validator + duplicate registrations deleted. *(Cleanup batch B)* |
| 13 | `KafkaClientOptions.EnableAutoCommit` / `AutoOffsetReset` / `ConsumerGroupId` were dead operator surface — every consumer deliberately overrides them; an operator setting them sees no change. | Removed; `GetConsumerConfig` takes a required group id, per-consumer policy is explicit at each call site. *(Cleanup batch B)* |
| 14 | The Envelope feature (`KafkaEnvelope`, extensions, header names) and the `IKafkaConsumerBuilder` DI registration are referenced by no production code — scaffolding from S-002 the final wiring never adopted, silently promising correlation-header tracing that doesn't exist. | Deleted (tests migrated to direct construction where they used the builder). *(Cleanup batch B)* |
| 15 | The 13-key Kafka config surface spans 9 hand-synchronized sites across 7 installer/config files with no consistency guard — a missed WiX line silently drops that install into fallback mode. | Consistency test parses the three `.wxs` files, `Setup.Dorc.msi.json`, and `Install.Orchestrator.bat` and asserts every Kafka key present in the appsettings templates is wired through each layer. *(Cleanup batch C)* |

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

## Verification

- Unit suites (Dorc.Kafka.Client/Events/Lock/ErrorLog.Tests, Dorc.Monitor.Tests): green locally on .NET 8.
- Compose-based integration + HA suites run in CI (`kafka-integration.yml`); the HA harness explicitly opts out of static membership and pins its own 10s session timeout, so scenario timings are unchanged.
- A stale test asserting the pre-#758 prod-cancellation guard (removed on `main`) was updated to pin the merged behaviour.
