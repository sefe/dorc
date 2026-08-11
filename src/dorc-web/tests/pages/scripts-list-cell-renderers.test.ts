import { expect, settle } from '../_helpers';
import { render } from 'lit';
import { vi } from 'vitest';
import { of } from 'rxjs';

// The page loads the PowerShell version list in its constructor, and the error
// path assigns its own fallback (`['v5.1', 'v7']`). This fixture used to race
// that: it assigned `powerShellVersions` after a fixed settle and hoped the
// AjaxError had already landed. When it landed later — a slower CI round trip
// is enough — the combo's items no longer contained '5.1'/'7.4', the combo
// cleared itself, and the value assertion failed. That was a real intermittent
// failure, reproducible by delaying the error handler.
//
// Mocking the API removes the race rather than documenting it.
vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    PowerShellVersionsApi: class {
      powerShellVersionsGet = () =>
        of([{ Version: '5.1' }, { Version: '7.4' }]);
    }
  };
});

await import('../../src/pages/page-scripts-list');

// These exercise the real renderer methods rather than a lookalike template,
// because the defect they guard is in the binding those methods choose.
//
// A grid cell is recycled by re-rendering the next row's value into it. That
// commit fires Vaadin's `checked-changed` / `value-changed` notify events — but
// not `change`, which is gesture-only. The handlers here persist to the API, so
// listening for the notify event meant scrolling the grid silently PUT changes
// to whichever script previously occupied the cell.
//
// Calling the renderer directly keeps this independent of the page's data
// provider while still covering the code that ships.

type Script = {
  Id?: number;
  IsEnabled?: boolean;
  PowerShellVersionNumber?: string;
};

type Page = HTMLElement & {
  userRoles: string[];
  powerShellVersions: string[];
  enabledRenderer(script: Script): unknown;
  psVersionRenderer(script: Script): unknown;
  _jsonRenderer(script: Script & { Path?: string; IsPathJSON?: boolean }): unknown;
};

// Deliberately not attached: connectedCallback would fire the page's data
// provider and roles lookup at the API. The renderers only need these two
// properties, and they are what gates editability.
const mountPage = async () => {
  const el = document.createElement('page-scripts-list') as Page;
  el.userRoles = ['Admin'];
  await settle();
  // The mocked API supplies these synchronously, so nothing can overwrite them
  // later. Assigned anyway so the fixture states what the combo needs to hold.
  el.powerShellVersions = ['5.1', '7.4'];
  await settle();
  return el;
};

describe('page-scripts-list editable cell renderers', () => {
  let page: Page;
  let host: HTMLElement;

  beforeEach(async () => {
    page = await mountPage();
    host = document.createElement('div');
    document.body.appendChild(host);
  });

  afterEach(() => {
    host.remove();
  });

  it('does not persist when the enabled checkbox is set programmatically', async () => {
    const script: Script = { Id: 1, IsEnabled: false };
    render(page.enabledRenderer(script) as never, host);
    await settle();

    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };
    checkbox.checked = true;
    await settle();

    expect(script.IsEnabled, 'model untouched by a non-gesture change').to.equal(
      false
    );
  });

  it('records a real click on the enabled checkbox', async () => {
    const script: Script = { Id: 1, IsEnabled: false };
    render(page.enabledRenderer(script) as never, host);
    await settle();

    (
      host.querySelector('vaadin-checkbox')?.querySelector('input') as
        | HTMLInputElement
        | undefined
    )?.click();
    await settle();

    expect(script.IsEnabled, 'a real click still registers').to.equal(true);
  });

  it('does not persist when the version combo is set programmatically', async () => {
    const script: Script = { Id: 1, PowerShellVersionNumber: '5.1' };
    render(page.psVersionRenderer(script) as never, host);
    await settle();

    const combo = host.querySelector('vaadin-combo-box') as HTMLElement & {
      value: string;
    };
    combo.value = '7.4';
    await settle();

    expect(
      script.PowerShellVersionNumber,
      'model untouched by a non-gesture change'
    ).to.equal('5.1');
  });

  // grid-cell-recycling.test.ts defines its own copy of the showJson helper, so
  // it demonstrates the mechanism without touching the shipped renderer.
  // Reverting `.data` binding here to the seed-once behaviour left that test —
  // and the whole suite — green.
  it('re-seeds the JSON viewer when the cell is recycled', async () => {
    const first = { Id: 1, IsPathJSON: true, Path: '{"a":1}' };
    const second = { Id: 2, IsPathJSON: true, Path: '{"b":2}' };

    render(page._jsonRenderer(first) as never, host);
    await settle();
    const viewer = host.querySelector('hegs-json-viewer') as HTMLElement & {
      data?: unknown;
    };
    expect(viewer.data).to.deep.equal({ a: 1 });

    render(page._jsonRenderer(second) as never, host);
    await settle();

    expect(
      host.querySelector('hegs-json-viewer'),
      'element reused'
    ).to.equal(viewer);
    expect(viewer.data, 'shows the new row').to.deep.equal({ b: 2 });
  });

  it('re-applies a checkbox value the user diverged from the model', async () => {
    // The `live()` half. Without it Lit dirty-checks against its last committed
    // value: the user's click never went through Lit, so committing the same
    // model value again is skipped and the next row inherits the tick.
    const script: Script = { Id: 1, IsEnabled: false };
    render(page.enabledRenderer(script) as never, host);
    await settle();

    const checkbox = host.querySelector('vaadin-checkbox') as HTMLElement & {
      checked: boolean;
    };
    checkbox.checked = true;
    await settle();

    // Cell recycled onto a row whose model value is still false.
    render(page.enabledRenderer({ Id: 2, IsEnabled: false }) as never, host);
    await settle();

    expect(checkbox.checked, 'shows the new row, not the old tick').to.equal(
      false
    );
  });

  it('re-applies a combo value the user diverged from the model', async () => {
    // Same `live()` mechanism on a `.value` binding. Without it the version the
    // user picked stays visible on whichever script is recycled into the cell.
    const script: Script = { Id: 1, PowerShellVersionNumber: '5.1' };
    render(page.psVersionRenderer(script) as never, host);
    await settle();

    const combo = host.querySelector('vaadin-combo-box') as HTMLElement & {
      value: string;
    };
    combo.value = '7.4';
    await settle();

    render(
      page.psVersionRenderer({ Id: 2, PowerShellVersionNumber: '5.1' }) as never,
      host
    );
    await settle();

    expect(host.querySelector('vaadin-combo-box'), 'element reused').to.equal(
      combo
    );
    expect(combo.value, 'shows the new row, not the old pick').to.equal('5.1');
  });
});
