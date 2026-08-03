# HLPS: Installer Decomposition — Splitting Setup.Dorc into Deployable Units

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | REVISION — rewritten after adversarial round 1 |
| **Author**  | Agent                                    |
| **Date**    | 2026-08-03                               |
| **Folder**  | docs/msi-decomposition/                  |

> **Revision note.** Round 1 of the adversarial panel rejected the substance of the first draft. Its §2 was derived from the *shipped MSI's* flattened `FeatureComponents` table, which hides every hand-authored component; that error concealed a destructive shared-root cleanup (C-7), an unlinkable `ProductVersion` (C-9), and 21 unassigned components carrying all the IIS, service and share authoring. Its build-time evidence did not support its headline claim. This revision re-derives everything from the `.wxs` sources. Version and configuration visibility has been split into a separate HLPS — see `HLPS-component-version-visibility.md`.

---

## 1. Problem Statement

`Setup.Dorc.msi` packages every DOrc component into one installable unit: the web UI, the API, the monitors and their runners, and five command-line tools. They run on different machines and change at different rates, but can only be installed, upgraded and rolled back together.

For a tool whose job is orchestrating other people's deployments, being unable to patch itself without disturbing in-flight work is a real operational constraint. **Independent deployability is the objective** (U-2).

**Build time is a secondary benefit, and smaller than the first draft claimed.** That draft asserted MSI construction costs more than all C# compilation. It does not, and the measurement it relied on cannot support the claim:

- The baseline binlog (`80fadbb`, run `30844659253`) is the commit that *disabled* parallel building, so any statement about behaviour "under `/m`" was describing a configuration never measured.
- At that commit the wixprojs still ran their `PublishProjects` `<Exec>` chains, so `Setup.Dorc`'s 266.3s is **packaging plus payload publishing**, and the child processes' compilation is invisible to the parent binlog. Comparing it against a parent-only `Csc` of 121.0s compares unlike things.

What the binlog does support: `WixBuild` is **152.7s for both installers combined**. Correctly bounded, a four-way split is worth roughly 60s — see G-3. Worth having, not worth justifying the change on.

---

## 2. Current Structure

Derived from `src/Setup.Dorc/Product.wxs`, **not** from the shipped MSI. The three features reference **36 items: 15 `ComponentGroupRef`s (heat-harvested files) and 21 bare `ComponentRef`s (hand-authored)**. The hand-authored ones carry every IIS site, application pool, certificate, Windows service registration, file share and configuration edit — that is, the entire hard part of the split. They are invisible in the compiled `FeatureComponents` table, which is why the first draft missed them.

All three features are unconditional `Level="1"`; there are no feature conditions. What *is* conditioned, at component level, is `ENVIRONMENTISPRODUCTION`, selecting prod against non-prod variants.

| Feature | Hand-authored `ComponentRef` | Harvested `ComponentGroupRef` | Proposed owner |
|---|---|---|---|
| `Web.Components` | `RemoveFolders` | — | **unassigned — see C-7** |
| | `RegistryEntries` | — | **shared — see C-8** |
| | `DOrcWebIISComponent`, `…Prod` | `DOrcWebComGroup` | UI |
| | `RequestApiComponent`, `…Prod` | `DOrcAPIComGroup` | API |
| `Monitors` | 8 × `DeployMonitorService*.exe` (carry both `ServiceInstall` rows) | 8 × `{NonProd,Prod}{DeployMonitor,DorcRunner,DeployRunner,TerraformRunner}ComGroup` | Monitors |
| | `LogsShare` | — | Monitors |
| `Tools` | `ToolsShare` | — | CLIs |
| | 5 × `*ConfigSetup` | 5 × `Tools*ComGroup` | CLIs |

**The UI/API cut is below a feature boundary,** not along one — both live inside `Web.Components`. The Monitors and Tools cuts do follow feature boundaries. The first draft's "split along the existing feature boundaries" framing was accurate for two of the four packages only.

Identity today: a single `ProductCode {F7894A5D-…}` under `UpgradeCode {72EDEA4C-…}`, with `ProductVersion` bound to an API file (C-9).

---

## 3. Scope

**In scope**

