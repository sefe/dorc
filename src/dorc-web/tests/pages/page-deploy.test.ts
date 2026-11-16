import { expect, fixture, html } from '../_helpers';
import '../../src/pages/page-deploy.js';
import type { PageDeploy } from '../../src/pages/page-deploy.js';

describe('PageDeploy responsive layout', () => {
  it('should NOT contain any table elements', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    await el.updateComplete;

    const tables = el.shadowRoot!.querySelectorAll('table');
    expect(tables.length).to.equal(
      0,
      'page-deploy should not use table layouts'
    );
  });

  it('should use flex layout for the project and environment selectors', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    await el.updateComplete;

    // Find the main container for the combo boxes
    const flexContainers = el.shadowRoot!.querySelectorAll(
      'div[style*="flex-direction: column"]'
    );
    expect(flexContainers.length).to.be.greaterThan(
      0,
      'Should have flex column containers'
    );
  });

  it('should have combo boxes using flex: 1 for responsive width', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    await el.updateComplete;

    const combos = el.shadowRoot!.querySelectorAll(
      'vaadin-combo-box[style*="flex: 1"]'
    );
    expect(combos.length).to.be.greaterThan(
      0,
      'Combo boxes should use flex: 1'
    );
  });

  it('should have a max-width constraint on the selector container', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    await el.updateComplete;

    const containers = el.shadowRoot!.querySelectorAll(
      'div[style*="max-width: 600px"]'
    );
    expect(containers.length).to.be.greaterThan(
      0,
      'Selector container should have max-width: 600px'
    );
  });

  it('should include the deploy-env component', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    await el.updateComplete;

    const deployEnv = el.shadowRoot!.querySelector('deploy-env');
    expect(deployEnv).to.not.be.null;
  });

  it('keeps the deploy mascot visible and centered in the right pane after selecting a project and environment', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    el.selectedEnvironmentName = 'Endur DV 10';
    await el.updateComplete;

    const pane = el.shadowRoot!.querySelector(
      '.deploy-mascot-pane'
    ) as HTMLElement | null;
    const img = el.shadowRoot!.querySelector(
      '.deploy-mascot-pane img.cover'
    ) as HTMLImageElement | null;
    expect(pane).to.not.be.null;
    expect(img).to.not.be.null;
    expect(img!.hidden).to.be.false;
    expect(img!.getAttribute('src')).to.match(/^\/modern-dorc-deploy/);
    expect(getComputedStyle(pane!).justifyContent).to.equal('center');
    expect(getComputedStyle(pane!).alignItems).to.equal('center');
  });

  it('keeps the mascot theme switcher hidden by default and reveals it for Admin users via the Ctrl+Shift+M Easter egg', async () => {
    const el = await fixture<PageDeploy>(html` <page-deploy></page-deploy> `);
    el.isAdmin = false;
    await el.updateComplete;
    window.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'M',
        ctrlKey: true,
        shiftKey: true,
        bubbles: true
      })
    );
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector('.mascot-theme-switcher')).to.be.null;

    el.isAdmin = true;
    await el.updateComplete;
    expect(
      el.shadowRoot!.querySelector('.mascot-theme-switcher'),
      'Switcher should stay hidden for Admins until the Easter egg is triggered'
    ).to.be.null;

    window.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'M',
        ctrlKey: true,
        shiftKey: true,
        bubbles: true
      })
    );
    await el.updateComplete;

    const switcher = el.shadowRoot!.querySelector('.mascot-theme-switcher');
    expect(switcher).to.not.be.null;

    const buttons = Array.from(
      switcher!.querySelectorAll<HTMLButtonElement>('.mascot-theme-btn')
    );
    const halloweenBtn = buttons.find(
      b => b.textContent?.trim() === 'Halloween'
    );
    expect(halloweenBtn).to.not.be.undefined;
    halloweenBtn!.click();
    await el.updateComplete;

    const img = el.shadowRoot!.querySelector(
      '.deploy-mascot-pane img.cover'
    ) as HTMLImageElement;
    expect(img.getAttribute('src')).to.equal(
      '/modern-dorc-deploy-halloween.webp'
    );

    const autoBtn = buttons.find(b => b.textContent?.trim() === 'Auto');
    autoBtn!.click();
    await el.updateComplete;

    // Pressing Ctrl+Shift+M again hides the switcher
    window.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'm',
        ctrlKey: true,
        shiftKey: true,
        bubbles: true
      })
    );
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector('.mascot-theme-switcher')).to.be.null;
  });
});
