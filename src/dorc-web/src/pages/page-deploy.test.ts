import { expect, fixture, html } from '@open-wc/testing';
import './page-deploy.js';
import type { PageDeploy } from './page-deploy.js';

describe('PageDeploy responsive layout', () => {
  it('should NOT contain any table elements', async () => {
    const el = await fixture<PageDeploy>(html`
      <page-deploy></page-deploy>
    `);
    await el.updateComplete;

    const tables = el.shadowRoot!.querySelectorAll('table');
    expect(tables.length).to.equal(0, 'page-deploy should not use table layouts');
  });

  it('should use flex layout for the project and environment selectors', async () => {
    const el = await fixture<PageDeploy>(html`
      <page-deploy></page-deploy>
    `);
    await el.updateComplete;

    // Find the main container for the combo boxes
    const flexContainers = el.shadowRoot!.querySelectorAll(
      'div[style*="flex-direction: column"]'
    );
    expect(flexContainers.length).to.be.greaterThan(0,
      'Should have flex column containers');
  });

  it('should have combo boxes using flex: 1 for responsive width', async () => {
    const el = await fixture<PageDeploy>(html`
      <page-deploy></page-deploy>
    `);
    await el.updateComplete;

    const combos = el.shadowRoot!.querySelectorAll(
      'vaadin-combo-box[style*="flex: 1"]'
    );
    expect(combos.length).to.be.greaterThan(0,
      'Combo boxes should use flex: 1');
  });

  it('should have a max-width constraint on the selector container', async () => {
    const el = await fixture<PageDeploy>(html`
      <page-deploy></page-deploy>
    `);
    await el.updateComplete;

    const containers = el.shadowRoot!.querySelectorAll(
      'div[style*="max-width: 600px"]'
    );
    expect(containers.length).to.be.greaterThan(0,
      'Selector container should have max-width: 600px');
  });

  it('should include the deploy-env component', async () => {
    const el = await fixture<PageDeploy>(html`
      <page-deploy></page-deploy>
    `);
    await el.updateComplete;

    const deployEnv = el.shadowRoot!.querySelector('deploy-env');
    expect(deployEnv).to.not.be.null;
  });
});
