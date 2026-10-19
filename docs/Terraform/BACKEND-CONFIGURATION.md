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

The backend explicitly sets `subscription_id` and `use_azuread_auth = true`. Grant **Storage Blob Data Contributor** on the container to the identity Terraform actually uses: the environment service principal when configured, otherwise the runner's ambient identity. It does not need permission to list storage account keys. Authentication errors are fatal; platform backends do not fall back to account keys, SAS tokens, or local state. Legacy sources with no platform backend configuration retain their own intentional backend/authentication modes.

Workload targeting remains independent: an environment subscription resource, `TerraformSubscriptionId`, or the runner's `Terraform:DefaultSubscriptionId` configures the workload provider, not the backend subscription. Cross-subscription deployments must configure the state subscription above even when all workloads use another subscription.

## Installer wiring

`install-scripts\DeploySettings.template.json` exposes these deployment properties, mapped through `Setup.Dorc.msi.json` to MSI properties and written to the Prod and NonProd Monitor configuration:

| Deployment property | MSI property | Monitor setting |
| --- | --- | --- |
| `TERRAFORM_STATE_STORAGEACCOUNT` | `TERRAFORM.STATE.STORAGEACCOUNT` | `Terraform:State:StorageAccount` |
| `TERRAFORM_STATE_CONTAINERNAME` | `TERRAFORM.STATE.CONTAINERNAME` | `Terraform:State:ContainerName` |
| `TERRAFORM_STATE_SUBSCRIPTIONID` | `TERRAFORM.STATE.SUBSCRIPTIONID` | `Terraform:State:SubscriptionId` |
| `TERRAFORM_STATE_RESOURCEGROUP` | `TERRAFORM.STATE.RESOURCEGROUP` | `Terraform:State:ResourceGroup` |

These are not the `AzureStorageAccount:*` artifact-storage credentials. Keep the artifact container private too: binary plans embed secrets even though the companion source snapshot redacts sensitive tfvars.

On upgrade, configure the backend subscription explicitly and recreate any pending plans that lack the plan-time source snapshot. Never apply an older plan by checking out its mutable source ref again.
