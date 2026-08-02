# IS: Retire `paper-dialog` and the Polymer Dependency Chain

| Field       | Value                                         |
|-------------|-----------------------------------------------|
| **Status**  | ❌ REVISION — failed review round 1            |
| **Author**  | Agent                                         |
| **Date**    | 2026-08-02                                    |
| **Folder**  | `docs/paper-dialog-retirement/`               |
| **HLPS**    | `HLPS-paper-dialog-retirement.md` (U-1, U-2 resolved) |

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

## 1. The Transformation

Both unknowns are resolved, so the mapping is now exact rather than
provisional. Every one of the 12 dialogs follows this single transformation.

### Before

```html
<paper-dialog class="size-position" id="add-user-dialog" allow-click-through modal>
  ${this.isAddUserOrGroupDialogOpened ? html`<add-user-or-group>` : nothing}
  <div style="display: flex; justify-content: flex-end">
    <vaadin-button dialog-confirm @click="${this.addUserOrGroupDialogClosed}">Close</vaadin-button>
  </div>
</paper-dialog>
```
```ts
(this.shadowRoot?.querySelector('#add-user-dialog') as PaperDialogElement).open();
```

### After

```html
<vaadin-dialog
  id="add-user-dialog"
  .opened="${this.addUserDialogOpened}"
  no-close-on-esc
  no-close-on-outside-click
  @opened-changed="${(e: DialogOpenedChangedEvent) => { this.addUserDialogOpened = e.detail.value; }}"
  ${dialogRenderer(this.renderAddUserDialog, [/* deps */])}
  ${dialogFooterRenderer(this.renderAddUserFooter, [])}
></vaadin-dialog>
```
```ts
this.addUserDialogOpened = true;
```

### Attribute and API mapping

| Polymer | Vaadin | Why |
|---|---|---|
| `modal` | `no-close-on-esc` **and** `no-close-on-outside-click` | U-1: `modal` implies `no-cancel-on-outside-click` + `no-cancel-on-esc-key` + `with-backdrop`, confirmed live. Vaadin has a backdrop by default but **closes on both** — omitting these opt-outs would silently change behaviour. |
| `allow-click-through` | *(nothing)* | U-1: only affects *stacked* overlays; inert for a lone dialog and inert once outside-click no longer closes anything. |
| `class="size-position"` | inline sizing on a wrapper inside the renderer | U-2: content renders into an overlay outside the component's shadow root, so the existing CSS cannot be re-pointed. |
| `dialog-confirm` on a child button | `opened = false` in that button's existing `@click` handler | Every instance already has a handler doing the real work. |
| `PaperDialogElement.open()` / `.close()` | set the `opened` state property | Matches the reactive pattern in the 8 existing `<vaadin-dialog>` files. |

**Watch item:** `no-close-on-esc` preserves today's behaviour but is worse UX
than Vaadin's default, and these are mostly forms where Escape-to-dismiss risks
data loss — which is presumably why `modal` was chosen. Preserving is correct
for this migration. Whether to *then* allow Escape is a product decision, raised
as a follow-up in §5, not folded in here.

---

## 2. Steps

Each step is independently shippable, leaves the suite green, and is verified
before the next begins.

| Step | Title | Files | Risk |
|---|---|---|---|
| **S-001** | Resolve U-1 and U-2 | — | ✅ **DONE** (see HLPS) |
| **S-002** | Delete dead `@polymer/paper-dialog` imports and orphaned `size-position` CSS | 10 imports + 7 CSS blocks | None |
| **S-003** | Convert `page-users-list` — 1 dialog | 1 | Low — the reference conversion |
| **S-004** | Convert `page-sql-ports-list` — 1 dialog | 1 | Low |
| **S-005** | Convert `page-config-values-list` — 1 dialog (has explicit width) | 1 | Low |
| **S-006** | Convert `page-permissions-list` — 2 dialogs | 1 | Medium |
| **S-007** | Convert `page-daemons-list` — 2 dialogs (has explicit width) | 1 | Medium |
| **S-008** | Convert `attached-databases` — 2 dialogs | 1 | Medium |
| **S-009** | Convert `clone-environment` — 1 dialog (explicit width) | 1 | Low |
| **S-010** | Convert `reset-app-password-behalf` — 1 dialog | 1 | Low |
| **S-011** | Convert `add-edit-access-control` — 1 dialog (explicit width) | 1 | Low |
| **S-012** | Drop `@polymer/paper-dialog`; confirm the tree is Polymer-free | `package.json` | None |
| **S-013** | Remove the `index.html` dark-mode override block; verify both themes | `index.html` | Medium |

S-003 is deliberately first and deliberately the simplest: it establishes the
concrete pattern — including how sizing is carried onto the overlay — that
S-004..S-011 then repeat. Its JIT spec is the one worth writing carefully.

### Per-step definition of done

1. `tsc` clean on both tsconfigs; `eslint` clean; build succeeds.
2. Full suite green.
3. For conversion steps, a regression test asserting, for each dialog: opens on
   its trigger, closes on its Close button, and does **not** close on Escape or
   on an outside click.

---

## 3. Verification Strategy

- **Per dialog**: an automated test covering open, close, and both
  non-dismissal paths. This is the contract U-1 established, and it is the part
  most easily lost by accident.
- **Sizing** (S-003 onward): a rendered-width assertion where a width was
  explicitly set — `page-config-values-list` and `page-daemons-list` (560px),
  `add-edit-access-control` (min(90vw, 650px)), `clone-environment`
  (min(90vw, 550px)).
- **Dark mode** (S-013): visual check of all 12 in both themes, driven through
  the existing Playwright setup.
- **Bundle size**: recorded before S-002 and after S-012.

---

## 4. Rollback

Every step is a single commit touching one file plus its test. Rollback is
`git revert` of that commit; no step leaves the app in a state where some
dialogs are converted and others are broken, because each file is self-
contained.

The one exception is **S-012** (dependency removal), which must not land before
S-003..S-011 are all complete and verified — it is the step that makes the
others irreversible without a reinstall.

---

## 5. Follow-ups Deliberately Not in This Sequence

- The **59 dead `focus-target` attributes** (HLPS §2.2) — independent cleanup.
- Whether to allow Escape-to-dismiss now that the framework offers it cleanly
  (§1 watch item) — a product decision.
- `hegs-dialog.ts`, the bespoke Lit dialog, which duplicates a good deal of what
  `<vaadin-dialog>` now provides. Worth a separate look, out of scope here.

---

## 6. Checkpoint

Per `CLAUDE.md` this IS requires approval before S-002 executes, and it has not
been through the adversarial review panel.
