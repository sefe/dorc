# Stock module: cosmosdb

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Data |
| **Provider** | `hashicorp/azurerm ~> 3.100` |
| **Terraform** | `>= 1.5.0` |

Deploys an Azure Cosmos DB account (SQL/Core API, `GlobalDocumentDB`) with a single SQL database. Public network access is disabled by default; single-region with configurable consistency.

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `resource_group_name` | string | yes | Existing resource group. |
| `location` | string | yes | Azure region. |
| `account_name` | string | yes | 3-44 lowercase alnum + hyphens; globally unique. |
| `database_name` | string | yes | 1-255 chars; `/ \ # ?` rejected. |
| `consistency_level` | string | no | Default `Session`; allow-listed. |
| `throughput` | number | no | Default 400 RU/s; multiple of 100, bounded 400-100000. |
| `public_network_access_enabled` | bool | no | Default `false`; explicit opt-in. |
| `local_authentication_disabled` | bool | no | Default `true` (Entra-ID-only data-plane auth); set `false` to allow account-key auth. |
| `tags` | `map(string)` | no | Applied to the account. |

## Outputs

`cosmosdb_account_id`, `cosmosdb_account_name`, `cosmosdb_endpoint`, `database_id`, `database_name`.

## Secret handling

This module takes no secret inputs and **does not output account keys or connection strings**. Per `MODULE-CONTRACT.md`, consumers needing keys should prefer Entra ID data-plane auth, or derive them via an `azurerm_cosmosdb_account` `data` block on their side.

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/cosmosdb/v<X.Y.Z>`.
