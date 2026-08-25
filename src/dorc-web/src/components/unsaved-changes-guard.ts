import type { Dialog, DialogOpenedChangedEvent } from '@vaadin/dialog';
import { confirmPrompt } from './confirm-prompt';

const dismissButtonLabels = new Set(['cancel', 'close']);

export class UnsavedChangesGuard {
  private readonly attachedDialogs = new WeakSet<Dialog>();
  private readonly dirtyDialogs = new WeakSet<Dialog>();
  private readonly promptTokens = new WeakMap<Dialog, object>();

  readonly attach = (element?: Element) => {
    if (
      !element ||
      element.localName !== 'vaadin-dialog' ||
      this.attachedDialogs.has(element as Dialog)
    ) {
      return;
    }

    const dialog = element as Dialog;
    this.attachedDialogs.add(dialog);

    const markDirty = () => {
      if (!dialog.opened) return;
      this.dirtyDialogs.add(dialog);
      dialog.noCloseOnEsc = true;
      dialog.noCloseOnOutsideClick = true;
    };

    dialog.addEventListener('input', markDirty);
    dialog.addEventListener('change', markDirty);
    dialog.addEventListener(
      'opened-changed',
      (event: DialogOpenedChangedEvent) => {
        if (!event.detail.value) this.reset(dialog);
      }
    );
    dialog.addEventListener(
      'click',
      event => {
        if (!this.dirtyDialogs.has(dialog)) return;

        const button = event
          .composedPath()
          .find(
            item =>
              item instanceof HTMLElement &&
              (item.localName === 'vaadin-button' ||
                item.localName === 'button')
          ) as HTMLElement | undefined;
        if (
          !button ||
          !dismissButtonLabels.has(
            button.textContent?.trim().toLowerCase() ?? ''
          )
        ) {
          return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        void this.confirmClose(dialog);
      },
      true
    );

    const interceptDismissal = (event: Event) => {
      if (!this.dirtyDialogs.has(dialog)) return;
      event.preventDefault();
      void this.confirmClose(dialog);
    };
    let boundOverlay: Element | undefined;
    let connectionObserver: MutationObserver | undefined;
    let overlayObserver: MutationObserver | undefined;
    const bindOverlay = () => {
      const overlay = dialog.shadowRoot?.querySelector('vaadin-dialog-overlay');
      if (!overlay) return false;
      if (overlay === boundOverlay) return true;

      overlay.addEventListener(
        'vaadin-overlay-outside-click',
        interceptDismissal
      );
      overlay.addEventListener(
        'vaadin-overlay-escape-press',
        interceptDismissal
      );
      boundOverlay = overlay;
      connectionObserver?.disconnect();
      overlayObserver?.disconnect();
      return true;
    };

    const bindWhenConnected = () => {
      if (bindOverlay()) return;

      if (!dialog.isConnected) {
        connectionObserver = new MutationObserver(() => {
          if (!dialog.isConnected) return;

          connectionObserver?.disconnect();
          connectionObserver = undefined;
          window.setTimeout(bindWhenConnected);
        });
        connectionObserver.observe(document.documentElement, {
          childList: true,
          subtree: true
        });
        return;
      }

      if (!dialog.shadowRoot) {
        window.setTimeout(bindWhenConnected);
        return;
      }

      overlayObserver = new MutationObserver(bindOverlay);
      overlayObserver.observe(dialog.shadowRoot, {
        childList: true,
        subtree: true
      });
    };

    bindWhenConnected();
    dialog.addEventListener(
      'opened-changed',
      (event: DialogOpenedChangedEvent) => {
        if (event.detail.value) bindWhenConnected();
      }
    );
  };

  private reset(dialog: Dialog) {
    this.dirtyDialogs.delete(dialog);
    this.promptTokens.delete(dialog);
    dialog.noCloseOnEsc = false;
    dialog.noCloseOnOutsideClick = false;
  }

  private async confirmClose(dialog: Dialog) {
    if (this.promptTokens.has(dialog)) return;
    const promptToken = {};
    this.promptTokens.set(dialog, promptToken);
    const dirtyDialogs = this.dirtyDialogs;
    const promptTokens = this.promptTokens;

    const discard = await confirmPrompt(
      'You have unsaved changes. Discard them?',
      {
        header: 'Unsaved changes',
        confirmText: 'Discard'
      }
    );

    if (promptTokens.get(dialog) !== promptToken) return;
    promptTokens.delete(dialog);
    if (!discard) return;

    dirtyDialogs.delete(dialog);
    dialog.noCloseOnEsc = false;
    dialog.noCloseOnOutsideClick = false;
    dialog.opened = false;
  }
}
