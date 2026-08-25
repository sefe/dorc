import { vi } from 'vitest';
import { expect, fixture, html, settle } from '../_helpers';

const { confirmPromptSpy } = vi.hoisted(() => ({
  confirmPromptSpy: vi.fn()
}));

vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: confirmPromptSpy
}));

import '@vaadin/button';
import '@vaadin/dialog';
import type { Dialog } from '@vaadin/dialog';
import { LitElement } from 'lit';
import { ref } from 'lit/directives/ref.js';
import { UnsavedChangesGuard } from '../../src/components/unsaved-changes-guard';

class UnsavedChangesGuardHost extends LitElement {
  private readonly guard = new UnsavedChangesGuard();

  protected render() {
    return html`
      <vaadin-dialog ${ref(this.guard.attach)} opened>
        <input />
      </vaadin-dialog>
    `;
  }
}

if (!customElements.get('unsaved-changes-guard-host')) {
  customElements.define('unsaved-changes-guard-host', UnsavedChangesGuardHost);
}

describe('UnsavedChangesGuard', () => {
  beforeEach(() => {
    confirmPromptSpy.mockReset();
  });

  async function guardedDialog() {
    const dialog = await fixture<Dialog>(html`
      <vaadin-dialog opened>
        <input />
        <vaadin-button>Close</vaadin-button>
      </vaadin-dialog>
    `);
    const guard = new UnsavedChangesGuard();
    guard.attach(dialog);

    dialog
      .querySelector('input')
      ?.dispatchEvent(
        new InputEvent('input', { bubbles: true, composed: true })
      );

    return dialog;
  }

  it('blocks framework dismissal after the form becomes dirty', async () => {
    const dialog = await guardedDialog();

    expect(dialog.noCloseOnEsc).to.equal(true);
    expect(dialog.noCloseOnOutsideClick).to.equal(true);
  });

  it('prompts for overlay dismissal when attached before connection', async () => {
    confirmPromptSpy.mockResolvedValue(false);
    const dialog = document.createElement('vaadin-dialog') as Dialog;
    const input = document.createElement('input');
    dialog.appendChild(input);
    dialog.opened = true;

    const guard = new UnsavedChangesGuard();
    guard.attach(dialog);
    await settle();
    document.body.appendChild(dialog);
    await settle();

    input.dispatchEvent(
      new InputEvent('input', { bubbles: true, composed: true })
    );
    const overlay = dialog.shadowRoot?.querySelector('vaadin-dialog-overlay');
    overlay?.dispatchEvent(
      new CustomEvent('vaadin-overlay-escape-press', { cancelable: true })
    );
    await settle();

    expect(confirmPromptSpy.mock.calls).to.have.length(1);
    expect(dialog.opened).to.equal(true);
    dialog.remove();
  });

  it('binds overlay dismissal when attached by ref inside a shadow root', async () => {
    confirmPromptSpy.mockResolvedValue(false);
    const host = await fixture<UnsavedChangesGuardHost>(
      html`<unsaved-changes-guard-host></unsaved-changes-guard-host>`
    );
    await settle();
    const dialog = host.shadowRoot?.querySelector('vaadin-dialog') as Dialog;

    dialog
      .querySelector('input')
      ?.dispatchEvent(
        new InputEvent('input', { bubbles: true, composed: true })
      );
    const overlay = dialog.shadowRoot?.querySelector('vaadin-dialog-overlay');
    overlay?.dispatchEvent(
      new CustomEvent('vaadin-overlay-escape-press', { cancelable: true })
    );
    await settle();

    expect(confirmPromptSpy.mock.calls).to.have.length(1);
    expect(dialog.opened).to.equal(true);
  });

  it('keeps the dialog open when discarding is declined', async () => {
    confirmPromptSpy.mockResolvedValue(false);
    const dialog = await guardedDialog();

    (dialog.querySelector('vaadin-button') as HTMLElement).click();
    await settle();

    expect(confirmPromptSpy.mock.calls).to.have.length(1);
    expect(dialog.opened).to.equal(true);
  });

  it('closes and clears the guard when discarding is confirmed', async () => {
    confirmPromptSpy.mockResolvedValue(true);
    const dialog = await guardedDialog();

    (dialog.querySelector('vaadin-button') as HTMLElement).click();
    await settle();

    expect(dialog.opened).to.equal(false);
    expect(dialog.noCloseOnEsc).to.equal(false);
    expect(dialog.noCloseOnOutsideClick).to.equal(false);
  });

  it('ignores a stale discard result after the dialog is reopened', async () => {
    let resolvePrompt: ((value: boolean) => void) | undefined;
    confirmPromptSpy.mockImplementation(
      () =>
        new Promise<boolean>(resolve => {
          resolvePrompt = resolve;
        })
    );
    const dialog = await guardedDialog();

    (dialog.querySelector('vaadin-button') as HTMLElement).click();
    await settle();
    dialog.opened = false;
    await settle();
    dialog.opened = true;
    await settle();
    resolvePrompt?.(true);
    await settle();

    expect(dialog.opened).to.equal(true);
  });
});
