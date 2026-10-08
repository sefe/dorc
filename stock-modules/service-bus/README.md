# Stock module: service-bus

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Messaging |
| **Provider** | `hashicorp/azurerm ~> 3.100` |
| **Terraform** | `>= 1.5.0` |

Deploys an Azure Service Bus namespace with a single queue. TLS 1.2 minimum is enforced. Public network access defaults to enabled because disabling it requires the Premium SKU with private endpoints.

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `resource_group_name` | string | yes | Existing resource group. |
| `location` | string | yes | Azure region. |
| `namespace_name` | string | yes | 6-50 chars; letters, numbers, hyphens; globally unique. |
| `queue_name` | string | yes | 1-260 chars; letters, numbers, `. - _ /`. |
| `sku` | string | no | Default `Standard`; allow-listed (`Basic`/`Standard`/`Premium`). |
| `max_delivery_count` | number | no | Default 10; bounded 1-2000. |
| `enable_partitioning` | bool | no | Default `false`; Basic/Standard only, set at creation. |
| `public_network_access_enabled` | bool | no | Default `true`; disabling requires Premium + private endpoints. |
| `local_auth_enabled` | bool | no | Default `true`; set `false` for Entra-ID-only auth. |
| `tags` | `map(string)` | no | Applied to the namespace. |

## Outputs

`namespace_id`, `namespace_name`, `namespace_endpoint`, `queue_id`, `queue_name`.

## Secret handling

This module takes no secret inputs and **does not output SAS keys or connection strings** (including the default `RootManageSharedAccessKey`). Per `MODULE-CONTRACT.md`, consumers should prefer Entra ID auth, or derive SAS credentials via a `data` block on their side.

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/service-bus/v<X.Y.Z>`.
