import { expect, fixture, html } from '../_helpers';
import '../../src/components/environment-tabs/env-control-center.js';
import type { EnvControlCenter } from '../../src/components/environment-tabs/env-control-center.js';

describe('EnvControlCenter delete status', () => {
  it('shows delete in progress state on delete button', async () => {
    const el = await fixture<EnvControlCenter>(
      html`<env-control-center></env-control-center>`
    );

    (el as unknown as { isAdmin: boolean }).isAdmin = true;
    (el as unknown as { isEnvOwner: boolean }).isEnvOwner = true;
    (el as unknown as { isDeletingEnvironment: boolean }).isDeletingEnvironment =
      true;
    await el.updateComplete;

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
});
