# Terraform backend configuration

Configure state storage in **Project Ref Data > Definitions > Terraform state
backends**, separately from the workload subscription/credentials and the DOrc
plan/artifact store. Add a project default, environment overrides, or only explicit
environment entries. Each override is complete; missing fields never inherit
from the default. Only mapped environments can be added as overrides.

The same configuration round-trips through Advanced JSON and the revision-checked
project APIs as `Project.TerraformStateBackends`:

```json
{
  "TerraformStateBackends": [
    {
      "EnvironmentId": null,
      "StorageAccount": "stprojectstate",
      "ContainerName": "tfstate",
      "SubscriptionId": "11111111-1111-1111-1111-111111111111",
      "ResourceGroup": "rg-project-state"
    },
    {
      "EnvironmentId": 42,
      "StorageAccount": "stprojectprodstate",
      "ContainerName": "tfstate",
      "SubscriptionId": "22222222-2222-2222-2222-222222222222",
      "ResourceGroup": "rg-project-prod-state"
    }
  ]
}
```

Replace `42` with the actual mapped DOrc environment ID. The account and private
container must already exist. `StorageAccount`, `ContainerName`, and a nonempty
Azure subscription GUID are required; `ResourceGroup` is optional. Do not put
credentials, URLs, keys or SAS tokens in these fields. Monitor retains the existing
project/component/environment state key and sends the submitted backend to the runner.

The backend explicitly sets `subscription_id` and `use_azuread_auth = true`.
Grant **Storage Blob Data Contributor** on the container to the identity Terraform
actually uses: the environment service principal when configured, otherwise the
runner's ambient identity. It does not need permission to list storage account
keys. Authentication errors are fatal; there is no key, SAS, local-state or
Monitor-wide fallback. These settings apply to Catalog components. Legacy Git,
shared-folder and artifact sources retain their own backend/authentication modes;
configuring a project does not silently migrate their state.

Workload targeting remains independent: an environment subscription resource,
`TerraformSubscriptionId`, or the runner's `Terraform:DefaultSubscriptionId`
configures the workload provider. State and workload subscriptions may match, but
neither is inferred from the other. Aiven workloads still need the Azure state backend.

## Submitted requests and existing state

The effective backend is selected and captured in the request's project snapshot
inside the submission transaction. Plan, approval, apply and retries use that
selection, never live project settings or request-property overrides. Missing
backend configuration refuses a new Catalog submission. Readiness shows whether
the selection is a project default or environment override, without claiming
storage existence, connectivity or permissions have been checked.

Once submitted, ordinary project edits cannot move/remove that scope's backend.
This deliberately also protects requests that have not yet run. Existing Catalog
receipts guard the previously observed storage account/container when adopting
configuration from the older Monitor-wide model. An actual move requires an
operator-reviewed migration and controlled configuration/binding update; the UI
does not provide a bypass. Preserve state lineage, resource addresses and the
existing key, then submit and approve a fresh plan.

There is no automatic adoption from Monitor settings and no reconstruction of
legacy submitted intent. Before cutover, record and verify the backend used by
each existing deployment, configure matching project/environment entries, and
drain/cancel old requests. Catalog requests whose project snapshot lacks a backend
must be resubmitted and replanned. Where historical evidence is missing, verify
the actual state under the approved operator process before submitting anything.

### Audited cutover of an empty testing backend

For a backend that failed before any retained Catalog plan, administrators can
use `POST /Projects/{projectId}/terraform-state-backend-cutovers`. This is an
operator API, not an ordinary reference-data edit or a state-copy operation.
It requires project and environment write access as well as the Admin role.

Administrators can drive the same operation from the project reference-data
page: each environment-scoped backend row shows a **Migrate backend…** button
(hidden for non-admins and disabled while the page has unsaved edits). The
dialog calls `GET …/terraform-state-backend-cutovers/preview?environmentId=`,
which returns the current backend, the pinned request count, any legacy
requests and the server's current blockers without changing anything. The
dialog will not submit while a blocker remains, and it submits exactly the
payload described below; the server re-evaluates every guard on submission.

Stop external writers and verify both the old and replacement containers are
empty using authorized blob-data access. Retain a non-secret verification record
with the resource identities, UTC time and checks performed; calculate its SHA-256.
The API records the operator's attestation and evidence digest. It does **not**
independently check Azure storage, connectivity or permissions.

