import '@vaadin/confirm-dialog';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { LOCATION_CHANGED_EVENT } from '../router/router';
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
  /** Theme for the cancel button. Defaults to a filled neutral button. */
  cancelTheme?: string;
  /**
   * Optional prominent warning callout rendered with a large warning symbol
   * inside the confirmation dialog.
   */
  warningTitle?: string;
  warningMessage?: string;
  warningActionHint?: string;
}

export function confirmPrompt(
  message: string,
  options: ConfirmPromptOptions = {}
): Promise<boolean> {
  const {
    header = 'Confirm',
    confirmText = 'OK',
    confirmTheme = 'primary error',
    cancelTheme = 'primary',
    warningTitle,
    warningMessage,
    warningActionHint
  } = options;

  return new Promise<boolean>(resolve => {
    const dialog = document.createElement(
      'vaadin-confirm-dialog'
    ) as ConfirmDialog;

    dialog.header = header;
    dialog.confirmText = confirmText;
    dialog.confirmTheme = confirmTheme;
    dialog.cancelTheme = cancelTheme;
    dialog.cancelButtonVisible = true;

    if (warningTitle || warningMessage || warningActionHint) {
      const banner = document.createElement('div');
      banner.className = 'confirm-prompt-warning';
      banner.setAttribute('role', 'alert');
      banner.style.display = 'flex';
      banner.style.alignItems = 'flex-start';
      banner.style.gap = '16px';
      banner.style.padding = '14px 16px';
      banner.style.marginBottom = '14px';
      banner.style.borderRadius = '8px';
      banner.style.border = '2px solid #d97706';
      banner.style.backgroundColor = 'rgba(245, 158, 11, 0.14)';
      banner.style.maxWidth = '520px';
      banner.style.overflowWrap = 'anywhere';

      const iconWrap = document.createElement('div');
      iconWrap.style.display = 'flex';
      iconWrap.style.alignItems = 'center';
      iconWrap.style.justifyContent = 'center';
      iconWrap.style.width = '52px';
      iconWrap.style.height = '52px';
      iconWrap.style.borderRadius = '50%';
      iconWrap.style.backgroundColor = 'rgba(217, 119, 6, 0.2)';
      iconWrap.style.color = '#b45309';
      iconWrap.style.flexShrink = '0';

      const icon = document.createElement('vaadin-icon');
      icon.setAttribute('icon', 'vaadin:warning');
      icon.style.width = '32px';
      icon.style.height = '32px';
      iconWrap.appendChild(icon);
      banner.appendChild(iconWrap);

      const body = document.createElement('div');
      body.style.display = 'flex';
      body.style.flexDirection = 'column';
      body.style.gap = '6px';
      body.style.minWidth = '0';
      body.style.overflowWrap = 'anywhere';

      if (warningTitle) {
        const titleEl = document.createElement('strong');
        titleEl.style.fontSize = 'var(--lumo-font-size-m, 1rem)';
        titleEl.style.lineHeight = '1.3';
        titleEl.textContent = warningTitle;
        body.appendChild(titleEl);
      }

      if (warningMessage) {
        const msgEl = document.createElement('div');
        msgEl.style.fontSize = 'var(--lumo-font-size-s, 0.875rem)';
        msgEl.style.lineHeight = '1.45';
        msgEl.textContent = warningMessage;
        body.appendChild(msgEl);
      }

      if (warningActionHint) {
        const hintEl = document.createElement('div');
        hintEl.style.fontSize = 'var(--lumo-font-size-s, 0.875rem)';
        hintEl.style.fontWeight = '600';
        hintEl.style.lineHeight = '1.4';
        hintEl.style.marginTop = '2px';
        hintEl.textContent = warningActionHint;
        body.appendChild(hintEl);
      }

      banner.appendChild(body);
      dialog.appendChild(banner);
    }

    // `white-space: pre-line` so the message keeps its line breaks. Several
    // call sites separate a question from its consequence with a blank line —
    // "mark this property as secure?\n\nThis will encrypt all existing
    // values. This action cannot be undone." — and window.confirm() honoured
    // that. Slotted into the overlay under the default `normal`, every break
    // collapses to a space and the warning runs on from the question.
    const text = document.createElement('div');
    text.style.whiteSpace = 'pre-line';
    // Every message interpolates an identifier the user did not choose — an
    // FQDN from `Delete server ${name}?`, a domain-qualified group from
    // `Remove Access from ${accessControl?.Name}?`. Dots and backslashes are
    // not break opportunities in CSS, and vaadin-confirm-dialog's overlay
    // content is `overflow-x: visible` (unlike vaadin-dialog's, which is
    // `auto`), so without this the name runs past the edge with nothing able
    // to scroll to it.
    text.style.overflowWrap = 'anywhere';
    text.textContent = message;
    dialog.appendChild(text);

    let settled = false;
    const finish = (result: boolean) => {
      if (settled) return;
      settled = true;
      window.removeEventListener(LOCATION_CHANGED_EVENT, onNavigated);
      resolve(result);
      // Close before removing. On the confirm and cancel paths the mixin has
      // already done this and it is a no-op; on the navigation path it is the
      // only thing that runs the overlay's `_exitModalState()`, which is what
      // puts `document.body { pointer-events }` back.
      dialog.opened = false;
      // `closed` fires after the overlay has gone; removing here keeps the
      // DOM clean without racing the close transition.
      queueMicrotask(() => dialog.remove());
    };

    // The dialog hangs off `document.body`, not off the component that raised
    // it, so a navigation replaces the page underneath it and leaves it there.
    // Because it is modal, `document.body` stays `pointer-events: none` and the
    // newly rendered page is dead to the mouse — with browser Back the only way
    // in or out. Treat navigating away as declining.
    //
    // The 31 page-level dialogs need no equivalent: DialogBaseMixin's
    // disconnectedCallback schedules `opened = false` when its host is
    // detached, so removing the page does exit modal state for those.
    const onNavigated = () => finish(false);
    window.addEventListener(LOCATION_CHANGED_EVENT, onNavigated);

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
