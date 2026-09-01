import { expect, fixture, html } from '../_helpers';
import { drawerShortcuts } from '../../src/components/drawer-shortcuts';

// P0 regression tests for three live defects in the navigation drawer's
// shortcut tabs, documented as D-01, D-05 and D-14 in
// docs/drawer-tabs-review/REVIEW-drawer-tabs.md.
//
// These are deliberately narrow. The wider remediation (single identity key,
// localStorage, the navigation restructure) is planned separately in
// docs/drawer-tabs-review/HLPS-drawer-shortcuts.md; nothing here should assume
// that design, so these tests stay valid across it.

interface DrawerNavbar extends HTMLElement {
  updateComplete: Promise<unknown>;
  openEnvTabs: unknown[];
  openProjTabs: unknown[];
  openResultTabs: unknown[];
  closeEnvDetail(e: CustomEvent): void;
  closeProjectEnvs(e: CustomEvent): void;
  closeMonitorResult(e: CustomEvent): void;
  setSelectedTab(path: string): void;
}

/**
 * The drawer renders every nav item through `urlForName`, which throws
 * `Route "<name>" not found` until routes are registered. Without this the
 * navbar's shadow render aborts part-way and `#tabs` never exists, so the
 * close-handler tests would fail for the wrong reason. `skipRender` stops
 * setRoutes from also trying to render the current location into the
 * `#outlet` element, which does not exist in the test document.
 */
async function registerRoutes(): Promise<void> {
  const { router } = await import('../../src/router/router.js');
  const { routes } = await import('../../src/router/routes.js');
  // The route array's type is deeply recursive (children of children of …), and
  // checking it against setRoutes' parameter type exceeds tsc's instantiation
  // depth (TS2589). The routes themselves are already type-checked where they
  // are declared, so narrow the signature here rather than the data.
  await (
    router as unknown as {
      setRoutes(r: unknown, skipRender?: boolean): Promise<unknown>;
    }
  ).setRoutes(routes, true);
}

async function mountNavbar(container: HTMLDivElement): Promise<DrawerNavbar> {
  const el = document.createElement('dorc-navbar') as DrawerNavbar;
  container.appendChild(el);
  await el.updateComplete;
  return el;
}

