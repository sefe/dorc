# Terraform state model

This document is the canonical reference for how Terraform state is managed by DOrc. Catalog state storage is **project/environment-owned**: DOrc renders a backend from the configuration pinned at request submission and requires an operator-provisioned durable store. Legacy non-catalog sources retain their own backend; project settings do not opt them into a state migration.

> **Status of this document.** Sections marked **[planned]** are unimplemented design proposals, not switchable features. There is no `Terraform:UseConsolidatedLifecycle` setting in the application.

## State container

State lives in an Azure Blob Storage container selected in Project Ref Data, using an environment override or project default. Required posture:

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

The triple uses sanitized project, component and environment names from the DOrc deployment request, not immutable database IDs. Keep these names stable for the lifetime of the managed infrastructure.

### Renames and state migration

Renaming a project, component or environment is **not** an infrastructure/state migration. Do not deploy under the new name until an operator has moved the existing state to the exact new key. Never accept a replacement plan that proposes recreating the estate as a substitute for migration.

1. Stop submissions and finish or cancel outstanding plans for the old identity on both Monitor tiers.
2. Back up/version the old state, record its lineage and serial, and verify that no Terraform process or state lock remains active.
3. Using the approved Terraform CLI and deployment identity, initialize against the old backend, then explicitly migrate to the new key with `terraform init -migrate-state`. Do not use `-force-copy` or disable locking to bypass an unexpected conflict.
4. Confirm that the migrated state contains the same resource addresses and remote IDs. Retain the old backup according to retention policy.
5. Submit and review a **new** DOrc plan under the renamed identity. Resolve unexpected creates, replacements or destroys before approval.

Introducing stable-ID keys later requires a separate, explicit migration; this release does not silently change existing keys.

## Backend rendering

Before `terraform init`, DOrc writes a `_dorc_backend.tf` file into the working directory containing only the `terraform { backend "azurerm" { ... } }` block, configured with the submitted storage account, container, state key, and **backend subscription ID**. Configure `Project.TerraformStateBackends` through Project Ref Data; `ResourceGroup` is optional. See [backend configuration](BACKEND-CONFIGURATION.md) for scope selection, immutable request binding and cutover from Monitor-wide settings.

Catalog submissions fail if their project/environment has no complete backend. Monitor uses the captured backend, not live project settings, request properties or global `Terraform:State` settings. Existing requests lacking that snapshot must be resubmitted and replanned. Explicit backend coordinates remain supported on the runner contract for non-Catalog callers; partial configuration never falls back to local state. Do not change a legacy component to Catalog mode as a substitute for migrating/importing its existing state.

The backend's explicit subscription is independent of the workload provider's environment subscription / `ARM_SUBSCRIPTION_ID`. Setting `Terraform:DefaultSubscriptionId` or attaching an environment subscription resource does not configure backend storage discovery.

When a platform backend is selected, user-checked-in `backend` blocks anywhere in the source are **rejected at pre-flight** with a precise validation error citing the offending file. Legacy self-managed backends are preserved. Terraform's `_override.tf` mechanism is not reliable for backend blocks.

## Approval and recovery

Confirmation is a transaction over the parent request, the selected result and an append-only `deploy.TerraformPlanApproval` audit record. Only a request awaiting approval (or already confirmed but not yet executing) can be confirmed. The API rejects cancellation, terminal states and concurrent changes rather than resurrecting a request. Declining a plan cancels its pending, confirmed and waiting siblings atomically.

The plan response supplies `PlanContentSha256`. A caller must return it as the `planContentSha256` query parameter when confirming, so a plan changed since display requires another review. The approval records the approver, UTC timestamp, request/result identities and SHA-256 digests of the human-readable plan, binary plan and source archive. Apply checks the downloaded binary plan and source against that approval **before** invoking Terraform. Missing approval/artifacts or changed bytes require a fresh plan and approval. The audit has no foreign key to mutable result rows, intentionally retaining approvals when a retry removes those rows.

