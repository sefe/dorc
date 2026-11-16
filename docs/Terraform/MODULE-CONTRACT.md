# Module contract

Every module published under `stock-modules/` in [`sefe/dorc-terraform-modules`](https://dev.azure.com/sefe/Deployment%20Orchestrator/_git/dorc-terraform-modules) must satisfy this contract. This is the public agreement between module authors and DOrc engineers consuming the library.

## Required files

```
stock-modules/<name>/
  versions.tf       # required_version + required_providers, both pinned
  variables.tf      # validation block on every user-facing input
  main.tf           # resources only; no providers/backend; no hardcoded secrets
  outputs.tf        # at minimum: the resource ID of the primary resource
  README.md         # purpose, inputs, outputs, secret handling, example
  examples/basic/   # runnable minimal usage
    main.tf
```

## Required README sections

The README must include a leading metadata table:

```
| Field | Value |
| Owner | <team or person; see MODULES.md> |
| Status | Active | Deprecated |
| Category | Networking | Data | Storage | Identity | Observability |
| Provider | hashicorp/<provider> ~> <X.Y> |
| Terraform | >= <X.Y.Z> |
```

Plus sections: **Inputs**, **Outputs**, **Secret handling**, **Example**, **Versioning**.

## Validation rules

- `versions.tf` must pin `required_version` and every entry in `required_providers` to a `~>` (pessimistic) constraint, not `>=`.
- Every variable in `variables.tf` that participates in the user-facing interface must have a `validation` block expressing its constraint (length, regex, allow-list, range).
- No default values for secret-like inputs (anything matching the secret pattern `(?i)(token|pat|secret|password|key|connectionstring)`). Such inputs are required and supplied by DOrc properties at deploy time.
- `main.tf` must not declare `terraform { backend ... }` blocks (DOrc renders the backend; see `STATE-MODEL.md`).
- `main.tf` must not declare `provider` blocks unless aliasing is required; the consuming root module supplies the provider.

## Secret handling rules

- Sensitive variables must be marked `sensitive = true`.
- Outputs that carry provider-returned secrets must be marked `sensitive = true`.
- **`sensitive = false` is forbidden on outputs whose names match the secret pattern.** This is enforced by the module CI workflow (see `S-013` in the IS).
- Modules should avoid emitting credentials at all where the consumer can derive them via a `data` block instead.

## Outputs

Every module must output the **resource ID of the primary resource** so that downstream modules can compose. Examples:

- `vnet`: `vnet_id` + per-subnet IDs.
- `cosmosdb`: `cosmosdb_account_id` + `database_id`.
- `service-bus`: `namespace_id` + `queue_id`.

Echoing the input name (e.g. `vnet_name`) is also encouraged for human-readable composition.

## Versioning

Modules use **stable semantic versions** with Git tags:

```
stock-modules/<name>/v<major>.<minor>.<patch>
```

- **Patch**: bug fixes, no input/output schema change.
- **Minor**: additive new optional inputs or new outputs; existing usage unaffected.
- **Major**: breaking change to inputs or outputs.

A breaking change requires a new major tag *and* a deprecation notice on the previous major (see below).

Catalog versions currently support `major.minor.patch` (an optional leading `v`
is accepted for compatibility), with no leading zeroes. Prerelease and build
labels are rejected rather than sorted as arbitrary strings. Latest-version
selection compares all three numeric components, including large components.

Each release manifest must supply `source.kind: git`, its HTTPS `locator`,
the intended release tag in `ref`, and a full 40-hex-character `commit_sha`.
The runner resolves that tag explicitly (a branch with the same name cannot
shadow it), peels annotated tags to their commit, and verifies the commit
against `commit_sha` before using the code. Moving a published tag causes a
deployment failure, not an unnoticed code upgrade. Publish a new tag and
manifest version for any source change; never retag a published release.

Scalar inputs remain strings at the DOrc property boundary. Manifest `Bool`
inputs accept only lowercase `true`/`false`; `Number` inputs accept invariant
decimal/exponent syntax within .NET decimal's exact range/precision, not
grouping separators (`1,000`), whitespace, locale-specific numbers or values
that silently round/underflow. The runner emits these as HCL scalars, not quoted
strings, and escapes real `String` inputs as HCL strings. A `Number` parameter
may declare `integer: true` for integral values (Kafka partitions, replication
and minimum in-sync replicas). Cross-input exclusions are declared as
`parameter_conflicts`: `parameter`, `value`, `when_parameter`, `when_value`,
and a clear `message`. Validation uses supplied values or declared defaults;
it rejects a matching pair before plan. The PostgreSQL v2 release excludes
`database_name=defaultdb` when `create_service=true`, including its default.

## Deprecation policy

A module version is deprecated when its successor has been published and is the recommended path forward. Deprecation requires:

1. The module's `README.md` carries a banner pointing to the successor.
2. The catalog manifest sets `deprecated: true` with a `deprecation_reason` string.
3. Retain the published manifest, Git tag and commit indefinitely. Deprecation
   hides a release from normal discovery; it must not prevent pinned
   components from resolving and deploying it. Give consumers at least 90 days
   to plan migration, not a deletion deadline. Deprecation is one-way; a
   corrected/recommended successor is a new release, not `true -> false`.

## Ownership

Each module's README declares an `Owner:` field in the metadata table. Initial owner of every stock module is **DOrc platform team** until reassigned. Aggregated ownership lives in `docs/Terraform/MODULES.md`.

## Module CI
The upstream [`sefe/dorc-terraform-modules`](https://dev.azure.com/sefe/Deployment%20Orchestrator/_git/dorc-terraform-modules)
CI runs against modules under `stock-modules/`:

- `terraform fmt -check`
- `terraform init -backend=false`
- `terraform validate`
- `tflint` with plugin versions pinned in `.tflint.hcl`
- A custom check enforcing the secret-output rule above

Failure blocks merge.

> Provider-version lock-file (`.terraform.lock.hcl`) verification and a
> committed plugin mirror are not currently enforced by upstream module CI
> or this repository's `terraform-modules-ci.yml`. A source commit pin is not
> a provider binary pin. Provider constraints may select newer compatible
> binaries on a fresh init; operators must retain/review the root lock file
> and use the same CLI/provider selections for plan and apply. The CI
> Terraform version is not a guarantee about every installed runner's CLI.

## Manifest immutability
`stock-modules-manifests/<name>-<version>.yaml` releases (published from the
`dorc-terraform-modules` repository, alongside module source) are **immutable per
`(name, version)`**, not merely per filename. The PR gate compares the complete
base (merge-base) and head catalogs. It rejects duplicate identities
(case-insensitively), deletion of any published identity, and any change to its
parsed YAML payload, including identity, source/tag/SHA, inputs, defaults and
outputs. A delete/re-add, earlier-sorting filename or rename cannot replace a
release. The runtime loader also fails closed on duplicate identities rather
than choosing the first file.

Allowed evolution:

- Add a genuinely new version and identity.
- Rename a manifest while preserving its identity and parsed payload.
- Set `deprecated: false -> true` with a nonempty `deprecation_reason`, or
  clarify that reason on an already deprecated release. These are the **only**
  mutable lifecycle fields. Reactivation (`true -> false`) is forbidden.
- Reformat YAML/comments without changing its parsed meaning.

Do not remove deprecated manifests. A pinned deployment still resolves through
the catalog, even when the source tag remains available upstream.

The gate is the `manifest-immutability <base-sha> <head-sha>` command of
`src/Tools.TerraformLint` (.NET, no extra toolchain), run by the modules
repository's Azure Pipeline from its own root. It reads manifests from
the Git trees rather than the working copy, treats `v2.1.0` and `2.1.0` as the
same identity, and fails closed on Git/YAML errors and duplicate YAML keys.
`--self-test` runs identity, retention, rename/re-add, payload and deprecation
transition regression tests, not just diff-status classification. It runs on
pull requests against the merge base and on pushes to `main` against the
previous commit.

## Managed operation boundary

The managed catalog path is **plan, review, then apply**. Outputs are descriptive
provenance/reference data; a `database_id` or `service_name` does not import a
resource into Terraform state. There is no managed import, destroy, state
handoff or resource-adoption endpoint in this feature, nor a dedicated module
lifecycle CLI. Do not infer ownership from DOrc reference-data registration.
Use the documented API/UI for supported operations; exceptional recovery or
ownership migration requires an operator-controlled Terraform procedure.

Saved plans are reviewed before apply. Approval does not automatically expire;
this preserves existing behavior, not a freshness guarantee. Operators should
decline and replan after material input, provider, infrastructure or state
changes. Existing state keys are retained for compatibility: renaming a
component/environment does not automatically migrate its state. Plan any such
rename with an explicit state/backend migration and verify the destination
before deployment; never silently start using an empty state.

## Sensitive-name lint
A non-blocking CI lint warns when a manifest parameter's `name` matches a secret-shape substring (case-insensitive) but does not declare `sensitive: true`. Default patterns: `password`, `secret`, `key`, `token`, `connection_string`. The pattern list is configurable via `stock-modules-manifests/.sensitive-name-patterns` (one substring per line; `#` comments and blank lines are stripped).

The lint emits `::warning::` annotations on the PR review surface but never fails the build — it surfaces producer omissions for human review. Implementation: `src/Tools.TerraformLint/` (`Tools.TerraformLint manifest-sensitive-names <dir>`); the same tool's `secret-outputs <dir>` subcommand enforces the secret-output rule above as a build-failing check, and `--self-test` exercises both modes against synthetic fixtures.

## Catalog deployment API behavior

- `POST /Request` accepts an artifact-free request selecting only fully bound Catalog components without `BuildText`, `BuildNum`, or `BuildUrl`. The API verifies project membership, environment authorization, and Catalog bindings before substituting its internal routing marker; clients must not send that marker themselves. Mixed and artifact-backed requests retain build validation.
- Template input resolution and instantiate-and-deploy validation seed ConfigValues using the Monitor's production/non-production selection and precedence. Environment scope values and explicit request overrides take precedence over those seeds, including when an inherited input interpolates another property (for example, `account_name=$AppCode$-cosmos`). Inherited values remain server-side; only explicit overrides are saved on the request.
- Instantiate-and-deploy retries reuse an identical existing Catalog component only when it is enabled. A disabled component returns HTTP 400 with instructions to enable it before retrying; the endpoint neither enables it automatically nor submits a deployment request.
