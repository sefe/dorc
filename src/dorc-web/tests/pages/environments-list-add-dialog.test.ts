import { expect, fixture, html, settle } from '../_helpers';
import '../../src/pages/page-environments-list';

// The Create Environment form moved into the dialog's renderer, which means it
// does not exist in the DOM until the dialog has opened once. The open handler
// still reached for it via @query and called clearAllFields() — that threw on
// null *before* the line that opens the dialog, so the button did nothing at
// all, permanently.
//
// These pin both halves: opening must not throw, and each open must hand the
// user a freshly built form rather than the previous one's state.

type Host = HTMLElement & {
  addEnvironment(): void;
  addEnvDialogOpened: boolean;
};

const mount = async () => {
  const el = (await fixture(
    html`<page-environments-list></page-environments-list>`
  )) as unknown as Host;
  await settle();
  return el;
};

const formIn = (el: Host) =>
  el.shadowRoot?.querySelector('#add-environment') ?? null;

describe('page-environments-list add dialog', () => {
  it('opens without throwing', async () => {
    const el = await mount();

    expect(() => el.addEnvironment()).to.not.throw();
    expect(el.addEnvDialogOpened, 'dialog opened').to.equal(true);
  });

  it('renders the form once open', async () => {
    const el = await mount();
    expect(formIn(el), 'absent before open').to.equal(null);

    el.addEnvironment();
    await settle();
    await settle();

    expect(formIn(el), 'present after open').to.not.equal(null);
  });

  it('builds a fresh form on each open', async () => {
    const el = await mount();

    el.addEnvironment();
    await settle();
    await settle();
    const first = formIn(el);

    el.addEnvDialogOpened = false;
    await settle();
    el.addEnvironment();
    await settle();
    await settle();
    const second = formIn(el);

    expect(first, 'first form built').to.not.equal(null);
    expect(second, 'second form built').to.not.equal(null);
    // The overlay caches its rendered root across close/reopen, so without the
    // keyed rebuild this would be the same element carrying the old input.
    expect(second, 'not the same element').to.not.equal(first);
  });
});
