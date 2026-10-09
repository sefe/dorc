# Stock module: kafka-topic

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Messaging |
| **Provider** | `aiven/aiven >= 4.42.0, < 5.0.0` |
| **Terraform** | `>= 1.5.0` |

Creates a single Kafka topic on an **existing** Aiven Kafka service, composing the topic name from the SEFE Kafka messaging standard:

```
<business_vertical>.<environment_tier>.<scope>.<data_grouping>.<data_description>.<integrity_level>[.<origin>][.<publisher_identifier>]
```

e.g. `tr.dv.gbl.traveler.trade-events.il2`. Optional segments (`origin`, `publisher_identifier`) are omitted when left empty. Conventions follow the Trading Core `Traveler.Topics.Terraform` repository.

## Target Kafka instances (SEFE BYOC)

Topics are created on the SEFE **traveler** Kafka instances, which run as Aiven BYOC services on Azure uksouth (custom clouds `custom-sefe-nprod-workload-azure-uksouth` / `custom-sefe-prod-workload-azure-uksouth`). `service_name` is restricted to them; leave it empty and the module selects from `environment_tier`:

| `environment_tier` | Instance |
|---|---|
| `dv` | `traveler-unstable-dev` |
| `ut`, `qa`, `pp` | `traveler-non-prod` |
| `pr` | `traveler-production` |

The Kafka service itself is referenced, never managed.

## Authentication

The Aiven provider authenticates with an API token. Per the module contract the module declares no provider block; supply the token via the `AIVEN_TOKEN` environment variable on the runner, or per DOrc environment with the secure `TerraformAivenApiToken` environment property (injected as `AIVEN_TOKEN` on the terraform process only).

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `project` | string | no (`trading-traveler`) | Aiven console project. |
| `service_name` | string | no (``) | One of the traveler instances; empty = derived from `environment_tier`. |
| `business_vertical` | string | yes | Two-character vertical code (e.g. `tr`). |
| `environment_tier` | string | yes | One of `dv`, `ut`, `qa`, `pp`, `pr`. |
| `scope` | string | yes | One of `gbl`, `lcl`, `usr`, `tst`. |
| `data_grouping` | string | yes | Logical grouping segment (app/domain). |
| `data_description` | string | yes | Data description segment (entity/message type). |
| `integrity_level` | string | yes | One of `il0`–`il3`. |
| `origin` | string | no (``) | Optional 3-char origin system code. |
| `publisher_identifier` | string | no (``) | Optional publisher segment. |
| `partitions` | number | no (`3`) | Can grow, never shrink. |
| `replication` | number | no (`2`) | Aiven minimum is 2. |
| `cleanup_policy` | string | no (`delete`) | `delete`, `compact`, or `compact,delete`. |
| `min_insync_replicas` | number | no (`2`) | Min ISR for acknowledged writes. |
| `retention_ms` | number | no (`604800000`) | 7 days; `-1` = unlimited. |
| `retention_bytes` | number | no (`-1`) | `-1` = unlimited. |
| `termination_protection` | bool | no (`true`) | Client-side delete protection. |

## Outputs

`topic_id`, `topic_name`, `project`, `service_name`, `partitions`.

## Secret handling

This module takes no secret inputs and outputs no credentials.

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/kafka-topic/v<X.Y.Z>`.
