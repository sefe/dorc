# Execution record — installer decomposition

What has actually been done, and what evidence exists for it. The IS defines two verification surfaces ([CI] and [ENV]); a step is complete only when both have passed. Steps whose [ENV] checks have not been run are recorded here as **partially verified**, not as done.

No [ENV] host has been available for any step so far. Every entry below that carries [ENV] intents is outstanding on those, and that is the single largest gap in this work.

| Step  | Commit    | [CI] | [ENV] | State |
|-------|-----------|------|-------|-------|
| S-001 | `581a35c`, `ce7f039` | pending run | n/a | harness written and wired; baseline not yet pinned |
| S-002 | `2a567da` | pending run | not run | partially verified |
| S-003 | `b6c19ee` | pending run | not run | partially verified |
| S-004 | `4894061` | pending run | n/a | partially verified |
| S-005 | —         | —    | —     | **not run**: needs a Windows host with IIS. S-008/S-009 authored on its likely answer instead, at the user's direction |
| S-006 | `e0b39d0` | pending run | not run | partially verified |
| S-007 | `d2fe1fd` | pending run | not run | partially verified |
| S-008 | `07f8121` | pending run | not run | partially verified; certificate handling **assumed** |
| S-009 | `1f3a678` | pending run | not run | partially verified |
| S-010…S-013 | — | — | — | not started |

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

**Not run.** S-008 and S-009 were authored against its likely answer instead, on the user's instruction, so the split could reach four packages while an environment is arranged. What that means concretely is in the S-008 entry below: it is an assumption carrying real risk, not a settled question.

**Still needs** a Windows host with IIS to install two packages declaring the same certificate and observe what uninstalling one does to the other. Nothing about it can be settled from the build, and S-008 and S-009 are both authored against its outcome.

The reading that motivates it: the UI and the API each already declare their own `iis:Certificate` over the same two PFX files, with their own `Binary` elements. `iis:CertificateRef` resolves within a package, so the earlier plan of "the API owns both certificates and the UI references them" was never implementable.

## S-006 — CLI package

`Setup.Dorc.Cli.msi` carries `ToolsShare`, the five `*ConfigSetup` components and the five harvested payloads, with component GUIDs unchanged. Its sidecar, its `DeploySettings.template.json` entry and its artifact collection in both pipelines landed in the same commit, so there is no window in which the package exists but nothing deploys it.

Its five parameters are a strict subset of the monolith's, so no environment needs a new property first.

**Outstanding [ENV]:** standalone install and uninstall, the `CLITools` share, and — the first live test of S-003 — that uninstalling the CLIs leaves the residual's files alone.

**Outstanding [CI]:** the G-1 union comparison, which cannot run until the baseline is pinned. Until then the file set is unverified against the monolith, and the claim that this is a pure move rests on reading rather than measurement.

## S-007 — Monitors package

`Setup.Dorc.Monitors.msi` carries the eight service and runner components, both `ServiceInstall` rows, the eight harvested groups and the `C:\Log` share, with component GUIDs unchanged. `Services\` is monitors-owned outright, so its recursive cleanup moved with it. Sidecar, registration and artifact collection landed in the same commit.

Its 45 parameters were derived from the properties the authoring actually references and checked against the monolith's 72, so they are a strict subset.

The Kafka drift guard was checking all three `.wxs` files against one sidecar. It now pairs each with the sidecar of the package that ships it — the same assertion, correctly targeted now that there is more than one package.

**Outstanding [ENV]:** that installing registers both services under the names `ServiceInstall` declares and uninstalling removes them; and that the Monitors package installs standalone on a machine with no other DOrc package, since the runners must not have acquired a hidden dependency on API files.

**Outstanding [CI]:** the G-1 union comparison, for the same reason as S-006 — the baseline is not pinned.

---

## What is needed from someone with an environment

Three things unblock the rest of this work, and none of them can be done from the repository:

1. **Pin the baseline.** Set `MSI_BASELINE_RUN_ID` to the last single-package build. Until then the union comparison — the invariant the whole sequence is built around — has never actually run against a real baseline.
2. **Provide a Windows host with IIS.** Everything marked [ENV] above is waiting on it, and S-005 cannot start without it.
3. **Run S-005.** S-008 and S-009 are authored against its assumed outcome, so it is now a confirmation rather than a gate — but an unconfirmed assumption about HTTPS is not something to carry into production.
4. **Then S-010–S-013**: the per-environment `DeploySettings` rollout, the conditional quiesce in `RunDeployment.ps1`, and the goal verification pass.


## S-008 — API package

`Setup.Dorc.Api.msi` carries `DOrcAPIComGroup`, both `RequestApi` components, the API's custom action and `RegistryEntries` — the last with its GUID unchanged, so the machine-scoped registry value keeps its identity.

**The certificate handling is assumed, not verified.** Each web package keeps its own `iis:Certificate`, now hosted in its own component marked `Permanent="yes"` so the removal custom action never runs. The reasoning: `iis:Certificate` compiles to a custom action rather than a refcounted resource, so without this, uninstalling either web package would strip a certificate the other is still bound to.

Two things follow that someone should check before this reaches production:

1. **The certificates now outlive the last package.** After all four are removed, `DorcNonProdSSLCert.pfx` and `deploymentportal.pfx` remain in `localMachine\root`. That is the price of the mechanism, and it is a deliberate trade rather than an oversight.
2. **`Shared="yes"` with matching GUIDs is the alternative** — real refcounting, so the certificate goes when the last package does, at the cost of coupling the two packages' component identity. S-005 is what chooses between them, and it has not been run.

A side effect worth knowing: the certificates used to resolve through a bind path the `Dorc.Api` project reference happened to contribute. The UI package no longer has that reference, so both packages now name the directory (`$(var.DorcCertDir)`) explicitly. A build failure here would be loud, not silent.

## S-009 — UI package; monolith retired

`Setup.Dorc` is now `Setup.Dorc.Web`, with a new `UpgradeCode` and `ProductCode`. The legacy `Deployment Orchestrator` and `DevOps Orchestrator` names moved to the API sidecar, which installs first — the installed monolith still carries the pre-S-003 `RemoveFolders`, so removing it after a new package had installed would delete that package's files.

Final order: **API → Web → Monitors → CLIs.**

**Outstanding [ENV], and this is the one that matters most:** the first deployment against an environment running the monolith. It exercises the legacy-name handover, the install order, and the S-003 cleanup scoping all at once, and none of the three has been observed.
