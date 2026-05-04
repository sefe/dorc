# S-005b HA-suite evidence — 2026-04-14

| Field | Value |
|---|---|
| Branch | `feat/kafka-migration` |
| Build commit | `8f487906` (parent) + HA-suite topic-provisioning fix |
| Broker | Local Podman compose (`dorc-kafka`, KRaft 3.7.0) at `localhost:9092` |
| Env | `DORC_KAFKA_HA_TESTS=1 DORC_KAFKA_BOOTSTRAP=localhost:9092` |
| Run duration | 3 min 22 s |
| Verdict | **PASS — 3/3 green** |

## Per-scenario outcomes

| Test | SC bar | Duration | Outcome |
|---|---|---|---|
| `SC2a_LeaderKillFailover` | SC-2a (≤60 s partition reassignment after leader-kill) | 16 s | PASS |
| `SC2b_NewDeploymentAcceptancePostFailover` | SC-2b (≤30 s new-deployment acceptance post-failover) | 16 s | PASS |
| `SC2c_TwentyRebalancesZeroDuplicates` | SC-2c (≥20 rebalances, zero duplicate (RequestId, Version)) | 2 min 48 s | PASS — 20 rebalances induced, zero duplicates |

Transcript: `ha-suite-transcript.txt`.

## Closes

Per SPEC-S-005b §R-8 Definition of Done — at least one observed HA-suite
green run with evidence captured under this directory. S-005b is now
complete; S-006 (Kafka request-lifecycle pub/sub) is unblocked.
