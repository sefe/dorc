# Operator runbook — S-013 script migration and S-015 credential rotation

| Field | Value |
|---|---|
| **Status** | READY TO EXECUTE — S-013 unblocked; S-015 preconditions met once the code steps are released |
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
    Get-ChildItem -Path $root -Recurse -Include *.ps1,*.psm1 -ErrorAction SilentlyContinue |
        Select-String -Pattern ($keys -join '|') -List |
        Select-Object Path, LineNumber, Line
}
```

Also re-run it with a **truncated** pattern (`DORC_NonProdDeployPassw`) to catch the fourth line,
and with `DeploymentServiceAccountPassword|ProgetAccountPassword|DorcCliSecret` to confirm the
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

### Verification

- Each migrated script deploys successfully.
- A re-run of the scan above, with the widened key set, returns **only** the migrated call sites.
- No migrated script reads any credential from the injected variable scope.

### Then

Extend `AppSettings:ConfigKeysWithheldFromScripts` to include the newly-freed keys, and confirm
the Monitor's "secure configuration value(s) are still published" warning shrinks accordingly.
That warning is the backlog measure — it is derived from the estate rather than from a list in
code, so it is the authority on what remains.

---

## S-015 — Rotate the deployment credentials

### Scope — all of these, not just the two deployment passwords

| Key | Population reached |
|---|---|
| `DORC_ProdDeployPassword` | every environment (was `IsForProd = NULL`) |
| `DORC_NonProdDeployPassword` | every environment (`IsForProd = NULL`) |
| `DORC_WebDeployPassword` | every environment (`IsForProd = NULL`) |
| `DorcApiAccessPassword` | every environment (`IsForProd = NULL`) |
| `DorcCliSecret` | its own population (correctly split 0/1) |
| `DeploymentServiceAccountPassword` | its own population (correctly split 0/1) |

An earlier revision of the plan scoped this to two keys, which contradicted the plan's own
principle. The usernames are script-visible by design and are **not** credentials to rotate.

### Preconditions — check each, do not assume

1. **S-004 is deployed** to the instance being rotated (expression evaluation contained by
   provenance).
2. **S-014 is deployed** to the same instance (the three zero-consumer keys withheld).
3. **S-013 is complete** for any key you intend to withhold as part of this exercise.
4. A **rollback plan for the accounts themselves** exists — this step is not revertible from
   DOrc's side. If a rotated account breaks a target server's access control list, the fix is on
   the target, not here.

### Sequence

1. For `DORC_ProdDeployPassword` and `DORC_NonProdDeployPassword`, check
   `AppSettings:DeploymentCredentialsFromVault` on the instance being rotated. Record which
   credential source is active before changing the account.
2. Rotate the account password in the directory.
3. Update the active credential store:
   - when `DeploymentCredentialsFromVault` is `true`, update the 1Password item named by
     `ProdDeployPasswordItemId` or `NonProdDeployPasswordItemId`, then read it through the
     configured Connect service to confirm the new value is available;
   - otherwise, update the corresponding `ConfigValue` through the DOrc UI or API so it is
     re-encrypted with the current key;
   - the other four keys in this runbook remain `ConfigValue` entries and are updated through
     the DOrc UI or API.

   **Do not** write to `deploy.ConfigValue` directly — the value is encrypted at rest and a
   direct write stores plaintext. Do not update a deployment password in `ConfigValue` when the
   vault source is active; that changes an inactive copy and leaves deployments using the stale
   vault value.
4. Deploy one component to a **non-production** environment and confirm success.
5. Deploy one component to a **production** environment and confirm success.
6. Repeat per key.

Rotate one key at a time and verify between each. Rotating all six together makes a failure
impossible to attribute.

### Note on rotating twice

S-020 migrates credential storage to a provider-backed store. Rotating now means rotating again
when that lands. That is expected and is the correct order — waiting for S-020 leaves a disclosed
credential live for the duration.

### Verification

- Deployments succeed against every environment class after rotation.
- No component holds the previous value.
- The rotation is recorded here, with dates, so it is not confused with S-000's containment.

---

## Record of execution

| Step | Key / script | Date | By | Verified |
|---|---|---|---|---|
| S-013 | | | | |
| S-013 | | | | |
| S-013 | | | | |
| S-013 | fourth (truncated) line — classified as: | | | |
| S-015 | `DORC_ProdDeployPassword` | | | |
| S-015 | `DORC_NonProdDeployPassword` | | | |
| S-015 | `DORC_WebDeployPassword` | | | |
| S-015 | `DorcApiAccessPassword` | | | |
| S-015 | `DorcCliSecret` | | | |
| S-015 | `DeploymentServiceAccountPassword` | | | |
