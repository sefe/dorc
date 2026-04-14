# JIT Spec — S-001: Aiven credentials + local dev environment

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Step ID** | S-001 |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-13 |
| **Governing IS** | `IS-Kafka-Migration.md` (APPROVED, R2) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |

---

## 1. Scope Recap (from IS S-001)

S-001 has two distinct work streams that proceed in parallel:

1. **Credentials (ops-coordination)** — obtain SASL/SCRAM-SHA-256 credentials for DOrc against the existing Aiven Kafka cluster (non-prod first, prod later). External dependency on SEFE ops.
2. **Local dev environment (code)** — deliver a `docker-compose` that stands up a vanilla Kafka broker + Karapace schema registry for OSS contributors and CI. Wire the compose into CI so that S-002's integration tests have a target to run against.

**Hard date:** non-prod credentials by **2026-05-01** (IS §4a). Slippage triggers R-4 escalation via the S-002 pre-execution audit gate.

## 2. Branching & Commit Strategy

- Branch: `feat/kafka-migration` — long-lived integration branch for the whole initiative (per IS §1).
- All S-001 work lands on this branch via small, logical commits. No sub-branch required for this step (low-risk, mostly additive).
- Commits small and frequent (one compose service, one CI change, one readme update per commit). Commit messages reference `S-001`.
- No squash-merge until cutover-ready main merge (per IS §1).

## 3. Test-First Approach

### 3.1 Acceptance tests (author before implementation)

| AT ID | Test intent | Phrased as |
|---|---|---|
| **AT-1** | Local developer can stand up the stack | A first-run developer, following only the kafka-migration README, runs one command and ends with a healthy broker + schema-registry + (optional) Kafka UI reachable on documented localhost ports. |
| **AT-2** | Sample round-trip works | A sample Confluent.Kafka producer produces a message; a sample consumer in a new group consumes it. Verifies bootstrap and schema-registry reachability. Local stack is PLAINTEXT — SASL/SCRAM verification is AT-4's sole responsibility. The AT-2 sample code is a throwaway smoke-test harness, not a precedent for the S-002 client layer (see §8). |
| **AT-3** | CI executes against the compose | A CI job — triggered on pushes to `feat/kafka-migration` — brings up the compose services, runs AT-2's round-trip, tears down cleanly. Fails fast with readable output on any service unhealthy. PR-trigger is nice-to-have but not an acceptance requirement. |
| **AT-4** | Aiven non-prod reachable (manual, credentials-gated) | A named SEFE engineer, using delivered SASL/SCRAM-SHA-256 credentials, authenticates from a DOrc-owned machine/service account to the Aiven non-prod cluster and produces+consumes one message. Evidence captured in §4.4's runbook artifact (fields specified there). Not automated (credentials are not in CI). |

AT-1, AT-2, AT-3 are automated and form the CI gate. **AT-4 is required for S-001 closure** (see AC-4), but it is decoupled from AT-1/2/3 in the sense that **S-002 code work may begin against the local compose while AT-4 is pending**. If AT-4 slips past 2026-05-01, the S-002 pre-execution audit cannot pass, and IS §4a R-8 escalation fires — S-001 remains OPEN until either credentials are delivered or a user-recorded R-4 decision supersedes this gate.

### 3.2 Out of scope for S-001 tests

- Rebalance-callback log shape (S-002 verification).
- Avro schema-registration (S-003 verification).
- Any DOrc application code (this step delivers only dev infra + compose + CI glue).

## 4. Deliverables

### 4.1 `docker-compose.kafka.yml` (or equivalent placement)
- Services: Kafka broker (vanilla, single-node is sufficient for dev/CI), Karapace schema registry, optionally a Kafka UI for developer ergonomics.
- Named volumes for data persistence across restarts during local dev; CI invocation uses ephemeral volumes.
- Healthchecks on broker and registry so dependent services/tests wait for readiness.
- Exposes documented, non-clashing localhost ports; documented in README.

### 4.2 CI integration
- A CI job (platform per repo convention — the existing DOrc CI setup, not a new CI vendor) runs the compose for the duration of integration tests and tears it down after.
- Exit code is test-result-determined; compose failures surface as readable job output.
- Job runs on the `feat/kafka-migration` branch at minimum; extension to PRs against the integration branch is in scope.

### 4.3 Developer documentation
- `docs/kafka-migration/README-local-dev.md` (or inline within an existing docs file — author discretion): one-command stand-up instructions, required tooling versions (Docker Desktop / Podman etc.), port map, common troubleshooting (port conflicts, broker unhealthy).
- **Image license inventory** — a short table in the README listing each compose service image with its SPDX license identifier, directly supporting AC-6. This is an explicit deliverable, not implied.

### 4.4 Credentials-delivery runbook artifact
- A short markdown artifact filed in `docs/kafka-migration/RUNBOOK-S-001-credentials-delivery.md` — capturing the successful completion of AT-4. Includes: delivery date, cluster identifier (non-prod), service-account identity, auth mechanism confirmed, verification timestamp. **No credentials in the artifact.** If any listed field is classified by SEFE policy, substitute a reference to the internal secret-store entry (e.g., vault path) instead of the value. This is the evidence that the R-8 gate is cleared.