1. Four wixprojs, with **every one of the 36 refs assigned an owner** — not just the 15 groups.
2. A shared-resource ownership policy (C-8): for each machine-scoped resource, either a sole owning package, or shared with the component GUID copied verbatim.
3. Re-scoping the shared-root cleanup so no package deletes another's files (C-7).
4. A `ProductVersion` source for each package (C-9).
5. Four `.msi.json` sidecars — `ProductNames`, `ServerType`, and the per-installer `Parameters` map.
6. Registering four `MsiFileNames` entries in `DeploySettings`, with ordering (C-10).
7. **`RunDeployment.ps1`: making the service stop/log-off prelude conditional on the selected installers, and adding an explicit start step.** Without this the deployment stops all monitors on every run regardless of which installers are selected, and G-2 is unreachable however well the MSIs are split.
8. Both pipelines, which currently hardcode `src\Setup.Dorc\bin\x64\$(Configuration)` as a wildcard copy source.

**Out of scope**

- `Setup.Acceptance.msi` — separate installer, separate concern.
- `Dorc.Database.dacpac` — remains one schema artefact.
- `heat` → `<Files>`: 6.3s, 1.4% of the build. A deprecation matter; mixing it in would confound G-3.
- **Version and configuration visibility** — moved to `HLPS-component-version-visibility.md`. C-6 remains here as a constraint; its solution is that document's problem.
- `CopyDeploymentArtefactsBeforePublish.ps1` — data-driven from `MsiFileNames`, needs no change.

**Dependency status:** PR #807 has **already merged** (`40f106a`). Note it did not remove the `<Exec>` publish chains — it gated them behind `DorcPayloadPrepublished` and retained them as a local-build fallback. CI is unaffected, but a four-way split multiplies the local-build race from two chains to four, so each new wixproj must either omit the fallback or be covered by C-11.

---

## 4. Goals and Success Criteria

| ID | Goal | Measured by |
|----|------|-------------|
| G-1 | No change to what gets installed | **Exact set equality** over (target directory, file name, version) between the union of the four packages and the current `Setup.Dorc.msi`. Expected diff: zero. Any non-empty diff is a failure requiring written justification. File *Ids* will legitimately churn on re-harvest, so they are excluded from the comparison; the first draft's "allowing for duplication" escape clause is removed as unfalsifiable. |
| G-2 | Components are independently **deployable**, not merely independently installable | Deploying one installer to a live environment leaves the other three components **running and serving**. Measured in an environment, not on a clean box. |
| G-3 | Packaging wall-clock reduced | `WixBuild` wall-clock on a post-#807 `/m` binlog, against a like-for-like re-baseline. Threshold: ≥30% reduction in `WixBuild` for `Setup.Dorc`'s successors. |
| G-4 | Existing installations transition cleanly | An environment on `26.8.3.x` of the single product reaches the four-installer layout with no orphaned files, services, shares or IIS entries. Maintenance window acceptable (U-3). |

**G-3's expected size, corrected.** The first draft applied the 46% `Tools` share to `Setup.Dorc`'s 266.3s *project* time and predicted ~120–140s wall-clock. That was the wrong base: at most 152.7s of it is `WixBuild`, across both installers. If Acceptance is ~40s, Setup.Dorc's packaging is ~113s, and a split bounded at 46% yields ~52s — a **~60s saving, ~13% of a 459s build**, roughly half what was claimed.

Component count is also a poor proxy: `Media Id="1"` is a single embedded cabinet, so compression is one serial pass over the payload and tracks **bytes**, not components. `Tools`' 2,282 components are five CLI publish folders sharing much of one dependency set. G-3's measurement should report payload bytes per package alongside component counts.

---

## 5. Constraints

