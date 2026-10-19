# Terraform state model

This document is the canonical reference for how Terraform state is managed by DOrc. Catalog state is **platform-owned**: DOrc renders the backend configuration at deploy time and requires an operator-provisioned durable backend. Legacy non-catalog sources may retain their own backend when no platform state configuration is supplied.

> **Status of this document.** Sections marked **[planned]** describe behaviour gated behind the `Terraform:UseConsolidatedLifecycle` feature flag.

## State container

State lives in an Azure Blob Storage container nominated by DOrc configuration. Required posture:

- **Access level**: `private` (no anonymous access).
- **Network access**: restricted to the DOrc deployment subnet (or the DOrc service identity's allowed networks).
- **Authentication**: the runner's Azure identity (managed identity preferred) is granted the `Storage Blob Data Contributor` role on the container only.
- The generated backend explicitly uses **Azure AD data-plane authentication** (`use_azuread_auth = true`), not storage-key discovery. The deployment identity (including any environment-specific service principal) needs that role. Inherited `ARM_ACCESS_KEY` and `ARM_SAS_TOKEN` are removed for platform-managed operations; there is no key/SAS fallback.
- **SAS tokens** (legacy self-managed backends only) must default to ≤ 1 hour lifetime. Long-lived SAS tokens must not be committed to source.
- **Versioning + soft delete**: enabled on the container. State is never hard-deleted by DOrc.

The container name and storage account are operator-provisioned, **not** auto-created by DOrc.

## State key

Every plan/apply targets a deterministic state key:

```
{project}/{component}/{environment}.tfstate
```

The triple is read from the DOrc deployment request. The state key never embeds user-supplied free-form values.

## Backend rendering

Before `terraform init`, DOrc writes a `_dorc_backend.tf` file into the working directory containing only the `terraform { backend "azurerm" { ... } }` block, configured with the resolved storage account, container, state key, and **backend subscription ID**. Configure `Terraform:State:StorageAccount`, `ContainerName`, and `SubscriptionId` in Monitor settings; `ResourceGroup` is optional. See [backend configuration](BACKEND-CONFIGURATION.md) for installer propagation.

Catalog deployments fail before source retrieval or any Terraform command if durable state is absent or incomplete. A partially configured platform backend is an error for every source type; it never silently falls back to local state. Legacy sources with no platform settings retain their existing backend behavior.

The backend's explicit subscription is independent of the workload provider's environment subscription / `ARM_SUBSCRIPTION_ID`. Setting `Terraform:DefaultSubscriptionId` or attaching an environment subscription resource does not configure backend storage discovery.

User-checked-in `backend` blocks anywhere in the source are **rejected at pre-flight** with a precise validation error citing the offending file. The pre-flight rejection is unambiguous: terraform's `_override.tf` mechanism is not reliable for backend blocks.

## Working directory & execution bundle [planned]

Plan and apply for the same logical operation must run against the same provider lock file, module cache, and binary plan. DOrc preserves identity by:

1. **Plan phase**: after `init` and `plan -out=plan.bin`, DOrc tarballs the entire working directory (including `.terraform/`, `.terraform.lock.hcl`, and `plan.bin`) into a single blob keyed by `bundles/{planOperationId}.tar.gz`. SHA-256 is recorded on the plan record.
2. **Apply phase**: DOrc downloads the bundle by `planOperationId`, recomputes SHA-256, fails fast on mismatch, extracts into a fresh local directory, and re-invokes terraform with the binary plan.

The state blob (the `.tfstate` itself) is **never** part of the bundle. It lives forever.

## Deployed-source archive

At plan time, the runner archives the exact terraform configuration that produced the plan. Monitor persists this required snapshot beside the binary plan using the existing Azure blob artifact store (`{planBlobName}.source.zip`) **before** marking the plan ready for confirmation. Apply restores this snapshot instead of fetching the source again, so a moved Git ref, changed catalog entry, or unavailable original source cannot change the approved input. After successful apply, the same archive bytes are uploaded as `{deploymentResultId}.terraform-source.zip`; provenance is never reconstructed from the apply request. The archive contains:

- the working directory's terraform configuration (`*.tf`, `.terraform.lock.hcl`, local modules and downloaded `.terraform/modules/`), **excluding** `.git/`, the rest of `.terraform/` (provider binaries — the lock file pins their versions), and any `*.tfstate*` files;
- `terraform.tfvars` with flagged-sensitive values redacted to `[REDACTED]` before archiving;
- `_dorc_provenance.json`: the module `name@version`, source type, resolved git repo/ref, the **exact commit SHA** checked out, sub-path, state-backend coordinates, request/result IDs, and timestamp.

Creating and storing the plan-time archive is mandatory. A missing/corrupt snapshot fails apply before Terraform is invoked; plans created before this contract must be recreated and approved again. Upload of the successful-apply audit copy remains best-effort, with errors logged. All local staging files are deleted after dispatch on success, failure, and cancellation. The snapshot is not the full provider-cache execution bundle described in the planned section above.

## Concurrency [planned]

Plan and apply on the same `(project, component, environment)` triple are serialised in two layers:

1. **In-process** `SemaphoreSlim` (single-monitor case).
2. **Cross-monitor**: an explicit Azure Blob lease (15-second auto-renewing) on the state blob, acquired by DOrc before invoking terraform.

If lease acquisition fails (held), DOrc surfaces a clean `409 Conflict` with the contending operation's identifier. This is preferable to relying on terraform's internal lock-wait timeout, which produces opaque output.

## Secrets in state

Terraform writes provider-returned values - including credentials - into `.tfstate` in plaintext. Mitigations:

- **Container ACL** (above) prevents non-DOrc principals reading state.
- **Module discipline**: per `MODULE-CONTRACT.md`, stock modules must not declare `sensitive = false` on outputs that carry provider-returned secrets. Use `sensitive = true` and avoid promoting credentials to outputs unless required.
- **Operator awareness**: state files may contain plaintext secrets; treat the state container with the same access controls as a credential vault.

## Force unlock runbook [planned]

If DOrc itself crashes mid-apply leaving a stuck blob lease, an admin may break the lease via the Azure CLI:

```
az storage blob lease break \
    --account-name <state-account> \
    --container-name <state-container> \
    --blob-name <project>/<component>/<environment>.tfstate
```

This action is auditable (Azure Activity Log) and should follow an incident response process - never as a routine operation.

## Out of scope

- Terraform Cloud / HCP backends: DOrc currently targets Azure Blob only. A pluggable backend renderer is a future option but is not on the current roadmap.
- Cross-region state replication: relies on Azure Storage GRS/RAGRS configuration of the storage account, which is operator-controlled.
