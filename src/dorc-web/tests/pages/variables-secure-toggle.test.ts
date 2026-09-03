import { expect, settle } from '../_helpers';
import { render, html } from 'lit';
import '@vaadin/checkbox';

// `updatePropertySecure` became async when window.confirm() was replaced, and
// its cancel path reverts the checkbox by writing `.checked` on the event
// target. That target is the light-DOM <input> inside vaadin-checkbox, not the
// host — so the revert left the visible tick in place, telling an admin that a
// variable's values are encrypted when they are still plaintext.
//
// The handler needs a live page and API to drive end to end, so this pins the
// mechanism the fix turns on: which node an async handler must write to for the
// rendered state to follow. If `currentTarget` ever goes back to `target`, the
// first test here is what fails.

const clickInput = (checkbox: HTMLElement) => {
  (checkbox.querySelector('input') as HTMLInputElement).click();
};

describe('async checkbox revert', () => {
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  it('restores the visible state when reverting through currentTarget', async () => {
    let captured: EventTarget | null = null;
    render(
      html`<vaadin-checkbox
        @click="${(e: Event) => {
          captured = e.currentTarget;
        }}"
      ></vaadin-checkbox>`,
      host
    );
    await settle();
    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };

    clickInput(checkbox);
    await settle();
    expect(checkbox.checked, 'ticked by the click').to.equal(true);

    // The revert, as it happens after the confirm dialog resolves.
    (captured as unknown as { checked: boolean }).checked = false;
    await settle();

    expect(checkbox.checked, 'visible tick cleared').to.equal(false);
    expect(checkbox.hasAttribute('checked'), 'checked attribute gone').to.equal(
      false
    );
  });

  it('does not restore the visible state when reverting through target', async () => {
    // Documents why the distinction matters: `target` is the inner input, and
    // writing to it leaves the host — which Lumo styles from — untouched.
    let captured: EventTarget | null = null;
    render(
      html`<vaadin-checkbox
        @click="${(e: Event) => {
          captured = e.target;
        }}"
      ></vaadin-checkbox>`,
      host
    );
    await settle();
    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };

    clickInput(checkbox);
    await settle();
    (captured as unknown as { checked: boolean }).checked = false;
    await settle();

    expect((captured as unknown as HTMLElement).tagName).to.equal('INPUT');
    expect(checkbox.checked, 'host still reads as ticked').to.equal(true);
  });
});
