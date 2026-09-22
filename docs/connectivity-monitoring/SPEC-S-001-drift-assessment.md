# SPEC-S-001 — Drift assessment + ratify/revise/supersede decision

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing IS | `docs/connectivity-monitoring/IS-connectivity-monitoring.md` (APPROVED) |
| Step | S-001 |
| Target branch | TBD by S-001's own output (see §3) |

---

## 1. Purpose

S-001 produces two artifacts:

1. **A decision note** — `docs/connectivity-monitoring/DECISION-S-001-drift.md` —
   that assesses the drift between PR #374's branch tip and current
   `main`, weighs the available routes (ratify / revise), and pins
   one as the chosen path for downstream IS steps.
2. **The branch setup** — PR #374's branch fast-forwarded /
   conflict-resolved against current `main`, ready for
   downstream-step verification commits to land on.

The chosen route gates **every** downstream step (S-002..S-011).
Without S-001 the IS author for any later step has no defined
starting point for their work.

**Constraint vs IS:** the IS S-001 (APPROVED) lists three routes
(ratify / revise / supersede). Per user directive 2026-04-28, the
supersede route is excluded for this work — all changes must land
on the existing PR #374. This SPEC therefore narrows the choice to
ratify vs revise. The decision note must still acknowledge the
supersede option in its Rejected Alternatives section so that
future readers see the constraint was applied deliberately, not
by oversight.

**Likely outcome:** the HLPS U-2 resolution mandates an
`IHostedService` + `Timer` impl. PR #374's current shape is
`BackgroundService`. No amount of merge conflict resolution
converts one into the other — that is a behavioural change. Q3 / Q4
will confirm, but the decision-note author should approach the
investigation expecting **revise** to be the chosen route, with
ratify reserved for the unlikely case PR #374 already meets every
HLPS contract item modulo the merge.

This spec defines the requirements for the decision note and the
branch setup. The note's content is a deliverable — its conclusions
are not pre-written here.

## 2. Pre-execution self-audit

Before starting S-001:

- [ ] HLPS-connectivity-monitoring status = APPROVED (user-approved)
- [ ] IS-connectivity-monitoring status = APPROVED (user-approved)
- [ ] This SPEC status = APPROVED (user-approved or auto-pilot)
- [ ] No in-flight adversarial reviews on connectivity-monitoring artifacts
- [ ] Working tree is clean (no uncommitted tracked changes)
- [ ] PR #374's branch is fetchable (network access to GitHub)
- [ ] **CI on `origin/main` is currently passing.** Confirm via the
      latest commit's required-checks status. If CI is broken on
      `main` for unrelated reasons, A4's "CI passes after merge"
      criterion becomes unachievable through no fault of S-001;
      the spec author must record the baseline and weaken A4 in
      the note from "all required checks green" to "no new
      failures vs the recorded `main` baseline".

If any item is unchecked, **STOP** and address it.

## 3. Branch and commit strategy

S-001 itself runs on the **current `main` branch** — the decision
note is committed to `main` (or to a small dedicated working branch
that fast-forwards back to `main` once the note is approved). This
keeps the planning artifact decoupled from the route choice.

The branch-setup output for the two permitted routes:

