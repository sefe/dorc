# Deployment definitions, revisions and execution evidence

`Component` is the deployment identity. PowerShell and Terraform SharedFolder
definitions can retain Script rows; Git, AzureArtifact, GitHubArtifact and
Catalog definitions do not acquire synthetic Script rows.

## Writing configuration

Read `GET RefData/{projectId}` and retain its opaque `Revision`. Send that token
with the complete `RefDataApiModel` on PUT. Structured editing and JSON editing
use the same mutation boundary. `CanEdit` and `CanDeploy` describe the current
principal's capabilities, not an authorization grant.

Project-source writers similarly round-trip `ProjectApiModel.Revision`.
Project deletion supplies `expectedRevision` as a query parameter. New-project
POST does not require an existing revision. Missing write preconditions return
428; changed definitions/consumers return 409 with reload guidance.

`RefDataScripts` listing returns `Revision`, `AffectedProjectIds` and `CanEdit`.
An explicit `/RefDataScripts/edit` PUT must return that revision and complete
impact list, set `ConfirmImpact`, satisfy the existing Admin/PowerUser role
gate, and have write access to every affected project. Its response remains
boolean. Project-local Script changes use copy-on-write.

Configuration, project-owner grants on creation, membership, and successful
change evidence commit on one EF context/SQL transaction. Catalog creation,
saved-input updates and upgrades append change evidence in their existing
transactions and retain their existing idempotency, pin, configuration-revision,
environment-impact and active-request gates. The change-set source-record key
links these operations to their existing component/input/upgrade records.

An omitted component detaches only from the edited project. Name collisions
are rejected, never resolved by renaming another component. Catalog pin changes
require the explicit upgrade action. Renaming Terraform components or their
project requires an operator-reviewed state procedure; editing definitions does
not migrate state or destroy infrastructure.

Known sensitive argument names, secure-property names and credential-bearing
URLs cannot be saved as literals. Replace a JSON-path argument value with
`${PropertyName}` to resolve it from the existing deployment properties at
execution. Missing referenced properties fail before invocation. Saved source
URLs must not embed userinfo or query credentials. History hashes argument
objects and sensitive fields; it does not store executable, silently redacted
replacements. Existing legacy audit payloads are not retroactively rewritten.

## Submitted intent versus execution

Every common request-persistence path, including Catalog idempotent submission,
captures ordered selected definitions, skip flags, effective Script identity,
path/JSON semantics/runtime, and project source settings in the request
transaction. Dispatch and retries read these snapshots, not current definitions.
Legacy requests with no snapshots are explicitly unsupported: drain/cancel or
resubmit them as new requests. No automatic historical backfill claims to know
what was originally submitted.

Environment properties and credentials retain their existing dynamic-resolution
policy. Pinning a Git branch name records requested intent, not immutable source
bytes. Terraform's existing approved plan/source archive and hashes remain
authoritative; its provenance now includes the definition revision and source
coordinates. Receipts expose requested coordinates separately from resolved
Git commits.

Both PowerShell runtimes read the script once and pass that exact string to
`AddScript`. The fingerprint is **SHA-256 of its UTF-8 representation**, not
raw-file-byte SHA-256: `File.ReadAllText` handles the original encoding/BOM,
and the returned text's newlines are unchanged. The hash does not cover imported
modules, dot-sourced dependencies, working-directory contents or network data.

The Monitor allocates a restricted, per-result evidence directory for the
deployment account. The properties pipe remains one-way and unchanged. Files
are identity-checked and bounded to 8 KiB each. Durable prepared metadata is
mandatory before invocation. `Prepared`, `InvocationObserved` and
`InvocationReturned` are distinct observations; return is not a success claim.
Corrupt/missing evidence becomes `Unknown`, never invented prepared/completed
evidence. Receipts retain request/result/revision/ordinal, source path/build,
requested/actual runtime and text hash, without script bodies or resolved values.

Ingestion failure leaves restricted files for operator recovery and fails
normal finalization; **do not automatically rerun a workload to repair its
evidence**. Investigate the result/process first. Files are removed only after
durable ingestion. This is provenance, not a script archive or replay facility.

## History and rollout

`GET DeploymentDefinitions` is a project-membership-aware, component-centric
list. `GET DeploymentDefinitions/history` accepts project/component filters,
with deterministic database paging and authorization before counts/paging.
Retained IDs are authorization keys; current names are not used to infer access
to deleted projects/environments.

History combines committed changes, labelled first observations, submissions,
PowerShell observations, Terraform receipts/approvals/outcomes, cancellation and
latest inventory observations. Legacy audits are explicitly labelled uncertain,
and correlated compatibility RefData audits are excluded from duplicate events.
Existing outcome/inventory records describe their latest recorded metadata;
they are not a fabricated complete historical recovery log. Historical result
labels use receipts, or explicitly labelled submitted intent when no execution
receipt exists—never today's mutable definition.

Apply the additive database package before coordinated API, Monitor, both runner
tiers and web upgrades. Four new tables have no cascading dependency on mutable
definitions/results. First-observation revisions are created idempotently when
definitions are first changed/submitted; they do not invent original authors.
Preserve evidence/archive ACLs and coordinate external writers for the new
preconditions. Existing first-party request CLI paths use the common submission
boundary; no separate CLI snapshot format is required.

Local automated validation covers SQL transactions, migration and repeated
publish, concurrency/impact, all Terraform sources, pinned submissions, paging,
and the shared exact-input contract plus both runtime builds. It does not
constitute live deployment acceptance under production identities. Coordinated
live upgrade, queued-edit, plan/apply and both PowerShell engine scenarios remain
operator acceptance tasks; no live deployment was performed.