Submit the current reference-data `Revision`, mapped `EnvironmentId`, the
`ExpectedBackend` coordinates, a complete environment-scoped replacement `Backend`,
`ConfirmBothContainersEmpty: true`, and the 64-character `VerificationSha256`.
The operation refuses active requests, retained runner processes, retained
Catalog plans/receipts, and missing or conflicting submitted backend evidence.
It cannot be used to migrate nonempty state or deployments with existing plans.
It also refuses any archived deployment history in that project/environment.

#### Legacy requests without pinned definitions

Requests submitted before definition pinning existed have no backend evidence.
Older successful or partially failed runs may have used source-owned or local
state: empty newly configured containers are not evidence that those runs created
no infrastructure. By default the cutover refuses such requests and lists their IDs.

An administrator may retire them explicitly by adding `LegacyRetirement` with the
exact `RequestIds` the server listed and a `StateDisposition` (20-4000 characters)
stating what was done about any infrastructure those runs applied - destroyed by
hand, imported into a fresh state under a separately reviewed procedure, or
knowingly abandoned with a reference to the decision record. The set must match
the server's current view; a stale or partial list conflicts. The disposition is
recorded verbatim on the change (`TerraformBackend.LegacyCutover`) and surfaces in
definition history. Retired legacy requests cannot restart or dispatch. This does
not recover state: it records who accepted the gap and why.

The project change, before/after history, verification digest and request-retirement
markers commit in one transaction. Old submitted definitions and results are not
rewritten. Retired requests remain readable but cannot restart or dispatch; submit
a fresh request against the new backend. Other environment overrides and the
project default remain unchanged. A repeated call with an old revision conflicts
rather than performing another cutover.

Deploy the additive request-retirement columns on both live and archive tables
before upgrading API and Monitor. After cutover, verify the new project revision
and backend, and check the old container is still empty and unused before deleting
only that explicitly identified container. Never delete its parent account as part
of a container cleanup.

## Installer configuration

`TERRAFORM_STATE_*` deployment properties and their MSI/Monitor configuration
mappings are no longer required or consumed. The installer does not configure
project state storage. Existing unused Monitor values are not a fallback.

`AzureStorageAccount:*` remains DOrc-wide **artifact storage**, not Terraform
state storage. Keep that container private: binary plans embed secrets even
though source snapshots omit tfvars files.

## Coordinated rollout and acceptance

Keep scheduling stopped while applying SQL and upgrading the API, both Monitor tiers and their runner payloads. New topology payloads contain CLR type identities that older runners cannot deserialize. Verify deployed versions and effective state/artifact settings before resuming scheduling; source configuration files alone are not evidence that MSI propagation succeeded.

The following are required **live acceptance checks**, not guarantees established by unit tests:

- Publish the DACPAC against a backed-up representative estate with data-loss protection enabled. Preflight the actual database collation against `SQL_Latin1_General_CP1_CI_AS`; do not bypass a collation/data-loss warning with a blanket unsafe publish option.
- Perform a clean MSI upgrade, inspect effective configuration on both tiers and verify deployed catalog/runner payloads and executable versions.
- Under the actual deployment identity, initialize state and create/read/lock a disposable state blob using data-plane RBAC, including a workload in a different subscription.
- Plan on one Monitor host and approve/apply on the other using the shared artifact account/container. Changed or missing artifacts must refuse execution.
- Cancel a long-running disposable operation and simulate HA lock loss. Verify runner, Terraform and provider processes stop, no further commands are issued, and request/result statuses remain terminal. Separately reconcile any operations already accepted by the cloud provider; process termination is not cloud rollback.
- Exercise a partial failure, interrupted apply and restore of the coordinated SQL/artifact/state recovery set. Confirm reconciliation is required rather than implicit replay.
- Verify secret masking with synthetic inherited, request-supplied and unrelated secure properties, including punctuation/newlines, without logging real credentials.

Pin the Terraform executable version and approved provider distribution/mirror operationally on every runner host. A provider lock file does not pin the executable on `PATH`. Record those versions with acceptance evidence. DOrc does not install or automatically upgrade the Terraform CLI.

Definition deletion only removes DOrc metadata/associations. Import, infrastructure destruction and general module-output composition remain outside this plan/apply release; use an approved operator runbook and then reconcile DOrc metadata. Never infer cloud rollback from an API deletion or failed request.
