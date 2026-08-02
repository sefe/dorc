# HLPS: Retire `paper-dialog` and the Polymer Dependency Chain

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | ❌ REVISION — failed review round 1                 |
| **Author**  | Agent                                              |
| **Date**    | 2026-08-02                                         |
| **Folder**  | `docs/paper-dialog-retirement/`                    |
| **Resolves**| `docs/vaadin-alignment/AUDIT-vaadin-25-alignment.md`, finding F-3b |
| **Scope**   | `src/dorc-web` — 12 `<paper-dialog>` elements in 9 files, 10 dead imports, 59 dead attributes, 12 npm packages |

> ⚠️ **FAILED ADVERSARIAL REVIEW ROUND 1 — DO NOT EXECUTE.**
> See `../vaadin-alignment/REVIEW-R1-triage.md`. Verified defects in this
> document: **zero** of the 12 `dialog-confirm` buttons have an `@click`
> handler, so the mapping as written produces 12 dialogs with no dismissal path
> at all; dialog content **is** reachable from host styles
> (`vaadin-dialog-overlay-mixin.js:71` appends the renderer root to the dialog
> element), so the mandated inline-wrapper sizing is wrong and `width`/`height`
> properties plus `::part(overlay)` are the correct mapping; configure-then-open
> call sites will null-dereference because content does not exist while closed;
> the `focus-target` cleanup is **retracted** (it is live Grid API); and it is
> 13 packages, not 12 (`@webcomponents/shadycss`).

---

## 1. Problem Statement

Vaadin 25 removed Polymer from its web components entirely — they are Lit-based
now. The application nonetheless still carries **12 Polymer packages**, all
transitively required by a single element: `@polymer/paper-dialog`.

Polymer 3 is end-of-life. Beyond the dependency itself, the cost shows up in
three places:

1. **`index.html` carries a dark-mode override block** setting
   `--paper-dialog-background-color`, `--paper-dialog-color`,
   `--primary-background-color` and `--primary-text-color` purely to stop
   `paper-dialog` rendering wrongly in dark mode. Vaadin's own components need
   none of this.
2. **Dialog state is imperative.** Dialogs are opened by querying the shadow
   root for a `PaperDialogElement` and calling `.open()` / `.close()` — 30 typed
   references across the codebase. The rest of the app has moved to reactive
   `.opened` bindings.
3. **`lit-analyzer` reports 24 unknown attributes** that are Polymer-only
   (`dialog-confirm` ×12, `allow-click-through` ×12).

The replacement pattern is **already established in this codebase**:
`page-servers-list.ts`, `page-databases-list.ts`, `page-project-envs.ts` and 5
other files use `<vaadin-dialog>` with `dialogRenderer` / `dialogFooterRenderer`
from `@vaadin/dialog/lit`. This is a consolidation onto an existing in-house
pattern, not the introduction of a new one.

---

## 2. Current State

Measured, not estimated:

| Item | Count |
|---|---|
| `<paper-dialog>` elements | **12**, across **9** files |
| Files importing `@polymer/paper-dialog` but never using it | **10** |
| `PaperDialogElement` typed references | 30 |
| Polymer npm packages in the tree | 12 |
| Files already using `<vaadin-dialog>` + Lit renderers | 8 |

> **Correction to the audit.** Finding F-3 said "19 files, 9 element usages".
> The real figures are **12 elements in 9 files**, with **10 of the 19
> importing files containing no usage at all** — dead imports, the same pattern
> already found and removed for `paper-toggle-button` in F-3a. The conversion
> work is therefore smaller than the audit implied; the cleanup is larger.

### 2.1 Every `<paper-dialog>` is structurally identical

All 12 use the same four attributes — `id`, `class`, `allow-click-through`,
`modal` — and follow one shape:

```html
<paper-dialog class="size-position" id="add-user-dialog" allow-click-through modal>
  ${this.isAddUserOrGroupDialogOpened ? html`<add-user-or-group>` : nothing}
  <div style="display: flex; justify-content: flex-end">
    <vaadin-button dialog-confirm @click="${this.addUserOrGroupDialogClosed}">
      Close
    </vaadin-button>
  </div>
</paper-dialog>
```

Opened and closed imperatively:

```ts
const dialog = this.shadowRoot?.querySelector('#add-user-dialog') as PaperDialogElement;
dialog.open();   // …and dialog.close() elsewhere
```

