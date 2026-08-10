import { expect, fixture, html } from '../_helpers';
import { of } from 'rxjs';

// tags-input drives @yaireo/tagify via window.Tagify, which the app loads globally
// but the test browser does not (same shim as tag-capacity-ui.test.ts).
class FakeTagify {
  value: { value: string }[] = [];
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  constructor(_input: HTMLInputElement, _opts: unknown) {}
  addTags(tags: string[]) {
    this.value.push(...tags.map(t => ({ value: t })));
  }
  removeAllTags() {
    this.value = [];
  }
}
(window as any).Tagify = FakeTagify;
import '../../src/components/add-edit-database.js';
import type { AddEditDatabase } from '../../src/components/add-edit-database.js';
import '../../src/components/attach-database.js';
import type { AttachDatabase } from '../../src/components/attach-database.js';
import '../../src/components/environment-tabs/env-control-center.js';
import { RefDataDatabasesApi } from '../../src/apis/dorc-api';
import { MAX_TAG_LENGTH } from '../../src/helpers/tag-limits';

// Five assertions — chip round-trip, over-limit
// visible rejection with no API call, exactly-at-limit accept, attach-database
// tag-set-overlap warning, and env-control-center ThinClient tag membership.

describe('add-edit-database tag editing', () => {
  it('round-trips chips from a semicolon-separated Tags value', async () => {
    const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
    el.database = { Id: 5, Name: 'D1', ServerName: 'S1', Tags: ['b', 'a'], ArrayName: '' };
    await el.updateComplete;

    const tagsInput = el.shadowRoot?.getElementById('db-tags') as any;
    expect(tagsInput).to.exist;
    // splitTags sorts; both chips are present, and the field is labelled Tags.
    expect(tagsInput.tags).to.deep.equal(['a', 'b']);
    expect(tagsInput.label).to.equal('Tags');
  });

  it('re-rendering the host with unchanged tags does not rebuild the chips', async () => {
    // Final gate F-A: hosts bind freshly-built arrays each render; the tags
    // setter must no-op when the chip set is unchanged, or every keystroke in a
    // sibling field tears down and rebuilds the chips (and real Tagify fires
    // add events on programmatic addTags).
    const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
    el.database = { Id: 5, Name: 'D1', ServerName: 'S1', Tags: ['a', 'b'], ArrayName: '' };
    await el.updateComplete;

    const tagsInput = el.shadowRoot?.getElementById('db-tags') as any;
    const tagify = tagsInput.tagify;
    let rebuilds = 0;
    const originalRemoveAll = tagify.removeAllTags.bind(tagify);
    tagify.removeAllTags = () => {
      rebuilds += 1;
      originalRemoveAll();
    };

    // An unrelated host re-render (same Type) must not touch the chips.
    (el as any).DatabaseName = 'D1-renamed';
    await el.updateComplete;
    expect(rebuilds).to.equal(0);

    // A genuine tag change still rebuilds.
    el.database = { Id: 5, Name: 'D1', ServerName: 'S1', Tags: ['a', 'b', 'c'], ArrayName: '' };
    await el.updateComplete;
    expect(rebuilds).to.be.greaterThan(0);
    expect(tagsInput.tags).to.deep.equal(['a', 'b', 'c']);
  });

  it('rejects an over-long tag without calling the API', async () => {
    const original = RefDataDatabasesApi.prototype.refDataDatabasesPut;
    let called = 0;
    (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = () => {
      called += 1;
      return of({});
    };
    try {
      const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
      el.database = {
        Id: 5, Name: 'D1', ServerName: 'S1', ArrayName: '',
        Tags: ['ok', 'x'.repeat(MAX_TAG_LENGTH + 1)]
      };
      await el.updateComplete;

      el.saveDatabase();
      expect(called).to.equal(0);
      await new Promise(r => setTimeout(r, 50));
      const card = document.querySelector('vaadin-notification-card');
      expect(card?.textContent).to.contain(String(MAX_TAG_LENGTH));
      document.querySelectorAll('vaadin-notification-card').forEach(c => c.remove());
    } finally {
      (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = original;
    }
  });

  it('saves an exactly-at-limit tag, and many of them, through the API', async () => {
    const original = RefDataDatabasesApi.prototype.refDataDatabasesPut;
    const payloads: any[] = [];
    (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = (req: any) => {
      payloads.push(req);
      return of({});
    };
    try {
      const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
      // 200 tags: past what the delimited column could have held once joined.
      const many = Array.from({ length: 200 }, (_, i) => `tag-${i}-${'x'.repeat(20)}`);
      el.database = {
        Id: 5,
        Name: 'D1',
        ServerName: 'S1',
        Tags: [...many, 'x'.repeat(MAX_TAG_LENGTH)],
        ArrayName: ''
      };
      await el.updateComplete;

      el.saveDatabase();
      expect(payloads.length).to.equal(1);
      expect(payloads[0].databaseApiModel.Tags.length).to.equal(201);
    } finally {
      (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = original;
    }
  });
});

describe('attach-database tag-set overlap', () => {
  async function warned(selectedType: string, existingType: string): Promise<boolean> {
    const el = await fixture<AttachDatabase>(html`<attach-database></attach-database>`);
    (el as any).selectedDatabase = { Id: 1, Name: 'New', ServerName: 's1', Tags: selectedType };
    (el as any).existingDatabases = [
      { Id: 2, Name: 'Existing', ServerName: 's2', Tags: existingType }
    ];
    (el as any).checkForSameTagWarning();
    await el.updateComplete;
    return (el as any).showSameTagWarning as boolean;
  }

  it('warns when any tag overlaps an attached database', async () => {
    expect(await warned('Endur;Ops', 'Reporting;Endur')).to.equal(true);
  });

  it('keeps single-value behaviour: identical single tags still warn', async () => {
    expect(await warned('Endur', 'Endur')).to.equal(true);
  });

  it('stays quiet for disjoint tag sets', async () => {
    expect(await warned('Endur;Ops', 'Reporting;Other')).to.equal(false);
  });

  it('names the overlapping tag in the warning', async () => {
    const el = await fixture<AttachDatabase>(html`<attach-database></attach-database>`);
    (el as any).selectedDatabase = { Id: 1, Name: 'New', ServerName: 's1', Tags: 'Endur;Ops' };
    (el as any).existingDatabases = [
      { Id: 2, Name: 'Existing', ServerName: 's2', Tags: 'Ops;Extra' }
    ];
    (el as any).checkForSameTagWarning();
    await el.updateComplete;
    expect(el.shadowRoot?.textContent).to.contain('Ops');
  });
});

describe('env-control-center ThinClient tag membership', () => {
  it('resolves the app database server when ThinClient matches any one tag', async () => {
    const el: any = await fixture(html`<env-control-center></env-control-center>`);
    el.environment = {
      EnvironmentId: 42,
      EnvironmentName: 'Endur DV 10',
      Details: { ThinClient: 'Endur' }
    };
    el.envContent = {
      EnvironmentName: 'Endur DV 10',
      DbServers: [
        { Id: 1, Name: 'OTHER_DB', ServerName: 'srv-a', Tags: 'Reporting' },
        { Id: 2, Name: 'APP_DB', ServerName: 'srv-b', Tags: 'Endur;Reporting' }
      ]
    };
    await el.updateComplete;

    expect(el.appDbServer?.Name).to.equal('APP_DB');
  });
});
