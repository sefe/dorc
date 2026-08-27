import { expect, fixture, html } from '../_helpers';
import '../../src/components/environment-tabs/env-control-center.js';
import type { EnvControlCenter } from '../../src/components/environment-tabs/env-control-center.js';

async function deletingEnvironment(): Promise<EnvControlCenter> {
  const el = await fixture<EnvControlCenter>(
    html`<env-control-center></env-control-center>`
  );

  (el as unknown as { isAdmin: boolean }).isAdmin = true;
  (el as unknown as { isEnvOwner: boolean }).isEnvOwner = true;
  (el as unknown as { environment: unknown }).environment = {
    EnvironmentName: 'Endur DV 14'
  };
  (el as unknown as { isDeletingEnvironment: boolean }).isDeletingEnvironment =
    true;
  await el.updateComplete;

  return el;
}

describe('EnvControlCenter delete status', () => {
  it('keeps the control-center icon aligned with its summary text', async () => {
    const el = await fixture<EnvControlCenter>(
      html`<env-control-center></env-control-center>`
    );

    const summaryLayout = el.shadowRoot!.querySelector(
      '.control-center-summary'
    ) as HTMLElement | null;

    expect(summaryLayout).to.not.equal(null);
    const summaryStyle = getComputedStyle(summaryLayout!);
    expect(summaryStyle.display).to.equal('inline-flex');
    expect(summaryStyle.alignItems).to.equal('center');
    expect(summaryStyle.whiteSpace).to.equal('nowrap');
  });

  it('shows delete in progress state on delete button', async () => {
    const el = await deletingEnvironment();

    const deleteButton = el.shadowRoot!.querySelector(
      'vaadin-button'
    ) as HTMLElement | null;

    expect(deleteButton).to.not.equal(null);
    if (!deleteButton) {
      throw new Error('Delete button was not rendered');
    }
    expect(deleteButton.hasAttribute('disabled')).to.equal(true);
    expect(deleteButton.textContent).to.include('Deleting Environment...');
  });

  it('shows a spinner while the delete is in flight', async () => {
    const el = await deletingEnvironment();

    const spinner = el.shadowRoot!.querySelector('dorc-spinner');

    expect(spinner).to.not.equal(null);
    expect(spinner!.hasAttribute('hidden')).to.equal(false);
  });

  it('names the environment being deleted and warns the delete is slow', async () => {
    const el = await deletingEnvironment();

    const progress = el.shadowRoot!.querySelector('.delete-progress');

    expect(progress).to.not.equal(null);
    expect(progress!.textContent).to.include('Endur DV 14');
    expect(progress!.textContent).to.include('leave this page open');
  });

  it('hides the in progress state once the delete finishes', async () => {
    const el = await deletingEnvironment();

    (
      el as unknown as { isDeletingEnvironment: boolean }
    ).isDeletingEnvironment = false;
    await el.updateComplete;

    expect(
      el.shadowRoot!.querySelector('dorc-spinner')!.hasAttribute('hidden')
    ).to.equal(true);
    expect(el.shadowRoot!.querySelector('.delete-progress')).to.equal(null);
  });
});
