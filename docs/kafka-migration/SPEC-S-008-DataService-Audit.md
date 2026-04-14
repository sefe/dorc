# JIT Spec — S-008: DataService Audit (Likely No-Op Confirmation)

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-008 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-008 (APPROVED R3) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.2, SC-3, R-4(a) (APPROVED R3) |

---

## 1. Purpose & Scope

Confirm or refute the IS R3 Implementation-Discovery finding that **no `RabbitDataService` pub/sub surface exists in the current DOrc codebase**. The IS already records the absence as discovered at IS-R3 authoring time; S-008's job is to **re-verify the absence at S-008 entry** with a reproducible audit, document the result, and either close the step as a no-op or — if a hidden surface is found — author a port-it spec at that point.

Per IS R3 the scope is intentionally narrow:

- If the audit confirms the absence → produce a short audit note under `docs/kafka-migration/`; close as a no-op; no code changes; HLPS R-4(a) descope lever for S-008 never fires (it has no scope to defer).
- If the audit finds a forgotten surface → record it, escalate to the user, and author a separate S-008-port JIT spec at that point. The 2026-10-15 R-4(a) gate continues to apply to that scope.

### In scope

- A reproducible audit script / checklist that anyone can re-run from the repo root.
- The audit deliverable: `docs/kafka-migration/S-008-DataService-Audit.md` capturing the exact patterns checked, the paths inspected, the raw match counts, and the verdict.
- A user-facing escalation if a surface is discovered (the spec authoring for that case is itself out of scope until the discovery is made).

### Out of scope

- Porting any DataService surface — only authored if the audit finds one; that work is a separate JIT spec.
- Removing any putative dead `*DataService*` types — that's S-009 territory if relevant.
- Updating SC-1's grep-target list — already narrowed at IS R3.

---

## 2. Requirements

### R-1 — Audit pattern coverage

The audit covers, at minimum, the patterns the IS R3 §3 S-008 step-entry audit enumerates:

- Source grep across `src/` for the literal patterns (**all case-insensitive** — pass `-i` to `rg`/`grep`):
  - `RabbitDataService`
  - `DataService.*Queue`
  - `DataService.*Subscribe`
  - `DataService.*Publish`
  - `data-service.*amqp`
  - `IDataService.*(Publish|Subscribe|Consume)`
