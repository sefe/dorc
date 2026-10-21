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
| [`clickhouse-database`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/clickhouse-database) | 2.0.0 | Data | Active | DOrc platform team | ClickHouse database on an Aiven ClickHouse service; creates the service layer by default (`create_service = false` re-uses an existing one); termination protection on by default. |
| [`kafka-topic`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/kafka-topic) | 2.1.0 | Messaging | Active | DOrc platform team | Kafka topic on an existing Aiven Kafka service, named per the SEFE Kafka messaging standard with the linked DOrc environment as the final segment; optional per-topic ACL grants. |
| [`postgresql-database`](https://github.com/sefe/dorc-terraform-modules/tree/main/stock-modules/postgresql-database) | 2.0.0 | Data | Active | DOrc platform team | PostgreSQL database on an Aiven PostgreSQL service; creates the service layer by default (`create_service = false` re-uses an existing one); termination protection on by default. |

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
DOrc properties, with explicit component/environment non-secret configuration
and optional request-specific overrides.

Every catalog release also pins a full `source.commit_sha` verified against
the intended tag at deployment time. A tag name alone is mutable; branches
with the same name are not considered for catalog releases. Consumers using
the raw Terraform Git source syntax above do not get DOrc's tag/SHA
verification and should use a reviewed full commit SHA for an immutable pin.
Catalog version ordering supports stable `major.minor.patch` releases only
(legacy leading `v` accepted); prerelease/build labels are not supported.

## Service ownership and v1 to v2 upgrades

ClickHouse/PostgreSQL v1 manages a database on an existing Aiven service.
Their v2 modules **create and own the service by default**. Changing the
component's pinned version from v1 to v2 is therefore not a routine patch:

- For a v1 database on an externally managed service, explicitly persist
  `create_service=false` before the v2 plan, retain the existing state identity,
  and inspect the plan for unintended service/database creation or replacement.
- For a new v2 deployment that creates its service, persist
  `create_service=true` for its entire lifetime. Switching to false does not
  hand off ownership: the counted service resource disappears from the
  configuration and Terraform may plan its destruction (or protection may
  reject the plan). Enabling it later on an existing externally managed
  service may instead attempt duplicate creation. Neither is an import.
- Any genuine ownership transfer requires a separately reviewed, backed-up,
  operator-controlled state/import procedure. Do not disable termination
  protection just to force an upgrade through. Keep component/environment
  state keys stable; renames require explicit state/backend migration, not a
  fresh empty state.

Kafka topic v2 adds the required `dorc_environment` parameter and appends it as
the final topic-name segment. Changing a component's pin from kafka-topic v1 to
v2 therefore renames the topic: Terraform plans a destroy/create of the
`aiven_kafka_topic` resource (rejected while `termination_protection` is on,
and a data-loss event if it is turned off to force the plan through). Treat it
as a new component, or perform an operator-controlled `moved`/import of the
existing topic into the v2 address before planning; do not use the catalog
Upgrade action as an in-place upgrade.

Aiven PostgreSQL service creation pre-creates `defaultdb`. PostgreSQL v2
therefore rejects `database_name=defaultdb` with `create_service=true`
(including when omitted, because true is the default) **before plan**.
Use a different database name for a new service. `create_service=false` means
an already existing, externally managed service, not adoption of its existing
databases; `defaultdb` on that service still needs a reviewed external import
if it already exists. If an earlier failed apply created the service before
the duplicate-database error, stop retries, inspect real infrastructure and
the current state, back it up, then have an operator reconcile/import the
existing resources or choose a new database through a reviewed plan. Do not
flip `create_service` merely to bypass the error.

The current upstream v2 source itself still attempts to create the database;
the guard is a DOrc manifest contract. An upstream correction needs its own
new module release and review, not mutation of the published tag.

## Supported operations and reproducibility

This feature provides plan/review/apply only. Returned module outputs describe
resources and provenance; they are not managed import/adoption or destroy
commands. DOrc reference-data deletion is not Terraform resource destruction.
There is no dedicated module lifecycle CLI or managed destroy/import endpoint.
Use the UI or documented API; exceptional state recovery is an operator task.
Approval has no automatic expiry. Decline and replan after material changes
rather than treating an old approval as a promise that infrastructure is current.

Module commit pins do not lock providers or the Terraform CLI. Both this
repository and upstream CI run formatting/validation with a selected CI CLI;
neither currently enforces committed `.terraform.lock.hcl` files or a plugin
mirror. Fresh init may resolve a newer compatible provider. Preserve/review
the root lock file and ensure plan/apply use the same CLI and provider binaries.

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
   Blank optional Number and Bool overrides are invalid; turn off the override
   to inherit or use the module default. Optional String overrides can be empty.
   Number inputs marked `integer` reject fractional values before submission.
   Overrides do not change saved environment variables; changing target clears
   entered overrides. Inherited values are not fetched into the form.
   An unresolved `resource_group_name` can be filled from the environment's
   single resource-group ARM identity in the Cloud tab, never its display name.
   Inherited/default values are preserved; ambiguous or invalid identities are
   left unset.
4. **Review:** inspect project, component, environment, template version and
   overrides. Sensitive overrides remain hidden. Submit the plan request.
5. DOrc opens that request's deployment results. When the plan is ready, review
   it and choose **Confirm & Apply** or **Decline**. Submission alone does not
   apply infrastructure changes, and a failed decision stays visible for retry.

The project/component/environment identity determines the state key. The wizard
previews the same case-folded, hex-escaped key used by Monitor. Reuse the
same component in the same environment for later updates; deploy it to another
mapped environment for separate state. Do not create a new component for every
deployment. See [the state model](./STATE-MODEL.md) for backend configuration.

### Readiness, receipts and submission retries

The Inputs and Review steps distinguish DOrc-resolved, Saved component
configuration, Module default, Missing, Not set and Not checked. DOrc-resolved
includes normal environment/global properties and configured fallbacks, not
necessarily a property owned directly by that environment. A sensitive input's source does not imply that its
value is disclosed. Configuration preflight uses the same Azure credential
and subscription-cloud-resource rules as the runner. It reports invalid
inherited input names without their values. This is **not a cloud permission
probe**. Configure Catalog state storage under Project Ref Data > Definitions >
Terraform state backends: a project default or complete per-environment override.
The API shows the selected backend and scope; submission pins that selection
through plan/apply and retries. There is no Monitor-wide state fallback. Runner ambient credentials/
default workload subscription remain explicitly unknown to the API. Known non-secret
subscription and standard Aiven project/service identifiers are shown where
available; other or sensitive targets remain undisclosed/unknown. One-off
overrides can change the target, so the final plan remains authoritative. Verify workload
permissions and Storage Blob Data Contributor on the Azure state container
before approval, including for Aiven workloads.

New plans retain a database receipt captured from the plan-time source archive:
project/environment/component, module/version, resolved source commit, state
identity, known non-secret workload target identifiers, creation time and artifact digests. The plan dialog shows that
receipt and retained approver/time, not the component's current pin. Terminal
results and archived attempts can open retained plans read-only. Restart does
not delete these receipts or approval records. Older records have no fabricated
receipt or timestamp; a missing blob is reported as unavailable. There is no
new retention deadline, and artifact access still requires environment modify
authority. Receipt storage does not contain tfvars, Terraform state, credentials
or general input values. A receipt is evidence of the plan, not a claim that inventory registration
or post-apply archive copying succeeded.

`POST /Terraform/templates/{name}/{version}/instantiate` accepts an optional
`SubmissionKey` random UUID for create-and-deploy submissions. Reuse the key
for a retry with the same explicit payload: it returns the original component
receipt and request ID, even after a lost HTTP response. Changing template,
target, parent, component name or explicit inputs requires a new key; conflicting
reuse returns HTTP 409. Keys are scoped to the authenticated caller and this
operation. The wizard keeps the key while retrying the same submission in the
open dialog; closing/reopening begins a new submission. External callers should
retain their own key until they have received the request ID.

Request creation and its durable key are committed in one SQL transaction;
there is no in-memory lock or expiry window. Only a salted payload fingerprint
is retained with the key, never the canonical payload used for comparison.
Legacy callers omitting a key remain non-idempotent. Create-only calls must
omit it. Component creation retains the existing separate atomic create/reuse
boundary; a submission failure can leave the component available for retry.

Rollout: publish the additive `TerraformDeploymentReceipt`, `CatalogSubmission`,
`CatalogEnvironmentInputs`, `CatalogRequestInputs`, `CatalogModuleUpgrade`,
`TerraformDeploymentOutcome` and `TerraformInventoryProvenance` schema before
upgrading API/Monitor/Runner. Do not
backfill receipts from current component pins. Deploy the matching API clients
and web UI together. Local SQL/browser regression coverage is not live cloud,
MSI or deployment acceptance.

### Saved configuration and safe upgrades

For an existing component, **Replace saved non-secret inputs** is an explicit,
separate action on the Inputs step. It replaces the entire saved set for that
component/environment with the current non-secret overrides; omitted entries
are removed. It does not submit a request or change global/environment
properties. Existing deployments are unchanged until someone saves. Manifest
sensitivity, secure property/configuration metadata and known secret-name
patterns are rejected by the server. Keep credentials in secure DOrc properties;
secret overrides remain one-off.

Saved direct module inputs override inherited values; explicit request overrides
win over saved inputs. They do not redefine unrelated expression dependencies.
Common request creation atomically snapshots the selected module pin and saved
configuration, including for non-wizard submissions. Later saves/upgrades do not
change an already queued snapshot. Old requests without snapshots retain legacy
resolution. Configuration saves require project/environment authority and an
expected revision; stale saves return 409. Persist `create_service=false` this
way when deliberately retaining an externally managed Aiven service.

Select a newer stable release in the catalog and choose **Upgrade an existing
component**. Preview lists the old/new version, contract changes and every
environment mapped to the owning project, not just those visible to the caller.
Execution requires project ownership/admin and modify authority in every
affected environment, explicit impact confirmation, matching component/config
revisions and an unchanged mapping set. Components shared by multiple projects
are rejected for operator review. Pending/running/approval-waiting requests in
affected project environments block an upgrade. Concurrent submissions and
upgrades serialize at the component row.

Upgrade changes the shared pin only, preserving component names and state keys;
it never submits deployment requests. Each environment needs a fresh plan and
approval. Removed/sensitive/incompatible saved inputs block the upgrade until
explicitly replaced. New required inputs are listed for configuration before
the next plan; inherited values and cross-input constraints are authoritatively
checked on submission/runner execution, not certified by upgrade preview.
Transitions introducing default service ownership require explicit consent to
save `create_service=false` across all mapped environments. This is not adoption
or import. Ref-data edits cannot silently repin, rename, change source type or
replace an existing Catalog identity.

### Apply outcomes, metadata recovery and inventory provenance

The receipt separately reports Terraform apply, inventory registration and audit
source-copy outcomes. A successful apply stays successful when post-apply
metadata work fails. Counts and bounded registration warnings are retained;
`RunningOrUnknown` / `FailedOrUnknown` must not be interpreted as proof that no
infrastructure changed. The plan-time source upload remains mandatory.

After successful apply, a restricted blob retains the runner's identity-only
inventory snapshot with a recorded digest, not raw Terraform state. Recovery
uses the verified retained snapshot or approved plan-time source archive:
**Retry inventory registration only** and **Retry audit source copy only** never
run Terraform apply or fetch a new checkout. Recovery is administrator-only
because registration can update shared definitions; original environment
authority, receipt/approval consistency, database-held serialization and
expected outcome revision are also required. Missing/changed/inaccessible
artifacts return a non-success response with manual-review guidance. Audit
copy recovery is independent of missing inventory material. Superseded
inventory cannot be replayed over a newer successful apply.

Inventory provenance records component/environment observations, first/last
deployment result, last-seen identifier/time and derivable database links.
It is not exclusive ownership of a shared definition and does not derive
ownership from editable tags. Only a complete, trustworthy state export with
successful registration marks that component/environment's missing observations
`MissingFromLatestState`. Incomplete/failed/ambiguous exports never do so.
Other owners and manually maintained definitions remain intact.

**Show current inventory provenance** is explicitly current metadata, not an old
state export. **Mark reviewed — metadata only** uses revision concurrency and
project/environment authorization. It changes only the provenance review flag:
no cloud deletion, definition deletion, detach or state mutation is performed.

### Project reference data versus execution history

Catalog instantiate creates an ordinary project-linked Terraform component with
an empty script path. It does not create a `Script` or `ScriptAudit` record.
Project reference-data JSON is current mutable configuration, not an immutable
execution ledger; the generic ref-data PUT audit records submitted JSON, while
catalog instantiate does not create that audit entry. Generic project build
history may show an empty build or `N/A` for artifact-free catalog requests.

Use the deployment result's retained plan receipt for the actual planned module
version/source SHA and approval, not Scripts or current component pins. Upgrade
revision records identify pin changes; request snapshots preserve queued input
selection. Restart archives result attempts while keeping receipts and approvals.
Reference-data removal does not destroy Terraform resources or state, and
missing historical material is not reconstructed from current configuration.

### Subscription targeting

Each DOrc environment targets its own Azure subscription, resolved in
precedence order:

1. **A `Subscription` cloud resource attached to the environment**
   (environment details → **Cloud** tab): create a cloud resource with
   resource type `Subscription` and the subscription GUID as its resource
   identifier (or in its subscription field). The environment model itself
   then names where its deployments land. Attach exactly one — conflicting
   subscription cloud resources make targeting ambiguous and fail resolution
   with a logged warning.
2. The legacy `TerraformSubscriptionId` environment property (a subscription
   GUID), for environments not yet carrying cloud resources.

The resolved GUID is rendered as `subscription_id` in the azurerm provider
configuration. Choose the subscription per the
[SEFE subscription standard](https://wiki/spaces/gar/pages/641725927): dev
environments → the domain's `SMT-<DOMAIN>-DV` subscription, QA/UAT/INT →
`SMT-<DOMAIN>-NP`, production → `SMT-<DOMAIN>-PR`. The Terraform state backend
is configured separately and is not
affected.

When neither is set, the runner falls back to its configured
`Terraform:DefaultSubscriptionId` (SMT-SH-DV for DOrc instances) and logs a
warning. The fallback exists only to bootstrap fresh projects/environments
that are not yet fully configured — established environments must designate
their subscription. The runner's credential (service principal or
managed identity) needs RBAC on every targeted subscription.

### Applied resources on the environment's Cloud tab

After a confirmed apply succeeds, DOrc registers each managed resource the
terraform state reports (resource groups, Cosmos accounts, Service Bus queues,
Kafka topics, …) as a cloud resource and attaches it to the target
environment, with the same audit trail as a manual edit on the **Cloud** tab.
The entry's resource type is the terraform type (e.g.
`azurerm_resource_group`) — never `Subscription`, so auto-registered entries
cannot interfere with the subscription targeting above. Existing entries are
matched by resource identifier and reused; a name collision with a different
resource is registered under `<name> (<terraform type>)`, then under a stable
SHA-256 identity-qualified name if that name is occupied too. Qualified names
fit the 250-character limit and are reused on redeployment, so identical names
in additional projects/environments are not silently omitted. Tags on existing
entries are operator-owned and preserved.
Registration is best-effort: a registration problem is logged but never fails
a deployment whose apply succeeded.

