import { expect, settle } from '../_helpers';
import { render, html } from 'lit';
import { live } from 'lit/directives/live.js';
import { ref } from 'lit/directives/ref.js';
import '@vaadin/checkbox';
import '@vaadin/text-field';
import '../../src/components/hegs-json-viewer';
import { HegsJsonViewer } from '../../src/components/hegs-json-viewer';

// Vaadin recycles grid cell content: it only clears a cell when the renderer
// function itself changes, which a Lit renderer directive assigns exactly once.
// So the same element survives into whatever row occupies that cell next, and
// two things that used to be safe under the old imperative renderers — which
// rebuilt the element every call — are no longer.
//
// These pin the two mechanisms rather than the individual call sites, since the
// same shape recurs across the scripts, config-values and access-control grids.

describe('recycled grid cells', () => {
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  it('re-applies a checkbox value the user diverged from the model', async () => {
    // Without live(), Lit dirty-checks against its last committed value: the
    // user's click never went through Lit, so committing the same model value
    // again is skipped and the next row inherits the tick.
    const tpl = (checked: boolean) =>
      html`<vaadin-checkbox .checked="${live(checked)}"></vaadin-checkbox>`;

    render(tpl(false), host);
    await settle();
    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };

    checkbox.checked = true;
    await settle();

    // Cell recycled onto a row whose model value is still false.
    render(tpl(false), host);
    await settle();

    expect(checkbox.checked, 'shows the new row, not the old tick').to.equal(
      false
    );
  });

  it('re-seeds a JSON viewer when the row changes', async () => {
    // hegs-json-viewer reads its text into `.data` once, in connectedCallback.
    // The element is reused, so `.data` has to be bound per render instead.
    const showJson = (raw: string) => (element?: Element) => {
      if (!element) return;
      const viewer = element as unknown as HegsJsonViewer & { data?: unknown };
      viewer.data = JSON.parse(raw);
      viewer.expand('**');
    };
    const tpl = (raw: string) =>
      html`<hegs-json-viewer ${ref(showJson(raw))}></hegs-json-viewer>`;

    render(tpl('{"a":1}'), host);
    await settle();
    const viewer = host.querySelector('hegs-json-viewer') as HTMLElement & {
      data?: unknown;
    };
    expect(viewer.data).to.deep.equal({ a: 1 });

    render(tpl('{"b":2}'), host);
    await settle();

    expect(
      host.querySelector('hegs-json-viewer'),
      'same element reused'
    ).to.equal(viewer);
    expect(viewer.data, 'shows the new row').to.deep.equal({ b: 2 });
  });

  it('does not write to the previous row when the cell is recycled', async () => {
    // `checked-changed` is a notify event: Vaadin fires it when the property
    // is set, not only when a user acts. Lit commits the property part before
    // the event part, so committing the next row's value fires it into the
    // previous row's listener — which is still attached — and any handler that
    // persists what it receives writes to the wrong record. `change` is
    // gesture-only, which is why the editable cell renderers use it.
    const saved: string[] = [];
    const rowA = { id: 'A', enabled: true };
    const rowB = { id: 'B', enabled: false };

    const tpl = (row: { id: string; enabled: boolean }) => html`
      <vaadin-checkbox
        .checked="${live(row.enabled)}"
        @change="${(e: Event) => {
          const checked = (
            e.currentTarget as HTMLElement & {
              checked: boolean;
            }
          ).checked;
          if (row.enabled === checked) return;
          row.enabled = checked;
          saved.push(`${row.id}=${row.enabled}`);
        }}"
      ></vaadin-checkbox>
    `;

    render(tpl(rowA), host);
    await settle();

    render(tpl(rowB), host);
    await settle();

    expect(saved, 'no row was written').to.deep.equal([]);
    expect(rowA.enabled, 'row A untouched').to.equal(true);
  });

  it('still records a real user click on the current row', async () => {
    const saved: string[] = [];
    const row = { id: 'A', enabled: false };

    render(
      html`<vaadin-checkbox
        .checked="${live(row.enabled)}"
        @change="${(e: Event) => {
          row.enabled = (
            e.currentTarget as HTMLElement & {
              checked: boolean;
            }
          ).checked;
          saved.push(`${row.id}=${row.enabled}`);
        }}"
      ></vaadin-checkbox>`,
      host
    );
    await settle();

    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(saved).to.deep.equal(['A=true']);
  });

  it('does not write a text field value into the previous row', async () => {
    // Same notify-event mechanism as the checkbox, and the one that persists:
    // the environment-history comment field writes into the row model that
    // edit-comments-controls later PUTs.
    const rowA = { Comment: 'A comment' };
    const rowB = { Comment: 'B comment' };

    const tpl = (history: { Comment: string }) => html`
      <vaadin-text-field
        readonly
        .value="${history.Comment ?? ''}"
        @change="${(e: Event) => {
          history.Comment = (
            e.currentTarget as HTMLElement & {
              value: string;
            }
          ).value;
        }}"
      ></vaadin-text-field>
    `;

    render(tpl(rowA), host);
    await settle();
    render(tpl(rowB), host);
    await settle();

    expect(rowA.Comment, 'row A untouched').to.equal('A comment');
    expect(rowB.Comment, 'row B untouched').to.equal('B comment');
  });
});
