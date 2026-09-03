import {
  expect,
  fixture,
  html,
  inDialog,
  isDialogOpen,
  pressEscape,
  settle
} from '../_helpers';
import '../../src/components/clone-environment';

type CloneDialog = HTMLElement & {
  open(environment: unknown): void;
  updateComplete: Promise<unknown>;
};

describe('clone environment dialog reset', () => {
  it('resets the clone form immediately when Escape dismisses it', async () => {
    const el = await fixture<CloneDialog>(
      html`<clone-environment></clone-environment>`
    );
    const source = { EnvironmentId: 4, EnvironmentName: 'DEV' };
    el.open(source);
    await el.updateComplete;
    await settle();

    const dialog = el.shadowRoot?.querySelector(
      '#clone-environment-dialog'
    ) as HTMLElement;
    const nameField = inDialog(dialog, '#new-env-name') as HTMLElement & {
      value: string;
    };
    const cloneOptions = Array.from(
      dialog.querySelectorAll('vaadin-checkbox')
    ) as (HTMLElement & { checked: boolean })[];
    expect(cloneOptions.length, 'all clone options render').to.be.greaterThan(
      1
    );
    nameField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'DEV-CLONE' } })
    );
    for (const option of cloneOptions) {
      option.dispatchEvent(
        new CustomEvent('checked-changed', { detail: { value: true } })
      );
    }
    await el.updateComplete;

    pressEscape();
    await settle();
    await el.updateComplete;

    expect(isDialogOpen(dialog), 'Escape closes the dialog').to.equal(false);
    const resetName = inDialog(dialog, '#new-env-name') as HTMLElement & {
      value: string;
    };
    expect(resetName.value).to.equal('');
    expect(
      Array.from(dialog.querySelectorAll('vaadin-checkbox')).every(
        option => !(option as HTMLElement & { checked: boolean }).checked
      ),
      'every clone option resets'
    ).to.equal(true);
  });
});
