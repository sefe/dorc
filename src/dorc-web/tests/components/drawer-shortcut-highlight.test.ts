import { expect } from '../_helpers';
import { drawerShortcuts } from '../../src/components/drawer-shortcuts';

// A drawer shortcut for an environment stopped being highlighted the moment you
// opened any of that environment's sub-tabs other than Metadata.
//
// getEnvDetailPath builds exactly one path — the Metadata one — and
// getIndexOfPath compared for equality, so /environment/Foo/projects matched
// nothing, returned -1, and `tabs.selected = -1` cleared the drawer's selection
// entirely. Because setSelectedTab runs from updated(), any re-render at all
// (the async metaData response, toggling the hamburger) re-triggered it.

interface DrawerNavbar extends HTMLElement {
  updateComplete: Promise<unknown>;
  setSelectedTab(path: string): void;
}

async function registerRoutes(): Promise<void> {
  const { router } = await import('../../src/router/router.js');
  const { routes } = await import('../../src/router/routes.js');
  await (
    router as unknown as {
      setRoutes(r: unknown, skipRender?: boolean): Promise<unknown>;
    }
  ).setRoutes(routes, true);
}

describe('Drawer shortcut highlighting across environment sub-tabs', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await registerRoutes();
    await import('../../src/components/dorc-navbar.js');
  });

  beforeEach(() => {
    drawerShortcuts.clear();
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    drawerShortcuts.clear();
    container.remove();
  });

  const mountWithShortcut = async (name: string) => {
    const navbar = document.createElement('dorc-navbar') as DrawerNavbar;
    container.appendChild(navbar);
    await navbar.updateComplete;
    drawerShortcuts.add('environments', {
      EnvironmentId: 1,
      EnvironmentName: name
    });
    await navbar.updateComplete;
    const tabs = navbar.shadowRoot?.getElementById('tabs') as HTMLElement & {
      selected: number;
    };
    if (!tabs) throw new Error('navbar rendered without #tabs');
    return { navbar, tabs };
  };

  ['metadata', 'projects', 'variables', 'deployments', 'monitor'].forEach(
    subTab => {
      it(`keeps the shortcut selected on the ${subTab} sub-tab`, async () => {
        const { navbar, tabs } = await mountWithShortcut('PROD-EMEA');

        navbar.setSelectedTab(`/environment/PROD-EMEA/${subTab}`);

        expect(
          tabs.selected,
          `selection must not be cleared on /${subTab}`
        ).to.be.greaterThan(-1);
      });
    }
  );

  it('does not match a different environment whose name shares a prefix', async () => {
    const { navbar, tabs } = await mountWithShortcut('PROD');

    // Without the trailing slash in the prefix, "PROD" would swallow "PRODUCTION".
    navbar.setSelectedTab('/environment/PRODUCTION/metadata');

    expect(tabs.selected, 'PROD must not claim PRODUCTION').to.equal(-1);
  });

  it('still matches nothing for an unrelated route', async () => {
    const { navbar, tabs } = await mountWithShortcut('PROD-EMEA');

    navbar.setSelectedTab('/environment/SOMETHING-ELSE/metadata');

    expect(tabs.selected).to.equal(-1);
  });
});
