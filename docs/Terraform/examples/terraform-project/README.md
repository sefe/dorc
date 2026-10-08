# Example: terraform-project

A minimal Terraform project that deploys a single Cosmos DB SQL database by referencing the [`cosmosdb` stock module](../../../../stock-modules/cosmosdb/).

## What this example demonstrates

- Referencing a stock module by relative path (in DOrc deploys: by catalog `(name, version)`).
- Pinning provider versions via the `~> 3.100` constraint inherited from the stock module.
- **Not** declaring a `backend` block - DOrc renders the Azure Blob backend at deploy time (see [`STATE-MODEL.md`](../../STATE-MODEL.md)).
- A module with no secret inputs or outputs: no account keys are emitted, so nothing can leak into logs. If a project does take secrets, supply them at deploy time via a DOrc property whose name matches the secret pattern (`(?i)(token|pat|secret|password|key|connectionstring)`); the runner redacts such values from logs.

## Local validation

```
cd docs/Terraform/examples/terraform-project
terraform init -backend=false
terraform validate
```

(For an actual `plan`/`apply`, DOrc supplies the backend, the property values, and the Azure identity. See [`terraform-setup-example.md`](../../terraform-setup-example.md).)
