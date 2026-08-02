# HLPS: Unify Every Dialog on Two Vaadin Patterns

| Field       | Value                                                    |
|-------------|----------------------------------------------------------|
| **Status**  | ❌ REVISION — failed review round 2                       |
| **Author**  | Agent                                                    |
| **Date**    | 2026-08-02                                               |
| **Folder**  | `docs/dialog-consistency/`                               |
| **Supersedes** | `docs/paper-dialog-retirement/` (failed review round 1) |
| **Resolves**| AUDIT F-3b, and the wider inconsistency it sat inside     |
| **Scope**   | `src/dorc-web` — **31 dialogs, 4 implementations → 2 patterns** |

> ⚠️ **FAILED ADVERSARIAL REVIEW ROUND 2 — DO NOT EXECUTE.**
> See `../vaadin-alignment/REVIEW-R2-triage.md`. Verified defects include:
> `<vaadin-confirm-dialog>` **cannot** close on outside click, so uniform
> dismissal across both patterns is impossible; dialog content **persists**
> across close/reopen rather than being destroyed, so the worked example drops
> the open-gate that yields a fresh form; `attached-servers` has 3 hegs-dialogs
> not 2; `confirm-dialog.ts` is dead code so the live count is 30 not 31;
> native `window.confirm()` is an uncounted fifth pattern at 23 call sites;
> and Constraint 3's "zero of 12 have a close handler" was **my** error while
> triaging round 1 — it is 3 of 12, as the original panel said.

---

## 1. Directive That Shaped This Plan

> **"I want consistency across the UI more than maintaining backwards
> compatibility. So dialog usage should be the same throughout."**
> — user, 2026-08-02

This replaces the previous plan's governing constraint. That plan was
behaviour-preserving by default and treated dismissal semantics as sacred; this
one treats **uniformity** as the goal and accepts behaviour changes to reach it.
Section 5 states exactly which behaviours change.

---

## 2. Problem Statement

The UI has **four** dialog implementations for **31 dialogs**:

| Implementation | Dialogs | Files | Status |
|---|---|---|---|
| `<vaadin-dialog>` | 11 | 9 | The de-facto house standard |
| `<paper-dialog>` | 12 | 9 | Polymer 3, end-of-life, 13 npm packages |
| `<hegs-dialog>` | 7 | 5 | Bespoke `LitElement`, maintained here |
| `<vaadin-confirm-dialog>` | 1 | 1 | Used once, correctly |

The cost is not hypothetical:

- **The same dialog is implemented twice.** `#add-edit-server-dialog` — "Add/Edit
  Server", wrapping the same `<add-edit-server>` component — exists as
  `<hegs-dialog>` in `attached-servers.ts:108` and as `<vaadin-dialog>` in
  `page-servers-list.ts:165`. Two implementations, two behaviours, one feature.
- **A single file mixes implementations.** `page-daemons-list.ts` uses
  `<paper-dialog>` twice and `<vaadin-confirm-dialog>` once.
- **Three different open/close idioms coexist**: imperative
  `PaperDialogElement.open()`, reactive `.opened` + `@opened-changed`, and
  `hegs-dialog`'s `.open` property with a custom `hegs-dialog-closed` event.
- **Dismissal is inconsistent.** The 12 paper dialogs close by neither Escape
  nor outside click; all 11 Vaadin dialogs close by both.

## 3. Target — Two Patterns, Chosen From Evidence

The house standard is not being invented; it is being *identified* and applied.
Of the 11 existing `<vaadin-dialog>` elements, **9 use `header-title` and
`draggable`**, and **11 of 11 use Vaadin's default dismissal**. That majority is
the target.

### 3.1 Content dialog — `<vaadin-dialog>`

For anything hosting a form or a component.

```html
<vaadin-dialog
  header-title="Add/Edit Server"
  draggable
  .opened="${this.addEditServerDialogOpened}"
  @opened-changed="${(e: DialogOpenedChangedEvent) => {
    this.addEditServerDialogOpened = e.detail.value;
  }}"
  ${dialogRenderer(this.renderAddEditServer, [this.selectedServer])}
  ${dialogFooterRenderer(this.renderAddEditServerFooter, [])}
></vaadin-dialog>
```

- Dismissal: **Vaadin default** — Escape and outside click both close. No
  `no-close-on-esc` / `no-close-on-outside-click` anywhere.
- Sizing: the `width` / `height` properties, or `::part(overlay)` from the host
  stylesheet. Not inline wrappers — see §6.
