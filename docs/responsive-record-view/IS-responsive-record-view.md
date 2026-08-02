# IS — Responsive Record View

| Field | Value |
|---|---|
| Status | **DRAFT** — not yet reviewed |
| Date | 2026-08-02 |
| Owner | Ben Hegarty |
| Governing HLPS | `HLPS-responsive-record-view.md` (**APPROVED** 2026-08-02, after panel rounds R1–R3) |
| Evidence base | `MEASURE-U-18-narrow-overflow.md` (8 of 10 views overflow at 375px; cheap width fix truncates ~54% of the identity field) |

The HLPS §8 sequencing is binding on this document. Steps are atomic and
ordered; JIT Specs are written per step, immediately before it enters the
Delivery Loop. This is a strategic roadmap: no method signatures, no line
numbers — those belong to the JIT Specs.

---

## Sequence overview

```
S-001  Infrastructure: controller + list-row helper + bar   (spikes the sort-connectivity risk FIRST)
S-002  Migrate page-monitor-requests                        (worked example; validates the contract)
S-003  Migrate make-like-production                         (container-width / dialog-hosted)
S-004  Migrate page-env-history                             (declarative sort + edit actions)
S-005  Migrate component-deployment-results                 (non-virtualised, details-hosted)
S-006  Migrate env-monitor                                  (activation semantics)
S-007  page-monitor-result narrow layout                    (journey step; SC9 precondition)
S-008  Pilot gate                                           (SC9 walkthrough, SC11 design bar, complexity adjudication, post-pilot escalation)

P-001  Width-attribute cleanup                              (parallel, independent)
P-002  Breakpoint consolidation (SC6)                       (parallel, independent)
```

`P-` steps are schedulable in parallel with the main sequence at any point;
they touch disjoint code and carry their own regression gates.

---

## S-001 — Infrastructure

**Objective.** Build the three §3.4 artifacts: the narrow-mode controller
(container-width detection, column collapse/restore, `ResponsiveMixin`
replacement), the shared list-row template helper (slots: primary, metadata,
chip, actions, disclosure — with the template-owned `aria-expanded`
obligation), and the header-hosted filter/sort/controls bar. Add axe-core to
the Vitest browser harness (U-16 approval).

**Order within the step is fixed:** the sort-connectivity spike comes first —
prove that the header-hosted bar keeps sorters connected while wide columns
are hidden, or fall back to controller-managed `sortOrders` at the
`dataProvider` level. This is the highest technical risk in the design (HLPS
§8); if both mechanisms fail, halt and escalate — do not proceed to
migrations on an unproven bar.

**Validated against** `page-monitor-requests`' row template as the worked
example (U-3: its field priority is the first the user confirms), but no view
is migrated in this step.

**Entry gates.** U-12 (container-width test pattern) answered in this step's
JIT Spec; U-10 residual (bar layout/content) decided in the same spec; SC5's
decomposed a11y criteria authored **before** the helper is built.

**Exit criteria.** Infrastructure tests green (HLPS §6.2): threshold at 768px
container width; collapse/restore including `auto-width` recalculation;
`sortOrders`/`filters` continuity across a crossing (SC10 mechanics); axe
runs in the harness. Nothing user-visible has changed.

**Risk retired.** Sort loss at narrow width; bar mechanism; test pattern.

---

## S-002 — Migrate `page-monitor-requests`

**Objective.** First real migration: row-template module, controller applied
(`ResponsiveMixin` dropped), bar carrying its five filter inputs **and** the
view-level controls (connection status, auto-refresh, manual refresh), actions
slot hosting `request-controls` via the overflow-menu pattern, disclosure for
the fields hidden today. Test migration per HLPS §3.1.

**Entry gates.** U-3 (field priority for the deployment-request entity —
user-confirmed in the JIT Spec); U-7 residual for `request-controls`; U-15
(activation: primary line carries the open-result link; activation toggles
disclosure — confirmed for this view).

**Exit criteria.** SC1, SC2 (against the pre-approved checklist), SC3, SC4,
SC5 (axe + keyboard), SC10, SC11-objective for this view. The near-duplicate
relationship with `env-monitor` is *not* exploited yet — S-006 stays
separate.

**Risk retired.** The contract works on the hardest view (lazy provider, all
filtering in headers, live header controls, in-cell activation).

---

## S-003 — Migrate `make-like-production`

**Objective.** Prove container-width behaviour: the same migration inside a
`vaadin-dialog`, where the controller must measure its host, not the
viewport. Fixes its two hardcoded 300px columns in passing (it overflows
+325px today — MEASURE §3).

**Entry gates.** U-3 for this view; `ResizeObserver` churn bound (HLPS §8
risk) asserted in this step's tests.

**Exit criteria.** SC1/SC2/SC3/SC5/SC11-objective at dialog-constrained
widths; threshold behaviour verified against **container** width in both
directions.

**Risk retired.** Dialog hosting; the one pilot view with no responsive
handling today.

---

## S-004 — Migrate `page-env-history`

**Objective.** Prove the declarative idiom: seven `vaadin-grid-sort-column`s
whose only sort affordance is the header row the narrow mode hides — the bar
must expose sort for a view with no filters. Actions slot hosts
`edit-comments-controls` including its edit/save/cancel cycle (the
interactive-renderer case from U-2).

