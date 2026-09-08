# Stock module: sql-database

| Field | Value |
|---|---|
| **Owner** | DOrc platform team |
| **Status** | Active |
| **Category** | Data |
| **Provider** | `hashicorp/azurerm ~> 3.100` |
| **Terraform** | `>= 1.5.0` |

Deploys an Azure SQL logical server with a single user database. Public network access is disabled by default; an Azure-internal-services firewall rule is opt-in.

## Inputs

| Name | Type | Required | Description |
|---|---|:-:|---|
| `resource_group_name` | string | yes | Existing resource group. |
| `location` | string | yes | Azure region. |
| `server_name` | string | yes | 1-63 lowercase alnum + hyphens. |
| `database_name` | string | yes | 1-128 chars; reserved chars rejected. |
| `administrator_login` | string | no | Default `sqladmin`; reserved names rejected. |
| `administrator_password` | string | yes | **Must be supplied via a sensitive DOrc property; never defaulted.** Min 16 chars. |
| `sku_name` | string | no | Allow-listed (default `S0`). |
| `max_size_gb` | number | no | Default 10; bounded 1-4096. |
| `allow_azure_services` | bool | no | Default `false`; explicit opt-in. |
| `tags` | `map(string)` | no | Applied to every created resource. |

## Outputs

`sql_server_id`, `sql_server_name`, `sql_server_fqdn`, `database_id`, `database_name`.

## Secret handling

`administrator_password` is sensitive. Per `MODULE-CONTRACT.md`, it must not be defaulted in `*.tfvars` and must be supplied at deploy time via a DOrc property whose name matches the secret pattern (`(?i)(token|pat|secret|password|key|connectionstring)`); the runner redacts it from logs. Avoid emitting the password in any output (this module does not).

## Example

See `examples/basic/`.

## Versioning

Released as Git tag `stock-modules/sql-database/v<X.Y.Z>`.
