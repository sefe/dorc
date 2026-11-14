# Operator runbook — S-013 script migration and S-015 credential rotation

| Field | Value |
|---|---|
| **Status** | READY TO EXECUTE — S-013 unblocked; S-015 gated on released code steps and active S-013 withholding |
| **Executed by** | Operator. Neither step can be performed from a build. |
| **Reversibility** | S-013 is a script edit and revertible. S-015 is **not** — a rotated credential cannot be un-rotated. |

Both steps change live systems: one edits deployment scripts on the production share, the other
changes credentials the whole estate authenticates with. They are documented here rather than
automated because getting the *order* right matters more than the mechanics, and because a script
that reverts cleanly and a credential that does not need different care.

---

## Why the order matters

W-4 established that every secure configuration value reaching a runspace has been delivered in
cleartext to deployment scripts. By the principle that any credential that has been in a runspace
is disclosed, they all require rotation.

But rotation is gated on **two** code steps, not one:

- **S-014** removes the credentials from the published property bag.
- **S-004** removes the path by which arbitrary code reads them directly out of the Monitor
  process.

Rotating after only one re-discloses the fresh credential to the lowest-privileged user in the
system, immediately. Both are now released, so the gate is satisfied — **verify they are actually
deployed to the instance being rotated** before proceeding.

`S-000` (the interim `IsForProd` change, applied 2026-08-10) is a *containment*, not a rotation.
Record the rotation separately so the two are not confused later.

---

## S-013 — Migrate the credential-consuming deployment scripts

### What has to change

Three scripts consume a deployment password as an injected variable. They must obtain it another
way before the reserved-key list can be extended to cover `DORC_NonProdDeployPassword`.

A fourth line, in a recycling script, matched the scan with its key **truncated**. Classify it
before treating the migration set as final — it is either a fourth consumer or a false positive,
and the difference decides whether this step is complete.

### Find the current call sites

Run against both script folders — production and System Test. Read-only.

```powershell
# The four keys whose consumption gates the denylist.
$keys = @(
    'DORC_NonProdDeployPassword',
    'DORC_ProdDeployPassword',
    'DORC_WebDeployPassword',
    'DorcApiAccessPassword'
)

$roots = @(
    '\\itss.global\prod\GLO\APP\DORC\Scripts\Scripts.PR',
    '\\itss.global\prod\GLO\APP\DORC\Scripts\Scripts.ST'
)

foreach ($root in $roots) {
    Write-Host "=== $root ==="
    Get-ChildItem -Path $root -Recurse -Include *.ps1,*.psm1 -ErrorAction Stop |
        Select-String -Pattern ($keys -join '|') -ErrorAction Stop |
        Select-Object Path, LineNumber, Line
}
```

Do **not** substitute `-ErrorAction SilentlyContinue` or `Select-String -List`: inaccessible
directories or unreadable files must fail the scan visibly rather than omitting subtrees, and
every matching line in every file must be reported so a later unmigrated reference in an
already-matched script cannot hide behind the first match.

Also re-run the same `-ErrorAction Stop` scan (without `-List`) with a **truncated** pattern
(`DORC_NonProdDeployPassw`) to catch the fourth line, and with
`DeploymentServiceAccountPassword|ProgetAccountPassword|DorcCliSecret` to confirm the
script-facing set has not grown since the last scan.

### One finding to resolve before migrating

One of the three sits in the **production** script folder and consumes the **non-production**
password. That is either intentional or a latent defect, and it belongs to the script's owner to
determine. Migrating it without deciding would silently preserve whichever it is.

### Migration target

The credential-provider abstraction (S-020) is the intended destination. If this step must
precede it, use whatever supported retrieval mechanism the estate already has — the requirement
is only that the script stops reading the value out of the injected variable scope.

**Do not** substitute a hard-coded value or a second copy of the credential. That converts a
disclosure into two disclosures.

