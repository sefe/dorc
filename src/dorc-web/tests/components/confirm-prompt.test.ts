import { expect, settle } from '../_helpers';
import { confirmPrompt } from '../../src/components/confirm-prompt';

// confirmPrompt replaces window.confirm(). These tests pin the contract every
// call site relies on: it resolves true only on confirm, false on every other
// dismissal path, and leaves no dialog behind.

const openDialog = () =>
  document.body.querySelector('vaadin-confirm-dialog') as
    | (HTMLElement & {
        opened: boolean;
        header: string;
        confirmText: string;
        confirmTheme: string;
      })
    | null;

const clickButton = (label: string) => {
  const dialog = openDialog();
  const overlay = dialog?.shadowRoot?.querySelector(
    'vaadin-confirm-dialog-overlay'
  );
  const buttons = [
    ...(dialog?.querySelectorAll('vaadin-button') ?? []),
    ...(overlay?.shadowRoot?.querySelectorAll('vaadin-button') ?? [])
  ] as HTMLElement[];
  const match = buttons.find(
    b => b.textContent?.trim().toLowerCase() === label.toLowerCase()
  );
  match?.click();
  return Boolean(match);
};

describe('confirmPrompt', () => {
  afterEach(() => {
    document.body
      .querySelectorAll('vaadin-confirm-dialog')
      .forEach(d => d.remove());
  });

  it('renders a vaadin-confirm-dialog with the message', async () => {
    void confirmPrompt('Delete server FOO?');
    await settle();

    const dialog = openDialog();
    expect(dialog).to.not.equal(null);
    expect(dialog?.textContent).to.contain('Delete server FOO?');
    expect(dialog?.opened).to.equal(true);
  });

  it('defaults to a Confirm header and a destructive confirm button', async () => {
    void confirmPrompt('Anything?');
    await settle();

    // These are set as properties; vaadin-confirm-dialog does not reflect them.
    const dialog = openDialog();
    expect(dialog?.header).to.equal('Confirm');
    expect(dialog?.confirmTheme).to.equal('primary error');
  });

  it('honours header, confirmText and confirmTheme overrides', async () => {
    void confirmPrompt('Anything?', {
      header: 'Detach server',
      confirmText: 'Detach',
      confirmTheme: 'primary'
    });
    await settle();

    const dialog = openDialog();
    expect(dialog?.header).to.equal('Detach server');
    expect(dialog?.confirmText).to.equal('Detach');
    expect(dialog?.confirmTheme).to.equal('primary');
  });

  it('resolves true when confirmed', async () => {
    const answer = confirmPrompt('Proceed?', { confirmText: 'Yes' });
    await settle();

    expect(clickButton('Yes'), 'confirm button not found').to.equal(true);
    expect(await answer).to.equal(true);
  });

  it('resolves false when cancelled', async () => {
    const answer = confirmPrompt('Proceed?');
    await settle();

    expect(clickButton('Cancel'), 'cancel button not found').to.equal(true);
    expect(await answer).to.equal(false);
  });

  it('removes the dialog from the DOM once settled', async () => {
    const answer = confirmPrompt('Proceed?');
    await settle();
    clickButton('Cancel');
    await answer;
    await settle();

    expect(document.body.querySelector('vaadin-confirm-dialog')).to.equal(null);
  });

  it('resolves exactly once even though close fires after confirm', async () => {
    // `confirm` and `closed` both fire on the confirm path; the promise must
    // keep the true result rather than being overwritten by the close.
    const answer = confirmPrompt('Proceed?', { confirmText: 'Yes' });
    await settle();
    clickButton('Yes');

    expect(await answer).to.equal(true);
  });
});
