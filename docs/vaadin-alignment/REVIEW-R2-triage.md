# Adversarial Review R2 — Triage

| Field       | Value                                                     |
|-------------|-----------------------------------------------------------|
| **Round**   | 2 of max 3                                                |
| **Date**    | 2026-08-02                                                |
| **Subject** | `HLPS`/`IS` for dialog consistency                        |
| **Panel**   | 2 reviewers — correctness / completeness                  |
| **Verdict** | ❌ **FAILS AGAIN.** And the process itself needs changing — §6. |

---

## 1. Outcome

Round 2 found **more** defects than round 1, in a document written specifically
to carry round 1's corrections forward. Every finding below was verified against
the installed Vaadin 25.2.6 sources before acceptance.

One of them was introduced **by me, during the round 1 triage** — see R2-09.

---

## 2. Accepted — the premise-breaking ones

### R2-01 · `<vaadin-confirm-dialog>` cannot close on outside click

`vaadin-confirm-dialog-mixin.js:393` is
`_onOverlayOutsideClick(event) { event.preventDefault(); }` — unconditional. The
mixin has **no** `noCloseOnOutsideClick` property at all.

So the plan's central promise — one uniform dismissal behaviour — is **false at
the framework level**. SC-3 ("every dialog … closes by outside click") is
unsatisfiable for the confirm pattern, and SC-4's `grep` for `no-close-on-*`
returns zero while the two patterns still differ.

**Correct position:** dismissal is uniform *per pattern*, not across patterns.
Content dialogs close on Escape and outside click; confirm dialogs close on
Escape only. That is a Vaadin constraint, not a choice.

### R2-02 · Dialog content is **not** destroyed on close — I had it backwards

`vaadin-dialog-overlay-mixin.js:68-75` caches the renderer root in
`__savedRoot` forever; it is cleared only when the *renderer identity* changes.
`dialogRenderer` assigns the renderer once. **Content persists across
close/reopen.**

Both my documents state the opposite (HLPS §5 row 4, IS §1.1). Two consequences:

1. My worked example drops the `${this.isOpen ? … : nothing}` gate that
   `page-users-list.ts:99` and `page-daemons-list.ts:165` use precisely to get a
   *fresh, empty* form on each open. Copied 19 times, that ships stale form
   state on reopen.
2. R1-08's "configure-then-open guarantees a null dereference" is also wrong —
   content survives, so the query fails only on the **first** open. An
   intermittent bug is harder to catch than a deterministic one.

### R2-09 · "Zero of 12 `dialog-confirm` buttons have a handler" — my error, not the panel's

Round 1's panel said **3 of 12**. My triage escalated that to **zero**, called it
"worse than reported", reported it to the user, and wrote it into HLPS
Constraint 3.

**The panel was right.** Re-measured with correct tag parsing: 3 of 12 have
`@click` — `add-edit-access-control.ts:290`, `clone-environment.ts:195`,
`page-users-list.ts:103`.

My regex was `<vaadin-button\b[^>]*dialog-confirm[^>]*>`. `[^>]*` stops at the
`>` inside `=>` in an arrow-function handler, so every button whose handler was
an arrow function was silently classified as having none.

It matters beyond the count: those three handlers call `close()`, which performs
side effects — `clone-environment.ts` calls `_resetForm()`,
`add-edit-access-control.ts` clears `Privileges`, `ErrorMessage`, a text field
and a combo-box selection. A blanket "create a new close handler" recipe would
duplicate or drop them.

**This is the worst kind of error in this review record: introduced while
correcting someone else's work, and asserted with more confidence than the thing
it replaced.**

---

## 3. Accepted — High