- **ratify**: fetch `pull/374/head` and merge `origin/main` into
  it. Conflicts are resolved per the assessment in the note;
  resolution rationale is recorded in the commit message
  **for semantically significant conflicts only** — files
  characterised in Q2 as semantic conflicts. Mechanical /
  trivial conflicts (e.g. namespace renames from PR #649) are
  grouped by type with one summary line per group. PR #374's
  branch is updated such that `origin/main` is an ancestor of
  the new tip; force-push semantics are addressed below. Output:
  PR #374's branch on a state where `origin/main` is in its
  ancestry, with no outstanding conflicts.
- **revise**: identical to ratify mechanically (merge first), but
  the note enumerates the surfaces that subsequent steps will
  rework. Output: same — PR #374's branch ready, with the
  rework-surface list captured in the note.

**Force-push handling:** if PR #374's branch has been rebased
since its last conflict-free state, the merge commit may not be
fast-forwardable, requiring a force-push. **Force-push is
permitted under this spec only with the user's explicit consent
in the note** — record the user-confirmation as a line item in
the note's Decision section before the push happens. Force-push
silently resets GitHub's review-comment state, including any
outstanding CodeQL threads, so the note must record the pre-push
state of those threads (Q6 captures this) and the post-push state
must be verified to match.

The decision note's conclusion includes PR #374's resolved
branch-tip commit SHA after the merge, so downstream JIT specs
reference a stable target.

The note itself is committed with a commit message of the form
`docs(connectivity-monitoring): S-001 drift assessment, route
chosen = <ratify|revise>` so the route choice is greppable from
the git log.

The merge / conflict-resolution commit on PR #374's branch is
**separate** from the note's commit (different branches anyway).
Its commit message records the resolution rationale per conflict
file.

## 4. Investigation scope and evidence requirements

The decision note must base its conclusions on **evidence**, not
assertion. "Evidence" means one of:

- **Concrete git output** — a `git diff`, `git log`, `git
  merge-base`, `git diff --stat`, or equivalent command captured
  inline (or as a referenced file under
  `docs/connectivity-monitoring/S-001-evidence/`).
- **Citation to a specific commit / PR / file path** — by SHA or
  permalink that resolves on `sefe/dorc`.
- **Citation to a specific HLPS or IS section** for previously-
  resolved decisions (e.g. "U-2 resolution: Timer impl required").
- **Reasoned analysis with citation** — for questions that
  require code-reading and behavioural reasoning rather than
  just observation (typically Q3 / Q4). Each load-bearing claim
  in the analysis must anchor to a specific file path + SHA (or
  line range at a SHA) so a reviewer can replay the read. This
  is the heaviest-burden category and must be used wherever the
  three observation-only types fall short.

Assertion-shaped statements without evidence are not acceptable.

**Negative-claim rule:** any answer of the form "no material
change", "no conflict", "no obstruction", etc., must include the
**bounded search** that justifies the negative — the specific
`git log` range with path filters, the specific `grep` query
across the touched surface, etc. — not just a summary assertion.
A reviewer must be able to re-run the same search and confirm
the empty result.

**Runtime-deferral fallback:** questions whose answers depend on
runtime behaviour (e.g. "does the SQLProj migration apply?")
cannot be answered in S-001 by any of the four evidence types
above; they must be answered by **deferral citation** to a
downstream step's verification intent (typically S-002's
dry-deploy for migration questions). The deferral citation must
name the step and the specific verification item.

The note must surface evidence for **at least** the following
questions. The author may add others. The questions below frame the
minimum.

