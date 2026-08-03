import '@vaadin/confirm-dialog';
import type { ConfirmDialog } from '@vaadin/confirm-dialog';

/**
 * Promise-based replacement for the browser's `window.confirm()`.
 *
 * The native dialog blocks the main thread, cannot be themed, and looks
 * nothing like the rest of the UI. This renders a `<vaadin-confirm-dialog>`
 * instead while keeping the call shape a one-liner, so call sites stay as
 * readable as they were:
 *
 * ```ts
 * if (!(await confirmPrompt(`Delete server ${name}?`))) return;
 * ```
 *
 * The dialog is created on demand, appended to `document.body`, and removed
 * once it closes — so nothing accumulates and no host component needs to hold
 * dialog state.
 */
export interface ConfirmPromptOptions {
  /** Dialog header. Defaults to "Confirm". */
  header?: string;
  /** Label on the confirming button. Defaults to "OK". */
  confirmText?: string;
  /**
   * Vaadin theme for the confirming button. Defaults to `primary error`,
   * since almost every call site guards a destructive action.
   */
  confirmTheme?: string;
}

export function confirmPrompt(
  message: string,
  options: ConfirmPromptOptions = {}
): Promise<boolean> {
  const {
    header = 'Confirm',
    confirmText = 'OK',
    confirmTheme = 'primary error'
  } = options;

  return new Promise<boolean>(resolve => {
    const dialog = document.createElement(
      'vaadin-confirm-dialog'
    ) as ConfirmDialog;

    dialog.header = header;
    dialog.confirmText = confirmText;
    dialog.confirmTheme = confirmTheme;
    dialog.cancelButtonVisible = true;

    // `white-space: pre-line` so the message keeps its line breaks. Several
    // call sites separate a question from its consequence with a blank line —
    // "mark this property as secure?\n\nThis will encrypt all existing
    // values. This action cannot be undone." — and window.confirm() honoured
    // that. Slotted into the overlay under the default `normal`, every break
    // collapses to a space and the warning runs on from the question.
    const text = document.createElement('div');
    text.style.whiteSpace = 'pre-line';
    text.textContent = message;
    dialog.appendChild(text);

    let settled = false;
    const finish = (result: boolean) => {
      if (settled) return;
      settled = true;
      resolve(result);
      // `closed` fires after the overlay has gone; removing here keeps the
      // DOM clean without racing the close transition.
      queueMicrotask(() => dialog.remove());
    };

    dialog.addEventListener('confirm', () => finish(true));
    // Escape arrives here, not on `closed`: the mixin's `_onOverlayEscapePress`
    // calls `__cancel()`, which dispatches `cancel` and only then sets
    // `opened = false`. Outside-click never closes it at all —
    // `_onOverlayOutsideClick` calls preventDefault.
    dialog.addEventListener('cancel', () => finish(false));
    // Backstop for any close path that fires neither, so the promise cannot be
    // left pending. Redundant for the two paths above, which is why `settled`
    // matters more for intent than for behaviour: a promise settles once
    // regardless, and the dialog is already removed by then.
    dialog.addEventListener('closed', () => finish(false));

    document.body.appendChild(dialog);
    dialog.opened = true;
  });
}
