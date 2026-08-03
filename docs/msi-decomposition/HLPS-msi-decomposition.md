# HLPS: Installer Decomposition — Splitting Setup.Dorc into Deployable Units

| Field       | Value                            |
|-------------|----------------------------------|
| **Status**  | IN REVIEW — U-1/U-2/U-3 resolved by user 2026-08-03 |
| **Author**  | Agent                            |
| **Date**    | 2026-08-03                       |
| **Folder**  | docs/msi-decomposition/          |

---

## 1. Problem Statement

`Setup.Dorc.msi` packages every DOrc component into a single installable unit: the web UI, the API, the monitors and their runners, and five command-line tools. This creates two distinct problems.

**Deployment.** The components do not run on the same machines and do not change at the same rate, but they can only be installed, upgraded and rolled back together. A fix to the API cannot be shipped without redeploying the monitors and runners — for a tool whose job is orchestrating other people's deployments, being unable to patch itself without disturbing in-flight work is a real operational constraint. The deployment mechanism already selects target servers per installer via the `ServerType` in each `.msi.json` sidecar (see C-2); the constraint is purely that one MSI carries all four components.

**Build time.** Measured from an MSBuild binary log of `main` at `80fadbb` (run `30844659253`), the installers dominate the build:

| Item | Time | Share of 459s build |
|------|-----:|--------------------:|
| `Setup.Dorc` project | 266.3s | 58% |
| `Setup.Acceptance` project | 52.4s | 11% |
| `WixBuild` task (2 calls) | 152.7s | — |
| `Csc` task (31 calls) | 121.0s | — |
| `HeatDirectory` task (16 calls) | 6.3s | 1.4% |

MSI construction costs more than all C# compilation. With only two installer projects — one of which is five times the size of the other — the packaging step is effectively serial even under `/m`, because parallelism is bounded by the largest single installer.

---

## 2. Current Structure

The MSI already carries a feature tree whose boundaries align closely with the proposed split. Component counts are from the `FeatureComponents` table of the shipped `Setup.Dorc.msi` (`26.8.3.2679`):

```
Deployment.Orchestrator  (root, 0 components)
├── Web.Components        701 components
│     ├── DOrcAPIComGroup      ← the API
│     └── DOrcWebComGroup      ← the web UI
├── Monitors             1939 components
│     ├── NonProd{DeployMonitor,DorcRunner,DeployRunner,TerraformRunner}ComGroup
│     └── Prod{DeployMonitor,DorcRunner,DeployRunner,TerraformRunner}ComGroup
└── Tools                2282 components
      ├── ToolsRequestComGroup
      ├── ToolsDeployCopyEnvBuildComGroup
      ├── ToolsPostRestoreEndurComGroup
      ├── ToolsPropertyValueCreationComGroup
      └── ToolsEncryptionMigrationComGroup
```

Total 4,922 components across 4,903 files.

Supporting tables, and where they appear to belong:

| Table | Rows | Apparent owner |
|-------|-----:|----------------|
| `ServiceInstall` | 2 | Monitors (`DeploymentActionServiceNonProd`, `DeploymentActionServiceProd`) |
| `Wix4IIsWebSite` / `WebApplication` / `WebVirtualDir` / `AppPool` / `MimeMap` / `WebAddress` / `WebSiteCertificates` | — | UI and API |
| `CustomAction` | 42 | 39 are supplied by WiX extensions (Util, IIs, JsonFile, Certificates, Smb, ServiceConfig). Only three are hand-authored: `SetCDrive`, `ReconfigureLoadUserProfileOrchestrator`, `ReconfigureLoadUserProfileRequestApi` |
| `Wix4User`, `Wix4FileShare`, `Wix4FileSharePermissions` | — | **Unassigned** — see U-4 |

That the extension-supplied custom actions dominate is favourable: they arrive automatically with whichever extensions each new installer references, rather than needing manual partitioning.

