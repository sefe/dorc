# Stock module: vnet

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Networking |
| **Provider** | `hashicorp/azurerm ~> 3.100` |
| **Terraform** | `>= 1.5.0` |

Deploys an Azure Virtual Network with a configurable list of subnets. Foundational for any deployment that places compute or data resources inside a private network boundary.

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `resource_group_name` | string | yes | Existing resource group. |
| `location` | string | yes | Azure region; must match the resource group. |
| `vnet_name` | string | yes | 2-64 chars, alphanumeric plus `. _ -`. |
| `address_space` | `list(string)` | yes | One or more CIDR blocks. |
| `subnets` | `list(object({name, address_prefix}))` | no | Subnets created inside the vnet. |
| `tags` | `map(string)` | no | Applied to every created resource. |

## Outputs

| Name | Description |
|---|---|
| `vnet_id` | Resource ID. |
| `vnet_name` | Echoed for downstream composition. |
| `subnet_ids` | Map of subnet name → resource ID. |
| `address_space` | CIDR list. |

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/vnet/v<X.Y.Z>`. See `docs/Terraform/MODULE-CONTRACT.md` for the deprecation policy.
