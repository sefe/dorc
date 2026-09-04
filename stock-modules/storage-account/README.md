# Stock module: storage-account

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Storage |
| **Provider** | `hashicorp/azurerm ~> 3.100` |
| **Terraform** | `>= 1.5.0` |

Deploys an Azure Storage account with secure-by-default settings: TLS 1.2 minimum, nested-public-access disabled, public network access opt-in only.

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `resource_group_name` | string | yes | Existing resource group. |
| `location` | string | yes | Azure region. |
| `account_name` | string | yes | 3-24 chars, lowercase alphanumeric only. |
| `account_tier` | string | no | Default `Standard`. |
| `replication_type` | string | no | Default `LRS`. Allow-listed. |
| `min_tls_version` | string | no | Default `TLS1_2`; older not permitted. |
| `public_network_access_enabled` | bool | no | Default `false`; explicit opt-in. |
| `tags` | `map(string)` | no | Applied to every created resource. |

## Outputs

`storage_account_id`, `storage_account_name`, `primary_blob_endpoint`, `primary_dfs_endpoint`.

## Secret handling

This module does not emit primary access keys as outputs by design. If a downstream consumer requires the connection string, derive it explicitly in the consumer with a `data` block plus a `sensitive` output.

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/storage-account/v<X.Y.Z>`.