Identity today is a single `ProductCode {F7894A5D-…}` under a single `UpgradeCode {72EDEA4C-…}`.

---

## 3. Scope

**In scope**

- Splitting `Setup.Dorc.msi` into four installers along the existing feature boundaries:
  1. **UI** — `DOrcWebComGroup`
  2. **API** — `DOrcAPIComGroup`
  3. **Monitors & Runners** — the eight `Monitors` component groups
  4. **CLIs** — the five `Tools` component groups
- Assigning upgrade identity (`ProductCode`, `UpgradeCode`) to each new installer.
- A migration path for existing installations from the single product to four.
- Updating `DeploySettings.template.json` and `CopyDeploymentArtefactsBeforePublish.ps1` for the new artefact set.
- Updating both pipelines to build and publish four installers.

**Out of scope**

- `Setup.Acceptance.msi` — already a separate installer for a separate concern; unchanged.
- `Dorc.Database.dacpac` — remains a single schema artefact.
- Migrating off `heat` to `<Files>`. Measured at 6.3s (1.4%), it is a deprecation matter, not a performance one, and mixing it in would confound verification of this change.
- Any change to what the components *do*.

**Dependency:** this work must land after PR #807. That PR removes the `<Exec>` chains from the wixprojs. Splitting first would multiply the CS2012 race from two concurrent `Exec` chains to four.

---

## 4. Goals and Success Criteria

| ID | Goal | Measured by |
|----|------|-------------|
| G-1 | No change to what gets installed | Union of the four new MSIs' `File` tables equals the current `Setup.Dorc.msi` `File` table, allowing for shared assemblies deliberately duplicated per installer. Verified with the `msitools` table diff already used on #807. |
| G-2 | Components become independently deployable | Each installer installs, upgrades and uninstalls without the other three present. |
| G-3 | Packaging step wall-clock reduced | `WixBuild` total wall-clock under `/m`, compared against the `80fadbb` binlog baseline. |
| G-4 | Existing installations upgrade cleanly | An environment running `26.8.3.x` of the single product reaches the four-installer layout with no orphaned files, services or IIS entries. A maintenance window is acceptable (U-3). |
| G-5 | Component versions are visible in the UI | The UI shows the installed version of each of the four components for an environment, not a single aggregate. Follows directly from U-1: skew is permitted, so it must be observable. |

**Expected size of G-3, stated up front to avoid over-claiming.** Parallel packaging is bounded by the largest resulting installer. `Tools` alone is 2,282 of 4,922 components (46%), so four installers built concurrently should approach roughly half the current packaging time, not a quarter. If `WixBuild` scales with component count, the 266s `Setup.Dorc` becomes ~120–140s wall-clock. A further split of `Tools` into its five CLIs would rebalance, but topology is the better organising principle than build-time balance, and that trade should be made deliberately rather than by accident.

---

## 5. Constraints

- **C-1 — Shared database schema, with skew permitted.** All four components share one schema via `Dorc.Database.dacpac`, and four independently deployable installers make version skew possible: API `v1` running against Monitor `v2`. Per U-1 this is **accepted, not prevented** — no start-up gate or refusal. The obligation it creates is observability (G-5): if skew is allowed, an operator must be able to see it. Note this shifts the risk rather than removing it — a mismatch will surface as behaviour, not as a clear error.
- **C-2 — Per-MSI targeting already exists.** *(Corrected 2026-08-03 after reading `sefe/dorc-ps-deploy-module`; an earlier revision of this document claimed a schema change was required. It is not.)* Each installer ships a `.msi.json` sidecar declaring `ServerType`, and `DeployMSI` filters the environment's target servers by it — `Setup.Dorc.msi.json` currently declares `"ServerType": "WebServer"`. Independent deployment topology is therefore a matter of each new installer declaring its own `ServerType`, not of extending `DeploySettings`. What *is* required is deciding which server type each of the four components targets.
- **C-3 — Shared assemblies duplicate.** `Dorc.Core`, `Dorc.PersistentData` and `Dorc.ApiModel` sit beneath most components and will be carried by several installers. Acceptable on disk; it makes C-1 sharper, because the duplicated copies can drift.
- **C-4 — Installed-state continuity.** Existing environments have the single product installed. Four new `ProductCode`s under new `UpgradeCode`s will not automatically supersede it.
- **C-5 — One property bag.** `DeploySettings` passes roughly 60 `MsiProperties` to the MSI. Whether each installer needs the full set or a subset is unresolved (U-5).