Database-flavoured resources additionally appear on the environment's
**Databases** tab. Their engine is stored separately from tags:
`CosmosDb`, `ClickHouse`, or `PostgreSql`. Cosmos databases use the globally
unique Cosmos account name as server. Aiven databases use `project/service`
as server, not just the service name: two projects can contain identically
named services and databases. Existing entries with the same database name
and scoped server are reused, and re-deployments leave attachments unchanged.

Both the environment tab and the main **Databases** page show engine logos
with accessible names and tooltips. The add/edit dialog allows an explicit
engine selection; manually created databases default to SQL Server.
API clients may omit `DatabaseType` (or send `null`): creation defaults to
`SqlServer`, whereas an update preserves the stored engine. To change an
engine, send an explicit enum value, including `SqlServer`.

### Upgrading earlier database registrations

All pre-existing estate databases remain SQL Server unless their engine is
set explicitly or a successful Terraform apply registers their actual type.
Engine tags are ordinary editable metadata, not a migration source: publishing
the dacpac never infers or overwrites a database engine from tags. The earlier
tag-based `BackfillDatabaseTypes.sql` has been removed.

`ScopeAivenDatabases.sql` upgrades earlier ClickHouse/PostgreSQL registrations
from `service` to `project/service`, preserving database IDs and environment
links. It requires an already classified engine and matching Aiven cloud-resource
identifiers attached to the same environment. Already scoped rows are unchanged.
Conflicting project evidence, duplicate target identities, or an oversized
scoped server stop the migration with an actionable error rather than guessing.
Reconcile the affected records and environment links before republishing.

