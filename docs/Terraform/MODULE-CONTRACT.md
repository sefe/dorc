# Module contract

Every module published under `stock-modules/` must satisfy this contract. This is the public agreement between module authors and DOrc engineers consuming the library.

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
- `sql-database`: `sql_server_id` + `database_id`.
- `storage-account`: `storage_account_id`.

Echoing the input name (e.g. `vnet_name`) is also encouraged for human-readable composition.

## Versioning

Modules use **semantic versioning** with Git tags:

```
stock-modules/<name>/v<major>.<minor>.<patch>
```

- **Patch**: bug fixes, no input/output schema change.
- **Minor**: additive new optional inputs or new outputs; existing usage unaffected.
- **Major**: breaking change to inputs or outputs.

A breaking change requires a new major tag *and* a deprecation notice on the previous major (see below).

## Deprecation policy

A module version is deprecated when its successor has been published and is the recommended path forward. Deprecation requires:

1. The module's `README.md` carries a banner pointing to the successor.
2. The catalog manifest sets `deprecated: true` with a `deprecation_reason` string.
3. The deprecation announcement gives consumers **at least 90 days** before the deprecated version is removed from the catalog. Removal does not delete the Git tag; existing pinned references continue to resolve via Git.

## Ownership

Each module's README declares an `Owner:` field in the metadata table. Initial owner of every stock module is **DOrc platform team** until reassigned. Aggregated ownership lives in `docs/Terraform/MODULES.md`.

## Module CI
The repository CI runs against every module under `stock-modules/`:

- `terraform fmt -check`
- `terraform init -backend=false`
- `terraform validate`
- `tflint` with plugin versions pinned in `.tflint.hcl`
- A custom check enforcing the secret-output rule above

Failure blocks merge.

> Note: provider-version lock-file (`.terraform.lock.hcl`) verification and a
> committed plugin mirror are planned but not yet enforced by
> `terraform-modules-ci.yml`.

## Manifest immutability
`stock-modules-manifests/<name>-<version>.yaml` files are **immutable per `(name, version)`**. The CI immutability gate rejects any pull request that modifies an existing manifest version file. Allowed operations:

- **Add** a new version file (`<name>-<next>.yaml`).
- **Delete** a version file (so a future v2 can reinstate a previously-removed module at a new path).
- **Rename** is treated as a (delete + add) pair.

Forbidden: editing the body of an existing version file. To publish a fix to a stock module's interface, create a new version with a bumped `version:` field.

The gate runs on every PR (not on `push: main`) and lives in `scripts/manifest-immutability.sh`. The script's `--self-test` flag exercises the four diff-status letters (`A`, `D`, `R`, `M`) against synthetic fixtures.

## Sensitive-name lint
A non-blocking CI lint warns when a manifest parameter's `name` matches a secret-shape substring (case-insensitive) but does not declare `sensitive: true`. Default patterns: `password`, `secret`, `key`, `token`, `connection_string`. The pattern list is configurable via `stock-modules-manifests/.sensitive-name-patterns` (one substring per line; `#` comments and blank lines are stripped).

The lint emits `::warning::` annotations on the PR review surface but never fails the build — it surfaces producer omissions for human review. Implementation: `src/Tools.TerraformLint/` (`Tools.TerraformLint manifest-sensitive-names <dir>`); the same tool's `secret-outputs <dir>` subcommand enforces the secret-output rule above as a build-failing check, and `--self-test` exercises both modes against synthetic fixtures.
