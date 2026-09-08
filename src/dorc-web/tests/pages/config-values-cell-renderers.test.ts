import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-config-values-list';

// The Secure and IsForProd columns are editable checkboxes in recycled grid
// cells, and both halves of that have to hold:
//
//  - `change` rather than `checked-changed`, so committing the next row's value
//    does not fire into the previous row's still-attached listener — which here
//    PUTs the config value.
//  - `live()`, so a tick the user made outside Lit is re-applied from the model
//    when the cell is recycled. Lit dirty-checks against its last committed
//    value otherwise, and the next row inherits the tick — showing a plaintext
//    value as encrypted, or a non-prod value as production.
//
// The renderers are called directly: connectedCallback would drive the page's
// data provider at the API, and `isAdmin` is all these two need.

type ConfigValue = {
  Id?: number;
  Key?: string;
  Secure?: boolean;
  IsForProd?: boolean;
};

type Page = HTMLElement & {
  isAdmin: boolean;
  isSecuredRenderer(configValue: ConfigValue): unknown;
  isForProdRenderer(configValue: ConfigValue): unknown;
};

describe('page-config-values-list editable cell renderers', () => {
  let host: HTMLElement;
  let page: Page;

  beforeEach(async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    page = document.createElement('page-config-values-list') as Page;
    page.isAdmin = true;
    await settle();
  });

  afterEach(() => host.remove());

  const recycles = async (
    renderer: (item: ConfigValue) => unknown,
    field: 'Secure' | 'IsForProd'
  ) => {
    render(renderer({ Id: 1, Key: 'first', [field]: false }) as never, host);
    await settle();

    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };
    // The user ticks it. This does not go through Lit.
    checkbox.checked = true;
    await settle();

    // Recycled onto a row that does not carry the flag.
    render(renderer({ Id: 2, Key: 'second', [field]: false }) as never, host);
    await settle();

    expect(host.querySelector('vaadin-checkbox'), 'element reused').to.equal(
      checkbox
    );
    return checkbox.checked;
  };

  it('Secure column shows the new row, not the old tick', async () => {
    expect(
      await recycles(item => page.isSecuredRenderer(item), 'Secure')
    ).to.equal(false);
  });

  it('IsForProd column shows the new row, not the old tick', async () => {
    expect(
      await recycles(item => page.isForProdRenderer(item), 'IsForProd')
    ).to.equal(false);
  });

  it('does not persist when Secure is set programmatically', async () => {
    // A recycled cell sets the property; only a gesture may reach the API.
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isSecuredRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox') as HTMLElement & {
        checked: boolean;
      }
    ).checked = true;
    await settle();

    expect(updated, 'no update from a non-gesture change').to.deep.equal([]);
  });

  it('does not persist when IsForProd is set programmatically', async () => {
    // The file's header promises both halves for both columns; only Secure had
    // the gesture-only half. Flipping IsForProd to `@checked-changed` left the
    // whole suite green, and that regression would PUT a config value to
    // production because a recycled cell committed the next row's flag.
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isForProdRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox') as HTMLElement & {
        checked: boolean;
      }
    ).checked = true;
    await settle();

    expect(updated, 'no update from a non-gesture change').to.deep.equal([]);
  });

  it('persists a real click on IsForProd', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isForProdRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated.length, 'one update').to.equal(1);
    expect(updated[0].IsForProd, 'carries the new value').to.equal(true);
  });

  it('persists a real click on Secure', async () => {
    const updated: ConfigValue[] = [];
    (
      page as unknown as { updateConfigItem(v: ConfigValue): void }
    ).updateConfigItem = v => updated.push(v);

    render(page.isSecuredRenderer({ Id: 1, Key: 'first' }) as never, host);
    await settle();
    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        HTMLInputElement | undefined
    )?.click();
    await settle();

    expect(updated.length, 'one update').to.equal(1);
    expect(updated[0].Secure, 'carries the new value').to.equal(true);
  });
});
