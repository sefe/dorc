# Stock modules index

Canonical index of every module under `stock-modules/`. A module is **active** when DOrc engineers should pick it as a starting point for new components; **deprecated** when superseded.

| Module | Latest | Category | Status | Owner | Description |
|---|---|---|---|---|---|
| [`vnet`](../../stock-modules/vnet/) | 1.0.0 | Networking | Active | DOrc platform team | Azure virtual network with a configurable list of subnets. |
| [`sql-database`](../../stock-modules/sql-database/) | 1.0.0 | Data | Active | DOrc platform team | Azure SQL logical server + single user database, public-network-disabled by default. |
| [`storage-account`](../../stock-modules/storage-account/) | 1.0.0 | Storage | Active | DOrc platform team | Azure storage account with TLS 1.2 minimum and public-network opt-in only. |

For the contract every module must satisfy, see [`MODULE-CONTRACT.md`](./MODULE-CONTRACT.md). For state ownership, see [`STATE-MODEL.md`](./STATE-MODEL.md).

## Tag convention

Modules are versioned via Git tags `stock-modules/<name>/v<X.Y.Z>`. To pin a module from a Terraform consumer:

```hcl
module "vnet" {
  source = "git::https://<repo>//stock-modules/vnet?ref=stock-modules/vnet/v1.0.0"
  # ...
}
```

In DOrc deploys, set the component properties `Terraform_Template_Name` and `Terraform_Template_Version` (catalog API; ships with IS step S-007).

## Adding a new module

1. Open a PR creating `stock-modules/<name>/` per the contract.
2. The CI workflow validates structure, formatting, provider lock, and the secret-output rule.
3. After merge, tag the commit `stock-modules/<name>/v1.0.0` and add the row above.

## Deprecation

When a module is superseded:

1. Open a PR adding the deprecation banner to its README and setting `deprecated: true` in the manifest.
2. Update this index's **Status** column.
3. Wait at least 90 days before removing the module's manifest entry from the catalog.
