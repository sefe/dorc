# Runbook — S-001 credentials delivery

AT-4 evidence artifact. Captures successful SASL/SCRAM-SHA-256 authentication from a DOrc-owned machine or service account to the Aiven for Apache Kafka non-prod cluster.

**No credentials are recorded in this file.** If any field below is classified by SEFE policy, substitute a reference to the internal secret-store entry (e.g. vault path) in place of the value.

## Request

| Field | Value |
|---|---|
| Requested by | _(e.g. Ben Hegarty)_ |
| Request date | _(YYYY-MM-DD)_ |
| Target environment | Aiven for Apache Kafka — non-prod |
| Cluster identifier | _(Aiven service name, or vault reference)_ |
| Auth mechanism | SASL/SCRAM-SHA-256 over TLS |
| Service-account identity | _(DOrc service-account name, or vault reference)_ |
| Secret-store location | _(vault path — the credential lives here, not in this file)_ |

## Delivery

| Field | Value |
|---|---|
| Delivered by | _(SEFE ops engineer)_ |
| Delivery date | _(YYYY-MM-DD — must be on or before 2026-05-01 per IS §4a R-8)_ |

## Verification (AT-4)

| Field | Value |
|---|---|
| Verified by | _(named engineer)_ |
| Verification date/time | _(YYYY-MM-DD HH:mm UTC)_ |
| Machine/service account used | _(host or service-account name)_ |
| Test message produced | _(Y/N — one produce + one consume through the non-prod cluster)_ |
| Schema registry reachable | _(Y/N — Karapace endpoint reachable from the same account)_ |
| Notes | _(anomalies, retries, anything relevant)_ |

## Status

- [ ] Credentials delivered
- [ ] AT-4 verification passed
- [ ] S-001 R-8 gate cleared

Signed-off (on completion of all three): _(name / date)_