describe('Drawer shortcut tabs — P0 regressions', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await registerRoutes();
    await import('../../src/components/dorc-navbar.js');
  });

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
  });

  // ─── D-01 ───────────────────────────────────────────────────────────────
  // getIndexOfPath returns -1 when no tab matches. The close handlers passed
  // that straight into `tabsArray[idx]`, so removeChild received undefined and
  // threw. The throw aborted the caller mid-handler: for environment deletion
  // that meant Router.go('/environments') never ran and the user was left on
  // the page of an environment that no longer existed.
  describe('D-01: closing a shortcut that has no drawer tab', () => {
    it('does not throw when the environment has no shortcut', async () => {
      const navbar = await mountNavbar(container);
      const event = new CustomEvent('close-env-detail', {
        detail: { Environment: { EnvironmentId: 999, EnvironmentName: 'Absent' } }
      });

      expect(() => navbar.closeEnvDetail(event)).to.not.throw();
    });

    it('does not throw when the project has no shortcut', async () => {
      const navbar = await mountNavbar(container);
      const event = new CustomEvent('close-project-envs', {
        detail: { Project: { ProjectId: 999, ProjectName: 'Absent' } }
      });

      expect(() => navbar.closeProjectEnvs(event)).to.not.throw();
    });

    it('does not throw when the monitor result has no shortcut', async () => {
      const navbar = await mountNavbar(container);
      const event = new CustomEvent('close-monitor-result', {
        detail: { request: { Id: 999 } }
      });

      expect(() => navbar.closeMonitorResult(event)).to.not.throw();
    });

    it('still removes the tab when the shortcut does exist', async () => {
      drawerShortcuts.clear();
      const navbar = await mountNavbar(container);
      const env = { EnvironmentId: 1, EnvironmentName: 'Present' };
      const tabs = navbar.shadowRoot?.getElementById('tabs');
      if (!tabs) throw new Error('navbar rendered without #tabs');

      const before = tabs.children.length;
      // Shortcuts now come from the store; the navbar renders it.
      drawerShortcuts.add('environments', env);
      await navbar.updateComplete;
      expect(tabs.children.length, 'tab was inserted').to.equal(before + 1);

      navbar.closeEnvDetail(
        new CustomEvent('close-env-detail', { detail: { Environment: env } })
      );
      await navbar.updateComplete;

      expect(tabs.children.length, 'tab was removed again').to.equal(before);
      drawerShortcuts.clear();
    });
  });

  // ─── D-14 ───────────────────────────────────────────────────────────────
  // The close handlers took no event argument, so the click bubbled on to
  // vaadin-tabs. ListMixin._onClick reads composedPath(), finds the tab and
  // sets `selected` — to an index the handler is about to remove, leaving the
  // drawer highlighting an unrelated item. _onClick bails on defaultPrevented,
  // so preventing the event is what actually fixes it.
  describe('D-14: the close click must not reach vaadin-tabs', () => {
    // P2a moved these components to light DOM (D-03) and replaced the bare
    // <vaadin-icon> close affordance with a real <vaadin-button> (D-04), so the
    // control is now a light-DOM descendant. The behaviour asserted below is
    // unchanged — only where the control lives has moved.
    const closeControlOf = (el: Element): Element => {
      const control = el.querySelector('.shortcut-close');
      if (!control) throw new Error('component rendered without a close control');
      return control;
    };

    const clickClose = async (el: Element) => {
      let bubbledOut = false;
      container.addEventListener('click', () => {
        bubbledOut = true;
      });

      let closeEventFired = false;
      container.addEventListener(
        'close-env-detail',
        () => {
          closeEventFired = true;
        },
        true
      );
      container.addEventListener(
        'close-project-envs',
        () => {
          closeEventFired = true;
        },
        true
      );
      container.addEventListener(
        'close-monitor-result',
        () => {
          closeEventFired = true;
        },
        true
      );

      const click = new MouseEvent('click', {
        bubbles: true,
        composed: true,
        cancelable: true
      });
      closeControlOf(el).dispatchEvent(click);

      return { bubbledOut, closeEventFired, click };
    };

    it('env shortcut: click is stopped and prevented, close still fires', async () => {
      await import('../../src/components/tabs/env-detail-tab.js');
      const el = await fixture(
        html`<env-detail-tab
          .env=${{ EnvironmentId: 1, EnvironmentName: 'Env' }}
        ></env-detail-tab>`
      );
      container.appendChild(el);

      const { bubbledOut, closeEventFired, click } = await clickClose(el);

      expect(closeEventFired, 'close-env-detail must still fire').to.be.true;
      expect(bubbledOut, 'click must not bubble on to vaadin-tabs').to.be.false;
      expect(click.defaultPrevented, 'click must be defaultPrevented').to.be
        .true;
    });

    it('project shortcut: click is stopped and prevented, close still fires', async () => {
      await import('../../src/components/tabs/project-envs-tab.js');
      const el = await fixture(
        html`<project-envs-tab
          .project=${{ ProjectId: 1, ProjectName: 'Proj' }}
        ></project-envs-tab>`
      );
      container.appendChild(el);

      const { bubbledOut, closeEventFired, click } = await clickClose(el);

      expect(closeEventFired, 'close-project-envs must still fire').to.be.true;
      expect(bubbledOut, 'click must not bubble on to vaadin-tabs').to.be.false;
      expect(click.defaultPrevented, 'click must be defaultPrevented').to.be
        .true;
    });

    it('monitor shortcut: click is stopped and prevented, close still fires', async () => {
      await import('../../src/components/tabs/monitor-result-tab.js');
      const el = await fixture(
        html`<monitor-result-tab
          .requestStatus=${{ Id: 7, EnvironmentName: 'Env', BuildNumber: 'b1' }}
        ></monitor-result-tab>`
      );
      container.appendChild(el);

      const { bubbledOut, closeEventFired, click } = await clickClose(el);

      expect(closeEventFired, 'close-monitor-result must still fire').to.be
        .true;
      expect(bubbledOut, 'click must not bubble on to vaadin-tabs').to.be.false;
      expect(click.defaultPrevented, 'click must be defaultPrevented').to.be
        .true;
    });
  });

  it('selects shortcuts whose names require URL encoding', async () => {
    drawerShortcuts.clear();
    const navbar = await mountNavbar(container);
    drawerShortcuts.add('environments', {
      EnvironmentId: 1,
      EnvironmentName: 'Perf 100% Load'
    });
    await navbar.updateComplete;

    navbar.setSelectedTab('/environment/Perf%20100%25%20Load/metadata');

    const tabs = navbar.shadowRoot?.getElementById('tabs') as
      (HTMLElement & { selected: number }) | null;
    expect(tabs?.selected).to.be.greaterThan(-1);
    expect(
      tabs?.children[tabs.selected]?.querySelector('env-detail-tab')
    ).to.not.equal(null);
    drawerShortcuts.clear();
  });
});

