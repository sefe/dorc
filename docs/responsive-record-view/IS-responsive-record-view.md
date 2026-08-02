# IS — Responsive Record View

| Field | Value |
|---|---|
| Status | **IN REVIEW — awaiting user approval.** IS-R1 2/2 REVISE → triage implemented → IS-R2 verification: **APPROVE WITH CHANGES**, all three edits applied. Approval covers **U-5** (pilot set) and **U-19** (rollback default). |
| Date | 2026-08-02 |
| Owner | Ben Hegarty |
| Governing HLPS | `HLPS-responsive-record-view.md` (**APPROVED** 2026-08-02) |
| Evidence base | `MEASURE-U-18-narrow-overflow.md` (8 of 10 views overflow at 375px; the cheap width fix truncates ~54% of the identity field) |

The HLPS §8 sequencing is binding. Steps are atomic and ordered; JIT Specs are
written per step immediately before Delivery. This is a strategic roadmap — no
method signatures, no line numbers. Figures follow the HLPS's binding counting
rule: derivation inline or inadmissible.

**Global exit condition (every step, S- and P-):** `npm run type-checking` and
`eslint` green (HLPS §6.11).

---

## 1. Sequence overview and dependencies

```
S-001a  Bar-mechanism spike → decision note        (halt-and-escalate lives here)
S-001b  Infrastructure: controller + helper + bar  (cites the approved note)
S-002   Migrate page-monitor-requests              (contract validation; halt gate)
S-003   Migrate make-like-production               (container-width; first declarative sorters)
S-004   Migrate page-env-history                   (sort-only bar mode; edit actions)
S-005   Migrate component-deployment-results       (non-virtualised; details-hosted)
S-006   Migrate env-monitor                        (activation semantics)
S-007   page-monitor-result narrow layout          (SC9 legs; fixture prepared here)
S-008   Pilot gate                                 (SC9/SC11/complexity; then escalation)

P-001   Width-attribute cleanup                    (parallel; interaction notes below)
P-002   Breakpoint consolidation / SC6             (parallel; shares the 768px constant with S-001b)
P-003   axe-core harness integration               (parallel; hard prerequisite of S-002)
```

