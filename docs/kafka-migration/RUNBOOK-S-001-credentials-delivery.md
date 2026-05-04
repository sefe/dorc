# Runbook — S-001 credentials delivery

AT-4 evidence artifact. Captures successful SASL/SCRAM-SHA-256 authentication from a DOrc-owned machine or service account to the Aiven for Apache Kafka non-prod cluster.

**No credentials are recorded in this file.** Values live in the SEFE secret store; only references / env-var names appear here.

## Request

| Field | Value |
|---|---|
| Requested by | Ben Hegarty |
| Request date | 2026-04-14 |
| Target environment | Aiven for Apache Kafka — non-prod (dev tier) |
| Cluster identifier | `traveler-unstable-dev-trading-traveler.e.aivencloud.com` (Aiven shared service — see secret-store reference) |
| Broker port | 26427 |
| Schema registry endpoint | `https://traveler-unstable-dev-trading-traveler.e.aivencloud.com:26419` (Karapace-compatible) |
| Auth mechanism | `SASL_SSL` + `SCRAM-SHA-256` |
| Service-account identity | `SVC-DV-GBL-DOPS-DORC` (username is case-sensitive; Aiven stores it upper-case) |
| CA certificate | Delivered as `ca.pem` (placed at `C:\src\dorc\ca.pem` on verifying developer workstation; `.gitignore` covers `*.pem` to prevent accidental commit) |
| Secret-store location | SEFE secret store — password under the `svc-dv-gbl-dops-dorc` Aiven service record |

## Delivery

| Field | Value |
|---|---|
| Delivered by | SEFE ops (via user) |
| Delivery date | 2026-04-14 (well inside the 2026-05-01 hard date per IS §4a R-8) |

## Verification (AT-4)

| Field | Value |
|---|---|
| Verified by | Ben Hegarty + Claude (assisted probe) |
| Verification date/time | 2026-04-14 (local) |
| Machine used | Developer workstation (`C:\src\dorc` working tree, `feat/kafka-migration` branch) |
| Verification tool | `tools/aiven-connectivity-check/` — reads credentials from `AIVEN_*` env vars at runtime; never persists them |
| Broker metadata fetch | **OK.** 3 brokers observable (ids 31 / 32 / 33), 91 topics visible to this service account. |
| Schema registry reachable | **OK.** Karapace-compatible `/subjects` returned 16 pre-existing subjects (none DOrc — all trades / Trading.Core.Sample / Trading.Power.Dlc — expected at this stage). |
| Test message produced | Not in this verification (metadata-only probe; S-003 / S-007 produce first real messages). |
| Notes | Initial probe with lower-case username (`svc-dv-gbl-dops-dorc`) failed at SASL auth; Aiven stores the username case-sensitively as upper-case `SVC-DV-GBL-DOPS-DORC`. Future config / docs use the upper-case form. |

## Status

- [x] Credentials delivered (2026-04-14)
- [x] AT-4 verification passed (2026-04-14 — broker metadata + schema registry both green)
- [x] S-001 R-8 gate cleared

Also closes by the same evidence:
- **S-002 AT-7** (Aiven SASL/SCRAM connectivity from a DOrc service account) — carried forward from S-002 completion.
- **S-003 AT-7** (carry-forward) — cleared.

Signed-off: Ben Hegarty / 2026-04-14
