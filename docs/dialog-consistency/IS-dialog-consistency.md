# IS: Unify Every Dialog on Two Vaadin Patterns

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | ❌ REVISION — failed review round 2                 |
| **Author**  | Agent                                              |
| **Date**    | 2026-08-02                                         |
| **Folder**  | `docs/dialog-consistency/`                         |
| **HLPS**    | `HLPS-dialog-consistency.md`                       |
| **Scope**   | 31 dialogs, 4 implementations → 2 patterns, 19 steps |

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

## 1. Decisions Taken Since the HLPS

- **U-3 — collapse the wrappers.** Directed: `confirm-dialog.ts` and
  `deploy-confirm-dialog.ts` are deleted, their call sites using
  `<vaadin-confirm-dialog>` directly.
- **U-4 — resolved by reading the component.** `<vaadin-confirm-dialog>`
  exposes `header`, `message`, `confirmText`, `confirmTheme`, `cancelText`,
  `cancelButtonVisible`, `rejectButtonVisible`, `opened`, and fires `confirm`,
  `cancel`, `reject`, `closed`. Every `hegs-dialog` behaviour maps onto it.

### 1.1 The two patterns have different content models

This was not in the HLPS and it changes which risks apply where.

| | `<vaadin-dialog>` | `<vaadin-confirm-dialog>` |
|---|---|---|
| Content | **Renderer** — created on open, destroyed on close | **Slots** — light DOM, always present |
| Dependency arrays | Required, and must name reactive fields | Not applicable |
| Configure-then-open | **Breaks** — must be inverted (R1-08) | **Still works** — content exists while closed |

So `deploy-confirm-dialog`'s imperative `Open()` — which reaches into
`<hegs-json-viewer>` to assign data and expand it — is safe to keep as-is once
the host is a confirm-dialog. The configure-then-open inversion is required only
for the `<vaadin-dialog>` conversions.

---

## 2. Worked Examples

Both patterns, because the previous IS gave one example for twelve conversions
and review called that out.

### 2.1 Content dialog

```html
<!-- before: paper -->
<paper-dialog class="size-position" id="add-user-dialog" allow-click-through modal>
  ${this.isOpen ? html`<add-user-or-group></add-user-or-group>` : nothing}
  <div style="display:flex;justify-content:flex-end">
    <vaadin-button dialog-confirm>Close</vaadin-button>
  </div>
</paper-dialog>
```
```html
<!-- after -->
<vaadin-dialog
  header-title="Add User or Group"
  draggable
  width="560px"
  .opened="${this.addUserDialogOpened}"
  @opened-changed="${(e: DialogOpenedChangedEvent) => {
    this.addUserDialogOpened = e.detail.value;
  }}"
  ${dialogRenderer(this.renderAddUser, [this.selectedUser])}
  ${dialogFooterRenderer(this.renderAddUserFooter, [])}
></vaadin-dialog>
```
```ts
private renderAddUser = () => html`<add-user-or-group></add-user-or-group>`;

// A close path must be BUILT — `dialog-confirm` is inert on <vaadin-dialog>
// and none of the 12 buttons had a handler (R1-02).
private renderAddUserFooter = () => html`
  <vaadin-button @click="${() => (this.addUserDialogOpened = false)}">Close</vaadin-button>
`;

// open: was querySelector(...).open()
this.addUserDialogOpened = true;
```

Sizing: `width` / `height` properties, or `::part(overlay)` from the host's
`static styles` — dialog content is inside the host's shadow tree
(`vaadin-dialog-overlay-mixin.js:71`), so those styles reach it. Carry
`max-width: calc(100vw - 32px)` where the original had it (R1-16):

```css
vaadin-dialog::part(overlay) { max-width: calc(100vw - 32px); }
```

### 2.2 Confirmation prompt

```html
<!-- before: a wrapper over hegs-dialog -->
<confirm-dialog id="confirm-delete-dialog" title="Delete project" message="Are you sure?">
</confirm-dialog>
```
```html
<!-- after: the wrapper is gone -->
<vaadin-confirm-dialog
  header="Delete project"
  confirm-text="Delete"
  confirm-theme="error primary"
  cancel-button-visible
  .opened="${this.confirmDeleteOpen}"
  @opened-changed="${(e: CustomEvent) => {
    this.confirmDeleteOpen = (e.detail as { value: boolean }).value;
  }}"
  @confirm="${this.performDelete}"
  @cancel="${this.cancelDelete}"
>Are you sure?</vaadin-confirm-dialog>
```

`page-daemons-list.ts:175` already matches this and is the exemplar.

---

## 3. Steps

### Phase A — establish the canonical pattern (no conversions)

| Step | Work | Risk |
|---|---|---|
| **S-001** | Fix `terraform-plan-dialog.ts:134` — dependency array is `[this.plan, this.error]` while the renderer branches on `this.loading`, so the spinner never clears (R1-15). Add `header-title`/`draggable` to the 2 of 11 existing dialogs that lack them. | Low |
| **S-002** | Delete 10 dead `@polymer/paper-dialog` imports and 7 orphaned `paper-dialog.size-position` CSS blocks. | None |

S-001 first, deliberately: the exemplar is copied 19 times, so it must be
correct before anyone copies it.