Only Pending components and explicitly Confirmed Terraform components are dispatched. Waiting or completed siblings are not replayed on another component's approval. Interrupted Running results are failed for operator reconciliation, not treated as permission to rerun an uncertain apply.

There is currently **no automatic approval expiry**. This preserves the existing scheduling contract, not a freshness guarantee. Cancel/decline and submit a new request after changes to inputs, credentials, targeting, ownership or infrastructure, or after a substantial scheduling delay. Terraform's saved-plan state checks do not replace policy review. A configurable TTL is a separate product-policy change.

## Working directory & execution bundle [planned]

Plan and apply for the same logical operation must run against the same provider lock file, module cache, and binary plan. DOrc preserves identity by:

1. **Plan phase**: after `init` and `plan -out=plan.bin`, DOrc tarballs the entire working directory (including `.terraform/`, `.terraform.lock.hcl`, and `plan.bin`) into a single blob keyed by `bundles/{planOperationId}.tar.gz`. SHA-256 is recorded on the plan record.
2. **Apply phase**: DOrc downloads the bundle by `planOperationId`, recomputes SHA-256, fails fast on mismatch, extracts into a fresh local directory, and re-invokes terraform with the binary plan.

The state blob (the `.tfstate` itself) is **never** part of the proposed bundle. State retention is operator-controlled.

## Deployed-source archive

At plan time, the runner archives the exact terraform configuration that produced the plan. Monitor persists this required snapshot beside the binary plan using the existing Azure blob artifact store (`{planBlobName}.source.zip`) **before** marking the plan ready for confirmation. Apply restores this snapshot instead of fetching the source again, so a moved Git ref, changed catalog entry, or unavailable original source cannot change the approved input. After successful apply, the same archive bytes are uploaded as `{deploymentResultId}.terraform-source.zip`; provenance is never reconstructed from the apply request. The archive contains:

- the working directory's terraform configuration (`*.tf`, `.terraform.lock.hcl`, local modules and downloaded `.terraform/modules/`), **excluding** `.git/`, the rest of `.terraform/` (provider binaries — the lock file pins their versions), and any `*.tfstate*` files;
- **no variable files**: generated `terraform.tfvars`, source-provided `.tfvars` / `.tfvars.json`, auto-variable files, and backup/case variants containing `.tfvars` in their filename are omitted entirely. Saved-plan apply uses the values embedded in the approved binary plan; the original variable files are not needed for init or apply;
- `_dorc_provenance.json`: the module `name@version`, source type, resolved git repo/ref, the **exact commit SHA** checked out, sub-path, state-backend coordinates, request/result IDs, and timestamp.

Creating and storing the plan-time archive is mandatory. A missing/corrupt snapshot fails apply before Terraform is invoked; plans created before this contract must be recreated and approved again. Upload of the successful-apply audit copy remains best-effort, with errors logged. Local staging is cleaned after dispatch on success, failure and cancellation; a hard machine/process kill can bypass normal cleanup and requires operator inspection. The snapshot is not the full provider-cache execution bundle described in the planned section above.

The archive is capped **during creation**, before the plan is offered for approval, using the same restoration limits: 10,000 entries (including provenance), 50 MiB per entry, and 500 MiB total uncompressed. A failure identifies the offending relative filename; reduce unnecessary source/module content and create a new plan. Limits also count provenance and actual copied bytes, not just file metadata.

The source snapshot is not a secret scanner. Do not commit credentials into `.tf`, scripts, lock files or arbitrary other source files. Runtime error and human-readable plan redaction use manifest-sensitive parameters (including inherited/default values), request sensitivity flags, secure environment/config metadata, and known provider credentials. The runner sanitizes the display plan before saving/uploading it; approval hashes that exact displayed representation as well as the unchanged binary plan and source. Redaction does not modify the actual values passed to Terraform.