| Step | Depends on (hard) | Depends on (soft) |
|---|---|---|
| S-001a | IS approval | — |
| S-001b | S-001a note APPROVED | P-002 (constant location — see interaction note) |
| S-002 | S-001b; **P-003** (SC5 needs axe) | — |
| S-003 | S-002 (contract validated) | P-001 (same file's widths — see note) |
| S-004 | S-002 | S-003 (declarative sorters first met there) |
| S-005 | S-002 | S-003 (container-width proven) |
| S-006 | S-002 (row-template module reused) | S-004 |
| S-007 | S-005 (hosts its component) | — |
| S-008 | S-002 + S-005 + S-007 (the SC9 journey chain) and S-003/S-004/S-006 complete | — |
| P-001 | none | coordinate with S-003/S-004 (below) |
| P-002 | none | coordinate with S-001b (below) |
| P-003 | none (U-16 approval already granted) | — |

**Interaction notes** *(replacing the withdrawn "disjoint code" claim — ISR1a-F5/F8, ISR1b-F13)*:

- **P-002 ↔ S-001b.** `NARROW_BREAKPOINT = 768` lives only in
  `responsive-mixin.ts` today, and S-001b's controller consumes that value.
  P-002-first: the controller imports the consolidated constant. S-001b-first:
  the controller is P-002's additional sweep site. Either order is legal; the
  active order is recorded in whichever step runs second.
- **P-001 ↔ S-003/S-004.** P-001's width fixes and the migrations of
  `make-like-production` (S-003) and `page-env-history` (S-004) touch the same
  column declarations. Whichever runs first owns the edit; the second cites it.
- **P-001's verification** re-runs the U-18 instrument, whose transcriptions
  mirror source: its post-cleanup column is meaningful only for rows **not yet
  migrated** at run time; the run date is recorded against the migration
  sequence in MEASURE.

**Cross-step coupling rule** *(ISR1a-F9; scope corrected per ISR2-F1)*: any
change to the shared S-001b artifacts (controller, template helper, bar) made
during **any step, S- or P-, that runs after a view has migrated — including
P-002's controller sweep** — re-triggers the full SC suite of every previously
migrated view as part of that step's exit criteria.

---

## 2. Steps

### S-001a — Bar-mechanism spike → decision note

- **Objective.** Decide the bar mechanism by experiment: prove the
  header-hosted bar keeps sorters connected while wide columns are hidden
  (HLPS §3.4 default), or select the fallback (controller-managed `sortOrders`
  at the `dataProvider` level). Output is a standalone decision note —
  `DECISION-bar-mechanism.md`, own DRAFT→APPROVED lifecycle, panel-reviewed —
  per the `grid-menu-a11y` S-001 precedent. Three outcomes: header-hosted /
  fallback / **halt and escalate** (both mechanisms fail).
- **Depends on.** IS approval.
- **Entry gates.** None beyond approval — this step exists to unblock U-10.
- **Exit criteria.** Decision note APPROVED, recording the experiment, the
  chosen mechanism, and its consequences for filter mirroring (U-14b) and
  header-row visibility.
- **Risk retired.** Sort loss at narrow width — the design's top HIGH risk —
  before any shared artifact is built on the answer.

### S-001b — Infrastructure

- **Objective.** Build the three §3.4 artifacts citing the approved note: the
  narrow-mode controller (container-width detection, collapse/restore,
  `ResponsiveMixin` replacement), the shared list-row template helper (slots:
  primary, metadata, chip, actions, disclosure — template-owned
  `aria-expanded`), and the bar (filters, sort control, view-level controls
  region). No view migrates here.
- **Depends on.** S-001a (hard); P-002 (soft — constant location, note above).
- **Entry gates.** U-10 residual (bar layout/content) and U-12 (container-width
  test pattern) answered in this step's JIT Spec; **U-6 naming user-confirmed
  in the JIT Spec** with HLPS §7's defaults as the proposal *(ISR1b-F1)*;
  **U-3 for the deployment-request entity user-confirmed here** — authored in
  this spec as the worked example, consumed unchanged by S-002 *(ISR1a-F6)*;
  SC5's decomposed a11y criteria authored before the helper is built.
- **Exit criteria.** Infrastructure tests green (HLPS §6.2): 768px
  container-width threshold; collapse/restore including `auto-width`
  recalculation; `sortOrders`/`filters` continuity across a crossing (SC10
  mechanics). Nothing user-visible changed.
- **Risk retired.** Test pattern for container-driven behaviour; the contract
  exists as reviewable code.

### S-002 — Migrate `page-monitor-requests`

- **Objective.** First migration and contract validation: row-template module,
  controller applied (`ResponsiveMixin` dropped), bar carrying its five filter
  inputs and the view-level controls (connection status, auto-refresh, manual
  refresh), actions slot hosting `request-controls` via the overflow-menu
  pattern, disclosure for today's hidden fields.
- **Depends on.** S-001b (hard); **P-003 (hard — SC5's axe assertions)**.
- **Entry gates.** SC2 baseline checklist authored and approved in the JIT
  Spec, with the §6.1 suites enumerated and pre-classified behaviour- vs
  idiom-asserting (`page-monitor-requests.test.ts` among them) *(ISR1b-F4)*;
  U-7 residual for `request-controls`; U-15 for this view (primary line
  carries the open-result link; activation toggles disclosure).
- **Exit criteria.** SC1; SC2 against the pre-approved checklist; SC3; SC4;
  SC5 (axe + keyboard + **manual screen-reader spot-check per
  `grid-menu-a11y` §5** *(ISR1b-F7)*); SC10; SC11-objective. Test migration:
  idiom-asserting coverage re-established against controller/template.
- **Failure path** *(ISR1a-F4)*: if the §3.4 contract does not hold on this
  view (U-8a wrong in practice), **halt; revise the IS before S-003 starts.**
- **Risk retired.** The contract works on the hardest view.

### S-003 — Migrate `make-like-production`

- **Objective.** Container-width proof inside `vaadin-dialog`: controller
  measures its host, not the viewport. Actions slot hosts the property
  **override button** (§3.4 slot map) *(ISR1b-F17)*. Width fix for its two
  hardcoded 300px columns per the P-001 interaction note (+325px overflow
  today — MEASURE §3).
- **Depends on.** S-002 (hard); P-001 (soft — same file's widths).
- **Entry gates.** SC2 baseline checklist approved in the JIT Spec; U-3 for
  this entity.
- **Exit criteria.** SC1/SC2/SC3/SC5/SC11-objective at dialog-constrained
  widths (SC4 inapplicable — 0 `dataProvider` references, derived by grep);
  threshold verified against **container** width both directions;
  **SC10 including declarative-sorter continuity — this view's two
  `<vaadin-grid-sort-column>`s are the sequence's first contact with
  declarative sort crossing the threshold** *(ISR1a-F2)*; `ResizeObserver`
  churn bound asserted; test migration per HLPS §3.1.
- **Risk retired.** Dialog hosting; first declarative-sorter crossing.

### S-004 — Migrate `page-env-history`

- **Objective.** The sort-only bar mode: seven declarative sort-columns, zero
  filters — the bar must expose sort with no filter inputs present. Actions
  slot hosts `edit-comments-controls` including its edit/save/cancel cycle
  (U-2's interactive-renderer case).
- **Depends on.** S-002 (hard); S-003 (soft).
- **Entry gates.** SC2 baseline checklist approved; U-3; U-7 residual for
  `edit-comments-controls`.
- **Exit criteria.** SC1, SC2, SC3, SC5, SC10 (declarative default sort
  direction survives the crossing), SC11-objective — enumerated, not a range:
  **SC4 is inapplicable (this view has zero `dataProvider` references —
  derived by grep, 0 occurrences)** *(ISR1a-F11)*; test migration per HLPS §3.1.
- **Risk retired.** The declarative idiom at bar level; in-row editing in a
  list row. *(22 remaining unmigrated files declare `<vaadin-grid-sort-column>`
  — 24 by `grep -l`, minus the two pilot members.)*

### S-005 — Migrate `component-deployment-results`

- **Objective.** The `.items` + `all-rows-visible` non-virtualised case,
  hosted inside `vaadin-details`; log/plan dialog-opening actions in the
  actions slot.
- **Depends on.** S-002 (hard); S-003 (soft — container-width proven).
- **Entry gates.** SC2 baseline checklist approved (its `responsive-grids.test.ts`
  assertions — including the rendered column-hiding block — pre-classified)
  *(ISR1b-F3)*; U-3; confirmation that `all-rows-visible` and the collapse
  interact sanely.
- **Exit criteria.** SC1 at the 375px oracle, measured at the component's real
  hosted container width (narrower than 375px — MEASURE §2), closing that
  row's "not-established" caveat *(ISR1b-F18)*; SC2/SC3/SC5 (SC4
  inapplicable — 0 `dataProvider` references, derived by grep);
  **SC10 including `auto-width` recalculation on return to wide — this view
  carries five `auto-width` columns** *(ISR1a-F2)*; SC11-objective; test
  migration (`responsive-grids.test.ts` coverage re-established).

### S-006 — Migrate `env-monitor`

- **Objective.** The activation-semantics case: `active-item-changed`
  currently opens the result page, colliding with activation-toggles-
  disclosure. Apply the U-15 default. The row-template module from S-002 is
  the starting point; **one module may export both views' declarations only
  if the JIT Spec confirms the entity identical, and SC3 must still resolve
  per view** *(ISR1b-F14)*.
- **Depends on.** S-002 (hard — module reuse); S-004 (soft).
- **Entry gates.** SC2 baseline checklist approved (its
  `responsive-grids.test.ts:171` assertion pre-classified) *(ISR1b-F3)*; U-3
  (expected: inherit S-002's ranking); U-15 confirmed for this view; **U-7
  residual for `request-controls` — this view renders it too** *(ISR1b-F10)*.
- **Exit criteria.** SC1–SC3, SC4 (lazy view), SC5, SC10, SC11-objective;
  `grid-menu-a11y` row-activation outcomes re-verified; test migration.

### S-007 — `page-monitor-result` narrow layout

- **Objective.** Journey step, not a grid migration: make the composition page
  usable at 375×667 so SC9's open/identify/read legs pass. **Prepares the SC9
  seeded fixture** (a named failed deployment request with component results
  and a readable failure reason, seeded via the existing test/data tooling —
  backend changes remain out of scope) *(ISR1b-F8)*.
- **Depends on.** S-005 (hard).
- **Entry gates.** SC9 walkthrough script drafted; **non-author executor
  identified** (resourcing arranged now, not at S-008 — acceptable
  substitution: any engineer who did not author U-3 priorities or the row
  templates) *(ISR1a-F12)*.
- **Exit criteria.** SC1 for the page; **SC5 for the new layout work**
  *(ISR1a-F11)*; SC9's step-2/3 legs walkable; fixture reviewed.

### S-008 — Pilot gate

- **Objective.** The reserved adjudications, then the escalation.
- **Depends on.** S-002 + S-005 + S-007 (the SC9 chain) **and** S-003, S-004,
  S-006 complete *(ISR1a-F3)*.
- **Entry gates.** Seeded fixture and executor confirmed (from S-007); all
  migration steps' SC suites green.
- **Gates.**
  1. **SC9 walkthrough** — seeded fixture, non-author executor, pass/fail
     recorded.
  2. **SC11 design bar** — side-by-side against GitHub's mobile issue/PR
     list; owner + panel; authority to fail the pilot.
  3. **Cognitive-complexity adjudication** (HLPS §4) — one migrated view's
     row-template module vs its pre-migration `render()`; panel may fail the
     pilot.
  4. **Static/diff review** — SC3, SC6, SC7, SC8 (HLPS §6.8) *(ISR1b-F6)*.
- **Failure paths** *(ISR1a-F4)*: a failed gate scopes a remediation loop to
  the step(s) that own the failing criterion (SC9 → S-002/S-005/S-007; SC11 →
  the offending view's step; complexity → S-001b + the compared view), with
  re-adjudication after; a second failure of the same gate escalates to the
  user. **Item 5 runs only after gates 1–4 pass.**
- **5. Escalation to the user** (hard stop): post-pilot sequencing — **32
  remaining grid-rendering views (37 per HLPS §1 minus the 5 grid-bearing
  pilot views)** *(ISR1b-F12)*, audit pages first per HLPS §3.3, **with U-8b's
  codebase-wide renderer cost estimate as an input** *(ISR1b-F16)*; the U-1
  follow-up commitment (session with a real on-call engineer); and the
  **U-20 idiom decision record authored as a deliverable of this step** —
  `docs/responsive-record-view/DECISION-new-view-idiom.md`, drafted from the
  HLPS default, user-approved *(ISR1b-F2)*.

### P-001 — Width-attribute cleanup *(parallel)*

- **Objective.** Fix the worst-authored width policies: `page-env-history`'s
  three hardcoded widths, the audit pages' `auto-width flex-grow="0"`
  columns, `make-like-production`'s 300px pair — per the §1 interaction
  notes. Explicitly not sufficient (MEASURE §4c); cleanup, not the fix.
- **Depends on.** None (hard); coordinate with S-003/S-004 (soft).
- **Entry gates.** Per-view before-figures recorded from MEASURE §3.
- **Exit criteria.** U-18 instrument re-run; post-cleanup column recorded in
  MEASURE with run date relative to the migration sequence (§1 note); no
  behavioural change at desktop width (existing suites green).

### P-002 — Breakpoint consolidation / SC6 *(parallel)*

- **Objective.** One module owns the 768px value; `dorc-app.ts` keeps its
  legitimate viewport subscription importing the constant; the 26 files / 28
  CSS occurrences resolve against the shared value; **the S-001b controller
  is in scope as a consumer or sweep site per the §1 interaction note**
  *(ISR1a-F5)*.
- **Depends on.** None (hard); coordinate with S-001b (soft).
- **Entry gates.** None.
- **Exit criteria.** SC6 static review passes; regression gate green
  (`responsive-mixin.test.ts`, `grid-column-hiding.test.ts` — synthetic
  fixtures, unaffected by migrations); unmigrated views' `?hidden` behaviour
  unchanged; **if the sweep touches the S-001b controller after any view has
  migrated, the §1 coupling rule applies: migrated views' SC suites re-run**
  *(ISR2-F1)*.

### P-003 — axe-core harness integration *(parallel; new step per ISR1a-F1)*

- **Objective.** Add axe-core to the Vitest/Playwright browser harness under
  the U-16 approval; prove it with an assertion against an existing
  component.
- **Depends on.** None. **Hard prerequisite of S-002** (first step asserting
  SC5 via axe).
- **Entry gates.** None (U-16 resolved 2026-08-02).
- **Exit criteria.** **SC8: `package.json` diff shows axe-core as a
  devDependency only, citing the U-16 approval** *(ISR1b-F5)*; one passing
  axe assertion in the suite.

---

## 3. Unknown routing (from HLPS §7)

| Unknown | Routed to |
|---|---|
| U-3 (field priority) | **Authored in S-001b's JIT Spec for the deployment-request entity (user-confirmed there), consumed unchanged by S-002** *(ISR1a-F6)*; each other migration step's JIT Spec for its own entity |
| U-4 (single threshold) | Default adopted (HLPS §3.4); revisit only on pilot evidence *(ISR1b-F16)* |
| U-5 (pilot set) | **User confirmation is part of approving this IS** — see Checkpoints *(ISR1b-F9)* |
| U-6 (naming) | **S-001b JIT Spec, user-confirmed, HLPS §7 defaults as the proposal** *(ISR1b-F1)* |
| U-7 residual | S-002 **and S-006** (`request-controls` — both views render it), S-004 (`edit-comments-controls`) *(ISR1b-F10)* |
| U-8b (codebase-wide renderer cost) | Input to S-008 item 5 *(ISR1b-F16)* |
| U-9, U-13 | HLPS defaults adopted; revisit on pilot evidence |
| U-10 residual (bar design) | S-001a decides the mechanism; S-001b JIT Spec the layout/content |
| U-12 (test pattern) | S-001b JIT Spec |
| U-14 residuals | S-001a/S-001b (mechanics) + SC10 assertions in **every** migration step (now including S-003, S-005) |
| U-15 (activation) | S-002 and S-006 JIT Specs |
| U-19 (rollback) | **Default proposed** — one-commit revert per view, stated in each JIT Spec — **user-confirmed at this IS's approval checkpoint** (HLPS leaves the choice open) *(ISR1b-F11)* |
| U-20 (new-view idiom) | **S-008 item 5 deliverable**: `DECISION-new-view-idiom.md`, user-approved *(ISR1b-F2)* |

## 4. Checkpoints (per CLAUDE.md)

- **This IS**: adversarial review, then user approval before S-001a begins.
  Approval explicitly covers: **U-5 (the pilot set S-002…S-007 against HLPS
  §3.3)** and **U-19 (the one-commit-revert default)** *(ISR1b-F9/F11)*.
- **Every step, S- and P-**: JIT Spec reviewed by the panel **and approved by
  the user** before the step enters Delivery (auto-pilot not enabled)
  *(ISR1a-F10)*.
- **S-001a's decision note**: own approval gate (it changes the design's
  highest-risk mechanism).
- **S-002's failure path** and **S-008 item 5** are hard stops.

---

## 5. IS-R1 Adversarial Review — Triage

Panel: ISR1a (ordering/dependencies/atomicity), ISR1b (HLPS traceability).
2/2 REVISE. Both confirmed the step *order* sound and faithful to HLPS §8;
the findings were scaffolding and coverage, all accepted, none rejected.

| Finding | Disposition |
|---|---|
| ISR1a-F1 (S-001 non-atomic; spike can invalidate its own approved spec) | **ACCEPT.** Split S-001a (spike → standalone decision note, `grid-menu-a11y` precedent) / S-001b; axe-core moved to P-003. |
| ISR1a-F2 (SC10 missing from S-003/S-005, contradicting the routing table; those views carry the exact constructs SC10 protects) | **ACCEPT.** Added, with the declarative-sorter first-contact named at S-003 (verified: 2 sort-columns) and `auto-width` recalc at S-005 (5 occurrences). |
| ISR1a-F3 (no dependency fields; SC9 chain unstated; S-008 gateless) | **ACCEPT.** Hard/soft `Depends on` per step + summary table; S-008 entry gates added. |
| ISR1a-F4 (failure paths only on S-001) | **ACCEPT.** S-002 halt-and-revise; S-008 per-gate remediation loops with second-failure escalation; item 5 conditional. |
| ISR1a-F5/F8, ISR1b-F13 ("disjoint code" false; P-001 coordination asymmetric; U-18 re-run caveat) | **ACCEPT.** Claim withdrawn; §1 interaction notes cover P-002↔S-001b (verified: constant defined once, imported once), P-001↔S-003/S-004, and the instrument-validity caveat. |
| ISR1a-F6 (U-3 circular as written) | **ACCEPT.** Authored in S-001b's spec, consumed by S-002; entry gate added. |
| ISR1a-F7 (P-steps lack the step template) | **ACCEPT.** P-001/P-002/P-003 in the full template. |
| ISR1a-F9 (cross-step coupling unhandled) | **ACCEPT.** §1 coupling rule: shared-artifact changes re-trigger prior views' SC suites. |
| ISR1a-F10 (per-step checkpoint weaker than CLAUDE.md) | **ACCEPT.** Panel review **and user approval**, P-steps included. |
| ISR1a-F11 (SC range sweeps in inapplicable SC4; S-007 no SC5; U-7 row) | **ACCEPT.** S-004 criteria enumerated (verified: 0 `dataProvider` refs); SC5 added to S-007; U-7 row corrected. |
| ISR1a-F12 (non-author executor unresourced) | **ACCEPT.** Arranged at S-007 entry with a named substitution rule. |
| ISR1b-F1 (U-6 vanished) | **ACCEPT.** Routed to S-001b, user-confirmed. |
| ISR1b-F2 (U-20 decision record orphaned) | **ACCEPT.** Named S-008 deliverable with a filename. |
| ISR1b-F3 (test migration pinned to the wrong step) | **ACCEPT.** In every migration step, suites named where known (verified: the cited assertions are on S-005/S-006's views). |
| ISR1b-F4 (baseline capture not an entry gate) | **ACCEPT.** Entry gate on every migration step. |
| ISR1b-F5 (SC8 unowned; axe approval trail unstated) | **ACCEPT.** P-003 exit criterion. |
| ISR1b-F6 (SC7/§6.8 static review unowned) | **ACCEPT.** S-008 gate 4. |
| ISR1b-F7 (manual SR spot-check dropped) | **ACCEPT.** Restored in S-002 and inherited. |
| ISR1b-F8 (fixture has no builder) | **ACCEPT.** S-007 prepares; S-008 entry gate. |
| ISR1b-F9 (U-5 confirmation not surfaced) | **ACCEPT.** Named in Checkpoints. |
| ISR1b-F10 (U-7 routing omitted S-006) | **ACCEPT.** Corrected (verified earlier: `env-monitor` renders `request-controls`). |
| ISR1b-F11 (U-19 unilaterally closed) | **ACCEPT.** Restated as a default for user confirmation at this checkpoint. |
| ISR1b-F12 (underived, inconsistent view counts — third occurrence of the prohibited defect class) | **ACCEPT.** 32 = 37 − 5, derivation inline; sort-column figure re-derived (22 = 24 files − 2 pilot members, by `grep -l`). |
| ISR1b-F14 (template-sharing clause invented) | **ACCEPT.** Restated conditionally; SC3 resolves per view. |
| ISR1b-F15 (§6.11 type/lint unowned) | **ACCEPT.** Global exit condition at head. |
| ISR1b-F16 (U-4/U-8b routing rows absent) | **ACCEPT.** U-4 revisit-on-pilot-evidence; U-8b an input to S-008 item 5. |
| ISR1b-F17 (S-003 actions slot silent) | **ACCEPT.** Override button named. |
| ISR1b-F18 (S-005 SC1 reads as substituted oracle) | **ACCEPT.** Rephrased as stricter-than, citing MEASURE §2. |

**Rejected: none.**

---

## Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|---|---|---|---|---|
| — | 2026-08-02 | DRAFT | — | Authored from the approved HLPS. |
| **IS-R1** | 2026-08-02 | **REVISION** | 2 (ordering/dependencies; HLPS traceability) | **2/2 REVISE.** Order confirmed sound; scaffolding and coverage defects — S-001 split, SC10 gaps on the two views carrying the protected constructs, four HLPS drops (U-6, U-20 record, SC8, baseline capture), U-7 routing contradiction, and a third occurrence of the underived-figure defect class. Triage §5; all findings accepted. |
| — | 2026-08-02 | REVISION | — | Full triage implemented. |
| **IS-R2** | 2026-08-02 | **IN REVIEW** | 1 verifier | **APPROVE WITH CHANGES.** Triage verified genuinely implemented; every re-derivable figure matched (incl. 32 = 37−5 and 22 = 24−2 under the HLPS counting rule). One MEDIUM (coupling-rule scope excluded P-002's post-migration controller sweep — its own §1 interaction note contemplated it) + two LOWs; all three edits applied same day. **Awaiting user approval, covering U-5 and U-19.** |
