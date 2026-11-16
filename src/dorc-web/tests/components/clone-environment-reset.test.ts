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
import { vi } from 'vitest';
import { NEVER } from 'rxjs';
import { RefDataEnvironmentsApi } from '../../src/apis/dorc-api';

type CloneDialog = HTMLElement & {
  open(environment: unknown): void;
  updateComplete: Promise<unknown>;
};

describe('clone environment dialog reset', () => {
  it('copies new component mappings only when explicitly selected', async () => {
    const clone = vi
      .spyOn(RefDataEnvironmentsApi.prototype, 'refDataEnvironmentsClonePost')
      .mockReturnValue(NEVER);
    const el = await fixture<CloneDialog>(
      html`<clone-environment></clone-environment>`
    );
    el.open({ EnvironmentId: 4, EnvironmentName: 'DEV' });
    await settle();
    const dialog = el.shadowRoot!.querySelector<HTMLElement>(
      '#clone-environment-dialog'
    )!;
    for (const id of [
      '#copy-container-mappings',
      '#copy-cloud-mappings',
      '#copy-api-mappings'
    ]) {
      const checkbox = inDialog(dialog, id) as HTMLElement & {
        checked: boolean;
      };
      expect(checkbox.checked).to.equal(false);
      checkbox.dispatchEvent(
        new CustomEvent('checked-changed', { detail: { value: true } })
      );
    }
    inDialog(dialog, '#new-env-name')!.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'DEV-CLONE' } })
    );
    await el.updateComplete;
    await settle();
    const button = Array.from(dialog.querySelectorAll('vaadin-button')).find(
      item => item.textContent?.trim() === 'Clone'
    ) as HTMLElement;
    button.click();
    expect(clone.mock.calls).to.have.length(1);
    expect(clone.mock.calls[0][0]?.cloneEnvironmentRequest).to.include({
      SourceEnvironmentId: 4,
      NewEnvironmentName: 'DEV-CLONE',
      CopyContainerMappings: true,
      CopyCloudResourceMappings: true,
      CopyApiRegistrationMappings: true
    });
  });

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