This uniformity is the single most favourable fact about this migration: it is
12 instances of one transformation, not 12 bespoke problems.

### 2.2 `focus-target` is a separate, larger, and independent problem

The audit grouped `focus-target` ×58 with the Polymer chain. Scoped analysis
shows that was wrong:

```
focus-target occurrences — inside <paper-dialog>: 0
                         — inside <vaadin-dialog>: 0
                         — elsewhere:             59
```

All 59 sit on ordinary `<vaadin-text-field>`, `<vaadin-password-field>` and
`<vaadin-button>` elements scattered through grid cell renderers and page
templates, nowhere near a dialog. `focus-target` is a `paper-dialog-behavior`
attribute; on a Vaadin component it is inert.

**This makes it a standalone dead-attribute cleanup**, exactly like
`auto-validate` was in F-1 — removable now, independent of this migration, and
worth doing separately rather than entangling it here. It is listed in §3 as
out of scope for that reason.

---

## 3. Scope

### In scope

- Convert 12 `<paper-dialog>` elements to `<vaadin-dialog>` with
  `dialogRenderer` / `dialogFooterRenderer`.
- Replace 30 imperative `PaperDialogElement` `.open()` / `.close()` calls with
  reactive `.opened` state and `@opened-changed` handlers.
- Delete 10 dead `@polymer/paper-dialog` imports.
- Remove `@polymer/paper-dialog` from `package.json`; confirm all 12 Polymer
  packages leave the tree.
- Remove the `paper-dialog` dark-mode override block from `index.html`.
- Regression tests for each converted dialog's open/close behaviour.

### Out of scope

- **The 59 `focus-target` attributes** (§2.2) — independent cleanup, own change.
- `hegs-dialog.ts`, a bespoke `LitElement` dialog unrelated to Polymer.
- Any change to what the dialogs *contain*. Content moves into a renderer
  function unchanged.
- Dialog visual design, sizing or positioning beyond what is needed to preserve
  current appearance.

---

## 4. Constraints

1. **Behaviour-preserving**: every dialog opens on the same trigger, closes on
   the same actions, and shows the same content.
2. **Follow the in-house pattern** in `page-servers-list.ts` — `.opened`
   binding, `@opened-changed` writing back to state, `${dialogRenderer(fn, deps)}`.
   Do not invent a second convention.
3. **`dialogRenderer` takes a dependency array**, and the same rule from the
   F-4 work applies: derive it by reading the content, not by pattern-matching.
4. **`allow-click-through` + `modal`.** Polymer's `modal` implies a backdrop and
   no light-dismiss; `allow-click-through` partially contradicts it. The current
   *effective* behaviour of each of the 12 must be observed in the running app
   before choosing between Vaadin's default (modal) and `modeless`. See U-1.
5. **`dialog-confirm`** means "close this dialog when clicked". Every instance
   already has an `@click` handler doing the real work, so closing becomes one
   added line setting `opened = false` in that handler.
6. The dark-mode override block may only be removed once no `paper-dialog`
   remains, and dark mode must be visually confirmed afterwards.
7. Existing tests must stay green throughout; run per step.
8. No new dependencies — `@vaadin/dialog@25.2.6` is already installed and in use.

---

## 5. Success Criteria

| # | Criterion | How it is verified |
|---|---|---|
| SC-1 | No `<paper-dialog>` elements and no `@polymer/*` imports remain | `grep` returns zero |
| SC-2 | All 12 Polymer packages leave the dependency tree | `package-lock.json` contains no `@polymer/` entry |
| SC-3 | Each dialog opens and closes as before | a regression test per dialog |
| SC-4 | `lit-analyzer` no longer reports `dialog-confirm` or `allow-click-through` | before/after counts recorded |
| SC-5 | Dark mode is correct with the override block removed | visual check of every converted dialog in dark mode |
| SC-6 | No regression | existing tests green, `tsc` clean, `eslint` clean, build succeeds |
| SC-7 | Bundle shrinks | before/after `dist/assets/index-*.js` size recorded |

---

