# S-008 DataService Pub/Sub — Audit Findings

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Date** | 2026-04-15 |
| **Branch / commit** | `feat/kafka-migration` @ `c2912cd3` |
| **Verdict** | **Empty surface — S-008 closes as a no-op.** |
| **Governing** | `SPEC-S-008-DataService-Audit.md` (APPROVED user-approved 2026-04-14) |

---

## 1. Verdict

The original HLPS R1 draft listed a `RabbitDataService` pub/sub flow as a
migration target. The IS R3 Implementation-Discovery revision narrative
(IS §6) revised that to "likely absent" pending S-008 confirmation.
**This audit confirms the absence.** S-008 closes as a no-op delivering
only this note. The R-4(a) descope gate for S-008 is therefore never
triggered and elapses clean on 2026-10-15.

---

## 2. Reproducible audit

All commands were executed with `rg` (ripgrep) from the repo root
against `src/`. Re-run them at the same commit (`c2912cd3`) to reproduce
the same counts.

### 2.1 Source greps (R-1 pattern set, all case-insensitive)

| # | Pattern | Command | Hits | Disposition |
|---|---|---|---|---|
| 1 | `RabbitDataService` | `rg -i "RabbitDataService" src/` | **0** | Empty — no class, no usage. |
| 2 | `class .*Rabbit.*DataService` | `rg -i "class\s+.*Rabbit.*DataService" src/` | **0** | Empty — no naming variant exists. |
| 3 | `DataService.*Queue` | `rg -i "DataService.*Queue" src/` | **0** | Empty. |
| 4 | `DataService.*Subscribe` | `rg -i "DataService.*Subscribe" src/` | **0** | Empty. |
| 5 | `DataService.*Publish` | `rg -i "DataService.*Publish" src/` | **0** | Empty. |
| 6 | `data-service.*amqp` | `rg -i "data-service.*amqp" src/` | **0** | Empty. |
| 7 | `IDataService.*(Publish\|Subscribe\|Consume)` | `rg -i "IDataService.*(Publish\|Subscribe\|Consume)" src/` | **0** | Empty. |
| 8 | `dorc\.data` (queue/exchange names) | `rg "dorc\.data" src/` | **0** | Empty — no DataService topic / queue topology. |

### 2.2 Project / solution-name scan

| # | Scan | Hits | Disposition |
|---|---|---|---|
| P1 | `src/` directories whose name contains `DataService` | **0** | Empty — no project folder named `*DataService*`. |
| P2 | `*.csproj` whose filename contains `DataService` | **0** | Empty. |
| P3 | `Dorc.sln` entries containing `DataService` | **0** | Empty (verified by `rg -i "DataService" src/Dorc.sln`). |

### 2.3 Configuration sweep

| # | Pattern | Hits | Disposition |
|---|---|---|---|
| C1 | `appsettings*.json` keys/values mentioning `DataService` / `data-service` | **0** | Empty. |
| C2 | `install-scripts/` config files mentioning DataService surfaces | **0** | Empty. |
| C3 | Helm chart `values.yaml` files | n/a | DOrc has no Helm chart in-tree. |

### 2.4 Reflection / DI sweep

Searched DI registration shapes (`AddTransient` / `AddScoped` /
`AddSingleton` — both generic-method and `typeof(...)` open-generic
forms; assembly-scan / convention-based registration via Scrutor / Lamar
scanner / MEF) for type-names containing `DataService`. Plus
`Activator.CreateInstance` and `Type.GetType` calls referencing
`DataService`.

| # | Pattern | Hits | Disposition |
|---|---|---|---|
| D1 | DI registration of `*DataService*` type-name | **0** | Empty. |
| D2 | `Activator.CreateInstance` referencing `DataService` | **0** | Empty. |
| D3 | `Type.GetType` referencing `DataService` | **0** | Empty. |

### 2.5 Naming-coincidence dispositions