| ID | Question | Why it matters |
|---|---|---|
| Q1 | What does the commit graph look like — how many commits is `pull/374/head` behind `main`? What is the merge base? | Establishes the size and shape of the drift. |
| Q2 | What is the **complete** set of conflict files when running `git merge --no-commit --no-ff origin/main` from a checkout of `pull/374/head`? Per file, characterise each conflict (semantic vs. trivial vs. mechanical). | The two flagged so far (`Server.cs`, `ServerEntityTypeConfiguration.cs`) are partial; the JIT spec author must enumerate the full set. |
| Q3 | How does the daemons modernisation (PR #649, merged `f70404e6`, including the rename `Server.Services` → `Server.Daemons` plus `ServiceStatus` → `DaemonStatusProbe`) interact with PR #374's diff? Does any file in PR #374 reference identifiers renamed by #649? | One of the largest mid-flight semantic changes in the drift window. |
| Q4 | What Monitor-side DI, hosting, and configuration changes have landed since PR #374's branch tip? Specifically: registration sites for `IHostedService` implementations, any new `BackgroundService` patterns, any `Task.Yield()`-style fixes that PR #374's `MonitorService.cs` change would conflict with. | Per HLPS U-2 the IS will land an `IHostedService`+Timer impl; this affects the merge surface. |
| Q5 | What schema state does `main` currently have on `SERVER` and `DATABASE` in `src/Dorc.Database/dbo/Tables/`? Structurally identify any intervening additions / renames / index changes since PR #374's branch tip that **might** obstruct SC-1's schema delta. (Running the migration to actually verify clean apply is **S-002's** job per HLPS U-7 — S-001 is structural / file-level analysis only.) | SC-1 is gated by U-7 — Q5 surfaces the structural drift; the runtime verification is deferred to S-002. |
| Q6 | What is the status of PR #374's outstanding CodeQL review threads? The commits `d3005f12` (resource injection fix) and `8413dea6` (log forging fix) are recorded as in-tree fixes; verify they are in the *current* tree of PR #374's branch and not lost in any of the rebases. | Required reconciliation under the ratify route. |
| Q7 | Of PR #374's **own diff files** (the files the PR adds/modifies — *not* the 251 intervening commits on `main`), what is salvageable (lands cleanly on `main` or with trivial conflicts)? What requires non-trivial rework against the HLPS contract? Categorise file-by-file. | Drives the ratify-vs-revise choice (supersede excluded by user directive — see §1). |

## 5. Note structure

`DECISION-S-001-drift.md` must contain at minimum the following
sections, in this order:

1. **Front-matter** — status field with its own document-status
   lifecycle (`DRAFT → IN REVIEW → APPROVED`), owner, governing
   IS reference, the resolved `pull/374/head` commit SHA at
   investigation time, the resolved `origin/main` commit SHA at
   investigation time, **the merge-base SHA** between those two
   (`git merge-base <pr374-sha> <main-sha>`), and the **named
   ref** at which the post-merge tree was pushed (typically PR
   #374's branch tip after resolution; an `origin/<branch>` ref
   that a reviewer can `git fetch` directly). With these four
   anchors a reviewer at T+1 day can fetch the exact tree the
   author saw and replay any of the Q1..Q7 commands.
2. **Decision** — one paragraph naming the chosen route (ratify
   or revise) plus PR #374's resolved branch-tip SHA after the
   merge.
3. **Investigation findings** — answers to Q1..Q7 with their
   evidence citations. Other observations the author considers
   material may appear here.
4. **Route comparison** — for the two permitted routes (ratify,
   revise), what pursuing it would entail in terms of:
   - mechanical work in S-001 (the merge + conflict resolution)
   - shape of downstream steps (S-002..S-011)
   - estimated scope of rework against the HLPS contract,
     **expressed as the count of HLPS success criteria (SC-1..
     SC-11) and unknowns (U-1..U-10) whose satisfaction requires
     new code beyond what PR #374 already contains**. (This unit
     replaces vague "scope of rework" estimates and is mechanically
     reviewable.)
   - residual risk (e.g. stale CodeQL re-firing, bot-authored
     code patterns that don't match repo conventions)
5. **Rejected alternatives** — one paragraph each for:
   - the **non-chosen** permitted route (ratify or revise);
   - the **supersede** route (excluded by user directive 2026-04-28
     — record the directive as the rejection reason, not a
     comparative analysis).
6. **Implications for S-002..S-011** — a forward-pointer summary
   of what each downstream JIT spec author needs to know:
   - the chosen branch name and SHA
   - any specific identifiers that S-002+ should beware (e.g.
     "the daemons rename means `Server.Services` is now
     `Server.Daemons` — S-006's API model should reference the
     new name")
   - any inherited risk (e.g. "PR #374's `MonitorService` change
     conflicts with main; downstream steps must not regress
     main's existing `Task.Yield` / cancellation handling")

## 6. Acceptance criteria

A1. The file `docs/connectivity-monitoring/DECISION-S-001-drift.md`
    exists, is committed and pushed, and its status field is
    `IN REVIEW` at submission and `APPROVED` only by the Adversarial
    panel.
A2. Each of Q1..Q7 has a documented answer accompanied by at least
    one piece of evidence whose **content supports the specific
    answer given** (not merely a tangential citation). For Q2, the
    answer must include the complete file list, not a sample.
A3. The chosen route is one of `ratify` or `revise`, named in the
    Decision section. The rationale must trace explicitly to the
    Q-findings that materially constrain the choice (typically
    Q3, Q4, Q7). The supersede route must be acknowledged as
    rejected per user directive in the Rejected Alternatives
    section.
A4. The branch-setup output corresponding to the chosen route is
    complete:
    - **ratify**: PR #374's branch has `origin/main` as an
      ancestor (or contains a merge of it) at submission, with
      no outstanding conflicts, AND PR #374's required GitHub
      checks are green at the post-merge tip — measured against
      the baseline recorded in §2's pre-execution self-audit.
      If `main`'s baseline showed pre-existing failures, the
      acceptance bar is "no NEW failures introduced by the merge"
      against that baseline (with the baseline failures itemised
      in the note).
    - **revise**: same as ratify, plus the note enumerates the
      surfaces that subsequent steps will rework.
A5. The note's "Implications for S-002..S-011" section is concrete
    enough that a downstream JIT spec author can reference it
    without re-running the drift assessment.
A6. The note contains no code that the delivery agent would be
    expected to copy verbatim into a downstream step's
    implementation. Pseudocode and plain language only — per
    CLAUDE.local.md JIT Spec abstraction-level rules.
    Pseudocode must avoid concrete C# syntax (no method
    signatures with parameter types, no `var`, no LINQ chains
    that map 1-to-1 onto code); plain-language step lists or
    pure pre/post-condition statements are fine.

## 7. Out of scope

- Any change to `src/`, `docs/` (other than the note itself and
  the optional `S-001-evidence/` folder), or any other source-code
  surface. The route-conditional branch setup is mechanical (a
  rebase or a branch cut) and is in scope per §3, but it does not
  modify any source content beyond resolving merge conflicts.
- Writing the JIT spec for S-002. That is downstream work.
- Investigation beyond Q1..Q7 unless the author judges additional
  questions material to the route choice.
- Resolving any of the U-* unknowns from the HLPS that S-001 is
  not specifically gating (U-2, U-7 are in scope; the rest are
  not).

## 8. Verification environment

- A working git checkout of `sefe/dorc` with network access to
  fetch `pull/374/head` and `origin/main`.
- The dorc-web `npm install` and Dorc.Core `dotnet build` toolchain
  is **not** required for S-001 — this is a planning artifact, not
  a code build. (Under the ratify route, the JIT spec for S-002
  will require the build; S-001 only confirms the merge succeeds
  textually.)

## 9. Adversarial Review

The note is reviewed by an Adversarial panel as a **first-class,
standalone review submission**, independently of any other
artifact. Per IS S-001 verification intent, the panel must:

- Verify the conflict list in Q2 is complete by replaying the
  `merge main → pr-374` themselves and cross-checking.
- Verify the chosen route's rationale is sound — does the evidence
  support the choice?
- Check for hallucinated facts: cited commit SHAs must resolve on
  `sefe/dorc`; cited file paths must exist; cited PR numbers must
  match the claimed merged-state.
- Reject the note if Q2 is incomplete, if the route choice doesn't
  trace to the findings, or if the branch-setup output (per A4)
  is incomplete.

Findings follow the same triage rules as any other artifact in
this project (Accept / Downgrade / Defer / Reject; cycle limit
3 rounds).

## 10. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| The investigation reveals that PR #374's diff is so badly drifted that mechanically merging `main` produces a thicket of conflicts that nobody wants to resolve — but the user directive forbids supersede. | The decision note is permitted to choose **revise** (which still requires merging `main` first) and then enumerate broad rework surfaces in subsequent steps. If even revise looks unworkable, that is a finding to escalate to the user before the IS proceeds — not a quiet promotion of supersede. **Concrete escalation path:** the decision note is committed with status `DRAFT` (pending user) and a "BLOCKED — awaiting user decision" section appended. An explicit ⏸️ CHECKPOINT is raised to the user with two options stated: (a) accept the revise scope as described and proceed with downstream IS steps; (b) reopen the IS for re-sequencing, possibly amending the user-directive that excluded supersede. The IS does not advance until the CHECKPOINT is resolved. |
| The author chooses a route that "looks easiest" but isn't the right call against the HLPS contract — e.g. choosing ratify because the merge happens to apply cleanly even though PR #374's diff doesn't satisfy U-2 (Timer impl). | Adversarial Review §9 requires the rationale to trace to the Q-findings; a route chosen for ease without HLPS-contract justification is a finding to reject. The Timer/HostedService impl is a known U-2 requirement; if PR #374 currently has BackgroundService, that alone forces revise (not ratify) because U-2 cannot be silently dropped. |
| The "investigation" balloons into a multi-day study. | The Q1..Q7 set is bounded; §7 declines investigation breadth beyond what the route choice needs. Author may add Qs only when material. The investigation should fit within a working day. |
| Conflict-resolution commit on PR #374 contains genuine merge errors that pass CI but break runtime behaviour. | A4 requires CI pass; the JIT spec for downstream steps will catch behavioural regressions via their verification intent. The merge commit itself does not need exhaustive testing — that's the next step's job. |
| **PR #374's branch has been previously rebased; the merge produces a non-fast-forwardable state requiring force-push, which silently resets GitHub's review-comment / CodeQL-thread state.** | §3 force-push handling: explicit user-confirmation is required before any force-push; pre-push CodeQL thread state (Q6) is recorded in the note; post-push state is verified to match. Reviewers must look for the explicit consent line in §3 of the decision note before approving. |
| CI on `main` is broken at the time S-001 executes for unrelated reasons. | Pre-execution self-audit (§2) catches this; A4 acceptance is recalibrated to "no NEW failures introduced" against the documented baseline. |

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (clarity/completeness, risk/feasibility, evidence
rigour) returned APPROVE_WITH_FIXES with 17 findings combined.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| **A4 CI-build contradicts §8 + no CI baseline check** | R1-clarity F1 (HIGH), R1-feasibility F-01 (HIGH) | Accept | §2 pre-execution audit gains a CI-baseline check; A4 reworded to require "PR #374's required GitHub checks green at post-merge tip" measured against the recorded baseline (with "no NEW failures vs baseline" as the acceptance bar if `main` had pre-existing failures). |
| **A2 negative-claim evidence loophole** | R1-clarity F2 (HIGH) | Accept | §4 evidence rules gain a "Negative-claim rule" requiring bounded-search evidence (specific `git log` range with path filters, specific `grep` query, etc.) for any answer of the form "no material change". |
| **Reproducibility — capture merge-base + named ref** | R1-evidence F-1 (MED) | Accept | §5 front-matter now requires four anchors: `pull/374/head` SHA, `origin/main` SHA, merge-base SHA between them, and the named ref at which the post-merge tree was pushed. With these a T+1 reviewer can fetch the exact tree the author saw. |
| **Fourth evidence type for Q3/Q4-style semantic questions** | R1-evidence F-2 (MED) | Accept | §4 adds a fourth evidence category: "Reasoned analysis with citation" — paragraph of analysis whose every load-bearing claim anchors to a file path + SHA (or line range at SHA). |
| **Escalation path needs concrete artifact + CHECKPOINT** | R1-feasibility F-03 (MED) | Accept | §10 row 1 extended: escalation note is appended to the decision note with status `DRAFT` (pending user); explicit ⏸️ CHECKPOINT raised with options (a) accept revise scope, (b) reopen IS for re-sequencing. |
| **Force-push to PR #374 silently resets review state** | R1-feasibility R-GAP-2 (MED) | Accept | §3 gains explicit force-push handling: permitted only with user consent recorded in the note; pre-push CodeQL thread state captured (Q6); post-push state verified. New §10 risk row. |
| **Per-file rationale unenforceable for 100+ file merge — bound to semantic conflicts** | R1-feasibility F-02 (MED) | Accept | §3 reworded: rationale required only for conflicts characterised in Q2 as semantic; mechanical/trivial conflicts grouped by type with one summary line. |
| **"Merges cleanly" ambiguous post-merge — restate as "main is ancestor"** | R1-clarity F3 (MED) | Accept | §3 and A4 reworded to "`origin/main` is an ancestor of (or contains a merge of) the new tip". |
| **§5 rework-scope unit (count of HLPS SCs/Us requiring new code)** | R1-clarity F4 (MED) | Accept | §5 item 4 now requires the rework-scope estimate to be expressed as the count of HLPS SC-1..SC-11 / U-1..U-10 whose satisfaction requires new code beyond what PR #374 already contains. |
| **Q5 structural-only clarifier** | R1-evidence F-3 (LOW) | Accept | Q5 reworded — structural / file-level analysis only; running the migration is S-002's job per HLPS U-7. |
| **Surface ratify near-disqualifier in §1** | R1-feasibility F-04 (LOW) | Accept | §1 gains a "Likely outcome" paragraph noting U-2's `IHostedService`+Timer requirement vs PR #374's BackgroundService — ratify is reserved for the unlikely case PR #374 already meets every contract item. |
| **Q7 scope — PR's diff only, not 251 commits** | R1-feasibility F-05 (LOW) | Accept | Q7 reworded — applies to PR #374's own diff files, not the 251 intervening commits on `main`. |
| **A6 pseudocode-vs-code threshold one-liner** | R1-evidence F-4 (LOW) | Accept | A6 extended — pseudocode must avoid concrete C# syntax (no method signatures with parameter types, no `var`, no LINQ chains). |
| **Runtime-deferral fallback evidence type** | R1-evidence F-6 (LOW) | Accept | §4 extended with "Runtime-deferral fallback" — questions whose answers depend on runtime behaviour are answered by deferral citation to a downstream step's verification intent. |
| **Q8 test coverage drift** | R1-clarity F5 (LOW) | Defer to author discretion | §7 already permits the author to add Qs at their discretion. Test-coverage drift is a candidate but not required by the HLPS contract. |
| **SHA staleness re-run if main advances** | R1-clarity F6 (LOW) | Defer to author discretion | The four-anchor reproducibility fix (R1-evidence F-1) is sufficient for review reproducibility; mid-investigation `main` advance is a question for the author's process. |
| **§9 reviewer-replay assumes findable SHA** | R1-evidence F-5 (LOW) | Resolved by F-1 | Closed by the four-anchor front-matter requirement. |

After this revision, status returns to `IN REVIEW` for R2. R2
reviewers must verify R1 fixes, check for regressions, and (per
CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on
R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Clarity / completeness | **APPROVE** — All six R1 findings verified (four accepted-and-applied, two correctly deferred). Four-anchor front-matter, force-push handling, and CI-baseline plumbing all interlock cleanly with A4. No regressions. |
| Reviewer B (Sonnet) | Risk / feasibility | **APPROVE** — All five R1 findings + R-GAP-2 verified resolved. One-working-day timebox still plausible despite added rigour. No contradictions introduced. |
| Reviewer C (default) | Evidence rigour | **APPROVE** — All six R1 findings verified. Q1..Q7 evidence-rule audit clean. Reviewer-reproducibility audit confirmed: four anchors let a T+1 reviewer replay any command deterministically. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
