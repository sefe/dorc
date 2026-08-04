# IS: Installer Decomposition — Implementation Sequence

| Field       | Value                                          |
|-------------|------------------------------------------------|
| **Status**  | REVISION — round 1 adversarial review folded in |
| **Author**  | Agent                                          |
| **Date**    | 2026-08-03                                     |
| **HLPS**    | HLPS-msi-decomposition.md (IN REVIEW, round 2 passed, no blocking unknowns) |
| **Folder**  | docs/msi-decomposition/                        |

---

## Correction carried from review: the UI does not reference the API's certificates

C-16 and the first draft of this sequence both stated that the UI package must reference certificates owned by the API package. Reading the authoring settles it, and the claim is wrong:

| Package | Component | Certificate | Binary | PFX |
|---------|-----------|-------------|--------|-----|
| UI  | `DOrcWebIISComponent`     | `MySSLCert`        | `certBinary`     | `DorcNonProdSSLCert.pfx` |
| UI  | `DOrcWebIISComponentProd` | `MySSLCertProd`    | `certBinaryProd` | `deploymentportal.pfx`   |
| API | `RequestApiComponent`     | `MySSLCertApi`     | `certBinaryApi`  | `DorcNonProdSSLCert.pfx` |
| API | `RequestApiComponentProd` | `MySSLCertApiProd` | `certBinaryApiProd` | `deploymentportal.pfx` |

The UI and the API each already declare their own `iis:Certificate` with their own embedded `Binary`. Four declarations install two physical certificates. `iis:CertificateRef` resolves against a `Certificate` row in the same package — it is not a cross-package reference and never could be — so "the API owns both certificates, the UI references them" is not implementable, and is also unnecessary: the duplication the split would need already exists.

This changes two things:

1. **C-8 is not a shared-declaration problem, it is a removal problem.** Both packages installing the same certificate into `localMachine\root` is what the source already does. What breaks after the split is uninstall: `iis:Certificate` is a custom action and is not reference-counted, so removing either web package deletes a certificate the other is still bound to. S-005 exists to settle how that is prevented.
2. **The UI→API install-time ordering rationale falls away.** The UI depends on the API at runtime (`$.api` / `DEPLOYAPI.ENDPOINT`), not at link or install time. The API is still sequenced before the UI, but for the reason given under Sequencing below, not because of certificates.

The HLPS's C-8 and C-16 need the same correction; it is recorded here so no step is authored against the wrong model in the meantime.

---

## Sequencing principle

**The risky authoring changes happen inside the monolith first (S-002…S-004), before anything is split.** `RemoveFolders` deleting the shared root, the `ServiceControl` names that address a service which is never installed, and the `ProductVersion` bound to an API-only file are all pre-existing defects. Fixing each while there is still exactly one package means each is independently verifiable against a single baseline, with no interaction between them and no four-way ownership question in flight.

**The one mechanism nothing in this repo demonstrates is proven by spike before it is depended on (S-005).** Every wixproj here is a self-contained compilation unit; nothing shows how two packages coexist over one machine-scoped certificate. S-008 and S-009 are both authored against whatever S-005 establishes.

**Extraction then proceeds in increasing order of blast radius (S-006…S-009):** CLIs (no IIS, no services, no certificates) → Monitors (services, still no IIS or certificates) → API (certificates, IIS, and the shared registry entry) → UI (the residual). S-008 is where the hardest unresolved authoring problem is actually taken on — the shared `RegistryEntries` and the uncounted certificates. S-009 is deliberately last because it is mechanically "whatever is left", and a residual that has been emptied step by step is the cheapest possible way to prove nothing was dropped. The earlier framing of the UI as "the riskiest package, authored last" overstated it; the order is unchanged, the reason is not.

**Every extraction registers itself in the same step.** A package that is built but not named in `MsiFileNames` is invisible to `RunDeployment.ps1`, which deploys strictly by iterating that list. If registration lagged behind extraction, a deployment run anywhere in the window would install a shrinking residual and silently lose the functionality that had already moved out, with nothing filling the gap. Each of S-006…S-009 therefore delivers its wixproj, its sidecar and its `DeploySettings.template.json` entry as one indivisible change.

