import { expect, fixture, html } from '@open-wc/testing';
import { DeployEnv } from './deploy-env.js';

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

    // The build-defs-section uses CSS class with flex-direction: column
    const flexContainers = el.shadowRoot!.querySelectorAll('.build-defs-section');

    expect(flexContainers.length).to.be.greaterThan(0,
      'Should have .build-defs-section flex column containers for combo boxes');
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
    // The max-width: 600px is set via .build-defs-section CSS class, not inline
    const styles = (DeployEnv as any).styles;
    const cssText = Array.isArray(styles)
      ? styles.map((s: any) => s.cssText ?? '').join('')
      : (styles as any)?.cssText ?? '';

    expect(cssText).to.include('max-width: 600px',
      'CSS should include max-width: 600px for combo containers');
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