The repo-wide grep `rg -i "DataService" src/` (broader than the R-1
pattern set, used as a safety net per spec R-1's "non-exhaustive"
clause) returned 3 hits, all in test-acceptance fixtures and unrelated
to pub/sub:

| File:line | Match context | Disposition |
|---|---|---|
| `src/Tests.Acceptance/StepDefinitions/RefDataServicesSteps.cs` | SpecFlow steps for **reference data services** (RBAC reference data, etc.) | **out-of-scope-naming-coincidence** — REST-only test fixtures. |
| `src/Tests.Acceptance/Support/Endpoints.cs` | URL routing for the same `RefDataServices` HTTP controllers | **out-of-scope-naming-coincidence** — pure HTTP, no pub/sub. |
| `src/Tests.Acceptance/Features/RefDataServices.feature` | BDD feature file for the same | **out-of-scope-naming-coincidence**. |

No other `*DataService*` token exists anywhere in `src/`.

---

## 3. Remaining RabbitMQ surfaces (full enumeration, S-009 ownership)

The following files still carry `RabbitMQ.*` references. **None** are in
S-008's scope; they are listed so a future reader can confirm S-008's
no-op verdict does not orphan a real surface.

| File | Owner step | Disposition |
|---|---|---|
| `src/Dorc.Monitor/HighAvailability/RabbitMqDistributedLockService.cs` | S-005b (substrate flag flip) → S-009 (deletion) | Disabled at runtime by `Kafka:Substrate:DistributedLock = Kafka` (already implemented); deleted in S-009 with the substrate-selector flag. |
| `src/Dorc.Monitor.Tests/DistributedLockServiceTests.cs` | S-009 | Test fixture for the Rabbit lock impl; deleted with the production class. |
| `src/Dorc.Monitor.IntegrationTests/HighAvailability/RabbitMqLockIntegrationTests.cs` | S-009 | Same. |
| `src/Tools.RabbitMqOAuthTest/Tools.RabbitMqOAuthTest.csproj` | S-009 | Standalone diagnostic tool; deleted in S-009. |
| `src/Tools.RabbitMqOAuthTest/Program.cs` | S-009 | Same. |
| `src/Dorc.Monitor/Dorc.Monitor.csproj` (PackageReference RabbitMQ.Client) | S-009 | Removed when `RabbitMqDistributedLockService` is deleted. |

---

## 4. AT-2 / R-3 disposition consistency

Every match across §2.1–§2.5 is dispositioned either *empty* (zero
hits) or *out-of-scope-naming-coincidence* (the three RefDataServices
test fixtures). **Zero matches are dispositioned in-scope-pub/sub**, so
the AT-2 invariant ("Empty surface verdict requires no in-scope match
left unaddressed") holds.

---

## 5. AT-3 — User escalation

Verdict is *Empty surface* → AT-3 is **auto-satisfied** (no user
escalation required). No port spec authoring is initiated. The
2026-10-15 R-4(a) descope gate for S-008 elapses clean.

---

## 6. AT-4 — IS / SC-1 alignment

- **IS R3 §6 narrative claim:** "No `RabbitDataService` implementation
  exists in the codebase — zero RabbitDataService.cs / equivalent
  surface; no DataService queue names in config; S-008's target is
  absent." → **Confirmed.**
- **S-009 SC-1 grep-target list:** **No additions required from S-008.**
  S-009's authoritative removal list (per IS §3 S-009) covers only the
  surfaces enumerated in §3 above.

---

## 7. Reviewer checklist

- [x] Every pattern from spec R-1 enumerated with the exact command and
      hit count.
- [x] Naming-coincidence safety-net grep performed; each hit
      dispositioned with one-line justification (§2.5).
- [x] Verdict matches the dispositions (AT-2).
- [x] User escalation handled per AT-3 (auto-satisfied — empty verdict).
- [x] IS / SC-1 cross-reference recorded (AT-4).
- [x] Commit SHA recorded so re-runs can be compared.
- [x] No code changes accompany this commit.