**A continuous invariant, checkable at every step.** At each point in S-006…S-009 the union of the extracted packages and the residual must equal the S-001 baseline **exactly**. G-1 is a gate on every extraction step, and a non-empty diff localises the fault to the step that produced it.

---

## Verification surfaces

Roughly half of the checks below cannot run in CI, and saying so is the difference between a gate and an aspiration. Every verification intent is tagged:

- **[CI]** — runs unattended on the Windows build agent. Structural: MSI table comparison, package metadata, build success, recorded timings. `Compare-MsiTables.ps1` reads through the `WindowsInstaller` COM API without installing anything, so it qualifies. S-001 wires it into both pipelines as an enforced gate; until then it is a script someone has to remember to run.
- **[ENV]** — requires a provisioned Windows host with IIS, service control and an existing DOrc installation. `.github/workflows/release.yml` does not install anything today, and is not asked to. These run on a DOrc non-production environment, by hand, against the checklist S-001 produces.

**A step is not complete until both surfaces pass.** [CI] going green on a step whose [ENV] checks have not been run is a step in progress, not a step done. Where no [ENV] host is available, the step is blocked and recorded as blocked — not passed on [CI] alone.

---

## Step Index

| ID    | Title                                                        | Addresses            | Depends On |
|-------|--------------------------------------------------------------|----------------------|------------|
| S-001 | Baseline harness, CI gate and the environment checklist       | G-1                  | —          |
| S-002 | Correct the `ServiceControl` service names                    | C-14                 | S-001      |
| S-003 | Re-scope the shared-root cleanup                              | C-7                  | S-001      |
| S-004 | Build-sourced package version                                 | C-9, SD-5            | S-001      |
| S-005 | Certificate coexistence spike                                 | C-8                  | S-001      |
| S-006 | Extract and register the CLIs package                         | SD-1, SD-2, G-1, G-3 | S-003, S-004 |
| S-007 | Extract and register the Monitors package                     | SD-1, SD-2, G-1, G-3 | S-002, S-006 |
| S-008 | Extract and register the API package                          | SD-1, SD-4, C-8      | S-005, S-006 |
| S-009 | Reduce the residual to the UI package; retire the monolith    | SD-1, SD-3, C-10, G-1 | S-008     |
| S-010 | Roll the four-package layout into the real `DeploySettings`   | scope 10, G-4        | S-009      |
| S-011 | Conditional quiesce, start step and outcome capture           | SD-6, G-2, G-5       | S-009      |
| S-012 | Pipelines and `Install.Orchestrator.bat`                      | scope 9, 11          | S-009      |
| S-013 | Goal verification pass                                        | G-1…G-5              | S-010, S-011, S-012 |

Steps with no ordering constraint between them (S-007 and S-008; S-011 and S-012) are still executed **serially**, because each mutates the same residual `Product.wxs` and the invariant harness has no notion of two extractions in flight. "Independent" here means "no correctness dependency", not "may be run concurrently".

---

## S-001 — Baseline harness, CI gate and the environment checklist

### What changes
A reference `Setup.Dorc.msi` is built and its installer tables captured as the acceptance baseline. `tools/msi-table-diff/Compare-MsiTables.ps1` takes any set of MSIs and compares their union against that baseline. It gates on three things, all of them hard failures:

- the **file set**, on (target directory, file name, version), ignoring File Ids — re-harvesting legitimately regenerates Ids, and comparing them reports total churn and tells you nothing;
- the **`Registry` table**, on (Root, Key, Name, Value) — `RegistryEntries` carries no file, so a file-set comparison cannot see it at all;
- the **`Certificate` table** written by the IIS extension, on (Name, StoreLocation, StoreName) — the resource C-8 identifies as the hardest to get right, and one no other check in this plan touches.

