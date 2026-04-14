# Kafka local dev environment

Local Kafka + schema registry stack used by DOrc contributors and CI during the Kafka migration (see `IS-Kafka-Migration.md`, step S-001).

This is **not** used at runtime — production DOrc points at Aiven for Apache Kafka.

## Prerequisites

| Tool | Version |
|---|---|
| Podman Desktop (or `podman` + `podman-compose`) | 4.x or newer |
| .NET SDK | 8.0.x (matches `Dorc.Kafka.SmokeTests` target framework) |
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

## Verify with the smoke test

```
dotnet test src/Dorc.Kafka.SmokeTests/Dorc.Kafka.SmokeTests.csproj
```

This produces one message and consumes it back, failing within 30s if the broker is unreachable. By default it uses `localhost:9092`; override with the `KAFKA_BOOTSTRAP` environment variable if the stack runs on a non-default port.

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
| Smoke test times out in 30s | Broker not yet healthy | Wait for `podman compose ps` to show `healthy`, then retry. The healthcheck has a 15s start period plus ~10s warmup. |
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
