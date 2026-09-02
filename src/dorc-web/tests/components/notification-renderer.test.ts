import { expect, fixture, html, settle } from '../_helpers';
import '../../src/components/notifications/error-notification';
import '../../src/components/notifications/success-notification';
import '../../src/components/notifications/warning-notification';

// The toasts render their message through a Vaadin Lit renderer directive with
// the message in its dependency array. Vaadin only re-runs a bare `.renderer`
// when `opened` flips, so before the directive an already-visible toast kept
// showing the message it opened with. These tests pin the new behaviour: the
// text tracks the property whether or not the toast is currently open.

type Toast = HTMLElement & { open(): void };

const cardText = (toast: HTMLElement) => {
  const notification = toast.shadowRoot?.querySelector(
    'vaadin-notification'
  ) as (HTMLElement & { _card?: HTMLElement }) | null;
  return notification?._card?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
};

const cases = [
  { tag: 'error-notification', attribute: 'errorMessage' },
  { tag: 'success-notification', attribute: 'successMessage' },
  { tag: 'warning-notification', attribute: 'warningMessage' }
] as const;

describe('notification toasts', () => {
  cases.forEach(({ tag, attribute }) => {
    describe(tag, () => {
      it('shows the message it was opened with', async () => {
        const toast = (await fixture(
          html`<div></div>`
        )) as unknown as HTMLElement;
        const el = document.createElement(tag) as Toast;
        el.setAttribute(attribute, 'First problem');
        toast.appendChild(el);
        await settle();
        el.open();
        await settle();

        expect(cardText(el)).to.contain('First problem');
      });

      it('repaints when the message changes while it is open', async () => {
        const host = (await fixture(
          html`<div></div>`
        )) as unknown as HTMLElement;
        const el = document.createElement(tag) as Toast;
        el.setAttribute(attribute, 'First problem');
        host.appendChild(el);
        await settle();
        el.open();
        await settle();
        expect(cardText(el)).to.contain('First problem');

        // No requestContentUpdate() call, and `opened` never flips — the
        // directive's dependency array is what drives the repaint.
        el.setAttribute(attribute, 'Second problem');
        await settle();

        expect(cardText(el)).to.contain('Second problem');
        expect(cardText(el)).to.not.contain('First problem');
      });
    });
  });

  it('reuses the active error notification for repeated failures', async () => {
    const host = (await fixture(html`<div></div>`)) as unknown as HTMLElement;
    const first = document.createElement('error-notification') as Toast;
    const second = document.createElement('error-notification') as Toast;
    first.setAttribute('errorMessage', 'First problem');
    second.setAttribute('errorMessage', 'Latest problem');
    host.append(first, second);
    await settle();

    first.open();
    second.open();
    await settle();

    expect(first.isConnected).to.equal(true);
    expect(second.isConnected).to.equal(false);
    expect(cardText(first)).to.contain('Latest problem');
    expect(host.querySelectorAll('error-notification')).to.have.length(1);
  });

  it('normalizes a raw AjaxError message at the notification boundary', async () => {
    const host = (await fixture(html`<div></div>`)) as unknown as HTMLElement;
    const el = document.createElement('error-notification') as Toast;
    el.setAttribute('errorMessage', 'ajax error 401');
    host.appendChild(el);
    await settle();

    el.open();
    await settle();

    expect(cardText(el)).to.contain(
      'Session has expired, please refresh the page.'
    );
    expect(cardText(el)).to.not.contain('ajax error');
  });
});