Request intake derives sensitivity from selected Catalog manifests and secure property metadata for every build path, not only wizard/artifact-free requests. Omitted or false client flags cannot downgrade those classifications. Metadata/catalog lookup failures refuse submission instead of persisting unclassified overrides. These changes do not rewrite historical request XML or plan artifacts: withdraw/replan pending old plans during the coordinated rollout and assess existing artifacts through the retention/access-review process below.

Cancellation and Monitor lock loss signal the runner through a marker in restricted plan storage. The runner passes cancellation through Terraform commands, reaps Terraform/provider process trees and deletes the working directory. Monitor allows up to five seconds for graceful cleanup, then terminates the Windows job containing the runner and all descendants, with a further five-second exit deadline. The job is assigned before the runner starts and is killed on Monitor handle closure/crash. Forced termination cannot guarantee application-level cleanup; treat any leftover restricted working directory as secret-bearing. Stopping local processes cannot undo or necessarily cancel operations already accepted by a cloud provider: reconcile provider operation status and remote state before retrying. Cancellation must not be recorded as a successful deployment.

### Existing artifact exposure

Earlier archives could retain unrelated secure environment properties and inherited sensitive inputs in tfvars. This fix prevents new archives from retaining those resolved values; it does not erase already uploaded artifacts or prove that any were accessed. Before rollout, identify the affected retention window, inspect access logs and permissions, quarantine/remove obsolete archives through the approved retention process, and rotate credentials if exposure is established or cannot be ruled out. Do not publish archive contents into tickets or logs while investigating.

## Recovery and retention

Treat SQL approval/request history, plan/source artifacts and Terraform state as one recovery set. Preserve state lineage/serial, state and artifact blob versions, approval digests and deployment identity access configuration. Retention rules must not expire artifacts for still-actionable approvals; expired/missing artifacts require replan, never a source-ref fallback.

After restoring state or SQL, keep scheduling stopped until an operator reconciles actual cloud resources, restored state and request status. Do not automatically replay historical Confirmed/Running requests. Produce and review a new plan after reconciliation. Partial apply is not transactional cloud rollback: resources can exist even when the request failed and registration did not finish.

Rolling back application binaries does not revert database identities, migrated state or infrastructure. Back up SQL and state before upgrade; retain the previous binaries and a reviewed rollback procedure. Do not deploy an old runner against new CLR-typed topology payloads or assume the old API/Monitor understands the approval contract.

## Concurrency [planned]

Plan and apply on the same `(project, component, environment)` triple are serialised in two layers:

1. **In-process** `SemaphoreSlim` (single-monitor case).
2. **Cross-monitor**: an explicit Azure Blob lease (15-second auto-renewing) on the state blob, acquired by DOrc before invoking terraform.

If lease acquisition fails (held), DOrc surfaces a clean `409 Conflict` with the contending operation's identifier. This is preferable to relying on terraform's internal lock-wait timeout, which produces opaque output.

## Secrets in state

Terraform writes provider-returned values - including credentials - into `.tfstate` in plaintext. **Binary saved plans also embed input values and sensitive planned state**; omitting tfvars from source archives does not sanitize plans or state. The display-plan backstop masks known DOrc-sensitive input values, including their JSON/HCL-escaped forms, but cannot infer unknown provider-generated secrets or every transformed value. Terraform module sensitivity declarations remain necessary. Mitigations:

- **Container ACL** (above) prevents non-DOrc principals reading state.
- **Module discipline**: per `MODULE-CONTRACT.md`, stock modules must not declare `sensitive = false` on outputs that carry provider-returned secrets. Use `sensitive = true` and avoid promoting credentials to outputs unless required.
- **Operator awareness**: state files may contain plaintext secrets; treat the state container with the same access controls as a credential vault.
- **Artifact ACLs**: apply the same vault-grade access/retention controls to binary plans, plan renderings and their artifact container. Approval hashes provide integrity, not encryption or secrecy.

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