Remaining tables (`Component`, `Directory`, `Feature`, `FeatureComponents`, `ServiceInstall`, `CustomAction`) are reported by row count only: a split legitimately redistributes them, so drift there is information rather than failure.

The script is wired into both pipelines through one shared entry point, so neither carries its own copy of the logic. Every build runs the harness self-test — a package against itself must report zero, an unrelated package must report drift — which needs nothing external and fails the job if either direction is wrong. The G-1 comparison itself runs when the pipeline is told where the baseline is (`MSI_BASELINE_RUN_ID` in Actions, `MsiBaselinePath` in ADO); setting that is what arms the gate, and until it is set the step warns rather than passing quietly.

A documented [ENV] checklist is produced covering install, uninstall, upgrade-over-existing, service state and HTTPS reachability, so the manual half of every later step is a list to work through rather than prose to re-derive.

The dead `Directory Id="ReferenceDataApi"` and the unassigned `DorcRefDataApiDir` constant are removed.

### Why it changes
Every subsequent step is verified against this baseline, so it must exist before the first change that could perturb it. A harness nobody runs is not a gate, which is why CI wiring is a deliverable of this step and not an afterthought. Removing the dead authoring first means an implementer following the "every ref gets an owner" mandate does not stop to work out whether an orphaned directory is a component they have missed.

### Dependencies
None.

### Verification intent
- **[CI]** The harness reports zero drift comparing the baseline against itself, and non-zero comparing `Setup.Dorc.msi` against `Setup.Acceptance.msi`. Both directions are checked before it is trusted — a comparison that cannot fail is not a test.
- **[CI]** The pipeline fails when the self-test's negative direction stops reporting drift, confirming the gate is enforcing rather than reporting. This runs on every build, not once.
- **[CI]** With the baseline variable set, a deliberately perturbed MSI fails the job.
- **[CI]** The build succeeds and the baseline MSI's table row counts match the shipped `26.8.3.x` reference.

---

## S-002 — Correct the `ServiceControl` service names

### What changes
The `ServiceControl` elements in `Monitors/Prod/ProdActionService.wxs` and `Monitors/NonProd/NonProdActionService.wxs` are corrected to name the services that `ServiceInstall` actually creates, and the missing `Stop` on the NonProd element is added. Still within the monolith.

### Why it changes
**Addresses C-14.** `ServiceInstall` creates `DeploymentActionServiceProd`/`NonProd`; `ServiceControl` targeted `DeployMonitorServiceProd`/`NonProd`, which is never installed. Prod declared `Stop="both"` while NonProd declared no `Stop` at all. The installers have therefore never been able to stop or remove their own services — `RunDeployment.ps1`'s blanket `Stop-Services` prelude has masked it. S-011 removes that mask, so this must be correct before then.

### Dependencies
S-001.

### Verification intent
- **[CI]** File-table diff against baseline: zero. `ServiceControl` rows name the same services as `ServiceInstall` rows.
- **[ENV]** An upgrade over an existing installation with the monitor services **running** completes without a files-in-use prompt and without requiring a reboot.
- **[ENV]** Uninstall removes both services rather than leaving them registered.

---

## S-003 — Re-scope the shared-root cleanup