Records with no matching cloud evidence, or those still carrying the old
`SqlServer` default, are not reclassified automatically. For these, inspect the
actual provider resource and set the engine and scoped server explicitly in
the add/edit dialog before reapplying. Aiven identifiers have the form
`project/service/database`. Do not infer the project or engine from tags.
If an earlier build already merged two projects into one database record,
split it into separate scoped records and correct their environment links;
a name-only migration cannot recover that ownership safely.

The deployment wizard closes the loop: when the selected environment carries
exactly one resource-group cloud resource (resource type `ResourceGroup` or
`azurerm_resource_group`) and the module declares a `resource_group_name`
input, the wizard prefills that input from the environment — editable like
any other override, and left empty when the environment has zero or several
resource groups.

### Cloning environments

The clone dialog offers separate, default-off options for Container, Cloud
Resource and API Registration mappings. These attach the same existing records
to the clone; they do not provision or duplicate infrastructure. Cloud mappings
include Subscription and ResourceGroup records, so selecting that option also
inherits their targeting. Leave it off when the clone must target different
infrastructure, then attach the intended targeting resources explicitly.
API callers opt in through `CopyContainerMappings`,
`CopyCloudResourceMappings` and `CopyApiRegistrationMappings`; omitted flags
preserve the previous clone behavior.