- **C-1 — Shared schema, skew permitted.** Per U-1, concurrent component versions are accepted, not prevented. The obligation this creates is observability, addressed in the separate visibility HLPS.
- **C-2 — Per-MSI targeting already exists.** Each installer's `.msi.json` declares a `ServerType`; `DeployMSI` resolves it against `ServerNames_<ServerType>` properties. No `DeploySettings` schema change. Each new `ServerType` needs a matching `ServerNames_*` property.
- **C-3 — Shared assemblies are already duplicated.** `Dorc.Core`, `Dorc.PersistentData` and `Dorc.ApiModel` already exist as separate components at separate target paths, with path-derived GUIDs. Splitting adds no disk duplication and no component-rule risk. The new exposure is version drift between copies.
- **C-4 — Installed-state continuity.** Existing environments run the single product. Removal must be ordered against installation (C-7).
- **C-5 — Per-installer properties.** Already the sidecar's job: `Setup.Acceptance.msi.json` declares 13 parameters, `Setup.Dorc.msi.json` 72.
- **C-6 — Configuration skew.** The installers write each component's configuration at install time via `Json:JsonFile` — the DB connection string, `$.AppSettings.RefDataApiUrl` from `[DEPLOYAPI.ENDPOINT]`, Kafka SASL and OpenSearch credentials. With four installers a shared-property change reaches only the packages actually run. Rotating `KAFKA_SASL_PASSWORD` and deploying the API alone leaves the Monitors on a stale credential and silently not consuming. **Solution is out of scope here** — see the visibility HLPS.
- **C-7 — The shared install root is deleted recursively.** `RemoveFolders` (`Product.wxs:81`) runs `util:RemoveFolderEx Property="INSTALLLOCATION" On="both"` and `RemoveFile Directory="INSTALLLOCATION" Name="*.*" On="both"`. `INSTALLLOCATION` is `ProgramFiles64\…\Deploy`, the parent of `Web\`, `Services\` and `Tools\`. Harmless in one transaction; post-split, whichever package owns it wipes the other three — and `On="both"` means on install as well as uninstall. The legacy product carries this component, so unordered legacy removal can delete packages already installed. **This is the largest single risk in the change.**
- **C-8 — Machine-scoped resources are not all refcountable.** Two distinct failure modes. `RegistryEntries` (`HKLM\…\DisableLoopbackCheck`, `Permanent="yes"`) is refcounted **only if the GUID is copied verbatim**; regenerating it per package is a component-rules violation. The certificates are worse: `DOrcWeb.wxs` and `RequestApi.wxs` both install `deploymentportal.pfx` into `localMachine\root` via `iis:Certificate`, which is a custom action and **not refcounted at all** — uninstalling either package removes the certificate the other's HTTPS binding depends on. Identical GUIDs do not help. The same question applies to the `util:User` `LogonAsService` grants on `[SERVICE.IDENTITY]` declared in both IIS components.
- **C-9 — `ProductVersion` binds to an API-only file.** `Dependencies.wxi` defines `ProductVersion="!(bind.FileVersion.DeployApiDll)"`, and `DeployApiDll` is minted by the API harvest transform. Three of the four packages have nothing to bind to and will not link. Today every component reports the API's version.
- **C-10 — Installs serialise per server, and order matters.** `_MSIExecute` is a machine-wide mutex: the four packages cannot install concurrently on one server. Independent does not mean simultaneous. Order is also load-bearing — Monitor configuration embeds `DEPLOYAPI.ENDPOINT` and the Monitor calls the API at runtime, so Monitors-before-API brings monitors up against a torn-down API.
- **C-11 — Local builds still run the `<Exec>` publish chains.** #807 gated them, not removed them. Four wixprojs carrying the fallback means four racing chains on a local `/m` build.
- **C-12 — IIS partitions cleanly.** Recorded as a constraint that *holds*: `Dorc Web`/pool `DOrcWeb` on 80/443 and `DOrcApi`/pool `DeployApi` on 8080/8443 are separate sites, separate pools, non-overlapping bindings, both `ConfigureIfExists="yes"`. Each package takes its `iis:` authoring wholesale. Only the certificates are shared (C-8).
- **C-13 — Component-versus-schema skew.** The dacpac and the single MSI ship in one window today. Four independently deployable installers newly permit "API upgraded, dacpac not" and the reverse. Distinct from C-1.

---

## 6. Proposed Solution Directions

### SD-1 — Four wixprojs, with every component assigned
Author four packages from the §2 table, assigning all 36 refs. The harvested groups move unchanged. The hand-authored components need individual decisions, and the two shared ones (`RemoveFolders`, `RegistryEntries`) need C-7 and C-8 resolved first. Addresses G-1, G-3.

### SD-2 — Ordered legacy removal, ahead of all four installs
Removing the monolith must be an explicitly ordered first step, not a `MajorUpgrade` safety net declared by an arbitrary package — because the monolith carries `RemoveFolders` (C-7). Note the repo has never used `MajorUpgrade`/`FindRelatedProducts`; upgrade today is uninstall-then-install by product *name*. Addresses G-4, C-4, C-7.

### SD-3 — A `.msi.json` sidecar per installer
Each declares its `ServerType`, `ProductNames` and `Parameters`. New sidecars UTF-8 (`Setup.Acceptance.msi.json` is UTF-16). Addresses G-2, C-2, C-5.

### SD-4 — Shared-resource ownership policy
For each machine-scoped resource: sole owner, or shared with GUID copied verbatim and never regenerated. Certificates need a sole owner or a small shared prerequisites package, since they cannot be refcounted. Addresses C-8.

### SD-5 — Per-package `ProductVersion`
Either a per-package bind target, or the `/p:Version` the pipelines already pass and the MSI currently ignores. The latter is simpler and makes the four versions independently meaningful. Addresses C-9.

### SD-6 — Conditional quiesce in `RunDeployment.ps1`
Make the stop/log-off prelude act only on the components being deployed, and add an explicit start-and-verify step. Nothing currently restarts the services — `ServiceInstall` is `Start="demand"` with a `ServiceControl` carrying no `Start` attribute. Without this, G-2 fails regardless of the MSI work. Addresses G-2.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | Version-skew policy | User | — | **RESOLVED.** Concurrent versions acceptable; visibility required. Moved to the visibility HLPS. |
| U-2 | Deployability or build time? | User | — | **RESOLVED.** Independent deployability is the objective. |
| U-3 | Migration for existing installs | User | — | **RESOLVED.** Uninstall-before-install; maintenance window acceptable. |
| U-4 | Owner of `Wix4User` / `Wix4FileShare` / `Wix4FileSharePermissions` | Agent | — | **RESOLVED** from `Product.wxs`. `ToolsShare` (`CLITools` share, `util:User Everyone`) → CLIs. `LogsShare` (`Log` share, `Everyone1`) → Monitors. The remaining `util:User` rows are the `[SERVICE.IDENTITY]` grants inside the two IIS components → UI and API (but see C-8). |
| U-5 | Property partitioning | Agent | — | **RESOLVED.** The sidecar `Parameters` map already does this. |
| U-7 | Attribution of hand-authored custom actions | Agent | — | **RESOLVED.** `SetCDrive` is not hand-authored — it is emitted by `<SetDirectory Id="CDrive" Value="C:\"/>` and its only consumer is `LogsShare` → Monitors. Only `ReconfigureLoadUserProfileOrchestrator` (UI) and `…RequestApi` (API) are hand-authored. |
| U-9 | `DeployMSI` across four products | User | — | **RESOLVED.** Takes `[String[]]$ProductNames` and iterates; already passes two names. One `Start-Job` per server. No module change needed. |
| U-11 | Do the four product names collide under the module's uninstall matching? If matching is `-like`/prefix-based, four names sharing a prefix could uninstall each other — including packages installed earlier in the same run. | User | **Blocking** for SD-2 | **Unresolved.** Depends on the matching semantics in `dorc-ps-deploy-module`. |
| U-12 | Who owns the certificates, given they cannot be refcounted (C-8)? Sole owner, duplicated-and-accept-breakage, or a shared prerequisites package? | User + Agent | **Blocking** for SD-1 | **Unresolved.** Affects package count — a prerequisites package would make it five, not four. |
| U-13 | Fixed or auto (`*`) `ProductCode`s for the four packages, and is `MajorUpgrade` introduced (a mechanism this product has never used)? | User | **Blocking** for SD-2 | **Unresolved.** |
| U-6 | Does `WixBuild` scale with component count or payload bytes? | Agent | Non-blocking | **Partially resolved.** Bytes, most likely — one embedded cabinet, one serial compression pass. G-3 should measure both. |
| U-14 | Do the four new wixprojs carry the `<Exec>` local fallback (C-11)? | Agent | Non-blocking | **Unresolved.** Omitting it means a local build produces no MSI without running the payload stage first. |

**Blocking: U-11, U-12, U-13.** All three are consequences of taking the installer authoring seriously rather than reasoning from the compiled MSI. Per `CLAUDE.md` the IS is not written until they close.

---

## 8. Out-of-Scope Risks

- **Rollback is no longer atomic.** Four operations can individually fail. `RunDeployment.ps1` iterates `MsiFileNames` with no `try/catch` and no per-MSI outcome capture, so a mid-run failure can leave a component **absent** (uninstall succeeded, install did not) while the run reports success. This deserves promotion into scope in the IS.
- **The real `DeploySettings.json` lives outside this repository.** A settings file still listing only `Setup.Dorc.msi` deploys nothing new, and does so **silently**.
- **Four `UpgradeCode`s to keep stable forever.** Regenerating one silently breaks upgrades for that component.
- **Further build gains require splitting `Tools`**, which is not a topology boundary and would pull the design away from the deployability goal.
- **`SuppressValidation=True`** is set in both configurations, and `Setup.Acceptance` already needs `SuppressIces=ICE03`. Each new package needs a deliberate decision, and ICE validation is exactly what would catch C-8-class component-rule errors.