If migrating a script also changes its `Script` path or component definition through the DOrc API
rather than editing the `.ps1` file in place on the share, supply the current concurrency and
impact preconditions required by `DeploymentDefinitions`: every `PUT /RefDataScripts/edit` call
unconditionally requires `Revision`, `ConfirmImpact = true`, and the exact complete
`AffectedProjectIds` list (including when a `Script` row is used by only a single project), and
`PUT /RefData` requires the project's current `Revision`. If S-018 script content verification
has already been switched to `Enforce` mode on the instance, re-record the updated file's baseline
via `PUT /RefDataScripts/{id}/content-hash` before dispatching.

### Verification

- Each migrated script deploys successfully.
- Re-running the `-ErrorAction Stop` scans above (without `-List`) across both script roots
  completes with zero enumeration/read errors and confirms:
  - **Zero** remaining reads from the injected variable scope on any line of any file for the
    **four operator keys** (`DORC_ProdDeployPassword`, `DORC_NonProdDeployPassword` — including
    the truncated `DORC_NonProdDeployPassw` pattern — `DORC_WebDeployPassword`, and
    `DorcApiAccessPassword`).
  - Matches for the **three intentionally script-visible keys**
    (`DeploymentServiceAccountPassword`, `ProgetAccountPassword`, and `DorcCliSecret`) match their
    expected consumer inventory (~60 files / ~80 call sites, ~12 call sites, and ~2 call sites
    respectively) with no unexpected or unauthorized consumers.
- None of the migrated scripts reads `DORC_NonProdDeployPassword` (or any other operator key) from
  the injected variable scope.

### Then — extend `AppSettings:ConfigKeysWithheldFromScripts` and restart hosts

1. Extend `AppSettings:ConfigKeysWithheldFromScripts` on **both** the **DOrc API** and **DOrc
   Monitor** hosts to include `DORC_NonProdDeployPassword` (plus any other newly-freed operating
   keys), alongside the three built-in zero-consumer keys (`DORC_ProdDeployPassword`,
   `DORC_WebDeployPassword`, `DorcApiAccessPassword`).
2. Perform a **controlled restart** of both the **DOrc API** and **DOrc Monitor** services/hosts
   after draining or waiting for in-flight Monitor deployments to complete.
   `ScriptScopeConfigValues` snapshots the configured keys in its constructor and is registered as
   a singleton in both `Dorc.PersistentData/PersistentDataRegistry.cs` and
   `Dorc.Monitor/Registry/PersistentSourcesRegistry.cs`; editing configuration without restarting
   both hosts leaves the running policy unchanged, continues publishing the credential, and keeps
   the Monitor warning unchanged.
3. Run a verification deployment **before** starting S-015 rotation and confirm both that:
   - The Monitor's `"secure configuration value(s) are still published"` warning shrinks
     accordingly and no longer names `DORC_NonProdDeployPassword` (that warning is derived from
     the live estate rather than a list in code, so it is the authority on what remains).
   - `DORC_NonProdDeployPassword` is absent from the deployment's resolved properties.

---

## S-015 — Rotate the deployment credentials

### Scope — all seven secure keys, across every population

| Key | `ConfigValue` rows (`IsForProd`) | Population reached |
|---|---|---|
| `DORC_ProdDeployPassword` | Single row (`1` after S-000; was `NULL`) | every environment prior to S-000; withheld by S-014 default |
| `DORC_NonProdDeployPassword` | Single row (`NULL`) | every environment; **must** be migrated (S-013) and withheld before rotation |
| `DORC_WebDeployPassword` | Single row (`1` after S-000; was `NULL`) | every environment prior to S-000; withheld by S-014 default |
| `DorcApiAccessPassword` | Single row (`1` after S-000; was `NULL`) | every environment prior to S-000; withheld by S-014 default |
| `DorcCliSecret` | Two rows: `0` (non-prod) and `1` (prod) | its own environment population per `IsForProd` row (~2 script call sites) |
| `DeploymentServiceAccountPassword` | Two rows: `0` (non-prod) and `1` (prod) | its own environment population per `IsForProd` row (~60 files / ~80 call sites) |
| `ProgetAccountPassword` | Two rows: `0` (non-prod) and `1` (prod) | its own environment population per `IsForProd` row (~12 script call sites) |