### Verification boundaries

Automated checks cover manifest rejection (including unsupported complex input
types), manifest immutability and sensitive-name lint self-tests, source ZIP
contents/provenance and tfvars redaction, request-property redaction, and
Terraform controller parameter handling. Database acceptance tests publish
the dacpac into disposable SQL Server databases to exercise upgrade behavior.
These checks do not substitute for the following live-environment acceptance:

- After a confirmed apply, download `{deploymentResultId}.terraform-source.zip`
  from the configured plan-artifact container; check the provenance commit
  against the deployed module and inspect redacted tfvars.
- Use a non-production module with a synthetic sensitive parameter; verify
  masking in the wizard, request/result API responses and deployment logs.
- Confirm the installed API/Monitor/runner identities can read the module
  source, access the state/plan storage and reach the intended cloud service.
  Never use a real credential as a test marker.

Record the environment, application commit and deployment-result ID when
performing these checks; a unit-test pass alone is not a live acceptance result.
Disposable SQL test databases explicitly use the project's
`SQL_Latin1_General_CP1_CI_AS` collation. Inheriting a different host-server
collation introduces an unintended column-collation change during legacy tag
widening, which DacFx correctly refuses with its data-loss guard enabled.
Existing installations using another collation need a separately reviewed
collation plan; do not disable the data-loss guard to force this upgrade.

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
is redacted from any logged terraform output. The resolved target
subscription (cloud resource, property, or the bootstrap fallback) is passed
as `ARM_SUBSCRIPTION_ID` alongside.

