# Execution record — installer decomposition

What has actually been done, and what evidence exists for it. The IS defines two verification surfaces ([CI] and [ENV]); a step is complete only when both have passed. Steps whose [ENV] checks have not been run are recorded here as **partially verified**, not as done.

No [ENV] host has been available for any step so far. Every entry below that carries [ENV] intents is outstanding on those, and that is the single largest gap in this work.

| Step  | Commit    | [CI] | [ENV] | State |
|-------|-----------|------|-------|-------|
| S-001 | `581a35c`, `ce7f039` | pending run | n/a | harness written and wired; baseline not yet pinned |
| S-002 | `2a567da` | pending run | not run | partially verified |
| S-003 | `b6c19ee` | pending run | not run | partially verified |
| S-004 | `4894061` | pending run | n/a | partially verified |
| S-005 | —         | —    | —     | **blocked**: needs a Windows host with IIS |
| S-006 | `e0b39d0` | pending run | not run | partially verified |
| S-007…S-013 | — | — | — | not started |

---

## S-001 — Baseline harness, CI gate and the environment checklist

`tools/msi-table-diff/Compare-MsiTables.ps1` compares files, registry rows and IIS certificate rows, and fails on drift in any of the three. `Verify-InstallerTables.ps1` is the entry point both pipelines call.

**Not yet done:** the baseline itself is not pinned. `MSI_BASELINE_RUN_ID` (Actions) and `MsiBaselinePath` (ADO) are unset, so only the harness self-test runs and the G-1 comparison does not. Arming it means choosing the last single-package build and setting the variable — a repository-settings action, not a code change.

**Also not yet done:** the [ENV] checklist. It should be written before S-005, because S-005 is the first step that cannot make progress without one.

## S-002 — `ServiceControl` service names

`ServiceControl` now names `DeploymentActionServiceProd` / `DeploymentActionServiceNonProd`, matching what `ServiceInstall` creates, and NonProd gained the `Stop` it was missing.

**Outstanding [ENV]:** upgrade with the services running, and uninstall removing them. Until `RunDeployment.ps1`'s blanket `Stop-Services` prelude goes (S-011), the old defect stays masked in a real deployment, so this cannot be observed by deploying — it needs a direct `msiexec` upgrade.

## S-003 — Shared-root cleanup

Recursive cleanup moved from `INSTALLLOCATION` onto each subtree, with its future owner. The root and `Web` keep an empty-only `RemoveFolder`.

**Two open questions, neither answerable from a build:**

1. Whether anything is written directly under `Deploy\` outside the four subtrees. Such files were previously swept by the root `RemoveFolderEx` and now are not.
2. Whether the original `RemoveFolderEx` was resolving `INSTALLLOCATION` at all. `WixRemoveFoldersEx` reads the property early in the execute sequence, and the usual guidance is to persist the path in the registry precisely because a directory property may not be resolved in time. If it never resolved, C-7 was latent rather than active — which changes how alarming the original was, but not whether the new scoping is right.

## S-004 — Build-sourced package version

`ProductVersion` takes `$(Version)` from the pipeline. `[System.Version]::Parse(...).ToString()` strips the leading zeros in `yy.MM.dd.<run>`, which should reproduce the value `bind.FileVersion` produced, since the Win32 version resource holds numbers rather than the literal string.

**Unverified until CI runs:** that `System.Version` is on MSBuild's static-method allow-list. There is no .NET SDK in the authoring environment, so this could not be checked locally. If it is not allowed the build fails loudly (MSB4185) rather than silently producing a wrong version.

## S-005 — Certificate coexistence spike

**Blocked.** Needs a Windows host with IIS to install two packages declaring the same certificate and observe what uninstalling one does to the other. Nothing about it can be settled from the build, and S-008 and S-009 are both authored against its outcome.

The reading that motivates it: the UI and the API each already declare their own `iis:Certificate` over the same two PFX files, with their own `Binary` elements. `iis:CertificateRef` resolves within a package, so the earlier plan of "the API owns both certificates and the UI references them" was never implementable.

## S-006 — CLI package

`Setup.Dorc.Cli.msi` carries `ToolsShare`, the five `*ConfigSetup` components and the five harvested payloads, with component GUIDs unchanged. Its sidecar, its `DeploySettings.template.json` entry and its artifact collection in both pipelines landed in the same commit, so there is no window in which the package exists but nothing deploys it.

Its five parameters are a strict subset of the monolith's, so no environment needs a new property first.

**Outstanding [ENV]:** standalone install and uninstall, the `CLITools` share, and — the first live test of S-003 — that uninstalling the CLIs leaves the residual's files alone.

**Outstanding [CI]:** the G-1 union comparison, which cannot run until the baseline is pinned. Until then the file set is unverified against the monolith, and the claim that this is a pure move rests on reading rather than measurement.
