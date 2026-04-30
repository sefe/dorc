# Kafka local dev environment

Local Kafka + schema registry stack used by DOrc contributors and CI during the Kafka migration (see `IS-Kafka-Migration.md`, step S-001).

This is **not** used at runtime — production DOrc points at Aiven for Apache Kafka.

## Prerequisites

| Tool | Version |
|---|---|
| Podman Desktop (or `podman` + `podman-compose`) | 4.x or newer |
| .NET SDK | 8.0.x |
| Free localhost ports | 9092 (Kafka), 8081 (Karapace), 8080 (Kafka UI, optional) |

Docker Desktop also works with the same compose file, but Podman is the project standard.

## Start the stack

From the repo root:

```
podman compose -f compose.kafka.yml up -d
```

Wait until both services report healthy:

```
podman compose -f compose.kafka.yml ps
```

The Kafka broker listens on `localhost:9092` (PLAINTEXT — no auth locally). Karapace listens on `http://localhost:8081`.

### Optional: Kafka UI

Gated behind the `ui` compose profile:

```
podman compose -f compose.kafka.yml --profile ui up -d
```

Then open http://localhost:8080.

## Verify the stack is live

The unit-test suite (`Dorc.Kafka.Client.Tests`, `Dorc.Kafka.Events.Tests`) does **not** require a live broker. For an end-to-end round-trip against the running compose stack, run the opt-in integration suite:

```
dotnet test src/Dorc.Kafka.Events.IntegrationTests/Dorc.Kafka.Events.IntegrationTests.csproj
```

These tests connect to `localhost:9092` (Kafka) + `http://localhost:8081` (Karapace) by default. Override via the `KAFKA_BOOTSTRAP` and `KAFKA_SCHEMA_REGISTRY` environment variables if the stack runs on non-default ports. CI excludes this project via the `IntegrationTests` path filter, so the suite runs only when explicitly invoked locally.

## Port overrides

Set env vars before `up` to avoid collisions:

- `KAFKA_PORT` (default 9092)
- `KARAPACE_PORT` (default 8081)
- `KAFKA_UI_PORT` (default 8080)

## Teardown

```
podman compose -f compose.kafka.yml down -v
```

`-v` removes the named volume and resets cluster state. Omit `-v` to preserve state across restarts during development.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `port already in use` on `up` | Another process holds 9092 / 8081 / 8080 | Set `KAFKA_PORT` / `KARAPACE_PORT` / `KAFKA_UI_PORT` env vars before `up`. |
| Kafka container keeps restarting | Insufficient memory | Raise the Podman VM memory (Podman Desktop → Settings → Resources). Broker wants at least 1 GB. |
| Integration tests time out / `rdkafka` connect retries | Broker not yet healthy | Wait for `podman compose ps` to show `healthy`, then retry. The healthcheck has a 15s start period plus ~10s warmup. |
| Karapace returns 502 | Broker not ready when Karapace started | Restart Karapace only: `podman compose -f compose.kafka.yml restart karapace`. |

## Image licenses (AC-6)

| Image | Version | License |
|---|---|---|
| `apache/kafka` | 3.7.0 | Apache 2.0 |
| `ghcr.io/aiven-open/karapace` | 3.11.1 | Apache 2.0 |
| `provectuslabs/kafka-ui` | v0.7.2 | Apache 2.0 |

All three are OSI-approved. No Confluent Community-licensed components are used (see `HLPS-Kafka-Migration.md` R-7).

## Aiven (non-local) access

The shared Aiven cluster is reached over SASL/SCRAM-SHA-256 on TLS. Credentials are not held in this repo; see `RUNBOOK-S-001-credentials-delivery.md` for the onboarding process.