### Phase B — confirmation prompts (slots; simplest)

| Step | Work | Risk |
|---|---|---|
| **S-003** | `page-projects-list` → `<vaadin-confirm-dialog>`; **delete `confirm-dialog.ts`**. | Low |
| **S-004** | `deploy-env` / `deploy-confirm-dialog` → `<vaadin-confirm-dialog>` with the JSON viewer slotted; **delete `deploy-confirm-dialog.ts`**. Its imperative `Open()` survives — slots exist while closed (§1.1). | Medium — the only wrapper with real content |

### Phase C — `hegs-dialog` content dialogs

| Step | Work | Risk |
|---|---|---|
| **S-005** | `attached-servers` ×2. Note `#add-edit-server-dialog` duplicates `page-servers-list`'s — converge on that one's markup. | Medium |
| **S-006** | `add-edit-project` ×1 | Low |
| **S-007** | `page-environments-list` ×1 | Low |
| **S-008** | **Delete `hegs-dialog.ts`**; confirm no importers remain. | None |

### Phase D — `paper-dialog` content dialogs, simplest first

| Step | File | Dialogs | Notes | Risk |
|---|---|---|---|---|
| **S-009** | `page-sql-ports-list` | 1 | **Reference conversion.** Chosen over `page-users-list`, which review found has a dead close listener (R1-25). | Low |
| **S-010** | `page-config-values-list` | 1 | explicit 560px width | Low |
| **S-011** | `page-users-list` | 1 | fix the `composed`-without-`bubbles` event while here (R1-25) | Low |
| **S-012** | `page-permissions-list` | 2 | **configure-then-open** at `:234-236` — invert | Medium |
| **S-013** | `page-daemons-list` | 2 | 560px width; already hosts a confirm-dialog, so the file ends fully consistent | Medium |
| **S-014** | `attached-databases` | 2 | **configure-then-open** at `:214-224` — invert | Medium |
| **S-015** | `clone-environment` | 1 | min(90vw, 550px) | Low |
| **S-016** | `reset-app-password-behalf` | 1 | `position: center` in its CSS | Low |
| **S-017** | `add-edit-access-control` | 1 | **6 grid renderers inside the dialog.** Convert those to `columnBodyRenderer` in this same step so the file is touched once (F-4 overlap, R1-02 correction). | **High** |

### Phase E — teardown

| Step | Work | Risk |
|---|---|---|
| **S-018** | Remove `@polymer/paper-dialog`; assert **13** packages leave — `@polymer/*` **and** `@webcomponents/shadycss` (R1-17). | None |
| **S-019** | Remove the `index.html` dark-mode override block; verify all 31 dialogs in both themes. | Medium |

---

## 4. Definition of Done — Per Step

Objective and checkable, since review found the previous version subjective.

1. `tsc` clean on both tsconfigs; `eslint` clean; `npm run build` succeeds.
2. Full suite green.
3. **Per converted dialog, a test asserting all four paths:**
   - opens on its trigger,
   - closes via its Close/Cancel button,
   - **closes on Escape**,
   - **closes on outside click**.

   The last two are the accepted behaviour change and the thing most easily lost
   by accident — they are assertions, not observations.
4. Where the original had an explicit width, assert the rendered overlay width;
   where it had `max-width: calc(100vw - 32px)`, assert it at a 600px viewport.
5. For `<vaadin-dialog>` steps only: every `dialogRenderer` dependency array
   entry is a `@property`/`@state`. A plain field never fires.

### S-019 dark mode — concrete criteria

Not "looks right". For each of the 31 dialogs, in both themes, via the existing
Playwright setup:

- the overlay's computed `background-color` is not `transparent` and differs
  from the page background;
- header text and body text resolve to the Lumo variables, not to
  `--primary-text-color` (the Polymer variable the override block set);
- a screenshot is captured per dialog per theme for eyeball review.

---

## 5. Rollback

Each step is one commit: source, tests and any CSS together. Reverting a
conversion step restores the previous dialog **and** removes its tests, so the
tree stays consistent — the previous IS claimed clean revert while adding tests
in the same commit, which review flagged as contradictory. It is consistent here
because the tests target the *converted* dialog and are meaningless without it.

**S-008** (delete `hegs-dialog.ts`) must not land before S-005..S-007, and
**S-018** must not land before S-009..S-017. Both are recorded as explicit
`Depends on:` rather than prose (R1-09 on the previous IS).

- S-008 — Depends on: S-005, S-006, S-007
- S-018 — Depends on: S-009 … S-017
- S-019 — Depends on: S-018

---

## 6. Open Questions for Review Round 2

1. **S-017 sizing.** Converting a dialog *and* 6 grid renderers in one step is
   large. The alternative — touch the file twice — is worse. Confirm.
2. **S-004.** `deploy-confirm-dialog` is a confirmation *prompt* by wording but
   carries a JSON viewer. Confirm `<vaadin-confirm-dialog>` with slotted content
   is right, rather than `<vaadin-dialog>` with a footer.
3. **Unsaved-changes guard** (HLPS §5.1) — confirm it stays a follow-up rather
   than landing with the nine form dialogs that gain Escape-dismissal.

---

## 7. Checkpoint

Requires review round 2 before S-001 executes.
