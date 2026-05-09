# DOrc stock Terraform modules

A library of curated, opinionated Terraform modules engineers can use as starting points when building application infrastructure under DOrc.

Each module satisfies `docs/Terraform/MODULE-CONTRACT.md`: pinned provider versions, validated inputs, secure-by-default outputs, a per-module `README.md` and a runnable `examples/basic/`. Modules are versioned via Git tags `stock-modules/<name>/v<X.Y.Z>` and may be referenced from a DOrc component by setting `Terraform_Template_Name` and `Terraform_Template_Version` (when the catalog API ships) or directly via Terraform `source = "git::...//stock-modules/<name>?ref=stock-modules/<name>/v1.0.0"`.

| Module | Category | Status | Owner |
|---|---|---|---|
| [`vnet`](./vnet/) | Networking | Active | DOrc platform team |
| [`sql-database`](./sql-database/) | Data | Active | DOrc platform team |
| [`storage-account`](./storage-account/) | Storage | Active | DOrc platform team |

See `docs/Terraform/MODULES.md` for the canonical index and `docs/Terraform/MODULE-CONTRACT.md` for the contract every module must satisfy.
