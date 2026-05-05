# AUDIT — S-001 — `ContainsExpression` Consumer Audit

**Status:** APPROVED — auto-pilot grant 2026-05-05
**Date:** 2026-05-05
**Step ID:** S-001
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED
- [`SPEC-S-001-contains-expression-audit.md`](SPEC-S-001-contains-expression-audit.md) — APPROVED

---

## 1. Method (R1)

**Search performed:**
- Tool: `Grep` (ripgrep) over `C:\src\dorc\src`
- Pattern: `\bContainsExpression\b`
- Commit SHA: `3af8a7f426532f74b41565ab417be7e17bfd54a1` (branch `perf/request-grid-sargable-filters`)

**Result set:** 20 hits. Excluding (a) the helper definition itself in `DataPagerExtension.cs` (1), (b) three call sites in the request-status grid path `RequestsStatusPersistentSource.cs` (the subject of this HLPS), (c) five hits in test code `DataPagerExtensionTests.cs` (per spec §3 out-of-scope), and (d) two comment-only hits in `DaemonAuditPersistentSource.cs` (lines 57 and 99 — comments referring to the helper; the live call site at line 101 is included as consumer #9 below), the audited consumer set is **9 call sites across 8 files**. Arithmetic: 20 − 1 helper − 3 request-status − 5 tests − 2 DaemonAudit-comments = 9 audited consumers.

| # | File | Line | Underlying Entity | API Surface |
|---|------|------|-------------------|-------------|
| 1 | `Dorc.PersistentData/Sources/ServersPersistentSource.cs` | 112 | `Server` | Servers list (`page-servers-list.ts`) |
| 2 | `Dorc.PersistentData/Sources/ScriptsPersistentSource.cs` | 56 | `Script` | Scripts list (`page-scripts-list.ts`) |
| 3 | `Dorc.PersistentData/Sources/ScriptsAuditPersistentSource.cs` | 63 | `AuditScript` | Scripts audit (`page-scripts-audit.ts`) |
| 4 | `Dorc.PersistentData/Sources/PropertyValuesPersistentSource.cs` | 469 | `FlatPropertyValueApiModel` (env-scoped) | Property-values views (`page-variables`, env-variables tab) |
| 5 | `Dorc.PersistentData/Sources/PropertyValuesPersistentSource.cs` | 600 | `FlatPropertyValueApiModel` (global-scoped) | Property-values views |
| 6 | `Dorc.PersistentData/Sources/PropertyValuesAuditPersistentSource.cs` | 65 | `Audit` | Property-values audit |
| 7 | `Dorc.PersistentData/Sources/ManageProjectsPersistentSource.cs` | 115 | `RefDataAudit` | Projects audit (`page-projects-audit`) |
| 8 | `Dorc.PersistentData/Sources/DatabasesPersistentSource.cs` | 222 | `Database` | Databases list (`page-databases-list.ts`) |
| 9 | `Dorc.PersistentData/Sources/DaemonAuditPersistentSource.cs` | 101 | `DaemonAudit` | Daemons audit |

**Cross-reference against HLPS §3 In-Scope enumeration** (Database, ManageProjects, PropertyValuesAudit, PropertyValues, DaemonAudit, DaemonObservation):

| HLPS-listed | Found in this audit | Note |
|-------------|---------------------|------|
| Database | ✓ #8 | Match. |
| ManageProjects | ✓ #7 | Match. |
| PropertyValuesAudit | ✓ #6 | Match. |
| PropertyValues | ✓ #4, #5 | Match (two call sites — env-scoped and global-scoped). |
| DaemonAudit | ✓ #9 | Match. |
| DaemonObservation | ✗ — not found | The `DaemonObservationPersistentSource` uses `Contains` against an `int` collection (an `IN`-clause `ids.Contains(o.DaemonId)`), not the string-substring `ContainsExpression` helper. Inclusion in the HLPS list was incorrect; this is a benign error and does not affect scope. |
| (not listed) | + #1 Servers | HLPS missed this consumer. Recorded here. |
| (not listed) | + #2 Scripts | HLPS missed this consumer. Recorded here. |
| (not listed) | + #3 ScriptsAudit | HLPS missed this consumer. Recorded here. |

**Deviation summary:** the HLPS enumeration was a 60% match. Three real consumers (Servers, Scripts, ScriptsAudit) were missing from the HLPS list; one listed consumer (DaemonObservation) does not actually use the helper. This audit supersedes the HLPS list for the purpose of S-001.

## 2. Per-Consumer Critical-Instance Evaluation (R2)

The three conditions per HLPS §3 / SPEC-S-001 §2 R2:

1. **(Rows)** Underlying table > ~100K rows in production.
2. **(Polling)** Filter invoked from a polled or auto-refreshed UI surface (per the SPEC-S-001 R2 definition: timer, SignalR push, focus re-fetch — anything that re-issues the request without explicit user action).
3. **(Index gap)** At least one filter column lacks a supporting index.

**Polling assessment basis:** comprehensive grep of `src/dorc-web` for `setInterval`, `setTimeout` (re-fetch patterns vs. debounce), and SignalR `hubConnection` usage at commit `3af8a7f4`. SignalR-subscribed surfaces are confined to the deployment-monitor stack (`env-monitor.ts`, `page-monitor-requests.ts`, `page-monitor-result.ts`, `request-status-card.ts`, `connection-status-indicator.ts`) — *none* of which consume any of the 9 audited endpoints; they all consume the request-status grid being fixed elsewhere in this HLPS. The audited consumers' surfaces inspected directly for periodic re-fetch were `page-servers-list.ts`, `page-scripts-list.ts`, `page-scripts-audit.ts`, `page-databases-list.ts`, `page-variables.ts`, `env-variables.ts`, and the projects/daemons audit pages — all `setTimeout` matches in those files are the standard filter-input debounce pattern (`later(); window.setTimeout(later, wait);`), and no `setInterval` exists anywhere in `src/dorc-web/src`. **None of the audited consumers' UI surfaces re-issue the request without explicit user action.** Condition (2) is therefore **False** for every audited consumer.

| # | Consumer | (1) Rows >100K? | (2) Polled? | (3) Index gap? | Disposition |
|---|----------|-----------------|-------------|----------------|-------------|
| 1 | ServersPersistentSource | Indeterminate (DBA) | **False** (user-triggered list page; debounced filter input only) | Indeterminate (not exhaustively verified across SSDT) | **Non-critical** (Condition 2 false) |
| 2 | ScriptsPersistentSource | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 3 | ScriptsAuditPersistentSource | Indeterminate (DBA) | **False** (audit pages are user-navigated, not auto-refreshed) | Indeterminate | **Non-critical** (Condition 2 false) |
| 4 | PropertyValuesPersistentSource (env-scoped) | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 5 | PropertyValuesPersistentSource (global-scoped) | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 6 | PropertyValuesAuditPersistentSource | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 7 | ManageProjectsPersistentSource (RefDataAudit) | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 8 | DatabasesPersistentSource | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |
| 9 | DaemonAuditPersistentSource | Indeterminate (DBA) | **False** | Indeterminate | **Non-critical** (Condition 2 false) |

**Disposition summary:** **0 Critical, 0 Potentially Critical, 0 Indeterminate (final), 9 Non-critical.** (Per-condition Indeterminate values for Conditions 1 and 3 do not promote any consumer to the Indeterminate *disposition* because Condition 2 is decisively False — the conjunctive test resolves to Non-critical regardless of the values of (1) and (3).)

The Critical-instance test is conjunctive — all three conditions must be True for a Critical disposition. Condition 2 (polled / auto-refreshed) is the discriminating factor: the request-status grid was uniquely subject to high-frequency polling because `env-monitor.ts` re-issues it via SignalR push subscriptions and `page-monitor-requests.ts` is the most actively-used grid on the platform. None of the audited consumers share that property.

Row counts (Condition 1) are recorded as Indeterminate because production row counts are not derivable from the repository alone; however, the conjunctive nature of the test means this Indeterminate value cannot promote any consumer to Critical or Potentially Critical given Condition 2 is False.

Index-gap (Condition 3) is recorded as **Indeterminate** for all consumers because exhaustive per-column SSDT analysis was not performed — the SSDT project shows few non-clustered indexes outside of FK columns and no string-search indexes on the filter columns of these tables, but a definitive True classification would require column-by-column verification per consumer. The Indeterminate value does not affect the conjunctive disposition (Condition 2 is False, so Non-critical regardless).

## 3. Escalation (R3)

**No escalation required.** Zero Critical or Potentially Critical findings.

## 4. Indeterminate Surfacing (R4)

The Row-count condition (Condition 1) is Indeterminate for all 9 consumers without DBA input. This residual ambiguity is **not a blocker** for S-001 completion because:

- The Critical-instance test is conjunctive; with Condition 2 = False, no row-count value (including True) can promote any of these consumers to Critical.
- The HLPS already accepts (HLPS §5 SC2) that BuildNumber-only filtering on the request grid is out-of-scope for the perf guarantee — i.e., the HLPS posture is that *polling frequency*, not raw table size, is the dominant cost driver. This audit's findings are consistent with that posture.

Recommended (non-blocking) follow-up: when DBA contact is next available, capture row counts for the underlying tables (Server, Script, AuditScript, FlatPropertyValueApiModel underlying view, Audit, RefDataAudit, Database, DaemonAudit) for the historical record. This is a documentation chore, not a scope-changing event.

## 5. Out-of-Scope Observations (informational)

Per SPEC-S-001 §3, inline `entity.Col.Contains(value)` calls written directly in LINQ (not via the helper) were **not** audited. Three such call sites were observed in passing during the helper-call enumeration:

- `ServersPersistentSource.cs:108` — `server.Environments.Any(ed => ed.Name.Contains(pagedDataFilter.FilterValue))` for the `EnvironmentNames` filter on the Servers grid.
- `DatabasesPersistentSource.cs:218` — same pattern on the Databases grid.
- `ScriptsPersistentSource.cs:52` — `s.Components.Any(c => c.Projects.Any(p => p.Name.Contains(...)))` for the `ProjectNames` filter on the Scripts grid.

These are recorded for informational purposes only. They are user-triggered surfaces (Condition 2 = False) so they would not meet the Critical-instance test even if audited. If a future incident implicates them, a separate audit can pick them up.

## 6. Conclusion

The HLPS hypothesis — that the request-status grid is the unique acute case among `ContainsExpression` consumers — is **confirmed by audit**. No other consumer in the codebase meets the Critical-instance test. S-002–S-007 may proceed under the existing HLPS scope without modification.

**S-001 is complete.**

## 7. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial audit. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6 | Submitted to a 2-model panel — audit deliverable, no code change. |
| R1    | 2026-05-05 | APPROVED | Opus 4.7, Sonnet 4.6 | Opus: APPROVE WITH MINOR FINDINGS (3 LOW). Sonnet: APPROVE (3 LOW + 1 INFORMATIONAL). All findings are presentation-level — none change the dispositions or conclusion. Three accepted and applied: result-set count corrected (19 → 20 with arithmetic shown); "Likely" Condition 3 values changed to "Indeterminate" (spec-vocabulary compliance); SignalR claim broadened to enumerate all deployment-monitor stack surfaces. Sonnet F1 (prose clarity on DaemonAudit comment exclusions) absorbed by the rewritten Method paragraph; Sonnet F2 (per-surface inspection naming) addressed by enumerating the inspected surfaces in the Polling assessment basis. Status APPROVED. |