- State: reactive `.opened` only. No `querySelector(...).open()`.

### 3.2 Confirmation prompt — `<vaadin-confirm-dialog>`

For "are you sure?" with no form.

```html
<vaadin-confirm-dialog
  header="Delete daemon"
  confirm-text="Delete"
  confirm-theme="error primary"
  cancel-button-visible
  .opened="${this.confirmDeleteOpen}"
  @opened-changed="${...}"
  @confirm="${this.performDelete}"
>Message</vaadin-confirm-dialog>
```

`page-daemons-list.ts:175` already does exactly this and becomes the exemplar.

### 3.3 `<hegs-dialog>` is deleted

Its 7 usages split cleanly by purpose:

| Usage | Target |
|---|---|
| `deploy/deploy-confirm-dialog.ts` | `<vaadin-confirm-dialog>` |
| `confirm-dialog.ts` | `<vaadin-confirm-dialog>` |
| `attached-servers.ts` ×2 | `<vaadin-dialog>` |
| `add-edit-project.ts` | `<vaadin-dialog>` |
| `page-environments-list.ts` | `<vaadin-dialog>` |

`hegs-dialog.ts` is then removed. Note `confirm-dialog.ts` and
`deploy-confirm-dialog.ts` are themselves thin wrappers over `hegs-dialog`; once
converted they may collapse into direct `<vaadin-confirm-dialog>` usage. That
consolidation is **in scope** — it is the same consistency argument one level up.

---

## 4. Scope

### In scope

- 12 `<paper-dialog>` → `<vaadin-dialog>`; delete `@polymer/paper-dialog` and
  confirm all **13** ecosystem packages leave (12 `@polymer/*` **plus**
  `@webcomponents/shadycss`).
- 7 `<hegs-dialog>` → `<vaadin-dialog>` or `<vaadin-confirm-dialog>`; delete
  `hegs-dialog.ts`.
- Normalise the 2 of 11 existing `<vaadin-dialog>` elements that lack
  `header-title` / `draggable`.
- Replace all 30 imperative `PaperDialogElement` open/close references and
  `hegs-dialog`'s `.open` / `hegs-dialog-closed` idiom with reactive `.opened`.
- Remove the `paper-dialog` dark-mode override block from `index.html`.
- Fix the reference exemplar's incomplete dependency array
  (`terraform-plan-dialog.ts:134` — review finding R1-15) **before** it is
  copied 19 more times.

### Out of scope

- `focus-target` — **retracted as a cleanup target**; it is live Vaadin Grid API
  and must be preserved (review R1-01).
- Grid renderers inside these dialogs — that is the F-4 migration. Note the
  overlap is real: `add-edit-access-control.ts` has 6 grid renderers *inside* its
  dialog (review R1-02 correction).
- Dialog visual design beyond preserving size and title.

---

## 5. Behaviour Changes Accepted

Stated explicitly, because this plan is **not** behaviour-preserving.

| Change | Affects | Rationale |
|---|---|---|
| Escape now closes the dialog | the 12 paper dialogs | Matches all 11 existing Vaadin dialogs |
| Clicking outside now closes the dialog | the 12 paper dialogs | Same |
| Dialogs become draggable | 12 paper + 7 hegs | 9 of 11 Vaadin dialogs already are |
| Content is created on open and destroyed on close | 12 paper + 7 hegs | Consequence of `dialogRenderer`; forces the configure-then-open fix (R1-08) |
| Title moves into a rendered header bar | 7 hegs | `header-title` vs hegs' custom title markup |

### 5.1 The risk this creates, and what I recommend

**Escape-to-dismiss on a part-completed form loses the user's input.** Of the 19
dialogs gaining this, these host forms where that is plausible:
`add-edit-access-control`, `clone-environment`, `reset-app-password-behalf`,
`page-config-values-list`, `page-daemons-list`, `page-permissions-list`,
`page-sql-ports-list`, `attached-servers` (add/edit server),
`add-edit-project`.

This is a real cost of the consistency you asked for, and I am not going to
quietly diverge from your instruction to avoid it. **The plan implements uniform
default dismissal.** My recommendation for afterwards, as a separate and equally
uniform rule: an unsaved-changes guard on `@opened-changed` for dialogs
containing forms — consistent, and it removes the data-loss risk without
reintroducing per-dialog special cases. Raised as a follow-up in §9, not folded
in here.

---

## 6. Constraints

