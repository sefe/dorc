# IS: Installer Decomposition — Implementation Sequence

| Field       | Value                                          |
|-------------|------------------------------------------------|
| **Status**  | DRAFT — pending adversarial review             |
| **Author**  | Agent                                          |
| **Date**    | 2026-08-03                                     |
| **HLPS**    | HLPS-msi-decomposition.md (IN REVIEW, round 2 passed, no blocking unknowns) |
| **Folder**  | docs/msi-decomposition/                        |

---

## Sequencing principle

Two decisions shape the order, and both exist to keep the blast radius small while the dangerous work happens.

**The risky authoring changes happen inside the monolith first (S-002…S-004), before anything is split.** `RemoveFolders` deleting the shared root, the `ServiceControl` names that address a service which is never installed, and the `ProductVersion` bound to an API-only file are all pre-existing defects. Fixing each while there is still exactly one package means each is independently verifiable against a single baseline, with no interaction between them and no four-way ownership question in flight. Splitting first would mean debugging these *and* the split simultaneously.

**Extraction then proceeds in increasing order of risk (S-005…S-008):** CLIs (no IIS, no services, no certificates) → Monitors (services, still no IIS or certificates) → API (certificates, IIS, and the shared registry entry) → UI (depends on the API per C-16). The riskiest package is authored last, when the mechanism has already been proven three times.

**A continuous invariant, checkable at every step.** At each point in S-005…S-008 the union of the extracted packages and the residual monolith must equal the S-001 baseline **exactly**. G-1 is therefore not a single acceptance test at the end — it is a gate on every extraction step, and a non-empty diff at any point localises the fault to the step that produced it.

---

## Step Index

| ID    | Title                                                        | Addresses            | Depends On |
|-------|--------------------------------------------------------------|----------------------|------------|
| S-001 | Baseline harness and removal of dead authoring               | G-1                  | —          |
| S-002 | Correct the `ServiceControl` service names                    | C-14                 | S-001      |
| S-003 | Re-scope the shared-root cleanup                              | C-7                  | S-001      |
| S-004 | Per-package `ProductVersion` source                           | C-9, SD-5            | S-001      |
| S-005 | Extract the CLIs package                                      | SD-1, G-1, G-3       | S-003, S-004 |
| S-006 | Extract the Monitors package                                  | SD-1, G-1, G-3       | S-002, S-005 |
| S-007 | Extract the API package, owning the shared resources          | SD-1, SD-4, C-8, C-16 | S-005      |
| S-008 | Reduce the residual to the UI package; retire the monolith    | SD-1, G-1            | S-007      |
| S-009 | Sidecars, `DeploySettings` registration and ordering          | SD-2, SD-3, C-10     | S-008      |
| S-010 | Conditional quiesce, start step and outcome capture           | SD-6, G-2, G-5       | S-009      |
| S-011 | Pipelines and `Install.Orchestrator.bat`                      | scope 9, 11          | S-008      |
| S-012 | Goal verification pass                                        | G-1…G-5              | S-010, S-011 |

---

## S-001 — Baseline harness and removal of dead authoring

### What changes
A reference `Setup.Dorc.msi` is built and its installer tables captured as the acceptance baseline. The `msitools` comparison used to verify PR #807 is turned into a repeatable check that takes any set of MSIs and compares their union against that baseline on (target directory, file name, version), ignoring File Ids. The dead `Directory Id="ReferenceDataApi"` and the unassigned `DorcRefDataApiDir` constant are removed.

### Why it changes
Every subsequent step is verified against this baseline, so it must exist before the first change that could perturb it. The comparison already exists as a one-off script from #807; making it repeatable is what allows G-1 to gate each extraction rather than only the final result. Removing the dead authoring first means an implementer following the "every ref gets an owner" mandate in S-005…S-008 does not stop to work out whether an orphaned directory is a component they have missed.

### Dependencies
None.

### Verification intent
- The harness reports zero drift when comparing the baseline against itself, and reports drift when comparing `Setup.Dorc.msi` against `Setup.Acceptance.msi`. Both directions are checked before the harness is trusted — a comparison that cannot fail is not a test.
- The build succeeds and the baseline MSI's table row counts match the shipped `26.8.3.x` reference.

---

## S-002 — Correct the `ServiceControl` service names

