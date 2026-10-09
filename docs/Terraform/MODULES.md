# Stock modules index

Canonical index of every stock module. Module source lives in the dedicated
[`sefe/dorc-terraform-modules`](https://github.com/sefe/dorc-terraform-modules)
repository (under `stock-modules/<name>/`) and is pulled at deploy time; only
the catalog manifests (`stock-modules-manifests/`) live in this repo. A module
is **active** when DOrc engineers should pick it as a starting point for new
components; **deprecated** when superseded.

| Module | Latest | Category | Status | Owner | Description |
|---|---|---|---|---|---|
| [`vnet`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/vnet) | 1.0.0 | Networking | Active | DOrc platform team | Azure virtual network with a configurable list of subnets. |
| [`cosmosdb`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/cosmosdb) | 1.0.0 | Data | Active | DOrc platform team | Azure Cosmos DB account (SQL API) + single SQL database, public-network-disabled by default. |
| [`service-bus`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/service-bus) | 1.0.0 | Messaging | Active | DOrc platform team | Azure Service Bus namespace + single queue, TLS 1.2 minimum, no SAS keys output. |
| [`clickhouse-database`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/clickhouse-database) | 1.0.0 | Data | Active | DOrc platform team | ClickHouse database on an existing Aiven ClickHouse service; termination protection on by default. |
| [`kafka-topic`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/kafka-topic) | 1.0.0 | Messaging | Active | DOrc platform team | Kafka topic on an existing Aiven Kafka service, named per the SEFE Kafka messaging standard. |

For the contract every module must satisfy, see [`MODULE-CONTRACT.md`](./MODULE-CONTRACT.md). For state ownership, see [`STATE-MODEL.md`](./STATE-MODEL.md).

## Tag convention

Modules are versioned via Git tags `stock-modules/<name>/v<X.Y.Z>` in [`sefe/dorc-terraform-modules`](https://github.com/sefe/dorc-terraform-modules). To pin a module from a Terraform consumer:

```hcl
module "vnet" {
  source = "git::https://github.com/sefe/dorc-terraform-modules.git//stock-modules/vnet?ref=stock-modules/vnet/v1.0.0"
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

### Subscription targeting

Each DOrc environment targets its own Azure subscription via the well-known
`TerraformSubscriptionId` environment property (a subscription GUID), rendered
as `subscription_id` in the azurerm provider configuration. Set it per the
[SEFE subscription standard](https://wiki/spaces/gar/pages/641725927): dev
environments → the domain's `SMT-<DOMAIN>-DV` subscription, QA/UAT/INT →
`SMT-<DOMAIN>-NP`, production → `SMT-<DOMAIN>-PR`. The Terraform state backend
is configured separately and is not
affected.

When the property is absent, the runner falls back to its configured
`Terraform:DefaultSubscriptionId` (SMT-SH-DV for DOrc instances) and logs a
warning. The fallback exists only to bootstrap fresh projects/environments
that are not yet fully configured — established environments must set
`TerraformSubscriptionId`. The runner's credential (service principal or
managed identity) needs RBAC on every targeted subscription.

### Per-environment credentials

By default terraform authenticates with the runner host's ambient Azure
identity, shared by every deployment the host runs. Each environment (and
therefore each subscription) can instead own its own service principal,
configured directly in DOrc as environment properties:

| Property | Value |
| --- | --- |
| `TerraformClientId` | The service principal's application (client) ID GUID |
| `TerraformClientSecret` | The client secret — **must be a secure property** |
| `TerraformTenantId` | The Entra tenant ID GUID |

Set all three or none. A partial configuration fails the deployment with an
error naming the missing properties, rather than silently deploying as the
shared host identity. The values are injected as `ARM_*` variables on the
terraform child process only — never the host environment — so concurrent
deployments for other environments cannot observe them, and the secret value
is redacted from any logged terraform output. `TerraformSubscriptionId` (or
the bootstrap fallback) is passed as `ARM_SUBSCRIPTION_ID` alongside.

Grant each environment's service principal RBAC only on that environment's
subscription; the shared host identity then needs no subscription-level
rights at all.

Modules using the [Aiven provider](https://registry.terraform.io/providers/aiven/aiven/)
(`clickhouse-database`, `kafka-topic`) authenticate with an Aiven API token
instead. Set the optional `TerraformAivenApiToken` environment property
(**as a secure property**); the runner injects it as `AIVEN_TOKEN` on the
terraform child process only and redacts the value from logged output. When
absent, the provider falls back to any `AIVEN_TOKEN` present in the runner
host's environment.

The API equivalent is
`POST /Terraform/templates/{name}/{version}/instantiate`, relative to the API
base URL. With `EnvironmentName` present, the endpoint validates the mapped
target and effective inputs before creating a component, then submits a
deployment request. Only explicitly supplied `Parameters` become request
overrides. Omitting `EnvironmentName` creates only the component. If request
submission fails after component creation, the component remains; retrying
with the same name and template reuses it.

## Adding a new module

1. Open a PR creating `stock-modules/<name>/` per the contract in [`sefe/dorc-terraform-modules`](https://github.com/sefe/dorc-terraform-modules).
2. That repo's CI validates structure, formatting, provider lock, and the secret-output rule.
3. After merge, tag the commit `stock-modules/<name>/v1.0.0` in that repo.
4. In this repo, add a manifest `stock-modules-manifests/<name>-1.0.0.yaml` pointing at the tag, and add the row above.

## Deprecation

When a module is superseded:

1. Open a PR adding the deprecation banner to its README and setting `deprecated: true` in the manifest.
2. Update this index's **Status** column.
3. Wait at least 90 days before removing the module's manifest entry from the catalog.