All **seven** secure keys identified in the `HLPS-deployment-privilege-containment.md` estate
inventory (`Secure = 1`, ten `ConfigValue` rows across the four single-row operator keys and the
three `IsForProd = 0` / `1` pairs) have reached deployment runspaces in cleartext and must be
rotated. Earlier drafts scoped this to two keys or omitted `ProgetAccountPassword`, which
contradicted the plan's principle and left a disclosed credential unchanged. The usernames
(`DORC_*DeployUsername`) are script-visible identities by design and are **not** credentials to
rotate.

### Preconditions — check each, do not assume

1. **S-004 is deployed** to the instance being rotated (expression evaluation contained by
   provenance).
2. **S-014 is deployed** to the same instance (the three zero-consumer keys withheld by default).
3. **S-013 migration and verified withholding are complete before rotating
   `DORC_NonProdDeployPassword`** (or any other consumer-bearing operating key). This is
   **mandatory**, not conditional on operator intent: S-014's built-in withheld set covers only
   `DORC_ProdDeployPassword`, `DORC_WebDeployPassword`, and `DorcApiAccessPassword`. Before
   rotating `DORC_NonProdDeployPassword`, all script consumers must be migrated, the key must be
   added to `AppSettings:ConfigKeysWithheldFromScripts` on both DOrc API and DOrc Monitor hosts,
   both hosts must undergo a controlled restart, and a verification deployment must confirm the
   Monitor's `"secure configuration value(s) are still published"` warning no longer lists the key
   and the key is absent from resolved properties — otherwise the verification deployment
   immediately discloses the freshly rotated password again. *(The three deliberately
   script-facing keys — `DorcCliSecret`, `DeploymentServiceAccountPassword`, and
   `ProgetAccountPassword` — remain published to their respective `IsForProd` populations by
   design until S-020 lands; rotating them here invalidates prior disclosure and they will be
   rotated again under S-020.)*
4. A **rollback plan for the accounts and secrets themselves** exists — this step is not
   revertible from DOrc's side. If a rotated account breaks a target server's access control list
   or downstream service authentication, the fix is on the target or identity store, not here.

### Sequence

Repeat the sequence below **per distinct account/secret and `(Key, IsForProd)` population** — not
merely once per key name. For `DorcCliSecret`, `DeploymentServiceAccountPassword`, and
`ProgetAccountPassword`, `IsForProd = 0` (non-production) and `IsForProd = 1` (production) are
separate `ConfigValue` rows and must each be rotated, updated, verified, and recorded
individually (if two `ConfigValue` rows share the same underlying directory/service account,
pause and drain consumers across both populations in step 1 and update **every** corresponding
`ConfigValue` row in step 3 before running verification).

1. **Pause new deployment work and drain affected in-flight consumers** before rotating the
   account. Rotating while deployments are active can cause authentication failures (and directory
   account lockouts): in-flight requests retain script-facing secrets already captured by
   `PendingRequestProcessor.SetUpConfigValuesAsProperties`, and any request or component dispatch
   starting between target rotation (step 2) and the DOrc `ConfigValue` update (step 3) receives
   the old value. (The earlier drain in S-013 covers only the host restart, not each rotation.)
   Keep ordinary deployment work paused until all corresponding `ConfigValue` rows for that
   account are updated and the controlled verification deployment(s) in step 4 succeed.
2. Rotate the target account password or client secret in the directory / identity store.
3. Update every corresponding `ConfigValue` row (`IsForProd = 0`, `IsForProd = 1`, or the single
   row for the four operator keys) through the DOrc UI or API so it is re-encrypted with the
   current key. **Do not** write to `deploy.ConfigValue` directly — the value is encrypted at rest
   and a direct write stores plaintext — and **do not** insert a second `IsForProd` row for any of
   the four single-row operator keys (`ConfigValuesPersistentSource.GetConfigValue` queries by
   `Key` with `SingleOrDefault`).
