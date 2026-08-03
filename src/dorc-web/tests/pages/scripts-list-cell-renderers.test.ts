import { expect, settle } from '../_helpers';
import { render } from 'lit';
import '../../src/pages/page-scripts-list';

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
};

// Deliberately not attached: connectedCallback would fire the page's data
// provider and roles lookup at the API. The renderers only need these two
// properties, and they are what gates editability.
const mountPage = async () => {
  const el = document.createElement('page-scripts-list') as Page;
  el.userRoles = ['Admin'];
  await settle();
  // After settle, not before: the constructor kicks off loadPowerShellVersions,
  // whose error path assigns its own fallback list and would overwrite these.
  // With the wrong list the combo cannot hold '7.4', clears itself, and the
  // handler's `if (!value) return` guard exits before the binding matters —
  // which made the combo test below pass against a reverted fix.
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
});
