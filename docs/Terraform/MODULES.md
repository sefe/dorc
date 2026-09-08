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

In DOrc, the template reference belongs to the **project component**:
`TerraformSourceType = Catalog`, `TerraformTemplateName`, and
`TerraformTemplateVersion`. Input values belong to the target environment's
DOrc properties, with optional request-specific overrides.

## Plan infrastructure in a DOrc environment

Open the **Terraform module catalog**, or use **Plan Terraform for this
environment** under an environment's mapped projects. The normal deployment
page also links to the catalog without requiring a dummy build artifact.

1. Choose a module version and select **Plan deployment**.
2. **Target:** select a project and an existing mapped DOrc environment with
   deployment access. Reuse an enabled component pinned to that template
   version, or create a separately named component for separate infrastructure.
   This flow requires project ownership or administrator access as well as
   permission to deploy to the environment. It does not create environments or
   map projects automatically.
3. **Inputs:** leave inputs inherited to use the environment's DOrc properties
   (including normal property scoping and resolution). If a property is absent,
   Terraform uses the module's default. Manifest defaults must describe those
   module defaults accurately. Enable **Override ... for this request** only for
   one-off differences. An omitted Boolean differs from an explicit `false`.
   Overrides do not change saved environment variables; changing target clears
   entered overrides. Inherited values are not fetched into the form.
4. **Review:** inspect project, component, environment, template version and
   overrides. Sensitive overrides remain hidden. Submit the plan request.
5. DOrc opens that request's deployment results. When the plan is ready, review
   it and choose **Confirm & Apply** or **Decline**. Submission alone does not
   apply infrastructure changes, and a failed decision stays visible for retry.

The project/component/environment identity determines the state key. Reuse the
same component in the same environment for later updates; deploy it to another
mapped environment for separate state. Do not create a new component for every
deployment. See [the state model](./STATE-MODEL.md) for backend configuration.

The API equivalent is
`POST /Terraform/templates/{name}/{version}/instantiate`, relative to the API
base URL. With `EnvironmentName` present, the endpoint validates the mapped
target and effective inputs before creating a component, then submits a
deployment request. Only explicitly supplied `Parameters` become request
overrides. Omitting `EnvironmentName` creates only the component. If request
submission fails after component creation, the component remains; retrying
with the same name and template reuses it.

## Adding a new module

1. Open a PR creating `stock-modules/<name>/` per the contract.
2. The CI workflow validates structure, formatting, provider lock, and the secret-output rule.
3. After merge, tag the commit `stock-modules/<name>/v1.0.0` and add the row above.

## Deprecation

When a module is superseded:

1. Open a PR adding the deprecation banner to its README and setting `deprecated: true` in the manifest.
2. Update this index's **Status** column.
3. Wait at least 90 days before removing the module's manifest entry from the catalog.
