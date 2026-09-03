# SPEC-S-001 — Decision Note for HLPS U-2 (active-item-changed semantics)

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing IS | `docs/grid-menu-a11y/IS-grid-menu-a11y.md` (APPROVED) |
| Step | S-001 |
| Target branch | `claude/github-azure-devops-interop-vhDvn` (PR #584) |

---

## 1. Purpose

S-001 produces `docs/grid-menu-a11y/DECISION-U-2.md` — a standalone
decision note that resolves HLPS U-2 (the semantics of `<vaadin-grid>`'s
`active-item-changed` event and the activation-gate strategy) and
selects one of three design paths for S-002.

This spec defines the requirements for that note. The note itself is a
deliverable — its content is not pre-written here.

## 2. Branch and commit strategy

- All work lands on the existing PR branch
  `claude/github-azure-devops-interop-vhDvn`. No per-step branch.
- A single commit introduces the new file `DECISION-U-2.md`. No other
  files are modified.
- Commit message style follows existing PR commits (e.g.
  `PR #584: …`). The exact wording is at the delivery agent's
  discretion provided it identifies the artifact and the chosen path.
- The commit lands and is pushed before S-002's JIT spec is submitted
  for Adversarial Review, since S-002's pre-execution self-audit
  requires `DECISION-U-2.md` status to be `APPROVED`.

## 3. Investigation scope and evidence requirements

The decision note must base its conclusions on evidence, not assertion.
"Evidence" here means one of:

- citation to authoritative Vaadin documentation for the resolved
  `@vaadin/grid` version recorded in
  `src/dorc-web/package-lock.json`;
- citation to the corresponding `@vaadin/grid` source, anchored by a
  git tag or commit hash that matches the resolved version (so a
  panel reviewer can resolve the citation deterministically);
- a runnable observation in the dorc-web dev environment, captured as
  a panel-verifiable artifact: an inline console-log transcript, a
  reference to a committed screenshot in
  `docs/grid-menu-a11y/`, or a screen-recording note. A reviewer who
  cannot re-run the dev env must still be able to assess the
  observation.

Additional evidence rules:

- **Support relation**: each citation must support the specific answer
  given. A citation that only tangentially relates to the question
  does not satisfy the evidence rule.
- **Version anchor**: the note must declare, up front, the exact
  `@vaadin/grid` version under investigation (resolved from
  `package-lock.json`). All doc and source citations are interpreted
  against this version.
- **Docs ↔ source conflicts**: where the documentation and source
  disagree for the version in use, the note must surface the conflict
  explicitly and treat the source as authoritative.
- **Non-deterministic observations**: if a runnable observation does
  not reproduce reliably, the note must record the inconclusive
  result, supplement with a source-level citation, or record an
  explicit unknown. The note must not present timing-dependent
  observations as if they were deterministic facts.

The note must surface evidence for at least the following questions.
The author may add others. The questions below frame the minimum.

| ID | Question | Why it matters |
|---|---|---|
| Q1 | Which user gestures cause `<vaadin-grid>` to emit `active-item-changed`? Specifically: mouse click, touch tap, pen tap, keyboard Enter, keyboard Space, arrow-key focus change between rows, page-up/down, programmatic `grid.activeItem = X`. | Determines whether arrow-drift can be distinguished from explicit activation purely by inspecting the event. |
| Q2 | When the existing row-click handler resets `grid.activeItem = null`, does the grid re-emit `active-item-changed` with `e.detail.value === null`? How is the re-entry currently handled by whatever guard is in place? | The HLPS §4.2 arrow-key suppression bullet requires correct handling of this re-entry case. |
| Q3 | Are there alternative grid events available (e.g. `item-click`, `cell-click`, focus-related events) that more cleanly express "the user activated this row" vs "focus drifted to this row"? | Determines whether Path B (replace the gate with a different primitive) has a clean handle to attach to. |
| Q4 | What is `<vaadin-grid>`'s native keyboard model for rows? Are rows focusable via the grid's own keyboard navigation, do they receive document focus, what is their tab-order participation, and what is the default behaviour of Enter/Space on a focused row? | Determines whether row-level Enter/Space activation in S-002 requires bolting on focus management or can rely on the grid's existing model. |
| Q5 | Are there any irreducibly mouse-coupled grid behaviours (e.g. open-on-active-item that fires only for pointer events) that would prevent any combination of Path A or Path B from satisfying HLPS §4.2? | Identifies whether Path C ("neither A nor B adequate") applies. Note: this is a negative claim; source-level citation is generally required to defend it. |

## 4. Note structure

`DECISION-U-2.md` must contain at minimum the following sections, in
this order:

1. **Front-matter** — status field carrying its own document-status
   lifecycle (`DRAFT → IN REVIEW → APPROVED`), owner, governing IS
   reference, and the resolved `@vaadin/grid` version under
   investigation (per §3 version anchor).
2. **Decision** — one paragraph naming the chosen path (A, B, or C).
   The rationale must reference the Q-findings (typically including
   Q2, Q3, Q5) that materially constrain the choice; a path chosen
   without traceable reference to the findings does not satisfy the
   acceptance criteria.
3. **Investigation findings** — answers to Q1..Q5 with their evidence
   citations. Other observations the author considers material may
   appear here.
4. **S-002 implications for the chosen path** — what the chosen path
   entails for S-002 in plain language (not code): which event(s) the
   gate listens to, which keys are handled, which lifecycle hooks are
   used, etc. If Path C is selected, this section names the specific
   gap that prevents A and B from satisfying HLPS §4.2 and describes
   the IS revision required. This section is a forward-pointer; it is
   not the S-002 spec itself.
5. **Rejected alternatives** — for the path(s) not chosen, a brief
   rationale (one paragraph each is sufficient). This section does
   NOT duplicate the depth of §4.

## 5. Acceptance criteria

The note is acceptable when all of:

1. The file `docs/grid-menu-a11y/DECISION-U-2.md` exists, is committed
   and pushed.
2. Its status field is set to `IN REVIEW` at submission and to
   `APPROVED` only by the Adversarial panel (per CLAUDE.local.md §2).
3. Each of Q1..Q5 has a documented answer accompanied by at least one
   piece of evidence whose content **supports the specific answer
   given** (mere existence of a tangential citation does not satisfy
   this criterion).
4. The chosen path is one of A, B, or C, named in the Decision
   section, with rationale that traces explicitly to the Q-findings
   that materially constrain the choice.
5. If Path C is selected, the §4 "S-002 implications" section names
   the specific gap (e.g. an irreducibly mouse-coupled Vaadin
   behaviour) that prevents A and B from satisfying HLPS §4.2, and
   describes what the IS revision must add. A note that says only "IS
   revision required" without naming the gap does not satisfy this
   criterion.
6. The note contains no code, and no pseudocode whose identifiers
   (method names, event names, property names, argument shapes) the
   S-002 spec or implementation could mechanically inherit. Per
   CLAUDE.local.md JIT Spec abstraction rules, the constraint applies
   recursively to anything this note tells the next layer.

## 6. Out of scope

- Any code change to `page-monitor-requests.ts`, `env-monitor.ts`,
  `project-controls.ts`, or any other source file. The note is the
  only deliverable.
- Writing the S-002 JIT spec. That is a downstream artifact and is
  produced after this note is APPROVED.
- Investigation beyond Q1..Q5 unless the author judges additional
  questions material to the path choice. Investigation breadth is at
  the author's discretion within the constraint of "enough evidence
  to defend the chosen path on adversarial review".

## 7. Adversarial Review

The note is reviewed by an Adversarial panel as a first-class
submission, independently of any other artifact. Per the IS R1 review
discussion, the panel must:

- evaluate the rigour of the investigation (are Q1..Q5 substantively
  answered, with evidence rather than assertion?);
- evaluate the soundness of the path choice (does the evidence
  support A, B, or C, and does the Decision rationale trace to the
  Q-findings per AC #4?);
- check for hallucinated facts: cited Vaadin docs and source must be
  resolvable against the version anchor declared in the note's
  front-matter (per §3); a citation whose target cannot be located
  by a panel reviewer using only the version anchor is grounds for
  rejection. Runnable observations must include enough of the
  panel-verifiable artifact (transcript / screenshot / recording
  note) to be assessable without re-running the dev env.

Findings follow the same triage rules as any other artifact in this
project (Accept / Downgrade / Defer / Reject; cycle limit 3 rounds).

## 8. Verification environment

Per IS §6.1: Windows + Chromium + (optionally) NVDA. For S-001
specifically, NVDA is not required since this step produces no UI.
The dorc-web local dev server (`npm run dev` or equivalent) is the
only environment used for runnable observations.

## 9. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| Vaadin grid behaviour differs between major versions; dorc-web is on a version where the path choice flips. | Note's front-matter declares the resolved `@vaadin/grid` version (per §3 / §4 version anchor). A future Vaadin upgrade that flips the answer makes this note a snapshot — the IS would need re-revision at that time. |
| Path C is selected and the IS revision becomes substantial. | Per IS §4.1 risk row 1, Path C triggers IS revision through the standard process. The note's "S-002 implications for the chosen path" section becomes the input to that revision. |
| The author selects a path that "looks reasonable" but doesn't fully cover the §4.2 success criteria. | Adversarial Review panel is briefed (per §7) to test the path choice against every HLPS §4.2 criterion and to require Decision-section rationale to trace to Q-findings. |
| Q2 (the `activeItem = null` re-entry case) is timing-dependent and the author observes one behaviour while the panel reviewer or a future S-002 implementer would see another. | §3 non-determinism rule requires the note to surface inconclusive observations and supplement with source-level citation. The author should also exercise at least one full panel open/close cycle before recording the Q2 observation, so the re-entry path has been actually traversed. |

---

## Review History

### R1 — DRAFT → REVISION

Three independent reviewers (clarity/completeness, risk/feasibility,
evidence rigour) returned APPROVE_WITH_FIXES. Findings and dispositions:

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| Evidence must support the claim, not merely exist; docs ↔ source conflict resolution; non-determinism fallback; observation form must be panel-verifiable; version/tag anchor required up front | R1-clarity F-01, F-03; R1-risk F-01; R1-evidence F-01, F-02 | Accept | §3 evidence rules expanded to add support-relation, version anchor (resolved from `package-lock.json`), docs ↔ source conflict rule, non-determinism fallback, and panel-verifiable observation form. §4 front-matter and Decision sections updated to require the version anchor and Decision-to-Findings traceability. §7 panel briefing updated. |
| Decision section must trace to Q-findings | R1-clarity F-02 | Accept | New AC #4 added; §4 Decision section now explicitly requires rationale linked to Q-findings. |
| Q2 references a guard expression that may not match the codebase | R1-clarity F-04 | Accept | Reworded Q2 as a behavioural question ("how is the re-entry currently handled by whatever guard is in place"). |
| §4.4 vs §4.5 path-comparison vs rejected-alternatives ambiguity | R1-clarity F-05 | Accept | §4 restructured: §4.4 covers chosen-path implications in depth; §4.5 covers rejected alternatives briefly; Path C handled as a special case within §4.4. |
| AC #6 "verbatim" too weak — "mechanically inherit identifiers" | R1-evidence F-03 | Accept | AC #6 reworded to ban code and pseudocode whose identifiers the S-002 spec or implementation could mechanically inherit. |
| Path C must name the specific gap | R1-risk F-03 | Accept | AC #5 expanded to require Path C to name the specific gap, not just "IS revision required". |
| `package.json` vs `package-lock.json` source-of-truth inconsistency | R1-clarity F-07; R1-risk F-02 | Accept | All version references consolidated on `package-lock.json` (which carries the resolved exact version). |
| Cold-start reproducibility — Q2 timing dependency | R1-risk Risk-Gap | Accept | New §9 risk row added requiring at least one full panel open/close cycle before recording Q2 observation. |
| Q4 benefits from two evidence types | R1-evidence F-04 | Defer to Delivery | Author's discretion within §6 investigation breadth. The note's reviewer can flag insufficient evidence on Q4 specifically without the spec needing to enforce it. |
| Race-condition Q6 (arrow + programmatic reset interleaving) | R1-clarity F-06 | Defer to Delivery | Author may add Q6 if material per §6; not required to make S-001 succeed. |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers
must verify R1 fixes, check for regressions, and (per CLAUDE.local.md
§4 Re-Review Scoping) NOT mine for new findings on R1 text that was
implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome | Notes |
|---|---|---|---|
| Reviewer A (Opus) | Clarity / completeness | **APPROVE** | All seven R1 findings (F-01..F-07) verified resolved. AC #6 "mechanically inherit identifiers" tightens rather than contradicts the original verbatim ban. No regressions. |
| Reviewer B (Sonnet) | Risk / feasibility | **APPROVE** | All four findings (F-01..F-03 + Risk-Gap) verified. Few-hours timebox unchanged — R1 fixes are quality gates on output, not extra output. |
| Reviewer C (default) | Evidence rigour | **APPROVE** | All four findings verified. Q1..Q5 remain answerable; new §3 rules close the rigour gaps. With `package-lock.json` anchor + git tag/commit on source citations, panel can deterministically resolve any cited reference. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
