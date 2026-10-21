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

  it('keeps a project shortcut selected across environments, definitions, components and bundles routes', async () => {
    const navbar = document.createElement('dorc-navbar') as DrawerNavbar;
    container.appendChild(navbar);
    await navbar.updateComplete;
    drawerShortcuts.add('projects', {
      ProjectId: 42,
      ProjectName: 'Platform Core'
    });
    await navbar.updateComplete;
    const tabs = navbar.shadowRoot?.getElementById('tabs') as HTMLElement & {
      selected: number;
    };

    for (const route of [
      '/project-envs/Platform%20Core',
      '/project-envs/Platform%20Core/bundles',
      '/project-ref-data/42',
      '/project-ref-data/42?componentId=99&tab=history',
      '/project-components/42'
    ]) {
      navbar.setSelectedTab(route);
      expect(
        tabs.selected,
        `project shortcut must stay selected on ${route}`
      ).to.be.greaterThan(-1);
    }

    navbar.setSelectedTab('/project-ref-data/999');
    expect(tabs.selected).to.equal(-1);
  });

  it('enriches a project shortcut in place when ProjectId is resolved later', async () => {
    const navbar = document.createElement('dorc-navbar') as DrawerNavbar;
    container.appendChild(navbar);
    await navbar.updateComplete;
    drawerShortcuts.add('projects', {
      ProjectName: 'Alpha'
    });
    drawerShortcuts.enrichProject({
      ProjectId: 77,
      ProjectName: 'Alpha'
    });
    await navbar.updateComplete;
    const tabs = navbar.shadowRoot?.getElementById('tabs') as HTMLElement & {
      selected: number;
    };

    expect(drawerShortcuts.snapshot().projects).to.deep.equal([
      { ProjectId: 77, ProjectName: 'Alpha' }
    ]);

    navbar.setSelectedTab('/project-ref-data/77');
    expect(tabs.selected).to.be.greaterThan(-1);
  });

  it('orders delivery and authoring tabs before infrastructure and governance sections', async () => {
    const navbar = document.createElement('dorc-navbar') as DrawerNavbar;
    container.appendChild(navbar);
    await navbar.updateComplete;
    const tabs = navbar.shadowRoot?.getElementById('tabs');
    const hrefs = Array.from(tabs!.querySelectorAll('vaadin-tab > a')).map(a =>
      a.getAttribute('href')
    );

    expect(hrefs.slice(0, 12)).to.deep.equal([
      '/deploy',
      '/monitor-requests',
      '/projects',
      '/deployment-definitions',
      '/module-catalog',
      '/scripts',
      '/variables',
      '/environments',
      '/servers',
      '/databases',
      '/sql-roles',
      '/sql-ports'
    ]);

    const catalogItem = tabs!.querySelector(
      'a[href="/module-catalog"] .sub-item'
    );
    const scriptsItem = tabs!.querySelector('a[href="/scripts"] .sub-item');
    expect(catalogItem, 'Module Catalog should be indented under Definitions')
      .to.exist;
    expect(scriptsItem, 'Scripts should be indented under Definitions').to
      .exist;
  });

  it('auto-expands the Audit menu when navigating directly to an audit route', async () => {
    const navbar = document.createElement('dorc-navbar') as DrawerNavbar;
    container.appendChild(navbar);
    await navbar.updateComplete;
    const tabs = navbar.shadowRoot?.getElementById('tabs') as HTMLElement & {
      selected: number;
    };

    navbar.setSelectedTab('/projects/audit');
    await navbar.updateComplete;

    const auditTab = Array.from(tabs.querySelectorAll('vaadin-tab')).find(t =>
      t.textContent?.trim().startsWith('Audit')
    );
    expect(auditTab?.getAttribute('aria-expanded')).to.equal('true');
    expect(tabs.selected).to.be.greaterThan(-1);
  });
});