## 6. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Modal semantics differ — a dialog that was light-dismissable becomes trapping, or vice versa | **High** | U-1: observe each of the 12 in the running app before converting; assert dismiss behaviour in the regression test |
| Focus management differs; Vaadin traps focus and restores it on close, Polymer's behaviour is not identical | Medium | Verify keyboard focus on open and restore on close for each dialog; this is an a11y *improvement* if it differs, but must be a deliberate one |
| Dark mode breaks when the override block is removed | Medium | Constraint 6 — removal last, then visual confirmation of all 12 |
| Dialog sizing/position changes (`class="size-position"`) | Medium | Inspect the `size-position` CSS; carry equivalent sizing onto `<vaadin-dialog>` via its theme or an overlay class |
| Converting imperative `.open()` to reactive state changes *when* content mounts, and some dialogs already gate content on a boolean | Medium | Note that several dialogs already gate content this way, e.g. `page-users-list`; align rather than duplicate the gate |
| 30 call sites is a wide change | Low | Only 9 files; slice the IS one file at a time |

---

## 7. Unknowns Register

| ID | Unknown | Blocking? | Resolution path |
|----|---------|-----------|-----------------|
| U-1 | What is the *effective* dismiss behaviour of `allow-click-through` + `modal` for each of the 12 dialogs? | ✅ **RESOLVED 2026-08-02** | Source + runtime probe: `modal` implies `no-cancel-on-outside-click`, `no-cancel-on-esc-key`, `with-backdrop`. Confirmed live — the dialog closes by **neither** outside click **nor** Escape, only programmatically. `allowClickThrough` affects only *stacked* overlays and is inert for a lone dialog. Vaadin's defaults are the opposite, so every conversion must set `no-close-on-esc` and `no-close-on-outside-click`. |
| U-2 | What does the `size-position` CSS class do, and does `<vaadin-dialog>` need an equivalent? | ✅ **RESOLVED 2026-08-02** | It is **not** shared — it is redefined in 16 files, 7 of which no longer contain a dialog (dead CSS matching the dead imports). The common body is `top: 16px; overflow: auto; padding: 10px`; `page-config-values-list` and `page-daemons-list` add `width: 560px`, `add-edit-access-control` and `clone-environment` set explicit widths instead. Selector is `paper-dialog.size-position`, so all of it dies with the element. **Vaadin renders dialog content into an overlay outside the component's shadow root, so these styles cannot simply be re-pointed** — sizing must move inline onto a wrapper inside the renderer, which is what the existing 8 `<vaadin-dialog>` files already do. |
| U-3 | Does any dialog depend on Polymer's focus or backdrop behaviour in a way users would notice? | **No** | Covered by the per-dialog keyboard check in §6 |
| U-4 | Do the 12 Polymer packages all leave once `paper-dialog` goes, or does something else still pull `@polymer/polymer`? | **No** | `npm uninstall` and re-read the lock file; F-3a showed 8 packages left cleanly with `paper-toggle-button` |
| U-5 | Should this land before, after, or interleaved with the F-4 renderer migration? Both touch some of the same files (`page-daemons-list`, `page-config-values-list`, `page-permissions-list`) | **No** | Recommend **F-3b first**: it is smaller, lower-risk, and shrinks the file set F-4 has to touch. Confirm at review |

**No blocking unknowns.** U-1 and U-2 are now resolved; see `IS-paper-dialog-retirement.md`.

---

## 8. Recommended Shape of the Implementation Sequence

1. **S-001** — Resolve U-1 and U-2: observe all 12 dialogs in the running app,
   record actual dismiss behaviour and the `size-position` CSS. No code change.
2. **S-002** — Delete the 10 dead `@polymer/paper-dialog` imports. Zero risk,
   and it shrinks the surface before any real conversion.
3. **S-003..S-011** — Convert one file per step, 9 steps, each with regression
   tests for its dialogs' open/close/dismiss behaviour.
4. **S-012** — Remove `@polymer/paper-dialog` from `package.json`; confirm the
   tree is Polymer-free.
5. **S-013** — Remove the `index.html` dark-mode override block; visually verify
   all 12 dialogs in both themes.

---

## 9. Relationship to F-4

Both this and `docs/grid-lit-renderers/HLPS-grid-lit-renderers.md` touch
`page-daemons-list.ts`, `page-config-values-list.ts` and
`page-permissions-list.ts`. They are independent in substance — dialogs versus
cell renderers — but doing this one first is recommended (U-5): it is the
smaller and lower-risk of the two, and several of the dialogs it removes contain
grid columns that F-4 would otherwise have to convert twice.

---

## 10. Checkpoint

Per `CLAUDE.md`, this HLPS requires approval before an Implementation Sequence
is drafted. It has not been through the adversarial review panel; that gate is
outstanding.