Grant each environment's service principal RBAC only on that environment's
subscription; the shared host identity then needs no subscription-level
rights at all.

Modules using the [Aiven provider](https://registry.terraform.io/providers/aiven/aiven/)
(`clickhouse-database`, `kafka-topic`, `postgresql-database`) authenticate with an Aiven API token
instead. Set the optional `TerraformAivenApiToken` environment property
(**as a secure property**); the runner injects it as `AIVEN_TOKEN` on the
terraform child process only and redacts the value from logged output. When
absent, the provider falls back to any `AIVEN_TOKEN` present in the runner
host's environment.

> **Aiven modules ignore the Azure subscription targeting above.** Aiven
> resources are addressed by Aiven project and service name, not by Azure
> subscription. SEFE's Aiven BYOC infrastructure sits in the historic flat
> subscriptions — non-production in **SMT-NP** (`rg-np-aiven-1-uks`) and
> production in **SMT-PR** (`rg-pr-aiven-1-uks`) — which predate the
> `SMT-<Vertical>-<Env>` subscription model and are not being expanded.
> Attaching a `Subscription` cloud resource or `TerraformSubscriptionId`
> property to an environment has no effect on the `clickhouse-database`,
> `kafka-topic` and `postgresql-database` modules; where they land is determined solely by the Aiven
> project/service inputs and the `TerraformAivenApiToken` credential.

The API equivalent is
`POST /Terraform/templates/{name}/{version}/instantiate`, relative to the API
base URL. With `EnvironmentName` present, the endpoint validates the mapped
target and effective inputs before creating a component, then submits a
deployment request. Only explicitly supplied `Parameters` become request
overrides. Omitting `EnvironmentName` creates only the component. If request
submission fails after component creation, the component remains; retrying
with the same name, project, template version and parent reuses it when enabled.

Component creation and reuse are one serializable database operation. Names
are checked globally using DOrc's case-insensitive SQL collation; a collision
never renames, replaces, reparents or enables an existing component. Reuse is
available only with `EnvironmentName` supplied and only for a Catalog component
owned exclusively by the destination project with the same template name,
version and parent. An omitted `ParentComponentId` means top-level placement,
not an unspecified parent. Create-only duplicates and incompatible identities
return HTTP 409, disabled reusable components and invalid parents return 400,
and a project deleted before persistence returns 404. Concurrent matching
deployments may reuse the same component, but each successful submission still
creates its own deployment request; this is not request-level idempotency.

## Adding a new module

1. Open a PR creating `stock-modules/<name>/` per the contract in [`sefe/dorc-terraform-modules`](https://github.com/sefe/dorc-terraform-modules).
2. That repo's CI runs formatting, init/validate, tflint and the secret-output rule. Provider lock-file verification is not currently enforced.
3. After merge, tag the commit `stock-modules/<name>/v1.0.0` in that repo.
4. Resolve the tag to its full commit (peel annotated tags), inspect the actual tagged inputs/outputs/provider constraints, then add `stock-modules-manifests/<name>-1.0.0.yaml` with that tag and its `source.commit_sha`. Add the row above. Do not publish duplicate identities or change an existing release payload; fixes require a new version.

## Deprecation

When a module is superseded:

1. Open a PR adding the deprecation banner to its README and setting `deprecated: true` in the manifest.
2. Update this index's **Status** column.
3. Give consumers at least 90 days to plan migration, but retain every published manifest, tag and commit indefinitely. Deprecation hides normal discovery, not pinned resolution. CI allows only the deprecation flag/reason lifecycle changes on existing releases; it rejects deletion, payload edits, duplicate identities and reactivation.
## Deployment definition revisions

Catalog and direct-source components participate in the common
[deployment definition and execution-evidence contract](../DeploymentDefinitions.md).
Reference-data edits cannot bypass Catalog upgrade gates or migrate state.
New requests retain submitted definitions; history distinguishes submitted
configuration from plan-time source/approval evidence.