### What changes
`RemoveFolders` no longer claims the shared `INSTALLLOCATION` root. Directory cleanup is re-expressed so that each subtree (`Web\`, `Services\`, `Tools\`) is cleaned by whichever package will own it, and no component performs a recursive delete of a directory it does not exclusively own. Still within the monolith.

### Why it changes
**Addresses C-7, the largest single risk in the change.** `util:RemoveFolderEx Property="INSTALLLOCATION" On="both"` plus `RemoveFile Directory="INSTALLLOCATION" Name="*.*" On="both"` recursively deletes the parent of all three subtrees, on install as well as uninstall. Harmless in one transaction; after the split, whichever package inherits it destroys the other three. Doing this while still monolithic means it can be verified in isolation rather than diagnosed later as one of four packages mysteriously losing files.

### Dependencies
S-001.

### Verification intent
- **[CI]** File-table diff against baseline: zero. No `RemoveFile`/`RemoveFolder` row targets `INSTALLLOCATION` itself.
- **[ENV]** Install, then uninstall, leaves no orphaned files or directories under the install root — the behaviour the original component provided, preserved by other means.
- **[ENV]** Install over an existing installation does not delete files placed by that installation — the `On="both"` half of the defect, and the half that bites after the split.

---

## S-004 — Build-sourced package version

### What changes
`ProductVersion` stops binding to `!(bind.FileVersion.DeployApiDll)` and takes the `Version` property both pipelines already pass and the wixproj ignored. The value is parsed and re-formatted so the leading zeros in `yy.MM.dd.<run>` are stripped, matching what the Win32 version resource produced. A local build with no `Version` gets a placeholder; a CI build without one fails.

### Why it changes
**Addresses C-9 and SD-5.** `DeployApiDll` is minted by the API harvest transform and exists only in the API's component group, so three of the four future packages would have nothing to bind to and would fail at link time. It also means every component reports the API's version — the illusion the visibility work exists to dispel.

### Dependencies
S-001.

### Verification intent
- **[CI]** The built MSI's `ProductVersion` matches the version the pipeline passed, and is byte-identical to the baseline's.
- **[CI]** A build with `Version` omitted under CI fails with the explicit error rather than producing a package.
- **[CI]** File-table diff against baseline: zero — this changes package metadata, not payload.

---

## S-005 — Certificate coexistence spike

### What changes
No production change. Two throwaway packages are built, each declaring the same `iis:Certificate` into `localMachine\root`, and installed and uninstalled in both orders on an [ENV] host. The outcome is a decision recorded in this folder: how two packages coexist over one machine-scoped certificate.

Candidate mechanisms, to be falsified rather than assumed:

1. **Both packages keep their own declaration, hosted in a `Permanent="yes"` component.** Permanent components are never scheduled for removal, so the uninstall custom action should not run. Cost: the certificate is left in the store after all packages are removed.
2. **Both keep their declaration unchanged and last-uninstall-wins is accepted**, with the surviving package's certificate restored by reinstall. Cost: a window where HTTPS is broken; almost certainly unacceptable, but it is the current behaviour's natural extension and should be measured rather than dismissed.
3. **The certificate leaves the MSIs entirely** and becomes an environment prerequisite. Cost: a new manual step, and a change to what "deploy DOrc" means.

### Why it changes
**Addresses C-8.** `iis:Certificate` compiles to a custom action, not a refcounted resource, so ordinary MSI component rules do not apply and identical GUIDs do not help. Nothing in this repository demonstrates the behaviour, and S-008 and S-009 are both authored against whatever the answer is. Committing both packages to an assumed mechanism and discovering it at S-009 would mean re-authoring two packages' IIS sections at the point where the residual is being emptied — the worst possible moment.

### Dependencies
S-001.

### Verification intent
- **[ENV]** For the chosen mechanism: installing A then B, then uninstalling A, leaves B serving over HTTPS. The same holds with the order reversed.
- **[ENV]** After all packages are uninstalled, the state of `localMachine\root` is known and recorded, whether or not the certificate remains.
- **[CI]** The mechanism is expressible in the `Certificate` table check S-001 added, so later steps can assert it structurally.

---

## S-006 — Extract and register the CLIs package

### What changes
A new wixproj produces a CLIs installer carrying `ToolsShare`, the five `*ConfigSetup` components and the five `Tools*ComGroup` groups. The monolith drops them. **In the same change**: its `.msi.json` sidecar (UTF-8, `ProductNames`, `ServerType`, its `Parameters` subset, its `Services` array) and its `DeploySettings.template.json` entry, positioned after the residual.

### Why it changes
**First extraction, chosen because it is the cleanest boundary.** The Tools feature has no IIS authoring, no Windows services, no certificates and no shared machine-scoped resources. It proves the extraction mechanism, the wixproj scaffolding, the sidecar shape and the G-1 invariant on the package where the least can go wrong. It is also the largest package by component count, so it produces the first real data point for U-17 and G-3.

### Dependencies
S-003 (the shared-root cleanup must be re-scoped before any package can own a subtree), S-004 (`ProductVersion` must resolve without the API).

### Verification intent
- **[CI]** Union of the CLIs package and the residual equals the S-001 baseline exactly.
- **[CI]** The sidecar's `ProductNames` contains no `*`. Per U-11 the module's matching is exact unless a wildcard is present, in which case it becomes a substring match that would uninstall sibling packages — including ones installed earlier in the same run.
- **[CI]** `WixBuild` time and payload bytes recorded for both packages — first evidence for G-3 and U-17.
- **[ENV]** The CLIs package installs and uninstalls standalone, creating and removing the `CLITools` share.
- **[ENV]** Uninstalling the CLIs package leaves the residual's files intact — the first live test of S-003.
- **[ENV]** A deployment run against an environment picks up both packages from `MsiFileNames` and produces the same functional result as the monolith did.

---

## S-007 — Extract and register the Monitors package

### What changes
A new wixproj produces a Monitors installer carrying the eight `DeployMonitorService*.exe` components (two of which carry the `ServiceInstall` rows), `LogsShare`, and the eight monitor and runner component groups. The monolith drops them. Sidecar and `MsiFileNames` entry land in the same change, positioned after the residual and before the CLIs.

### Why it changes
Second-lowest blast radius: services and a file share, but no IIS and no certificates. Sequenced after S-002 so the service control authoring is correct before the services move into a package of their own. The dependency on S-006 is a **deliberate risk-reduction choice**, not a technical constraint — the two packages share no resource — taken so the extraction mechanism is proven once before it is applied to a package that installs services.

### Dependencies
S-002 (service names), S-006 (process choice, see above).

### Verification intent
- **[CI]** Union of both extracted packages and the residual equals the baseline exactly.
- **[CI]** `ServiceInstall` rows move wholesale; none remain in the residual.
- **[ENV]** Installing the Monitors package registers both services under the names `ServiceInstall` declares; uninstalling removes them.
- **[ENV]** Installing the Monitors package alone, on a machine with no other DOrc package, succeeds — the runners are plain executables and must not have acquired a hidden dependency on the API package's files.

---

## S-008 — Extract and register the API package

### What changes
A new wixproj produces an API installer carrying `DOrcAPIComGroup`, `RequestApiComponent` and its Prod variant with their existing certificate declarations (per the S-005 decision), and `RegistryEntries`. Its sidecar carries its own `ProductNames` — the legacy names stay with the residual until S-009 retires it. `MsiFileNames` entry positioned first, ahead of the residual. The monolith drops them.

### Why it changes
**Addresses C-8 and SD-4.** This is where the two genuinely hard resources land: `RegistryEntries`, machine-scoped and previously shared, and the certificates, whose coexistence mechanism S-005 settled. Both non-production and production certificate authoring moves together — a change addressing only the prod certificate would leave the identical defect in every non-production environment.

### Dependencies
S-005 (certificate mechanism decided), S-006 (mechanism proven). No correctness dependency on S-007; still executed after it.

### Verification intent
- **[CI]** Union of three extracted packages and the residual equals the baseline exactly.
- **[CI]** The `RegistryEntries` GUID is **identical** to the baseline's, compared against the Component table rather than read by eye. A regenerated GUID is a component-rules violation that would not surface until an uninstall in production.
- **[CI]** The `Registry` and `Certificate` table checks from S-001 both pass over the union.
- **[ENV]** The API serves over HTTPS on its own bindings with no other package installed.
- **[ENV]** Uninstalling the API leaves the residual UI serving over HTTPS — the S-005 decision under real conditions, and the check that would have caught the cross-package certificate error had it been assumed instead of spiked.

---

## S-009 — Reduce the residual to the UI package; retire the monolith

### What changes
The residual becomes the UI package: `DOrcWebComGroup`, `DOrcWebIISComponent` and its Prod variant, with their own certificate declarations. It takes a **new** `UpgradeCode` and `ProductCode` and a new product name, so `Setup.Dorc.msi` as a product ceases to exist rather than mutating into something with a different payload under the same identity. The legacy `Deployment Orchestrator` and `DevOps Orchestrator` names move to the API sidecar's `ProductNames`, and the API is sequenced first, so the monolith is uninstalled before any new package installs.

Through S-006…S-008 the residual deliberately keeps its identity, so every intermediate build is an ordinary in-place upgrade of the installed monolith. This step is the single point at which that identity is retired.

### Why it changes
**Addresses C-10 and closes G-1.** Framing this as *reducing the residual* rather than authoring a fourth package means the invariant closes by construction: when the residual contains only UI components, the union of the four packages is the baseline. Placing the legacy names on the first-sequenced package means the monolith is gone before any new package installs, which matters because the monolith still carries the pre-S-003 `RemoveFolders` — an installed monolith removed *after* a new package would delete that package's files on its way out.

Ordering across the four is fixed by two runtime dependencies: Monitor configuration embeds `DEPLOYAPI.ENDPOINT`, and the UI reads `$.api`. Final order: **API → UI → Monitors → CLIs**.

### Dependencies
S-008.

### Verification intent
- **[CI]** Union of the four packages equals the S-001 baseline exactly, with no residual remaining. This is the full G-1 acceptance test.
- **[CI]** Each sidecar's `Parameters` set is a subset of the baseline's 72, and their union covers every property the four packages consume.
- **[CI]** No sidecar `ProductNames` entry contains `*`; the legacy names appear exactly once, on the API.
- **[ENV]** A deployment against an environment running the monolith reaches the four-package layout with no orphaned services, shares or IIS entries (G-4), and the monolith is uninstalled before the first new package installs.
- **[ENV]** Installing all four in order on a clean machine produces an environment functionally equivalent to one installed from the baseline MSI.
- **[ENV]** The UI serves over HTTPS with the API installed; behaviour without it is recorded, since C-16's runtime dependency makes that an invalid state rather than a bug.

---

## S-010 — Roll the four-package layout into the real `DeploySettings`

### What changes
The per-environment `DeploySettings.json` files — the real ones, not the template each earlier step maintained — are updated to the four-package `MsiFileNames` list, in the S-009 order, with the matching `ServerNames_<ServerType>` properties. A rollout procedure and a rollback to the monolith list are written down.

### Why it changes
**Addresses HLPS scope item 10**, which the first draft of this sequence dropped entirely. `DeploySettings.template.json` is what the repository carries; it is not what a deployment reads. Every step up to here has kept the template honest, and no environment has changed. This is the step where environments actually move, and it is the last point at which the change is reversible by editing a settings file rather than by rebuilding packages.

### Dependencies
S-009.

### Verification intent
- **[ENV]** One non-production environment is migrated first and left running for an observation period before any other environment follows.
- **[ENV]** Reverting a migrated environment to the monolith list, with the monolith MSI still available, restores it — the rollback is exercised, not just documented.
- **[CI]** Every migrated `DeploySettings.json` names exactly the four packages the pipeline publishes, checked mechanically rather than by review.

---

## S-011 — Conditional quiesce, start step and outcome capture

### What changes
`RunDeployment.ps1` stops only the services belonging to installers in the covered set, derived as the complement of `Exclude_MSI` (U-15) using each sidecar's `Services` array (U-16). An explicit start-and-verify step is added after installation. The MSI loop gains per-package outcome capture, fails fast, and reports which products are installed and which are not. The global `DeploymentServices` string is retired.

### Why it changes
**Addresses SD-6, G-2 and G-5.** Today the script stops both monitors on every target server before any installer is selected, and nothing restarts them — so a UI-only deployment stops the monitors and leaves them stopped, and independent deployability is unreachable however well the packages are split. Separately, the bare `foreach` over `MsiFileNames` means a mid-run failure can leave a component absent while the run reports success; splitting one atomic operation into four makes that materially more likely.

### Dependencies
S-009 (the sidecars carry the service association this step consumes). Independent of S-012; executed serially with it.

### Verification intent
- **[ENV]** Deploying the API alone leaves the monitors **running and processing** — the criterion G-2 actually needs, measured in an environment rather than on a clean box.
- **[ENV]** A deliberately failed install of the second package produces a failed run naming which products are installed and which are not (G-5). The current behaviour — silent success — is confirmed absent.
- **[ENV]** Services are running after a Monitors deployment without manual intervention.

---

## S-012 — Pipelines and `Install.Orchestrator.bat`

### What changes
Both pipelines build, collect and publish four MSIs and four sidecars in place of the wildcard copy from `src\Setup.Dorc\bin\x64\$(Configuration)`. `Install.Orchestrator.bat` is resolved — replaced, generalised or removed.

### Why it changes
The pipelines hardcode a single output path. `Install.Orchestrator.bat` is a manual helper hardcoded to `msiexec /i Setup.Dorc.msi` with ~25 properties spanning three of the four packages, carried into the build output as `<Content>`; the split orphans it silently unless it is dealt with deliberately.

### Dependencies
S-009. Independent of S-010 and S-011; executed serially with them.

### Verification intent
- **[CI]** Both pipelines produce all four MSIs and four sidecars with correct versions, and the S-001 gate runs over the union.
- **[CI]** `WixBuild` wall-clock and payload bytes recorded per package on a `/m` binlog, resolving U-17 and giving G-3 its measurement. The binlog is attached to the step's record rather than the figures being restated from memory.

---

## S-013 — Goal verification pass

### What changes
No production change. A verification pass against G-1 through G-5, recording evidence for each, and a note of which evidence is [CI] and which is [ENV] attestation.

### Why it changes
The per-step gates verify locally; this confirms the goals as stated. G-3 in particular has been wrong twice in this project's history and its threshold (≥30% reduction in `WixBuild`) should be judged against measured evidence, not against the estimate in the HLPS.

### Dependencies
S-010, S-011, S-012.

### Verification intent
- **[CI]** G-1: union file-table, `Registry` and `Certificate` diffs, all zero.
- **[ENV]** G-2: single-package deployment leaves the other three serving.
- **[CI]** G-3: measured `WixBuild` reduction against a like-for-like post-#807 `/m` baseline, with U-17 resolved. **If the threshold is not met, the finding is recorded rather than the goal quietly restated.**
- **[ENV]** G-4: an environment upgraded from the monolith is clean.
- **[ENV]** G-5: injected failure produces an accurate state report.

---

## Carried risks

- **The [ENV] surface has no owner named in this document.** Roughly half the gates above need a Windows host with IIS and an existing DOrc installation, and nobody has been assigned to run them. Until that is resolved, every step carrying [ENV] intents completes on [CI] evidence only and is recorded as *partially verified*. This is the largest process risk in the plan and it is not solvable inside the repository.
- **C-15 remains in force throughout.** Until the visibility work lands, any change to a shared `MsiProperty` requires a full four-package deployment. The visibility HLPS is in REVISION with blocking unknowns of its own and cannot be relied on to arrive first, so this is an operational rule from S-006 onward.
- **C-13 is unobservable.** Component-versus-schema skew has no route to detection; the dacpac has no service to interrogate. Accepted, and not addressed by any step here.
- **U-15 and U-16 are proposals, not decisions.** S-011 is specified against them. If either is settled differently, S-011's spec changes and the sidecar shape each of S-006…S-009 delivers changes with it.
- **The HLPS still states C-8 and C-16 in the corrected-away form.** Until it is amended, the correction at the head of this document is the authority.
