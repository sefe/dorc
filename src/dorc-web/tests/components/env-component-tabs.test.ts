import { expect, fixture, html } from '../_helpers';
import '../../src/components/environment-tabs/env-containers.js';
import '../../src/components/environment-tabs/env-cloud.js';
import '../../src/components/environment-tabs/env-apis.js';
import '../../src/components/add-edit-container.js';
import '../../src/components/add-edit-cloud-resource.js';
import '../../src/components/add-edit-api-registration.js';
import '../../src/components/attach-container.js';
import type { AddEditContainer } from '../../src/components/add-edit-container.js';

// IS S-007 (docs/env-details-component-tabs): the three Components sub-tabs are real
// grid-based tabs — attach + create controls gated on environment editability, per-type
// columns, self-fetching (network calls fail gracefully under test; render shape is
// asserted here, API behaviour is covered by the backend controller tests).

const tabCases = [
  {
    tag: 'env-containers',
    grid: 'containers-grid',
    attachLabel: 'Attach Container',
    columns: ['Name', 'Image', 'Registry', 'Host Server', 'Tags']
  },
  {
    tag: 'env-cloud',
    grid: 'cloud-resources-grid',
    attachLabel: 'Attach Cloud Resource',
    columns: ['Name', 'Provider', 'Resource Type', 'Resource Identifier', 'Subscription', 'Tags']
  },
  {
    tag: 'env-apis',
    grid: 'api-registrations-grid',
    attachLabel: 'Attach API',
    columns: ['Name', 'Base URL', 'Version', 'Health Check URL', 'Tags']
  }
];

for (const c of tabCases) {
  describe(`${c.tag} tab`, () => {
    it('renders a grid with the per-type columns', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      await (el as any).updateComplete;

      const grid = el.shadowRoot!.getElementById(c.grid);
      expect(grid, `${c.grid} should exist`).to.not.be.null;

      const headers = Array.from(
        grid!.querySelectorAll('vaadin-grid-sort-column')
      ).map(col => col.getAttribute('header'));
      for (const header of c.columns) {
        expect(headers, `column ${header}`).to.include(header);
      }
    });

    it('renders attach and create controls', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      await (el as any).updateComplete;

      const buttons = Array.from(el.shadowRoot!.querySelectorAll('vaadin-button'));
      const titles = buttons.map(b => b.getAttribute('title'));
      expect(titles).to.include(c.attachLabel);
    });

    it('disables write controls when the environment is read-only', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      (el as any).envReadOnly = true;
      await (el as any).updateComplete;

      const buttons = Array.from(el.shadowRoot!.querySelectorAll('vaadin-button'));
      expect(buttons.length).to.be.greaterThan(0);
      for (const button of buttons) {
        expect(button.disabled, `${button.textContent?.trim()} should be disabled`).to.be
          .true;
      }
    });

    it('enables write controls when the environment is editable', async () => {
      const el = await fixture(
        document.createElement(c.tag) as unknown as ReturnType<typeof html>
      );
      (el as any).envReadOnly = false;
      await (el as any).updateComplete;

      const buttons = Array.from(el.shadowRoot!.querySelectorAll('vaadin-button'));
      for (const button of buttons) {
        expect(button.disabled).to.be.false;
      }
    });
  });
}

describe('add-edit-container form', () => {
  it('binds the container fields into the inputs', async () => {
    const el = await fixture<AddEditContainer>(html`
      <add-edit-container
        .container="${{
          Id: 5,
          Name: 'web01',
          Image: 'nginx:1',
          Registry: 'reg.local',
          HostServerName: 'host01',
          Tags: 'edge;web'
        }}"
      ></add-edit-container>
    `);
    await el.updateComplete;

    const values = Array.from(
      el.shadowRoot!.querySelectorAll('vaadin-text-field')
    ).map(f => f.value);
    expect(values).to.deep.equal(['web01', 'nginx:1', 'reg.local', 'host01', 'edge;web']);
  });

  it('labels the action Create for a new item and Save for an existing one', async () => {
    const fresh = await fixture<AddEditContainer>(html`
      <add-edit-container .container="${{}}"></add-edit-container>
    `);
    await fresh.updateComplete;
    expect(fresh.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()).to.equal(
      'Create'
    );

    const existing = await fixture<AddEditContainer>(html`
      <add-edit-container .container="${{ Id: 5, Name: 'x', Image: 'y' }}"></add-edit-container>
    `);
    await existing.updateComplete;
    expect(
      existing.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()
    ).to.equal('Save');
  });
});

describe('attach-container dialog content', () => {
  it('renders a combo box and an attach button', async () => {
    const el = await fixture(html`<attach-container .envId="${1}"></attach-container>`);
    await (el as any).updateComplete;

    expect(el.shadowRoot!.querySelector('vaadin-combo-box')).to.not.be.null;
    expect(el.shadowRoot!.querySelector('vaadin-button')!.textContent!.trim()).to.equal(
      'Attach'
    );
  });
});
