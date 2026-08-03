import { expect, settle } from '../_helpers';
import { render, html } from 'lit';
import { live } from 'lit/directives/live.js';
import { ref } from 'lit/directives/ref.js';
import '@vaadin/checkbox';
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
});