---

## 6. Proposed Solution Directions

### SD-1 — One wixproj per deployable unit, along existing feature lines

Create four wixprojs, each referencing the component groups already defined. Because the groups are disjoint and named by concern, the authoring largely moves rather than being rewritten. Each installer references only the WiX extensions it needs — IIs for UI and API, Util/ServiceConfig for Monitors, and so on — which partitions the extension-supplied custom actions automatically.

Addresses G-1, G-3.

### SD-2 — Rely on the existing uninstall-before-install deployment

Per U-3, `RunDeployment.ps1` already uninstalls before installing, driven by the `ProductNames` on each `MsiFileNames` entry, and a maintenance window is acceptable. So no `Upgrade`-element coexistence dance is needed: the four installers are registered as four `MsiFileNames` entries with four `ProductNames`, and the existing mechanism removes the old product and installs the new set.

Two caveats. First, `DeployMSI` is **not defined in this repository** — it comes from the external `DOrcDeployModule` that `RunDeployment.ps1` installs, so its behaviour across four products needs confirming from whoever owns that module (U-9). Second, the legacy `UpgradeCode {72EDEA4C-…}` should still be declared for removal by one of the new installers as a safety net for any environment the scripted path misses.

Addresses G-4, C-4.

### SD-3 — Declare `ServerType` per installer in its `.msi.json` sidecar

No schema change is needed. Each of the four installers ships its own sidecar declaring the `ServerType` it targets, and the existing `DeployMSI` filtering does the rest. The sidecar also carries the `Parameters` map, so each installer declares only the MSI parameters it actually consumes — `Setup.Acceptance.msi.json` already demonstrates this with 12 entries against `Setup.Dorc.msi.json`'s ~70.

The work is therefore authoring four sidecars and deciding the target server type for each component, not extending the deployment configuration format.

One authoring detail: `Setup.Acceptance.msi.json` is UTF-16 encoded while `Setup.Dorc.msi.json` is UTF-8. New sidecars should be UTF-8.

Addresses G-2, C-2, C-5.

### SD-4 — Per-component version reporting, surfaced in the UI

Per U-1 skew is permitted, so the requirement is visibility rather than enforcement.

Today the only version surface is `MetadataController`, which returns a single unstructured string — `"{env} - {assembly version}"` — describing the **API alone**. There is no channel by which Monitors, Runners or CLIs report a version: the schema has no heartbeat or instance table, Runners execute on target servers, and CLIs run ad hoc.

So this is a genuine feature, not a display tweak. It needs:

1. A structured replacement for the metadata endpoint, returning a version per component rather than one string.
2. A reporting channel for the components that do not currently have one (U-8).
3. UI presentation of the set, with skew made visually obvious rather than merely listed.