**Entry gates.** U-3; U-7 residual for `edit-comments-controls`; bar sort
control design from S-001 exercised without filter inputs present.

**Exit criteria.** SC1–SC5, SC10 (declarative default sort direction survives
the crossing — the exact failure mode R2b found), SC11-objective.

**Risk retired.** The dominant codebase idiom (33 remaining views skew
declarative); in-row editing in a list row.

---

## S-005 — Migrate `component-deployment-results`

**Objective.** The `.items` + `all-rows-visible` non-virtualised case, hosted
inside `vaadin-details` on the result page; log/plan dialog-opening actions in
the actions slot.

**Entry gates.** U-3; confirmation that `all-rows-visible` and the collapse
interact sanely (small result sets; no virtualiser in play).

**Exit criteria.** SC1 (measured at its real hosted width, not 375px —
MEASURE's "not-established" caveat closes here), SC2/SC3/SC5,
SC11-objective.

---

## S-006 — Migrate `env-monitor`

**Objective.** The activation-semantics case: `active-item-changed` currently
opens the result page, colliding with activation-toggles-disclosure. Apply
the U-15 default (open-result moves to the primary line; activation =
disclosure), reusing S-002's row template with the delta documented — the
two views' templates should share one module if the JIT Spec confirms the
entities are identical.

**Entry gates.** U-3 (expected: inherit S-002's ranking); U-15 confirmed for
this view.

**Exit criteria.** SC1–SC5, SC10, SC11-objective; `grid-menu-a11y`
row-activation outcomes re-verified on the migrated view.

---

## S-007 — `page-monitor-result` narrow layout

**Objective.** Not a grid migration: make the composition page
(`request-status-card` + details sections + S-005's component) usable at
375×667 so the SC9 journey can pass through it. Card/section layout work
only; scope anchored by HLPS §3.1.

**Entry gates.** S-005 complete (its component is most of this page's
content).

**Exit criteria.** SC1 for the page; SC9's step-2/3 legs walkable.

---

## S-008 — Pilot gate

**Objective.** The three adjudications the HLPS reserves for this point, then
the escalation.

1. **SC9 walkthrough** — seeded fixture, non-author executor, pass/fail
   recorded.
2. **SC11 design bar** — side-by-side against GitHub's mobile issue/PR list;
   owner + panel; authority to fail the pilot on presentation quality alone.
3. **Cognitive-complexity adjudication** (HLPS §4) — one migrated view's
   row-template module vs its pre-migration `render()`; panel may fail the
   pilot.
4. **Escalate to the user**: post-pilot sequencing (the ~31 remaining
   grid-rendering views, audit pages first per HLPS §3.3), the U-1 follow-up
   commitment (a session with a real on-call engineer), and U-20's idiom rule
   taking effect for new views.

**Exit criteria.** All three gates passed and recorded in this document's
review trail; user decision on post-pilot scope captured as a new HLPS or an
extension of this IS.

---

## P-001 — Width-attribute cleanup (parallel)

`page-env-history`'s three hardcoded widths; the audit pages' and
`make-like-production`'s width policies (S-003 covers the latter if P-001 has
not run first — coordinate, don't duplicate). Hundreds of px of overflow
removed at every width for a handful of attribute edits (MEASURE §4a).
Verification: re-run the U-18 instrument; figures recorded in MEASURE as a
post-cleanup column. **Explicitly not sufficient** (MEASURE §4c) — this is
cleanup, not the fix.

## P-002 — Breakpoint consolidation / SC6 (parallel)

One module owns the 768px value; `dorc-app.ts` keeps its legitimate viewport
subscription but imports the constant; the 26 files / 28 CSS occurrences
resolve against the shared value. Regression gate: `responsive-mixin.test.ts`,
`grid-column-hiding.test.ts`; unmigrated views' `?hidden` behaviour unchanged.

---

## Unknown routing (from HLPS §7)

| Unknown | Routed to |
|---|---|
| U-3 (field priority) | Each migration step's JIT Spec; S-002's is the worked example validated in S-001 |
| U-7 residual | S-002 (`request-controls`), S-004 (`edit-comments-controls`) |
| U-10 residual (bar design) | S-001 JIT Spec |
| U-12 (test pattern) | S-001 JIT Spec |
| U-14 residuals (sorter connectivity, filter mirroring, width recalc) | S-001 (mechanics) + SC10 assertions in every migration step |
| U-15 (activation) | S-002 and S-006 JIT Specs |
| U-19 (rollback) | Constraint on every migration step: one-commit revert per view, stated in each JIT Spec |
| U-9, U-13, U-20 defaults | Adopted as HLPS defaults; revisit only on pilot evidence |

## Checkpoints (per CLAUDE.md)

- This IS requires adversarial review and user approval before S-001 begins.
- Each step's JIT Spec is reviewed before that step enters Delivery
  (auto-pilot not assumed).
- S-008 item 4 is a hard stop for user decision.

---

## Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|---|---|---|---|---|
| — | 2026-08-02 | DRAFT | — | Authored from the approved HLPS. Not yet submitted to the panel. |
