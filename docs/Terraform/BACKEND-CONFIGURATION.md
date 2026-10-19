# Terraform backend configuration

Configure state storage on **both Monitor tiers**, separately from the plan/artifact store:

```json
{
  "Terraform": {
    "State": {
      "StorageAccount": "stdorcstate",
      "ContainerName": "tfstate",
      "SubscriptionId": "11111111-1111-1111-1111-111111111111",
      "ResourceGroup": "rg-dorc-state"
    }
  }
}
```

The account and private container must already exist. `StorageAccount`, `ContainerName`, and a valid Azure subscription GUID are required for catalog deployments; `ResourceGroup` is optional. Monitor derives the state key from project/component/environment and sends all backend settings to the runner through `ScriptGroup`.

The backend explicitly sets `subscription_id` and `use_azuread_auth = true`. Grant **Storage Blob Data Contributor** on the container to the identity Terraform actually uses: the environment service principal when configured, otherwise the runner's ambient identity. It does not need permission to list storage account keys. Authentication errors are fatal; platform backends do not fall back to account keys, SAS tokens, or local state. These global settings apply only to catalog components. Legacy Git, shared-folder and artifact sources retain their own intentional backend/authentication modes even when global settings are populated; configuring the platform does not silently migrate their state.

Workload targeting remains independent: an environment subscription resource, `TerraformSubscriptionId`, or the runner's `Terraform:DefaultSubscriptionId` configures the workload provider, not the backend subscription. Cross-subscription deployments must configure the state subscription above even when all workloads use another subscription.

## Installer wiring

`install-scripts\DeploySettings.template.json` exposes these deployment properties, mapped through `Setup.Dorc.msi.json` to MSI properties and written to the Prod and NonProd Monitor configuration:

| Deployment property | MSI property | Monitor setting |
| --- | --- | --- |
| `TERRAFORM_STATE_STORAGEACCOUNT` | `TERRAFORM.STATE.STORAGEACCOUNT` | `Terraform:State:StorageAccount` |
| `TERRAFORM_STATE_CONTAINERNAME` | `TERRAFORM.STATE.CONTAINERNAME` | `Terraform:State:ContainerName` |
| `TERRAFORM_STATE_SUBSCRIPTIONID` | `TERRAFORM.STATE.SUBSCRIPTIONID` | `Terraform:State:SubscriptionId` |
| `TERRAFORM_STATE_RESOURCEGROUP` | `TERRAFORM.STATE.RESOURCEGROUP` | `Terraform:State:ResourceGroup` |

These are not the `AzureStorageAccount:*` artifact-storage credentials. Keep the artifact container private too: binary plans embed secrets even though the companion source snapshot omits all tfvars files.

On upgrade, configure the backend subscription explicitly and recreate pending plans without the current snapshot/approval contract. Never apply an older plan by checking out its mutable source ref again.

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
