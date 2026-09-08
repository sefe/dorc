# Terraform state model

This document is the canonical reference for how Terraform state is managed by DOrc. State is **platform-owned**: DOrc renders the backend configuration at deploy time, manages the state container's lifecycle, and never relies on user-supplied `backend` blocks.

> **Status of this document.** Sections marked **[planned]** describe behaviour gated behind the `Terraform:UseConsolidatedLifecycle` feature flag.

## State container

State lives in an Azure Blob Storage container nominated by DOrc configuration. Required posture:

- **Access level**: `private` (no anonymous access).
- **Network access**: restricted to the DOrc deployment subnet (or the DOrc service identity's allowed networks).
- **Authentication**: the runner's Azure identity (managed identity preferred) is granted the `Storage Blob Data Contributor` role on the container only.
- **SAS tokens** (if used) must default to ≤ 1 hour lifetime. Long-lived SAS tokens must not be committed to source.
- **Versioning + soft delete**: enabled on the container. State is never hard-deleted by DOrc.

The container name and storage account are operator-provisioned, **not** auto-created by DOrc.

## State key

Every plan/apply targets a deterministic state key:

```
{project}/{component}/{environment}.tfstate
```

The triple is read from the DOrc deployment request. The state key never embeds user-supplied free-form values.

## Backend rendering [planned]

Before `terraform init`, DOrc writes a `_dorc_backend.tf` file into the working directory containing only the `terraform { backend "azurerm" { ... } }` block, configured with the resolved storage account, container, and state key.

User-checked-in `backend` blocks anywhere in the source are **rejected at pre-flight** with a precise validation error citing the offending file. The pre-flight rejection is unambiguous: terraform's `_override.tf` mechanism is not reliable for backend blocks.

## Working directory & execution bundle [planned]

Plan and apply for the same logical operation must run against the same provider lock file, module cache, and binary plan. DOrc preserves identity by:

1. **Plan phase**: after `init` and `plan -out=plan.bin`, DOrc tarballs the entire working directory (including `.terraform/`, `.terraform.lock.hcl`, and `plan.bin`) into a single blob keyed by `bundles/{planOperationId}.tar.gz`. SHA-256 is recorded on the plan record.
2. **Apply phase**: DOrc downloads the bundle by `planOperationId`, recomputes SHA-256, fails fast on mismatch, extracts into a fresh local directory, and re-invokes terraform with the binary plan.

The state blob (the `.tfstate` itself) is **never** part of the bundle. It lives forever.

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