| ID | Finding | Evidence |
|----|---------|----------|
| R2-03 | `attached-servers.ts` has **3** `<hegs-dialog>`s, not 2 — `#add-edit-server-dialog`, `#tags-dialog`, `#daemon-mapping-dialog`, with 3 typed `@query` refs. My §3.3 breakdown summed to 6 against my own stated total of 7. S-008 would delete `hegs-dialog.ts` with a live importer and fail `tsc`. | `attached-servers.ts:108,116,127,44,46,48` |
| R2-04 | **`confirm-dialog.ts` is dead code** — never rendered anywhere, no listeners for its events. S-003 would *invent* a delete-confirmation flow that does not exist. The live dialog count is **30**, not 31. | `grep '<confirm-dialog'` → nothing |
| R2-05 | **Native `window.confirm()` is a fifth, uncounted pattern — 23 call sites** across grid controls, pages and components. Under a "dialog usage should be the same throughout" directive these are squarely in scope, or need an explicit reasoned exclusion. Neither document mentions them. | 23 sites incl. `request-controls.ts` ×4, `server-controls.ts` ×2 |
| R2-06 | S-001 **repeats R1-04 in the very first step**: `terraform-plan-dialog.ts:27` is `private loading: boolean = false` — undecorated. Adding it to a dependency array fires nothing. The step must promote it to `@state`. | |
| R2-07 | "11 of 11 use default dismissal" is false — `terraform-plan-dialog.ts:133` sets `modeless`, so no backdrop and no outside-click listener. Already non-uniform, and SC-4's grep passes regardless. | `vaadin-overlay-mixin.js:263` |
| R2-08 | **4** existing dialogs are non-compliant, not 2. Worse: `page-servers-list.ts:151` uses `title=` (native tooltip) instead of `header-title=`, so it renders **no header** — and S-005 says "converge on `page-servers-list`'s markup", which would copy the bug. | |
| R2-10 | S-004 leaves `deploy-env.ts:181-189` listening for `deploy-confirm-dialog-begin`/`-closed`, custom events invented by the file being deleted. Deploy silently stops working. Also `closed` on `<vaadin-confirm-dialog>` is neither bubbling nor composed, and fires on *every* close including confirm — different semantics from today. | |
| R2-11 | S-004 introduces two undeclared regressions: `<vaadin-confirm-dialog>` sets `max-width: 25em` (~400px) — the deploy dialog shows a fully-expanded JSON tree with no width constraint today; and it **auto-focuses the confirm button** on open, so Enter immediately submits a deployment. | `vaadin-confirm-dialog-overlay-base-styles.js:13`, `mixin:289` |
| R2-12 | `reset-app-password-behalf.ts:81-97` has a `.renderer` **inside** its dialog — same class as `add-edit-access-control`, which the plan flagged as High. S-016 is graded Low and doesn't mention it. | |
| R2-13 | Cross-component public `open()`/`close()` APIs are in no step: `add-edit-access-control.open()` is called from **5** other files, `add-edit-project.open()` from 2, plus `clone-environment` and `reset-app-password-behalf`. | |
| R2-14 | `<vaadin-confirm-dialog>` **cannot be draggable** — `ConfirmDialogMixinClass extends DialogSizeMixin` only. HLPS §5's "12 paper + 7 hegs become draggable" is wrong for the 2 routed to confirm. | |

## 4. Accepted — Medium

R2-15 no dialog test infrastructure exists (zero tests mention dialogs), yet the
DoD mandates per-dialog Escape/outside-click assertions and 31×2 screenshots —
helpers must be built first, and Vaadin overlays attach outside `fixture()`'s
tracked container so cleanup needs handling. · R2-16 `theme-manager.ts:28`
propagates `theme="dark"` only to overlays present at toggle time, so any dialog
opened afterwards is unpatched — a second dark-mode mechanism S-019 ignores. ·
R2-17 `top: 16px`, `padding: 10px`, `box-sizing` from the `size-position` blocks
are unmapped; only width was carried. · R2-18 the "30 `PaperDialogElement`
references" figure double-counts — 9 are imports, 21 are call sites, and
`attached-databases.openDialog(name)` is a dynamic dispatcher with no direct
reactive equivalent. · R2-19 IS §1.1 says `deploy-confirm-dialog`'s `Open()` "is
safe to keep as-is" while S-004 deletes the file that defines it. · R2-20 R1-09
is mis-cited (the Depends-on correction was R1-20), so "every round-1 finding
carried forward" cannot be audited. · R2-21 R1-11/R1-12 (whole-grid re-render,
`auto-width` recalculation) are not carried into S-017 despite it converting 6
renderers across 5 `resizable` columns.

## 5. Rejected / Downgraded

- **"S-011 contradicts R1-25"** — R1-25 logged the dead listener as pre-existing;
  folding a one-line fix into a step that already rewrites that markup is
  proportionate. **DOWNGRADED to LOW.**
- **Drag-off-screen and focus-restoration changes** (R2 items 20-21 of the
  correctness report) — real but speculative; **ACCEPTED as LOW**, added to the
  risk table rather than treated as defects.

---

## 6. The Real Finding: The Process Is Not Working

Two rounds, five reviewers, and the pattern is unambiguous.

**What has been reliable:** everything derived mechanically. Dialog counts,
package counts, `size-position` blocks, `requestContentUpdate()` call sites, the
AST renderer audit — all verified intact across both rounds.

**What has failed repeatedly:** every claim about *runtime behaviour* asserted
from reading names, documentation, or a component's shape rather than executing
it. `focus-target`. Dismissal semantics. Content lifecycle. Handler presence.
Each was stated confidently, each was wrong, and each survived until a reviewer
opened the implementation.

R2-09 is the clearest signal: I corrupted a *correct* finding while triaging it,
because I re-measured with a regex I did not test against an arrow function.

Writing a third, longer plan is not the answer — rounds 1 and 2 both produced
documents whose defect count rose with their detail. The behaviour these plans
keep guessing at is cheap to observe: the app runs, the test harness drives it,
and a single converted dialog would settle dismissal, content lifecycle, focus,
sizing and close-handler semantics simultaneously and permanently.

**Recommendation: stop planning at this granularity and convert one dialog end
to end** — `page-sql-ports-list`, the simplest — building the test helpers R2-15
requires as part of it. Let the working conversion and its tests define the
pattern, then apply it. Escalated to the user rather than starting round 3.

---

## 7. Status

Both documents remain **REVISION**. No step executes. Round 3 is not started
pending the user's decision on §6.
