# SPEC-S-000: Interim Exposure Containment via `IsForProd`

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | APPLIED IN PRODUCTION 2026-08-10 — verification outstanding  |
| **Step**    | S-000 (operational — data change, no code)                   |
| **Author**  | Agent                                                        |
| **Date**    | 2026-08-10                                                   |
| **IS**      | IS-deployment-privilege-containment.md (DRAFT)               |
| **HLPS**    | HLPS-deployment-privilege-containment.md (APPROVED)          |
| **Addresses** | SD-0, W-4                                                  |
| **Folder**  | docs/deployment-privilege-containment/                       |
| **Governing constraints** | C-02 (incremental and reversible, no flag day), C-03 (no schema change) |

---

## 0. Applied — record

Applied to the **production** instance (`DeploymentOrchestrator`) on 2026-08-10 via the admin UI rather than `S-000-apply.sql`. Verified: three rows, three distinct keys, all `IsForProd = 1`, no duplicates.

| Id | Key | Prior | Now |
|----|-----|-------|-----|
| 9  | `DORC_ProdDeployPassword` | NULL | 1 |
| 13 | `DORC_WebDeployPassword` | NULL | 1 |
| 16 | `DorcApiAccessPassword` | NULL | 1 |

**These Ids are what `S-000-rollback.sql` requires.**

The UI's edit path was the safe one — it updates `IsForProd` on the existing row. The *add* path would not have been: it only rejects a duplicate when the normalised flags match, so `NULL` versus `1` does not collide and a second row would have been created. That remains the standing hazard in the header warning.

**Outstanding, because the UI route bypassed them:**

- **The token-consumer check (R1 precondition, `S-000-apply.sql` STEP 1c) was not run.** It is now detection rather than prevention: a property or config value containing `$KeyName$` resolves to the literal string and the failure is swallowed, so the deployment succeeds having written garbage where a password belonged. AC-2 cannot detect this class. Run it.
- **Functional verification (AC-2, AC-3) not yet performed.**
- **System Test not yet changed.** R1 calls for both instances.

---

## 1. Context

W-4 is a confirmed live production disclosure. `SetUpConfigValuesAsProperties` decrypts every secure configuration value and publishes it into the deployment's variable scope, filtered only by `IsForProd`. A value with `IsForProd = NULL` short-circuits that filter and reaches **every** deployment.

The production instance carries `IsForProd = NULL` on four secret keys. Three of them have **zero consumers** anywhere in the production script share (U-12). The production estate holds 1,285 environments: 1,083 non-production, 202 production.

Consequence: the production deployment credential is presently readable, as an ordinary PowerShell variable, by any script running in any of the 1,083 non-production environments.

This step does not fix that. S-014 does. This step reduces the exposed population by roughly 84% using a reversible data change that ships today, because the real remedy cannot begin until S-004 and S-014 have both landed.

**The change is a correction toward the estate's own convention, not a new restriction.** Three of the seven secure keys — `DeploymentServiceAccountPassword`, `DorcCliSecret`, `ProgetAccountPassword` — already carry properly split `0`/`1` pairs. The NULL rows are the exceptions.

---

## 2. Requirements

### R1 — Scope of the change

Set `IsForProd = 1` on exactly these rows in `deploy.ConfigValue`:

| Key | Current | Target | Consumers in script estate |
|-----|---------|--------|---------------------------|
| `DORC_ProdDeployPassword` | NULL | 1 | none |
| `DORC_WebDeployPassword` | NULL | 1 | none |
| `DorcApiAccessPassword` | NULL | 1 | none |

Apply to **both** the production instance and System Test, which carry identical configuration.

### R2 — Rows that must NOT change

- **`DORC_NonProdDeployPassword`** stays `NULL`. Scoping it to non-production is correct in principle but alters behaviour for *production* deployments, and three scripts consume it (S-013). That is S-014's business, not an interim step's.
- **The three username keys** (`DORC_ProdDeployUsername`, `DORC_NonProdDeployUsername`, `DORC_WebDeployUsername`) stay as they are. They are deliberately script-visible; several basebuild scripts require them to grant logon rights.
- **`DeploymentServiceAccountPassword`, `DorcCliSecret`, `ProgetAccountPassword`** are untouched — already correctly split, and heavily consumed by design.

### R3 — Reversibility

The prior value of each row is recorded before the change so it can be restored by a single statement per row. No schema change, no code change, no deployment.

### R4 — Recording

The change is recorded — date, operator, rows affected, prior values — so that S-015's rotation is not later confused with it, and so the exposure window can be bounded during any subsequent incident review.

### R5 — Sequencing relative to rotation

This step does **not** substitute for rotation, and must not be reported as having contained the incident. Any credential that has been published into a runspace is disclosed; S-015 rotates it, gated on S-004 and S-014. S-000 limits ongoing exposure while that work proceeds.

---

## 3. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | The three named rows carry `IsForProd = 1` in both instances. |
| AC-2 | A deployment to a **non-production** environment completes successfully after the change. This is the primary regression check. |
| AC-3 | The three values are absent from the resolved variable scope of a non-production deployment, verified by inspecting a deployment's resolved properties rather than by inference from the config table. |
| AC-4 | A deployment to a **production** environment completes successfully and still receives the values. |
| AC-5 | `DORC_NonProdDeployPassword` and all three username keys are unchanged. |
| AC-5a | The three script-facing secure keys — `DeploymentServiceAccountPassword`, `DorcCliSecret`, `ProgetAccountPassword` — are unchanged, retaining their existing `0`/`1` pairs. R2 forbids touching them and they are the rows most easily caught by a careless widening of the predicate. |
| AC-6 | Prior values are recorded and a tested rollback statement exists for each row. |

---

## 4. Verification Approach

**Order matters.** Apply and verify in System Test first, then production, even though the change is identical — ST is the lower-consequence rehearsal for a change whose only real risk is an unmodelled consumer.

1. Record current values for the three rows in both instances.
2. Apply to System Test.
3. Run one non-production deployment that exercises a basebuild script (these are the heaviest config-value consumers) and confirm success — AC-2.
4. Inspect the resolved property set for that deployment and confirm the three keys are absent — AC-3.
5. Run one production-tier deployment in ST and confirm the values are still delivered — AC-4.
6. Repeat 2–5 in production.

If any deployment fails in a way that implicates a missing variable, revert the row and record what consumed it — that is a finding for S-014's classification, not a reason to abandon the step.

---

## 5. Accepted Risks

- **An unmodelled consumer outside the scanned share.** U-12 scanned `*.ps1`, `*.psm1` and `*.psd1` beneath the configured script root. A consumer in another file type, in a script invoked from outside DOrc, or in compiled code would not have been seen. The risk is low — these three keys had zero hits of any kind — and it is bounded by R3's reversibility. Mitigated by AC-2's regression check rather than by further scanning.
- **This step creates a false sense of containment if reported carelessly.** The production credential remains readable by every production deployment, and remains disclosed historically. R5 exists to make that explicit in whatever record accompanies the change.

---

## 6. Out of Scope

- Rotation of any credential — S-015.
- Removing these values from the property bag entirely — S-014.
- `DORC_NonProdDeployPassword`'s scoping — S-014, after S-013's migration.
- Any change to how `IsForProd` is interpreted in code. This step works within the existing semantics deliberately: changing the filter's behaviour is a code change with its own risk, and the point of S-000 is to buy time without one.