## 5. Acceptance Criteria

Step S-001 is complete when **all** of the following hold:

| AC ID | Criterion |
|---|---|
| AC-1 | AT-1 passes end-to-end from a fresh clone on a developer machine. |
| AC-2 | AT-2 passes against the locally-running compose. |
| AC-3 | AT-3 passes in CI on the `feat/kafka-migration` branch. |
| AC-4 | AT-4 completed and runbook artifact captured in `docs/kafka-migration/`. |
| AC-5 | README-local-dev covers: prerequisite tooling + versions, the one-command start command, the port map, and a troubleshooting section listing at minimum port-conflict and broker-unhealthy remediation. |
| AC-6 | Compose services use only OSI-licensed images (Apache/MIT/BSD or equivalent). Image licenses documented in README. |

## 6. Risk & Mitigation (step-local)

| Risk | Mitigation |
|---|---|
| Aiven credential delivery slips past 2026-05-01 | IS §4a pre-authorises R-4 escalation. AT-4 is decoupled from AT-1/2/3 so code work on S-002 can proceed in parallel against the local compose if credentials slip. |
| Karapace image availability / licensing | Karapace is Apache 2.0 and Aiven-published. If upstream image becomes unavailable, fall back to **Apicurio Registry** (Apache 2.0, OSI-clean). The Confluent Schema Registry is explicitly excluded as a fallback because its Community License is non-OSI. |
| CI runner resource constraints running Kafka | Single-node broker with reduced JVM heap sizing; document minimum runner specs. |
| Port conflicts on developer machines | Configurable port overrides via env vars; README documents common collisions. |

## 7. Documentation Updates

- New: `docs/kafka-migration/README-local-dev.md` (or chosen placement).
- New: `docs/kafka-migration/RUNBOOK-S-001-credentials-delivery.md` (AC-4 artifact).
- Update (if present): top-level DOrc README's contributor-setup section to link to the local-dev readme.

## 8. What this spec does NOT prescribe

- Exact compose service names, image tags, version pins (Delivery phase).
- Specific Kafka client settings in the sample producer/consumer (S-002 concern).
- CI job YAML structure, step ordering, or runner selection (Delivery phase — author chooses idiomatic for the existing CI setup).
- Exact port numbers, volume paths, healthcheck commands (Delivery phase).

## 9. Review History

### R1 (2026-04-13) — APPROVE WITH MINOR (2-reviewer panel, appropriate for bite-sized spec)

Panel: Claude Sonnet 4.6, GPT 5.3-codex. Both APPROVE WITH MINOR. No Critical/High findings. Convergent findings addressed inline:

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| AC-4 vs §3.1/§6 contradiction on whether AT-4 blocks S-001 closure | Sonnet F-1 | Medium | Accept | §3.1 rewritten: AT-4 is required for S-001 closure; the "decoupled" concept applies to S-002 code-work parallelism, not closure. R-4 escalation path spelled out. |
| AT-3 CI trigger scope ambiguous (branch-push vs PR) | GPT F-1 | Medium | Accept | AT-3 updated: branch-push on `feat/kafka-migration` is required; PR trigger nice-to-have. |
| AT-2 "auth (PLAINTEXT locally)" misleading — PLAINTEXT is not auth | GPT F-2 | Medium | Accept | AT-2 updated: auth verification is AT-4's sole responsibility; local is PLAINTEXT only. |
| AC-5 "unassisted" subjective / unverifiable | Sonnet F-2, GPT F-3 | Low | Accept | AC-5 rewritten as objective content requirements (prerequisites + one-command start + port map + named troubleshooting entries). |
| Runbook may include SEFE-classified fields | Sonnet F-3 | Low | Accept | §4.4: substitute secret-store references if classified. |
| AC-6 license inventory ownership | GPT F-4 | Low | Accept | §4.3 explicitly requires an image-license table in the README. |
| Confluent SR fallback license claim thin | GPT F-5 | Low | Accept | §6 fallback replaced with Apicurio Registry (Apache 2.0); Confluent explicitly excluded. |
| S-001 sample code vs S-002 precedent scoping seam | Sonnet F-5 | Low | Accept | AT-2 clarifies: sample code is throwaway, not S-002 precedent (cross-refs §8). |
| AT-3 failure-fast not verified | Sonnet F-4 | Low | Defer to Delivery | Delivery chooses whether to add a deliberately-broken compose test; not blocking. |
| AT-4 evidence format cross-ref | GPT F-6 | Low | Defer to Delivery | §4.4 field list already adequate; Delivery may elaborate. |
| AT-3 teardown idempotency | GPT F-7 | INFO | Defer to Delivery | Standard CI hygiene; not a spec concern. |

All Medium findings addressed; all Low findings either accepted or deferred with rationale. Status after fixes: APPROVE WITH MINOR resolved inline → transitions directly to APPROVED under CLAUDE.md triage rule (only High/Critical/Urgent/Mandatory mandate resubmission).