- Project-name scan: any directory under `src/` whose name contains `DataService`, **and** any `*.csproj` whose filename or `Dorc.sln` entry references `DataService`.
- Configuration sweep: `appsettings*.json` files across `src/` and `install-scripts/` (and any Helm chart `values.yaml` if present) for keys whose name or value mentions a DataService surface (`DataService`, `data-service`, queue / exchange / routing-key strings paired with the substring).
- Reflection / DI sweep: `Activator.CreateInstance`, `Type.GetType`, **and** any DI registration shape (`AddTransient/AddScoped/AddSingleton` — both generic-method and `typeof(...)` open-generic forms — plus assembly-scan / convention-based registrations such as `Scrutor`, `Lamar`'s scanner, `MEF`, etc., if used in DOrc) whose target type-name includes `DataService`. The pattern list is **non-exhaustive**; the disposition step (§R-3) is the safety net for naming patterns the explicit list misses.

A surface-pattern match elsewhere (e.g. legitimate use of "DataService" as a non-pub/sub class name) does **not** count as a finding; the audit deliverable enumerates each match and dispositions it as either *in-scope-pub/sub* or *out-of-scope-naming-coincidence*.

### R-2 — Reproducibility

The audit deliverable records the **exact** commands or tooling used (e.g. `rg --no-heading -n 'RabbitDataService' src/`) so a reviewer can re-run them at any point and reproduce the result. Each pattern's raw match count appears in the deliverable.

### R-3 — Verdict

The audit deliverable carries a single-line verdict at the top:

- *Empty surface — S-008 closes as a no-op.*
- *Surface discovered: see §X for details. Escalating to user; separate port spec to be authored.*

If the second verdict is recorded, the spec for the port work is **not** authored in this S-008 deliverable — escalation to the user is the action, and the port spec is its own JIT spec.

### R-4 — Cross-link to S-009

If the verdict is *empty surface*, the deliverable explicitly states "S-009's Rabbit-removal grep-target list does not need to add DataService entries." If the verdict is *surface discovered*, the discovery is added to S-009's known scope.

---

## 3. Out of Scope (explicit)

- Porting any discovered surface — separate JIT spec.
- Removal of unrelated `*DataService*` type names that are not pub/sub — S-009 / future cleanup.
- A test for the audit itself (the audit is a one-shot human-readable artefact; reproducibility per R-2 is the durability mechanism).

---

## 4. Acceptance Criteria

### AT-1 — Audit deliverable exists and is reproducible

`docs/kafka-migration/S-008-DataService-Audit.md` exists and contains:

- The exact pattern set from R-1, each shown with the command used.
- For each pattern, the raw match count and the matched paths (or "0 matches").
- A disposition for each match: *in-scope-pub/sub* or *out-of-scope-naming-coincidence* with a one-line justification per disposition.
- The R-3 single-line verdict at the top.
- The current commit SHA at the time of audit run, so re-runs at later commits can be compared.

### AT-2 — Verdict is consistent with the dispositions

If any match is dispositioned *in-scope-pub/sub*, the verdict must be *Surface discovered*; otherwise *Empty surface*. A *Empty surface* verdict with an unaddressed in-scope match is a defect.

### AT-3 — User escalation if surface discovered

If the verdict is *Surface discovered*:

- The audit deliverable records a user-acknowledgement signature line (date + user-confirmed disposition).
- The port-spec authoring decision is captured in the deliverable: either *port spec being authored now* (with a placeholder filename), or *deferred pending R-4(a) descope-gate review* (with the date of the next gate check), or *deferred to post-cutover* (with explicit user direction).
- **S-008 closes on the escalation + decision capture**, not on the port spec being authored. The port spec is its own JIT-spec lifecycle if/when authored.

If the verdict is *Empty surface*, this AT is auto-satisfied (no escalation needed).

### AT-4 — IS / SC-1 alignment

The audit deliverable explicitly cross-references:

- The IS R3 §6 narrative claim (DataService surface absent) — confirmed or contradicted.
- S-009's Rabbit-removal grep-target list — no change needed (empty verdict) or addition flagged (surface verdict).

---

## 5. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| The audit may produce false positives (e.g. a `MetricsDataService` that has nothing to do with pub/sub). | R-1 disposition step | Accepted — the disposition step is exactly the protection against this; deliverable shows the reasoning per match. |
| The audit may miss a surface that uses a name not matching any listed pattern (e.g. `EventBus` for what is really a DataService). | R-1 pattern set | Accepted — the IS-prescribed pattern set is the stated bar; the long-lived guard is **S-009's SC-1 grep suite** (`RabbitMQ.*`, `EasyNetQ`, `amqp://`, etc.) which would catch any Rabbit-based pub/sub surface regardless of naming. |
| Re-running the audit at a later commit may yield a different result if someone introduces a DataService pub/sub between now and S-009. | R-2 commit-SHA recording | Accepted — the deliverable is a snapshot; S-009's grep suite is the long-lived guard against drift. |
| If a surface is discovered, the R-4(a) 2026-10-15 descope gate still applies and may force the port to be deferred post-cutover. | IS §4a R-4(a) | Accepted — the spec for the port work decides at authoring time whether to invoke R-4(a). |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Tooling:** `rg` (ripgrep) is the recommended search tool but not mandatory; `grep -rn`, GitHub code-search, or VS Code's search-in-files all produce the same result. The deliverable records whichever tool was used so the audit is reproducible.
- **Effort:** approximately 30–60 minutes of human (or sub-agent) execution + 30 minutes of write-up. No code changes expected if the verdict is *Empty surface*.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Whether the R-1 pattern set covers the IS R3 §3 S-008 audit prescription faithfully.
- Whether the R-3 verdict / escalation flow is unambiguous.
- Whether AT-1 produces a deliverable a future reviewer can re-run and trust.
- Risk coverage in §5.

Reviewers should **NOT**:

- Demand the audit run inside this review — it's a Delivery activity post-approval.
- Re-litigate the IS R3 Implementation-Discovery finding — it's settled; S-008's job is to re-verify it, not re-debate it.
- Demand the port spec be co-authored speculatively — it's only authored if a surface is discovered.

---

## 8. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR (2-reviewer panel)

Panel: Sonnet-4.6, GPT-5.3-codex (small low-risk audit-deliverable spec; 2-reviewer panel per CLAUDE.md §4 sizing). Verdicts: APPROVE WITH MINOR × 2. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| GPT-F3 | GPT | MEDIUM | AT-3 vs §1/§3 timing contradiction: must port spec be authored before S-008 closes, or is it follow-on? | **Accepted** — AT-3 now explicit: S-008 closes on escalation + user-acknowledged decision capture (port spec authoring is a separate JIT-spec lifecycle if/when initiated). |
| Sonnet-F1 | Sonnet | LOW | DI registration patterns narrow (only `typeof(...)` form) — generic-method + assembly-scan forms missed | **Accepted** — R-1 reflection/DI bullet now lists generic-method form + assembly-scan / convention-based (Scrutor, Lamar scanner, MEF), explicitly non-exhaustive with the disposition step as the safety net. |
| Sonnet-F2 | Sonnet | LOW | Case-insensitivity not explicit | **Accepted** — R-1 grep block now states "all case-insensitive — pass `-i`". |
| Sonnet-F3 | Sonnet | LOW | AT-3 wording softening (overlap with GPT-F3) | **Accepted** — subsumed by GPT-F3 fix. |
| Sonnet-F4 | Sonnet | LOW | §5 row 2 cited S-006/S-007 as the long-lived guard; should cite S-009 SC-1 grep suite | **Accepted** — §5 row 2 now cites S-009's SC-1 grep suite. |
| GPT-F1 | GPT | LOW | Project-name scan should also cover `*.csproj` and solution entries | **Accepted** — R-1 project-name scan extended. |
| GPT-F2 | GPT | LOW (non-finding) | Helm "if present" hedge tracks IS R3 wording | Acknowledged. |
| GPT-F4 | GPT | LOW | Verdict-token casing consistency | No action — wording across §R-3 / AT-2 / R-4 already uses the same `Empty surface` / `Surface discovered` tokens. |

All findings accepted and resolved via surgical edits or acknowledged. No re-litigation of HLPS / IS settled decisions. Per CLAUDE.md §4: two APPROVE-tier verdicts with all findings resolved = **unanimous approval**. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
