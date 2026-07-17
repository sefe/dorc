import { expect, fixture, html } from '../_helpers';
import { of } from 'rxjs';

// tags-input drives @yaireo/tagify via window.Tagify, which the app loads globally but
// the test browser does not. A minimal fake keeps the chip API (value/addTags/
// removeAllTags) honest for these tests.
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
import '../../src/components/database-tags.js';
import '../../src/components/server-tags.js';
import type { AddEditDatabase } from '../../src/components/add-edit-database.js';
import type { DatabaseTags } from '../../src/components/database-tags.js';
import type { ServerTags } from '../../src/components/server-tags.js';
import { RefDataDatabasesApi, RefDataServersApi } from '../../src/apis/dorc-api';
import { MAX_TAG_STRING_LENGTH } from '../../src/helpers/tag-limits';

// docs/tag-capacity-expansion IS S-004: per-field limits (nothing silently loosens),
// chip-style database tag editing (U-2a), and joined-string enforcement in both save
// paths so the UI never submits what the API will 400.

describe('add-edit-database per-field limits and chip editor', () => {
  it('keeps name/type/instance at 50 and replaces the Array Name text field with chips', async () => {
    const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
    await el.updateComplete;

    const fields = Array.from(el.shadowRoot!.querySelectorAll('vaadin-text-field'));
    const byLabel = new Map(fields.map(f => [f.getAttribute('label'), f]));
    expect(byLabel.get('Database')!.getAttribute('maxlength')).to.equal('50');
    expect(byLabel.get('Application Tag')!.getAttribute('maxlength')).to.equal('50');
    expect(byLabel.get('Instance')!.getAttribute('maxlength')).to.equal('50');
    // The plain Array Name field is gone; the chip editor is present.
    expect([...byLabel.keys()].some(l => l?.startsWith('Array Name'))).to.be.false;
    expect(el.shadowRoot!.querySelector('#db-tags-input')).to.not.be.null;
  });

  it('splits an existing ArrayName into chips', async () => {
    const el = await fixture<AddEditDatabase>(html`<add-edit-database></add-edit-database>`);
    el.database = { Id: 3, Name: 'db', ArrayName: 'edge;web tier' };
    await el.updateComplete;

    const tagsInput = el.shadowRoot!.querySelector('#db-tags-input') as any;
    expect(tagsInput.tags).to.deep.equal(['edge', 'web tier']);
  });
});

describe('database-tags joined-string enforcement', () => {
  it('round-trips ArrayName through chips', async () => {
    const el = await fixture<DatabaseTags>(html`<database-tags></database-tags>`);
    el.database = { Id: 3, Name: 'db', ArrayName: 'b;a' };
    await el.updateComplete;

    const tagsInput = el.shadowRoot!.querySelector('#tag-input') as any;
    // splitTags sorts; join preserves the chip list.
    expect(tagsInput.tags).to.deep.equal(['a', 'b']);
  });

  it('rejects an over-limit joined string without calling the API', async () => {
    const original = RefDataDatabasesApi.prototype.refDataDatabasesPut;
    let called = 0;
    (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = () => {
      called += 1;
      return of({});
    };
    try {
      const el = await fixture<DatabaseTags>(html`<database-tags></database-tags>`);
      const oversized = Array.from({ length: 200 }, (_, i) => `tag-${i}-${'x'.repeat(20)}`);
      expect(oversized.join(';').length).to.be.greaterThan(MAX_TAG_STRING_LENGTH);
      el.database = { Id: 3, Name: 'db', ArrayName: oversized.join(';') };
      await el.updateComplete;

      el.save();
      expect(called).to.equal(0);
    } finally {
      (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = original;
    }
  });

  it('saves an at-limit joined string through the API', async () => {
    const original = RefDataDatabasesApi.prototype.refDataDatabasesPut;
    const payloads: any[] = [];
    (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = (req: any) => {
      payloads.push(req);
      return of({});
    };
    try {
      const el = await fixture<DatabaseTags>(html`<database-tags></database-tags>`);
      el.database = { Id: 3, Name: 'db', ArrayName: 'edge;web' };
      await el.updateComplete;

      el.save();
      expect(payloads.length).to.equal(1);
      expect(payloads[0].databaseApiModel.ArrayName).to.equal('edge;web');
    } finally {
      (RefDataDatabasesApi.prototype as any).refDataDatabasesPut = original;
    }
  });
});

describe('server-tags joined-string enforcement', () => {
  it('rejects an over-limit joined string without calling the API', async () => {
    const original = RefDataServersApi.prototype.refDataServersPut;
    let called = 0;
    (RefDataServersApi.prototype as any).refDataServersPut = () => {
      called += 1;
      return of({});
    };
    try {
      const el = await fixture<ServerTags>(html`<server-tags></server-tags>`);
      const oversized = Array.from({ length: 200 }, (_, i) => `tag-${i}-${'x'.repeat(20)}`);
      el.setTags({ ServerId: 1, Name: 's', ApplicationTags: oversized.join(';') });
      await el.updateComplete;

      el.save();
      expect(called).to.equal(0);
    } finally {
      (RefDataServersApi.prototype as any).refDataServersPut = original;
    }
  });
});
