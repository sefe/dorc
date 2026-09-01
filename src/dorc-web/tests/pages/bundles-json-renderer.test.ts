import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-project-bundles';

// `page-project-bundles` carries a byte-identical copy of the `showJson` fix in
// `page-scripts-list`, including the comment explaining it — but only the
// scripts copy was guarded. Memoizing the returned callback so its identity is
// stable (which is exactly the pre-fix bug) left the whole suite green here.
//
// `hegs-json-viewer` reads its text into `.data` once, in connectedCallback, and
// the element is reused across rows. `ref` only re-fires when the callback's
// identity changes, so the helper has to return a fresh closure per render.
// Without that, scrolling the Project Bundles grid shows the first bundle's
// request JSON under every later row's chevron.

type Page = HTMLElement & {
  _jsonRenderer(bundle: { Request?: string }): unknown;
};

describe('project bundles request column', () => {
  let page: Page;
  let host: HTMLElement;

  beforeEach(async () => {
    // Not attached: connectedCallback drives the page's data provider.
    page = document.createElement('page-project-bundles') as Page;
    host = document.createElement('div');
    document.body.appendChild(host);
    await settle();
  });

  afterEach(() => host.remove());

  it('re-seeds the viewer when the cell is recycled', async () => {
    render(page._jsonRenderer({ Request: '{"a":1}' }) as never, host);
    await settle();
    const viewer = host.querySelector('hegs-json-viewer') as HTMLElement & {
      data?: unknown;
    };
    expect(viewer.data, 'first row').to.deep.equal({ a: 1 });

    render(page._jsonRenderer({ Request: '{"b":2}' }) as never, host);
    await settle();

    expect(host.querySelector('hegs-json-viewer'), 'element reused').to.equal(
      viewer
    );
    expect(viewer.data, 'shows the new row').to.deep.equal({ b: 2 });
  });
});
