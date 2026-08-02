# CONVERSION: `page-sql-ports-list` — the reference dialog

| Field       | Value                                                    |
|-------------|----------------------------------------------------------|
| **Status**  | COMPLETE — shipped and verified                          |
| **Author**  | Agent                                                    |
| **Date**    | 2026-08-02                                               |
| **Folder**  | `docs/dialog-consistency/`                               |
| **Replaces**| planning rounds 1 and 2 as the source of truth on dialog behaviour |

---

## 1. Why This Exists

Two planning rounds and five reviewers produced documents whose defect count
rose with their detail, all concentrated in claims about **runtime behaviour**
asserted from names and documentation. This conversion was done instead of a
third round, to settle those questions by observation.

It did. **Three things every planning document had wrong are corrected below**,
and each is now pinned by a test.

---

## 2. What the Running Code Actually Does

### 2.1 Where dialog content lives — nobody had this right

Observed by probing the mounted component in a real browser:

```
<page-sql-ports-list> #shadow-root
  └ <vaadin-dialog id="add-sqlport-dialog">
      ├ <vaadin-dialog-content slot="footer">   ← dialogFooterRenderer output
      ├ <vaadin-dialog-content>                 ← dialogRenderer output
      └ #shadow-root
          └ <vaadin-dialog-overlay opened>
```

- Renderer output is **light-DOM children of the `<vaadin-dialog>` element**.
- The overlay lives in **that element's own shadow root**.
- **Nothing is appended to `document.body`.**
  `document.querySelector('vaadin-dialog-overlay')` returns `null` while a
  dialog is open. Verified in the app as well as in tests.

This is not the Vaadin 23/24 model, which both my planning documents and my
first attempt at test helpers assumed.

**Three consequences:**

1. Test helpers must reach the overlay through
   `dialog.shadowRoot.querySelector('vaadin-dialog-overlay')`. The ones I wrote
   first, querying `document`, found nothing and failed 5 of 11 tests.
2. **No overlay cleanup between tests is needed** — removing the host removes
   the overlay with it. R2-15's concern about overlays escaping `fixture()`'s
   container does not apply.
3. **`theme-manager.ts:28` cannot work for dialogs.** It does
   `document.querySelectorAll('vaadin-dialog-overlay, vaadin-overlay')` to
   propagate `theme="dark"`, and that query cannot reach an overlay nested in a
   component's shadow root. This confirms audit finding F-11 and review R2-16
   decisively: for dialogs, that mechanism is dead code. Dark mode works anyway,
   because Lumo custom properties inherit through shadow boundaries.

### 2.2 Dismissal — confirmed by real keyboard and mouse

| Path | Result |
|---|---|
| Close button | closes ✓ |
| **Escape** | closes ✓ |
| **Outside click** | closes ✓ |
| Programmatic (`sqlPortCreated()`) | closes ✓ |

Escape and outside click are **new** — `paper-dialog`'s `modal` implied
`no-cancel-on-esc-key` and `no-cancel-on-outside-click`. This is the accepted
behaviour change, and it is now asserted rather than assumed.

Verified twice: with synthetic events in the test suite, and in the running app
with `page.keyboard.press('Escape')` and `page.mouse.click(5, 5)`.

### 2.3 Content persists across close and reopen

Confirmed — review R2-02 was right and both my documents were wrong. The
overlay caches its renderer root, so `<add-sql-port>` is the same element after
a close/reopen cycle.

**This matters for the remaining conversions.** Any dialog that today gates its
content on an open-flag (`page-users-list.ts:99`, `page-daemons-list.ts:165`
use `${this.isOpen ? … : nothing}`) does so to get a *fresh* form each time.
That gate must be carried into the renderer body, not dropped. This dialog did
not have one, so its form persists — matching the old `paper-dialog` behaviour,
which also kept its content mounted.

### 2.4 Sizing via `::part(overlay)` works

```css
vaadin-dialog::part(overlay) {
  top: 16px;
  overflow: auto;
  max-width: calc(100vw - 32px);
}
```

Declared in the host's `static styles` and it reaches the overlay, because the
overlay is inside the dialog element in this shadow root. Replaces the old
`paper-dialog.size-position` rule. No inline wrapper needed — the previous IS
mandated one on a false premise.

---

## 3. The Conversion

```diff
- <paper-dialog class="size-position" id="add-sqlport-dialog" allow-click-through modal>
-   <add-sql-port id="add-sql-port"></add-sql-port>
-   <div style="display: flex; justify-content: flex-end">
-     <vaadin-button dialog-confirm>Close</vaadin-button>
-   </div>
- </paper-dialog>
+ <vaadin-dialog
+   id="add-sqlport-dialog"
+   header-title="Add SQL Port"
+   draggable
+   .opened="${this.addSqlPortDialogOpened}"
+   @opened-changed="${(e: DialogOpenedChangedEvent) => {
+     this.addSqlPortDialogOpened = e.detail.value;
+   }}"
+   ${dialogRenderer(this.renderAddSqlPort, [])}
+   ${dialogFooterRenderer(this.renderAddSqlPortFooter, [])}
+ ></vaadin-dialog>
```

```diff
- addSqlPort() {
-   const dialog = this.shadowRoot?.getElementById('add-sqlport-dialog') as PaperDialogElement;
-   dialog.open();
- }
+ addSqlPort() {
+   this.addSqlPortDialogOpened = true;
+ }
```

`dialog-confirm` was inert, so the Close button got a real handler — an empty
dependency array is correct here because neither renderer reads component state.

---

## 4. Verification

- **11 new tests**, covering all four dismissal paths, content location, footer
  slot, header title, and reopen persistence.
- **164 tests total pass**; `tsc` clean on both tsconfigs; `eslint` clean;
  production build succeeds.
- **10 checks against the running app** with real keyboard and mouse input,
  including that no overlay leaks into `document.body`.

---

## 5. What the Remaining 29 Dialogs Inherit

1. Copy the shape in §3.
2. Sizing goes on `::part(overlay)` in the host's `static styles`.
3. Build a close handler — `dialog-confirm` does nothing. Where one already
   exists (3 of the 12 paper dialogs), keep its side effects: `close()` in
   `clone-environment` and `add-edit-access-control` also resets form state.
4. If the dialog currently gates content on an open-flag, carry that gate into
   the renderer — content is not rebuilt on open.
5. Test helpers are in `tests/_helpers.ts`: `dialogIn`, `isDialogOpen`,
   `inDialog`, `pressEscape`, `clickOutside`, `settle`.
6. `<vaadin-confirm-dialog>` differs — slots not renderers, no outside-click
   close, no `draggable` (review R2-01, R2-14). Confirmation prompts need their
   own reference conversion before that pattern is replicated.