1. **One pattern per purpose**, per §3. A third variant is a defect.
2. **No `no-close-on-esc` / `no-close-on-outside-click` anywhere.** Uniform
   dismissal is the point.
3. **Every dialog needs a close path built, not assumed.** Review R1-02
   established that **zero** of the 12 `dialog-confirm` buttons carry an
   `@click` handler; `dialog-confirm` is inert on `<vaadin-dialog>`.
4. **Sizing uses `width`/`height` properties or `::part(overlay)`.** Review
   R1-07 established that dialog content *is* inside the host's shadow tree
   (`vaadin-dialog-overlay-mixin.js:71` appends the renderer root to the dialog
   element), so the previous plan's inline-wrapper mandate was wrong. Carry
   `max-width: calc(100vw - 32px)` too — R1-16.
5. **Configure-then-open must be inverted** into property bindings, because
   content does not exist while the dialog is closed (R1-08). Affects
   `attached-databases.ts:214-224`, `page-permissions-list.ts:234-236`.
6. **`dialogRenderer` dependency arrays** follow the same rule as F-4: every
   entry must be a `@property`/`@state`, or it never fires.
7. Existing tests green per step; no step ships a dialog without a test.

---

## 7. Success Criteria

| # | Criterion | Verified by |
|---|---|---|
| SC-1 | Exactly two dialog implementations remain | `grep` finds no `<paper-dialog>` and no `<hegs-dialog>`; `hegs-dialog.ts` deleted |
| SC-2 | All 13 Polymer-ecosystem packages leave | `package-lock.json` matches neither `@polymer/` **nor** `@webcomponents/shadycss` (R1-17) |
| SC-3 | Every dialog opens, closes by button, by Escape, and by outside click | one test per dialog, all four paths |
| SC-4 | No dialog sets a `no-close-on-*` attribute | `grep` returns zero |
| SC-5 | Every content dialog has `header-title` and `draggable` | `grep`-checkable |
| SC-6 | Dark mode correct with the override block removed | all 31 dialogs, both themes |
| SC-7 | No regression | tests green, `tsc` clean, `eslint` clean, build succeeds |
| SC-8 | Bundle shrinks | baseline recorded: 5471.2 KB |

---

## 8. Unknowns Register

| ID | Unknown | Blocking? | Resolution |
|----|---------|-----------|------------|
| U-1 | Paper dialog dismissal semantics | ✅ Resolved | `modal` ⇒ no outside-click, no Escape, backdrop. Now **deliberately discarded** per §5. |
| U-2 | Where dialog content lives; how to size it | ✅ Resolved | Host shadow tree; use `width`/`height` + `::part(overlay)` (R1-07) |
| U-3 | Do `confirm-dialog.ts` / `deploy-confirm-dialog.ts` survive as wrappers, or collapse? | ✅ **RESOLVED 2026-08-02** | **Collapse**, per direction. Both are deleted; call sites use `<vaadin-confirm-dialog>` directly. |
| U-4 | Does `<vaadin-confirm-dialog>` cover everything the wrappers did? | ✅ **RESOLVED 2026-08-02** | Yes. It exposes `header`, `message`, `confirmText`, `confirmTheme`, `cancelText`, `cancelButtonVisible`, `rejectButtonVisible`, `opened`, and fires `confirm`/`cancel`/`reject`/`closed`. Every wrapper behaviour maps. |
| U-6 | Do the two target patterns share a content model? | ✅ **RESOLVED 2026-08-02** | **No, and it matters.** `<vaadin-dialog>` renders content on open and destroys it on close; `<vaadin-confirm-dialog>` uses **slots**, so content exists while closed. Dependency arrays and the configure-then-open inversion therefore apply to `<vaadin-dialog>` conversions **only**. See `IS-dialog-consistency.md` §1.1. |
| U-5 | Ordering against F-4, given `add-edit-access-control` has 6 grid renderers inside its dialog | **No** | Recommend dialogs first, and convert that file's renderers in the same step to avoid touching it twice |

**No blocking unknowns.**

---

## 9. Follow-ups

- **Uniform unsaved-changes guard** on form dialogs (§5.1) — recommended, and
  the right answer to the risk this plan accepts.
- `hegs-json-viewer.ts` and other bespoke components are unaffected; only the
  dialog was duplicated framework functionality.

---

## 10. Checkpoint

This supersedes `docs/paper-dialog-retirement/`, which failed review round 1.
It requires review round 2 before an Implementation Sequence executes.