4. Run the controlled verification deployment(s) for the rotated population before resuming
   ordinary deployment work or proceeding to the next population:
   - For a **single-row operator key** (`DORC_ProdDeployPassword`, `DORC_NonProdDeployPassword`,
     `DORC_WebDeployPassword`, `DorcApiAccessPassword`): deploy a component exercising that
     credential path to a **non-production** environment and to a **production** environment,
     confirm both succeed, and confirm the key is absent from resolved deployment properties.
   - For a **split population** (`DorcCliSecret`, `DeploymentServiceAccountPassword`,
     `ProgetAccountPassword`): deploy a component that consumes that key to an environment in the
     matching class (`IsForProd = 0` → non-production; `IsForProd = 1` → production) and confirm
     success. If both `0` and `1` rows shared one underlying account and were updated together,
     verify **both** a consuming non-production deployment and a consuming production deployment.
5. Resume ordinary deployment work for the verified population, record the completed
   `(Key, IsForProd)` population in the execution table below, then repeat from step 1 for the
   next population.

Rotate one credential population at a time and verify between each. Rotating all seven keys (ten
`ConfigValue` rows) together makes a failure impossible to attribute.

### Note on rotating twice

S-020 migrates credential storage to a provider-backed store (including replacing script-injected
delivery for `DorcCliSecret`, `DeploymentServiceAccountPassword`, and `ProgetAccountPassword`).
Rotating now means rotating again when that lands. That is expected and is the correct order —
waiting for S-020 leaves disclosed credentials live across all seven keys for the duration.

### Verification

- Deployments exercising each rotated key succeed against every environment class (`IsForProd = 0`
  non-production and `IsForProd = 1` production).
- Every `ConfigValue` row across all seven secure keys holds a freshly rotated value; no row or
  component holds a previous value.
- All four operator keys (`DORC_ProdDeployPassword`, `DORC_NonProdDeployPassword`,
  `DORC_WebDeployPassword`, `DorcApiAccessPassword`) are absent from resolved deployment
  properties.
- Each population's rotation is recorded below, with dates, so it is not confused with S-000's
  containment.

---

## Record of execution

| Step | Key / script (`IsForProd` population) | Date | By | Verified |
|---|---|---|---|---|
| S-013 | Script consumer 1: | | | |
| S-013 | Script consumer 2: | | | |
| S-013 | Script consumer 3: | | | |
| S-013 | Fourth (truncated) line — classified as: | | | |
| S-013 / S-014 | `AppSettings:ConfigKeysWithheldFromScripts` updated with `DORC_NonProdDeployPassword`; controlled restart of DOrc API + DOrc Monitor; Monitor warning & property withholding verified | | | |
| S-015 | `DORC_ProdDeployPassword` (single row; `IsForProd = 1` post-S-000) | | | |
| S-015 | `DORC_NonProdDeployPassword` (single row; `IsForProd = NULL` — after S-013 withholding verified) | | | |
| S-015 | `DORC_WebDeployPassword` (single row; `IsForProd = 1` post-S-000) | | | |
| S-015 | `DorcApiAccessPassword` (single row; `IsForProd = 1` post-S-000) | | | |
| S-015 | `DorcCliSecret` (`IsForProd = 0` — non-production) | | | |
| S-015 | `DorcCliSecret` (`IsForProd = 1` — production) | | | |
| S-015 | `DeploymentServiceAccountPassword` (`IsForProd = 0` — non-production) | | | |
| S-015 | `DeploymentServiceAccountPassword` (`IsForProd = 1` — production) | | | |
| S-015 | `ProgetAccountPassword` (`IsForProd = 0` — non-production) | | | |
| S-015 | `ProgetAccountPassword` (`IsForProd = 1` — production) | | | |
