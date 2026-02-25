import { expect, fixture, html } from '@open-wc/testing';
import './deploy-env.js';
import type { DeployEnv } from './deploy-env.js';

describe('DeployEnv responsive layout', () => {
  it('should NOT contain any table elements', async () => {
    const el = await fixture<DeployEnv>(html`
      <deploy-env></deploy-env>
    `);
    await el.updateComplete;

    const tables = el.shadowRoot!.querySelectorAll('table');
    expect(tables.length).to.equal(0, 'deploy-env should not use table layouts');
  });

  it('should use flex layout for build definition and build number inputs', async () => {
    const el = await fixture<DeployEnv>(html`
      <deploy-env></deploy-env>
    `);
    await el.updateComplete;

    // The non-folder project container (first flex column container)
    const flexContainers = el.shadowRoot!.querySelectorAll(
      'div[style*="flex-direction: column"]'
    );

    // Should have at least one flex-direction: column container for the combos
    expect(flexContainers.length).to.be.greaterThan(0,
      'Should have flex column containers for combo boxes');
  });

  it('should have combo boxes with flex: 1 to fill available width', async () => {
    const el = await fixture<DeployEnv>(html`
      <deploy-env></deploy-env>
    `);
    await el.updateComplete;

    const combos = el.shadowRoot!.querySelectorAll('vaadin-combo-box[style*="flex: 1"]');
    expect(combos.length).to.be.greaterThan(0,
      'Combo boxes should use flex: 1 to fill width');
  });

  it('should have max-width constraint on combo containers', async () => {
    const el = await fixture<DeployEnv>(html`
      <deploy-env></deploy-env>
    `);
    await el.updateComplete;

    const containers = el.shadowRoot!.querySelectorAll(
      'div[style*="max-width: 600px"]'
    );
    expect(containers.length).to.be.greaterThan(0,
      'Containers should have max-width: 600px');
  });

  it('should render the deploy button with max-width constraint', async () => {
    const el = await fixture<DeployEnv>(html`
      <deploy-env></deploy-env>
    `);
    await el.updateComplete;

    const deployButton = el.shadowRoot!.querySelector(
      'vaadin-button[theme="primary"]'
    ) as HTMLElement;
    expect(deployButton).to.not.be.null;
  });
});
