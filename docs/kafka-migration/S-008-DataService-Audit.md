# S-008 — DataService Audit Deliverable

> **Verdict: Empty surface — S-008 closes as a no-op.**
>
> No `*DataService*` Rabbit pub/sub surface exists in the DOrc codebase at the audited commit. S-009's Rabbit-removal grep-target list does not need to add DataService entries.

| Field | Value |
|---|---|
| **Audit run by** | Claude (Opus 4.6) |
| **Audit date** | 2026-04-14 |
| **Commit SHA at audit time** | `4afcc7514fb7b3567ffcc330a02b6f84354a8017` |
| **Branch** | `feat/kafka-migration` |
| **Governing spec** | `SPEC-S-008-DataService-Audit.md` (APPROVED, user-approved 2026-04-14) |
| **Tooling** | `rg`/`grep -i` over the working tree at the commit above |

---

## 1. Source pattern grep across `src/`

All patterns case-insensitive (`-i`).

| Pattern | Command | Raw match count | Disposition |
|---|---|---|---|
| `RabbitDataService` | `rg -in 'RabbitDataService' src/` | **0** | n/a |
| `DataService.*Queue` | `rg -in 'DataService.*Queue' src/` | **0** | n/a |
| `DataService.*Subscribe` | `rg -in 'DataService.*Subscribe' src/` | **0** | n/a |
| `DataService.*Publish` | `rg -in 'DataService.*Publish' src/` | **0** | n/a |
| `data-service.*amqp` | `rg -in 'data-service.*amqp' src/` | **0** | n/a |
| `IDataService.*(Publish\|Subscribe\|Consume)` | `rg -in 'IDataService.*(Publish\|Subscribe\|Consume)' src/` | **0** | n/a |

All six patterns return zero matches. No DataService pub/sub surface in source.

---

## 2. Project-name scan

| Probe | Command | Raw match count | Disposition |
|---|---|---|---|
| Directory under `src/` named `*DataService*` | `find src/ -maxdepth 2 -type d -iname '*DataService*'` | **0** | n/a |
| `*DataService*.csproj` anywhere in `src/` | `find src/ -iname '*DataService*.csproj'` | **0** | n/a |
| `Dorc.sln` entries mentioning `DataService` | `grep -i 'DataService' src/Dorc.sln` | **0** | n/a |

No project, csproj, or solution entry references `DataService`.

---

## 3. Configuration sweep

| Probe | Command | Raw match count | Disposition |
|---|---|---|---|
| `appsettings*.json` (any nested) referencing `DataService` | `rg -in 'DataService' --glob '**/appsettings*.json'` | **0** | n/a |
| `appsettings*.json` referencing `data-service` | `rg -in 'data-service' --glob '**/appsettings*.json'` | **0** | n/a |
| `install-scripts/` referencing `DataService` | `rg -in 'DataService' src/install-scripts/` | **0** | n/a |
| Helm chart `values.yaml` (search broader DOrc tree) | none present in the repo at audit time | **n/a** | Helm not used in this repo. |

No configuration carries a DataService queue/exchange/routing-key reference.

---

## 4. Reflection / DI sweep

### 4.1 `Activator.CreateInstance` / `Type.GetType`

Single match found:

| File:Line | Match | Disposition |
|---|---|---|
| `src/Dorc.ApiModel/MonitorRunnerApi/VariableValueJsonConverter.cs:24` | `var type = Type.GetType(serialSimple.FullTypeName);` | **Out-of-scope — naming coincidence.** This is a JSON converter that resolves runtime variable-value types for monitor↔runner serialisation. Nothing to do with DataService or RabbitMQ pub/sub. |

### 4.2 DI registrations (`AddTransient` / `AddScoped` / `AddSingleton` — generic + open-generic forms)

A broad sweep across `src/` for any registration whose target type-name includes `DataService` returned **0 matches**. (Verified by `grep -rn -i "DataService" src/ --include="*.cs"` and inspecting every result; the only `DataService`-like names found were in `RefDataServices` Acceptance-test artefacts — see §4.3.)

### 4.3 Assembly-scan / convention-based registrations (Scrutor / Lamar scanner / MEF)

Search for `Scrutor`, `Lamar` scanner import, `MEF`, `AddRegistry`, etc. across `src/`: no imports or scan-based registrations relating to `DataService` were found.

### 4.4 Catch-all DataService substring

For completeness, a fully unfiltered case-insensitive search for `DataService` substring across all `*.cs` files in `src/`:

| File pattern | Match family | Disposition |
|---|---|---|
| `src/Tests.Acceptance/Features/RefDataServices.feature.cs`, `src/Tests.Acceptance/StepDefinitions/RefDataServicesSteps.cs` | `RefDataServices` Acceptance-test specifications + step definitions for the `/RefDataServices` REST endpoint | **Out-of-scope — naming coincidence.** `RefDataServices` is a REST API endpoint serving reference-data lookups; not RabbitMQ pub/sub. |

No other `DataService`-substring matches in source.

---

## 5. IS / SC-1 alignment

- **IS R3 §6 narrative claim** ("no `RabbitDataService` pub/sub implementation exists in the codebase") — **CONFIRMED.**
- **S-009 Rabbit-removal grep-target list** — **NO CHANGE NEEDED.** The narrative `Tools.RabbitMqOAuthTest` + `RabbitMqDistributedLockService` enumeration in IS R3 §3 S-009 stands; the SC-1 grep suite (`RabbitMQ.*`, `EasyNetQ`, `amqp://`, etc.) remains the long-lived guard against any unforeseen Rabbit pub/sub regression.

---

## 6. Verdict

**Empty surface — S-008 closes as a no-op.**

Per spec AT-3: no escalation needed. The R-4(a) descope-gate dated 2026-10-15 is logically dormant for S-008 (no scope to defer).

---

## 7. Re-run instructions

A future reviewer can reproduce this audit at any subsequent commit by running, from the repo root:

```sh
# §1 source patterns
for pat in 'RabbitDataService' 'DataService.*Queue' 'DataService.*Subscribe' \
           'DataService.*Publish' 'data-service.*amqp' \
           'IDataService.*(Publish|Subscribe|Consume)'; do
  echo "=== $pat ==="
  rg -in "$pat" src/
done

# §2 project/sln scan
find src/ -maxdepth 2 -type d -iname '*DataService*'
find src/ -iname '*DataService*.csproj'
grep -i 'DataService' src/Dorc.sln

# §3 config sweep
rg -in 'DataService|data-service' --glob '**/appsettings*.json'
rg -in 'DataService' src/install-scripts/

# §4 reflection / DI
rg -in 'Activator\.CreateInstance|Type\.GetType' src/
rg -rn -i 'DataService' src/ --include="*.cs"

# Compare commit
git rev-parse HEAD
```

Any new in-scope-pub/sub match on a re-run = surface discovered, escalate to user per spec AT-3.
