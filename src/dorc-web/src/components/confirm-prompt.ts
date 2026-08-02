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
    dialog.textContent = message;

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
    dialog.addEventListener('cancel', () => finish(false));
    // Escape resolves as a cancel, matching window.confirm()'s behaviour.
    dialog.addEventListener('closed', () => finish(false));

    document.body.appendChild(dialog);
    dialog.opened = true;
  });
}