Addresses G-5, C-1, C-3.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | What version-skew policy applies once components deploy independently against one shared schema? Refuse to start on mismatch, warn, or tolerate? | User | **Blocking** for SD-4 | **RESOLVED** (2026-08-03). Multiple concurrent versions are acceptable — no gate, no refusal to start. The condition attached is that the UI must show all component versions, which becomes G-5 and reshapes SD-4 from enforcement to observability. Raises U-8. |
| U-2 | Is independent deployability actually wanted, or is the build-time win the real objective? | User | **Blocking** for scope | **RESOLVED** (2026-08-03). Independent deployability is the objective. SD-3 and SD-4 are therefore in scope, and the build-time gain (G-3) is a secondary benefit rather than the justification. |
| U-3 | How do existing installations migrate? Is a maintenance window acceptable, or must the transition be seamless? | User | **Blocking** for SD-2 | **RESOLVED** (2026-08-03). `RunDeployment.ps1` performs a full uninstall before installing, and a maintenance window is acceptable. SD-2 collapses to registering four `MsiFileNames`/`ProductNames` entries. Raises U-9. |
| U-4 | Which installer owns `Wix4User`, `Wix4FileShare` and `Wix4FileSharePermissions`? These are shared infrastructure with no obvious single owner. | Agent | **Blocking** for SD-1 | **Unresolved** — resolvable by reading the authoring; not yet done. |
| U-5 | Can the ~60 `MsiProperties` be partitioned per installer, or must each receive the full set? | Agent | Non-blocking | **RESOLVED** (2026-08-03). Already the existing design: the `Parameters` array in each `.msi.json` sidecar maps MSI parameters to deploy properties per installer. `Setup.Acceptance.msi.json` declares 12; `Setup.Dorc.msi.json` declares ~70. Each new installer declares only what it consumes. |
| U-6 | Does `WixBuild` time scale with component count, as G-3's estimate assumes? | Agent | Non-blocking | **Unresolved.** Testable once the first split installer exists; the estimate should be revised then rather than defended. |
| U-7 | Are the three hand-authored custom actions (`SetCDrive`, `ReconfigureLoadUserProfileOrchestrator`, `ReconfigureLoadUserProfileRequestApi`) cleanly attributable to single installers? | Agent | Non-blocking | Two are named for Orchestrator and RequestApi respectively, suggesting UI and API. `SetCDrive` is unclear. |
| U-8 | How do Monitors, Runners and CLIs report their version for G-5? No channel exists today — the schema has no heartbeat or instance table, `MetadataController` reports only the API's own assembly version, Runners execute on target servers and CLIs run ad hoc. | User + Agent | **Blocking** for G-5 | **Unresolved.** Raised by the resolution of U-1. Options include a heartbeat row written by each component, reading installed `ProductVersion` from the target servers' registry at query time, or scoping G-5 to only those components that can already be reached. These differ substantially in cost. |
| U-9 | Does `DeployMSI` handle four products correctly, including uninstall ordering? It is defined in the external `DOrcDeployModule`, not this repository. | User | **Blocking** for SD-2 | **RESOLVED** (2026-08-03) by reading `sefe/dorc-ps-deploy-module`. `DeployMSI` takes `[System.String[]]$ProductNames` and iterates it, uninstalling each before installing — `Setup.Dorc.msi.json` already exercises this with two names (`Deployment Orchestrator`, `DevOps Orchestrator`), a precedent from an earlier rename. Deployment runs one `Start-Job` per target server, so per-server work is already parallel. No module change is required for four installers. |

**Remaining blocker: U-8 only.** U-1, U-2 and U-3 were resolved by the user; U-5 and U-9 were then resolved by reading `sefe/dorc-ps-deploy-module`, which also corrected C-2 and shrank SD-3 substantially. U-8 (no version-reporting channel for Monitors, Runners and CLIs) still gates G-5. SD-1, SD-2 and SD-3 are unblocked and can form the early IS steps while U-8 is settled.

---

## 8. Out-of-Scope Risks

- **Rollback complexity grows.** Four installers mean four rollback operations, with the possibility of a partially rolled-back environment. Today rollback is atomic by construction.
- **More upgrade identity to manage.** Four `UpgradeCode`s must remain stable forever; regenerating one silently breaks upgrades for that component.
- **Artefact count in the drop.** `CopyDeploymentArtefactsBeforePublish.ps1` and the drop-share consumers see four MSIs plus `.msi.json` sidecars where they saw one.
- **The build-time win is bounded by `Tools`.** If build time is the dominant motivation, the natural next step is splitting `Tools` further — which is not a deployment-topology boundary and would pull the design away from the deployability goal.
