# Credentials Delivery Runbook

Captures the procedure for delivering and verifying SASL/SCRAM-SHA-256 credentials from a DOrc-owned machine or service account to the Aiven for Apache Kafka non-prod cluster.

**No credentials are recorded in this file.** Values live in the SEFE secret store; only references / env-var names appear here.

## Request

| Field | Value |
|---|---|
| Target environment | Aiven for Apache Kafka — non-prod (dev tier) |
| Cluster identifier | `<service>-<project>.aivencloud.com` (Aiven shared service — see secret-store reference) |
| Broker port | per the Aiven service connection info |
| Schema registry endpoint | `https://<service>-<project>.aivencloud.com:<schema-registry-port>` (Karapace-compatible) |
| Auth mechanism | `SASL_SSL` + `SCRAM-SHA-256` |
| Service-account identity | `<service-account>` (username is case-sensitive; Aiven stores it upper-case) |
| CA certificate | Delivered as `ca.pem` (placed at `C:\src\dorc\ca.pem` on verifying developer workstation; `.gitignore` covers `*.pem` to prevent accidental commit) |
| Secret-store location | SEFE secret store — password under the DOrc Aiven service-account record |

## Delivery

| Field | Value |
|---|---|
| Delivered by | SEFE ops (via user) |

## Verification

| Field | Value |
|---|---|
| Machine used | Developer workstation (`C:\src\dorc` working tree, `feat/kafka-migration` branch) |
| Verification tool | `tools/aiven-connectivity-check/` — reads credentials from `AIVEN_*` env vars at runtime; never persists them |
| Broker metadata fetch | **OK.** 3 brokers observable (ids 31 / 32 / 33), 91 topics visible to this service account. |
| Schema registry reachable | **OK.** Karapace-compatible `/subjects` returned 16 pre-existing subjects (none DOrc — all trades / Trading.Core.Sample / Trading.Power.Dlc — expected at this stage). |
| Test message produced | Not in this verification (metadata-only probe; subsequent integration tests produce real messages). |
| Notes | Initial probe with a lower-case username failed at SASL auth; Aiven stores the username case-sensitively (upper-case here). Future config / docs use the upper-case form. |
