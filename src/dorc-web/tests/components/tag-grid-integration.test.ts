import { expect, fixture, html } from '../_helpers';

// tags-input needs window.Tagify (app loads it globally; test browser does not).
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
import '../../src/components/grid-button-groups/database-controls.js';
import '../../src/components/grid-button-groups/database-env-controls.js';
import '../../src/components/attached-databases.js';
import { MAX_TAG_STRING_LENGTH } from '../../src/helpers/tag-limits';

// docs/tag-capacity-expansion IS S-005: grid-launched database tag editing mirrors the
// servers pattern (manage-database-tags event from grid controls), the ArrayName column
// is relabelled "Tags", and near-limit values render.

describe('database grid tag controls', () => {
  it('database-controls exposes an Edit Database Tags button firing manage-database-tags', async () => {
    const el = await fixture(html`
      <database-controls
        .databaseDetails="${{ Id: 3, Name: 'db' }}"
        .readonly="${false}"
      ></database-controls>
    `);
    await (el as any).updateComplete;

    const button = Array.from(el.shadowRoot!.querySelectorAll('vaadin-button')).find(
      b => b.getAttribute('title') === 'Edit Database Tags'
    );
    expect(button, 'tags button exists').to.not.be.undefined;

    let detail: any;
    el.addEventListener('manage-database-tags', ((e: CustomEvent) => {
      detail = e.detail;
    }) as EventListener);
    (button as HTMLElement).click();
    expect(detail?.database?.Id).to.equal(3);
  });

  it('database-env-controls tags button is disabled when readonly', async () => {
    const el = await fixture(html`
      <database-env-controls
        .dbDetails="${{ Id: 3, Name: 'db' }}"
        .readonly="${true}"
      ></database-env-controls>
    `);
    await (el as any).updateComplete;

    const button = Array.from(el.shadowRoot!.querySelectorAll('vaadin-button')).find(
      b => b.getAttribute('title') === 'Edit Database Tags'
    );
    expect(button).to.not.be.undefined;
    expect((button as any).disabled).to.be.true;
  });
});

describe('attached-databases tag column', () => {
  it('relabels ArrayName to Tags and renders a near-limit value', async () => {
    const nearLimit = Array.from({ length: 190 }, (_, i) => `tag-${i}-abcdefghijk`).join(';');
    expect(nearLimit.length).to.be.lessThan(MAX_TAG_STRING_LENGTH);

    const el = await fixture(html`
      <attached-databases
        .databases="${[{ Id: 3, Name: 'db', ServerName: 's', ArrayName: nearLimit }]}"
      ></attached-databases>
    `);
    await (el as any).updateComplete;

    const columns = Array.from(el.shadowRoot!.querySelectorAll('vaadin-grid-column'));
    const headers = columns.map(c => c.getAttribute('header'));
    expect(headers).to.include('Tags');
    expect(headers).to.not.include('Array Name');
    // The grid accepted the near-limit item without error; the tags dialog is present.
    expect(el.shadowRoot!.getElementById('database-tags-dialog')).to.not.be.null;
  });
});