// ─── D-05 ─────────────────────────────────────────────────────────────────
// `openProjectEnvs` blanked ArtefactsSubPaths on the model it was handed, to
// work around a cookie problem it had misdiagnosed (setCookie percent-encodes,
// so ';' was never the cause). The grid passes its row object by reference, so
// this destroyed a live model: the column went blank, and for FileShare
// projects a subsequent metadata save PUT the empty value to the server.
describe('D-05: opening project environments must not mutate the caller model', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await registerRoutes();
    await import('../../src/components/dorc-app.js');
  });

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
  });

  it('leaves ArtefactsSubPaths intact on the dispatched project', async () => {
    const el = document.createElement('dorc-app') as HTMLElement & {
      updateComplete: Promise<unknown>;
    };
    container.appendChild(el);
    await el.updateComplete;

    const project = {
      ProjectId: 1,
      ProjectName: 'Payments',
      ArtefactsSubPaths: 'drop/a;drop/b'
    };

    el.dispatchEvent(
      new CustomEvent('open-project-envs', {
        detail: { Project: project },
        bubbles: false,
        composed: false
      })
    );

    expect(project.ArtefactsSubPaths).to.equal('drop/a;drop/b');
  });

  it('only prunes an environment after a confirmed not-found response', async () => {
    drawerShortcuts.clear();
    const el = document.createElement('dorc-app') as HTMLElement & {
      updateComplete: Promise<unknown>;
    };
    container.appendChild(el);
    await el.updateComplete;
    drawerShortcuts.add('environments', {
      EnvironmentId: 1,
      EnvironmentName: 'KEEP-ME'
    });
    window.history.replaceState(null, '', '/environment/KEEP-ME/metadata');

    el.dispatchEvent(
      new CustomEvent('environment-not-found', {
        detail: { confirmedNotFound: false }
      })
    );
    expect(drawerShortcuts.snapshot().environments).to.have.lengthOf(1);

    el.dispatchEvent(
      new CustomEvent('environment-not-found', {
        detail: { confirmedNotFound: true }
      })
    );
    expect(drawerShortcuts.snapshot().environments).to.be.empty;
  });

  it('synchronizes shortcut selection after router navigation', async () => {
    drawerShortcuts.clear();
    const el = document.createElement('dorc-app') as HTMLElement & {
      updateComplete: Promise<unknown>;
    };
    container.appendChild(el);
    await el.updateComplete;
    drawerShortcuts.add('environments', {
      EnvironmentId: 1,
      EnvironmentName: 'Selected Env'
    });
    await el.updateComplete;
    const navbar = el.shadowRoot?.getElementById(
      'dorcNavbar'
    ) as DrawerNavbar | null;
    await navbar?.updateComplete;
    window.history.replaceState(
      null,
      '',
      '/environment/Selected%20Env/metadata'
    );

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));

    const tabs = navbar?.shadowRoot?.getElementById('tabs') as
      (HTMLElement & { selected: number }) | null;
    expect(
      tabs?.children[tabs.selected]?.querySelector('env-detail-tab')
    ).to.not.equal(null);
  });
});
