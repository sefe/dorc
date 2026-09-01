import { of } from 'rxjs';
import { expect, fixture, html } from '../_helpers';

class FakeTagify {
  value: { value: string }[] = [];

  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  constructor(_input: HTMLInputElement, _opts: unknown) {}

  addTags(tags: string[]) {
    this.value.push(...tags.map(tag => ({ value: tag })));
  }

  removeAllTags() {
    this.value = [];
  }
}

(window as any).Tagify = FakeTagify;

import '../../src/components/database-tags.js';
import type { DatabaseTags } from '../../src/components/database-tags.js';
import '../../src/components/grid-button-groups/database-controls.js';
import '../../src/components/grid-button-groups/database-env-controls.js';
import '../../src/components/attached-databases.js';
import '../../src/pages/page-databases-list.js';
import { RefDataDatabasesApi } from '../../src/apis/dorc-api';

describe('database tag management', () => {
  it('updates only the selected database tags through the API', async () => {
    const original = RefDataDatabasesApi.prototype.refDataDatabasesPut;
    const payloads: any[] = [];
    (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = (
      request: any
    ) => {
      payloads.push(request);
      return of(request.databaseApiModel);
    };

    try {
      const el = await fixture<DatabaseTags>(
        html`<database-tags
          .database="${{
            Id: 5,
            Name: 'D1',
            ServerName: 'S1',
            ArrayName: 'AG',
            Tags: ['existing']
          }}"
        ></database-tags>`
      );
      await el.updateComplete;

      const tagsInput = el.shadowRoot?.getElementById('tag-input') as any;
      tagsInput.tagify.value = [{ value: 'new-tag' }];

      let updated = false;
      el.addEventListener('database-tags-updated', () => (updated = true));
      el.save();

      expect(payloads).to.have.length(1);
      expect(payloads[0].id).to.equal(5);
      expect(payloads[0].databaseApiModel.Tags).to.deep.equal(['new-tag']);
      expect(payloads[0].databaseApiModel.ArrayName).to.equal('AG');
      expect(updated).to.equal(true);
    } finally {
      (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = original;
    }
  });

  it('exposes tag controls on both database routes', async () => {
    const globalControls = await fixture<any>(html`
      <database-controls
        .databaseDetails="${{ Id: 3, Name: 'db' }}"
        .readonly="${false}"
      ></database-controls>
    `);
    const environmentControls = await fixture<any>(html`
      <database-env-controls
        .dbDetails="${{ Id: 3, Name: 'db' }}"
        .readonly="${false}"
      ></database-env-controls>
    `);

    for (const controls of [globalControls, environmentControls]) {
      await controls.updateComplete;
      const button = controls.shadowRoot?.querySelector(
        'vaadin-button[aria-label="Edit Database Tags"]'
      ) as HTMLElement;
      expect(button).to.exist;

      let selectedId: number | undefined;
      controls.addEventListener('manage-database-tags', (event: Event) => {
        selectedId = (event as CustomEvent).detail.database.Id;
      });
      button.click();
      expect(selectedId).to.equal(3);
    }
  });

  it('disables environment tag management when the environment is read-only', async () => {
    const el = await fixture<any>(html`
      <database-env-controls
        .dbDetails="${{ Id: 3, Name: 'db' }}"
        .readonly="${true}"
      ></database-env-controls>
    `);
    await el.updateComplete;

    const button = el.shadowRoot?.querySelector(
      'vaadin-button[aria-label="Edit Database Tags"]'
    ) as HTMLButtonElement;
    expect(button.disabled).to.equal(true);
  });

  it('opens the tag editor from an attached database row', async () => {
    const el = await fixture<any>(html`
      <attached-databases
        .databases="${[{ Id: 3, Name: 'db', Tags: ['one'] }]}"
        .readonly="${false}"
      ></attached-databases>
    `);
    await el.updateComplete;

    const controls = await fixture<any>(
      el.databaseControlsRenderer({
        Id: 3,
        Name: 'db',
        Tags: ['one']
      })
    );
    controls.dispatchEvent(new CustomEvent('manage-database-tags'));
    await el.updateComplete;

    expect(el.tagsDialogOpened).to.equal(true);
    expect(el.selectedDatabase.Id).to.equal(3);
    expect(el.shadowRoot?.getElementById('database-tags-dialog')).to.exist;
  });

  it('opens the tag editor from the global databases route', () => {
    const page = document.createElement('page-databases-list') as any;
    page.openManageDatabaseTagsDialog(
      new CustomEvent('manage-database-tags', {
        detail: { database: { Id: 3, Name: 'db', Tags: ['one'] } }
      })
    );

    expect(page.manageTagsDialogOpened).to.equal(true);
    expect(page.selectedDatabase.Id).to.equal(3);
  });
});