### What changes
The `ServiceControl` elements in `Monitors/Prod/ProdActionService.wxs` and `Monitors/NonProd/NonProdActionService.wxs` are corrected to name the services that `ServiceInstall` actually creates, and the missing `Stop` on the NonProd element is added. Still within the monolith.

### Why it changes
**Addresses C-14.** `ServiceInstall` creates `DeploymentActionServiceProd`/`NonProd`; `ServiceControl` targets `DeployMonitorServiceProd`/`NonProd`, which is never installed. Additionally Prod declares `Stop="both"` while NonProd declares no `Stop` at all. The installers have therefore never been able to stop or remove their own services — `RunDeployment.ps1`'s blanket `Stop-Services` prelude has masked it. S-010 removes that mask, so this must be correct before then, and fixing it here means it is verified while the monitors are still a feature of one package rather than a package of their own.

### Dependencies
S-001 (baseline).

### Verification intent
- An upgrade over an existing installation with the monitor services **running** completes without a files-in-use prompt and without requiring a reboot — the failure this defect would produce once the script prelude no longer stops them.
- Uninstall removes both services rather than leaving them registered.
- File-table diff against baseline: zero.

---

## S-003 — Re-scope the shared-root cleanup

### What changes
`RemoveFolders` no longer claims the shared `INSTALLLOCATION` root. Directory cleanup is re-expressed so that each subtree (`Web\`, `Services\`, `Tools\`) is cleaned by whichever package will own it, and no component performs a recursive delete of a directory it does not exclusively own. Still within the monolith.

### Why it changes
**Addresses C-7, the largest single risk in the change.** `util:RemoveFolderEx Property="INSTALLLOCATION" On="both"` plus `RemoveFile Directory="INSTALLLOCATION" Name="*.*" On="both"` recursively deletes the parent of all three subtrees, on install as well as uninstall. Harmless in one transaction; after the split, whichever package inherits it destroys the other three. Doing this while still monolithic means the change can be verified in isolation — an install and uninstall cycle against a single package — rather than diagnosed later as one of four packages mysteriously losing files.

### Dependencies
S-001.

### Verification intent
- Install, then uninstall, leaves no orphaned files or directories under the install root — the behaviour the original component provided, preserved by other means.
- Install over an existing installation does not delete files placed by that installation, which is the `On="both"` half of the defect and the half that will bite after the split.
- File-table diff against baseline: zero.

---

## S-004 — Per-package `ProductVersion` source

### What changes
`ProductVersion` stops binding to `!(bind.FileVersion.DeployApiDll)` and takes the `Version` property that both pipelines already pass and the wixproj currently ignores.

### Why it changes
**Addresses C-9 and SD-5.** `DeployApiDll` is minted by the API harvest transform and exists only in the API's component group, so three of the four future packages would have nothing to bind to and would fail at link time. It also means every component currently reports the API's version — precisely the illusion the visibility work exists to dispel. Taking the pipeline's `Version` makes each package's version independently meaningful, which is a prerequisite for that later work.

### Dependencies
S-001.

### Verification intent
- The built MSI's `ProductVersion` matches the version the pipeline passed, rather than the API assembly's file version.
- A local build with no `Version` supplied still produces a valid package with a defensible default.
- File-table diff against baseline: zero (this changes package metadata, not payload).

---

## S-005 — Extract the CLIs package

### What changes
A new wixproj produces a CLIs installer carrying `ToolsShare`, the five `*ConfigSetup` components and the five `Tools*ComGroup` groups. The monolith drops them.

### Why it changes
**First extraction, chosen because it is the cleanest boundary.** The Tools feature has no IIS authoring, no Windows services, no certificates and no shared machine-scoped resources — the only hand-authored items are `ToolsShare` and the five config components, all of which are unambiguously CLI-owned. It proves the extraction mechanism, the wixproj scaffolding and the G-1 invariant on the package where the least can go wrong. It is also the largest package by component count, so it produces the first real data point for U-17 and G-3.

### Dependencies
S-003 (shared-root cleanup must be re-scoped before any package can own a subtree), S-004 (`ProductVersion` must resolve without the API).

### Verification intent
- Union of the CLIs package and the residual monolith equals the S-001 baseline exactly.
- The CLIs package installs and uninstalls standalone, creating and removing the `CLITools` share.
- Uninstalling the CLIs package leaves the residual monolith's files intact — the first live test of S-003.
- `WixBuild` time and payload bytes recorded for both packages, as the first evidence for G-3 and U-17.

---

## S-006 — Extract the Monitors package

### What changes
A new wixproj produces a Monitors installer carrying the eight `DeployMonitorService*.exe` components (two of which carry the `ServiceInstall` rows), `LogsShare`, and the eight monitor and runner component groups. The monolith drops them.

### Why it changes
Second-lowest risk: services and a file share, but no IIS and no certificates. Sequenced after S-005 so the extraction mechanism is already proven, and after S-002 so the service control authoring is correct before the services move into a package of their own.

### Dependencies
S-002 (service names), S-005 (mechanism proven).

### Verification intent
- Union of both extracted packages and the residual monolith equals the baseline exactly.
- Installing the Monitors package registers both services under the names `ServiceInstall` declares; uninstalling removes them.
- Installing the Monitors package alone, on a machine with no other DOrc package, succeeds — the runners are plain executables and must not have acquired a hidden dependency on the API package's files.

---

## S-007 — Extract the API package, owning the shared resources

### What changes
A new wixproj produces an API installer carrying `DOrcAPIComGroup`, `RequestApiComponent` and its Prod variant, **both certificates** (`DorcNonProdSSLCert.pfx` and `deploymentportal.pfx`), and `RegistryEntries`. Its sidecar will later carry the legacy `ProductNames`. The monolith drops them.

### Why it changes
**Addresses C-8, C-16 and the U-12 decision.** The API owns both certificates because `iis:Certificate` is a custom action and is not reference-counted, so a shared declaration would mean uninstalling either web package removes the other's HTTPS certificate. `RegistryEntries` moves with it as the other machine-scoped resource. Both certificates move together — a change addressing only the prod certificate would leave the identical defect in every non-production environment.

This is sequenced before the UI because the UI references what this package owns.

### Dependencies
S-005 (mechanism proven). Independent of S-006.

### Verification intent
- Union of three extracted packages and the residual equals the baseline exactly.
- Both certificates are installed into `localMachine\root` by this package, and the `RegistryEntries` GUID is **identical** to the baseline's — verified by comparing the Component table, not by inspection. A regenerated GUID here is a component-rules violation that would not surface until an uninstall in production.
- The API serves over HTTPS on its own bindings with no other package installed.

---

## S-008 — Reduce the residual to the UI package; retire the monolith

### What changes
The residual monolith wixproj becomes the UI package: `DOrcWebComGroup`, `DOrcWebIISComponent` and its Prod variant, referencing the certificates the API package installs. `Setup.Dorc.msi` as a product ceases to exist.

### Why it changes
Last, because per C-16 the UI depends on certificates owned by the API. Framing it as *reducing the residual* rather than authoring a fourth package means the G-1 invariant closes naturally: when the residual contains only UI components, the union of the four packages is the baseline by construction.

### Dependencies
S-007 (the UI references API-owned certificates).

### Verification intent
- Union of the four packages equals the S-001 baseline exactly, with the residual now empty. This is the full G-1 acceptance test.
- The UI serves over HTTPS **with the API package installed**; the documented and expected failure without it is confirmed and recorded, since C-16 makes that an invalid state rather than a bug.
- Installing all four in the S-009 order on a clean machine produces an environment functionally equivalent to one installed from the baseline MSI.

---

## S-009 — Sidecars, `DeploySettings` registration and ordering

### What changes
Four `.msi.json` sidecars, UTF-8, each declaring `ProductNames`, `ServerType`, its `Parameters` subset and a `Services` array (U-16). `DeploySettings.template.json` gains four `MsiFileNames` entries in the order **API → UI → Monitors → CLIs**, and the matching `ServerNames_<ServerType>` properties. The API sidecar carries the legacy `Deployment Orchestrator` and `DevOps Orchestrator` names so the monolith is removed first.

### Why it changes
**Addresses SD-2, SD-3 and C-10.** Ordering is fixed by two independent dependencies: Monitor configuration embeds `DEPLOYAPI.ENDPOINT`, and the UI needs the API's certificates. Placing the legacy names on the first-sequenced package means the monolith is gone before any new package installs, which matters because the monolith carries the pre-S-003 `RemoveFolders`.

### Dependencies
S-008.

### Verification intent
- No sidecar `ProductNames` entry contains `*`. Per U-11 the module's matching is exact unless a wildcard is present, in which case it becomes a substring match that would uninstall sibling packages — including ones installed earlier in the same run. This is checked explicitly, not assumed.
- Each sidecar's `Parameters` set is a subset of the baseline's 72, and their union covers every property the four packages consume.
- A deployment against an environment running the monolith reaches the four-package layout with no orphaned services, shares or IIS entries (G-4).

---

## S-010 — Conditional quiesce, start step and outcome capture

### What changes
`RunDeployment.ps1` stops only the services belonging to installers in the covered set, derived as the complement of `Exclude_MSI` (U-15) using each sidecar's `Services` array (U-16). An explicit start-and-verify step is added after installation. The MSI loop gains per-package outcome capture, fails fast, and reports which products are installed and which are not. The global `DeploymentServices` string is retired.

### Why it changes
**Addresses SD-6, G-2 and G-5.** Today the script stops both monitors on every target server before any installer is selected, and nothing restarts them — so a UI-only deployment stops the monitors and leaves them stopped, and independent deployability is unreachable however well the packages are split. Separately, the bare `foreach` over `MsiFileNames` means a mid-run failure can leave a component absent while the run reports success; splitting one atomic operation into four makes that materially more likely.

### Dependencies
S-009 (the sidecars carry the service association this step consumes).

### Verification intent
- Deploying the API alone to a live environment leaves the monitors **running and processing** — the criterion G-2 actually needs, measured in an environment rather than on a clean box.
- A deliberately failed install of the second package produces a failed run that names which products are installed and which are not (G-5). The current behaviour — silent success — is confirmed absent.
- Services are running after a Monitors deployment without manual intervention.

---

## S-011 — Pipelines and `Install.Orchestrator.bat`

### What changes
Both pipelines build, collect and publish four MSIs and four sidecars in place of the wildcard copy from `src\Setup.Dorc\bin\x64\$(Configuration)`. `Install.Orchestrator.bat` is resolved — replaced, generalised or removed.

### Why it changes
The pipelines hardcode a single output path. `Install.Orchestrator.bat` is a manual helper hardcoded to `msiexec /i Setup.Dorc.msi` with ~25 properties spanning three of the four packages, carried into the build output as `<Content>`; the split orphans it silently unless it is dealt with deliberately.

### Dependencies
S-008. Independent of S-009 and S-010, so it can run in parallel with them.

### Verification intent
- Both pipelines produce all four MSIs and four sidecars with correct versions.
- `WixBuild` wall-clock and payload bytes recorded per package on a `/m` binlog, resolving U-17 and giving G-3 its measurement. The binlog is attached to the step's record rather than the figures being restated from memory.

---

## S-012 — Goal verification pass

### What changes
No production change. A verification pass against G-1 through G-5, recording evidence for each.

### Why it changes
The per-step gates verify locally; this confirms the goals as stated. G-3 in particular has been wrong twice in this project's history and its threshold (≥30% reduction in `WixBuild`) should be judged against measured evidence, not against the estimate in the HLPS.

### Dependencies
S-010, S-011.

### Verification intent
- G-1: union file-table diff, zero.
- G-2: single-package deployment leaves the other three serving.
- G-3: measured `WixBuild` reduction against a like-for-like post-#807 `/m` baseline, with the parallelism assumption (U-17) now resolved. **If the threshold is not met, the finding is recorded rather than the goal quietly restated.**
- G-4: an environment upgraded from the monolith is clean.
- G-5: injected failure produces an accurate state report.

---

## Carried risks

- **C-15 remains in force throughout.** Until the visibility work lands, any change to a shared `MsiProperty` requires a full four-package deployment. The visibility HLPS is in REVISION with blocking unknowns of its own and cannot be relied on to arrive first, so this is an operational rule from S-009 onward, not a temporary inconvenience.
- **C-13 is unobservable.** Component-versus-schema skew has no route to detection; the dacpac has no service to interrogate. Accepted, and not addressed by any step here.
- **U-15 and U-16 are proposals, not decisions.** S-010 is specified against them. If either is settled differently, S-010's spec changes and S-009's sidecar shape changes with it.
