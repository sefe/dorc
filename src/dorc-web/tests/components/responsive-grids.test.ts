import { expect, fixture, html } from '../_helpers';

// ─── mockMatchMedia helper (same pattern as grid-column-hiding.test.ts) ───
let originalMatchMedia: typeof window.matchMedia;

function saveMM() {
  originalMatchMedia = window.matchMedia;
}
function restoreMM() {
  window.matchMedia = originalMatchMedia;
}

function mockMatchMedia(matches: boolean) {
  const listeners: Array<(e: MediaQueryListEvent) => void> = [];
  const mql: MediaQueryList = {
    matches,
    media: '(max-width: 768px)',
    onchange: null,
    addEventListener: (_event: string, handler: any) => {
      listeners.push(handler);
    },
    removeEventListener: (_event: string, handler: any) => {
      const idx = listeners.indexOf(handler);
      if (idx >= 0) listeners.splice(idx, 1);
    },
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => true,
  };
  window.matchMedia = () => mql;
  return { mql, listeners };
}

// ─── Helper: extract CSS text from a LitElement class ───
function getCssText(componentClass: any): string {
  const styles = componentClass.styles;
  if (Array.isArray(styles)) {
    return styles.map((s: any) => s.cssText ?? '').join('');
  }
  return (styles as any)?.cssText ?? '';
}

function assertResponsiveCss(Cls: any, name: string) {
  const cssText = getCssText(Cls);
  expect(cssText).to.include(
    'max-width: 768px',
    `${name} should include the 768px media query`
  );
  expect(cssText).to.include(
    'word-wrap: break-word',
    `${name} should include word-wrap: break-word`
  );
  expect(cssText).to.include(
    'overflow-wrap: break-word',
    `${name} should include overflow-wrap: break-word`
  );
}

// ════════════════════════════════════════════════════════════════════
// A. Structural CSS tests — verify all 19 components include the
//    @media (max-width: 768px) cell-wrapping rule
// ════════════════════════════════════════════════════════════════════

describe('Responsive grid CSS (structural)', () => {
  // Sub-components
  it('attached-app-users CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/attached-app-users.js');
    assertResponsiveCss(mod.AttachedUsers, 'attached-app-users');
  });

  it('attached-databases CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/attached-databases.js');
    assertResponsiveCss(mod.AttachedDatabases, 'attached-databases');
  });

  it('attached-servers CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/attached-servers.js');
    // attached-servers carried an UNCONDITIONAL cell-wrapping rule on main, so
    // it must wrap on all viewports (not gated behind the 768px query) to avoid
    // a desktop regression. Verify the wrapping rules are present unconditionally.
    const cssText = getCssText(mod.AttachedServers);
    expect(cssText).to.include(
      'word-wrap: break-word',
      'attached-servers should include word-wrap: break-word'
    );
    expect(cssText).to.include(
      'overflow-wrap: break-word',
      'attached-servers should include overflow-wrap: break-word'
    );
    // Guard the actual intent: the wrapping must NOT be re-gated behind the
    // narrow-screen media query, or desktop wrapping regresses again.
    expect(cssText).to.not.include(
      'max-width: 768px',
      'attached-servers cell wrapping must be unconditional, not gated in a 768px media query'
    );
  });

  it('component-deployment-results CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/component-deployment-results.js');
    assertResponsiveCss(mod.ComponentDeploymentResults, 'component-deployment-results');
  });

  it('env-deployments CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/env-deployments.js');
    assertResponsiveCss(mod.EnvDeployments, 'env-deployments');
  });

  it('env-variables CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/environment-tabs/env-variables.js');
    assertResponsiveCss(mod.EnvVariables, 'env-variables');
  });

  // Page-level components
  it('page-config-values-list CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-config-values-list.js');
    assertResponsiveCss(mod.PageConfigValuesList, 'page-config-values-list');
  });

  it('page-daemons-list CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-daemons-list.js');
    assertResponsiveCss(mod.PageDaemonsList, 'page-daemons-list');
  });

  it('page-permissions-list CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-permissions-list.js');
    assertResponsiveCss(mod.PagePermissionsList, 'page-permissions-list');
  });

  it('page-project-bundles CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-project-bundles.js');
    assertResponsiveCss(mod.PageProjectBundles, 'page-project-bundles');
  });

  it('page-scripts-list CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-scripts-list.js');
    assertResponsiveCss(mod.PageScriptsList, 'page-scripts-list');
  });

  it('page-variables-audit CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-variables-audit.js');
    assertResponsiveCss(mod.PageVariablesAudit, 'page-variables-audit');
  });

  it('page-variables-value-lookup CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-variables-value-lookup.js');
    assertResponsiveCss(mod.PageVariablesValueLookup, 'page-variables-value-lookup');
  });

  it('page-daemons-audit CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-daemons-audit.js');
    assertResponsiveCss(mod.PageDaemonsAudit, 'page-daemons-audit');
  });

  it('page-projects-audit CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-projects-audit.js');
    assertResponsiveCss(mod.PageProjectsAudit, 'page-projects-audit');
  });

  it('page-scripts-audit CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-scripts-audit.js');
    assertResponsiveCss(mod.PageScriptsAudit, 'page-scripts-audit');
  });

  it('page-project-components CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/pages/page-project-components.js');
    assertResponsiveCss(mod.PageProjectComponents, 'page-project-components');
  });

  it('env-monitor CSS contains responsive cell wrapping', async () => {
    const mod = await import('../../src/components/environment-tabs/env-monitor.js');
    assertResponsiveCss(mod.EnvMonitor, 'env-monitor');
  });
});

// ════════════════════════════════════════════════════════════════════
// B. Rendering tests — verify column hiding for the 6 simpler
//    sub-components that accept data via properties
// ════════════════════════════════════════════════════════════════════

describe('Responsive grid column hiding (rendered)', () => {
  beforeEach(() => saveMM());
  afterEach(() => restoreMM());

  // ── attached-app-users ──
  describe('attached-app-users', () => {
    const mockUsers = [
      {
        DisplayName: 'User1',
        LoginId: 'u1',
        LoginType: 'AD',
        LanId: 'lan1',
        LanIdType: 'Domain',
        Team: 'TeamA',
      },
    ];

    it('hides secondary columns on narrow screens', async () => {
      mockMatchMedia(true);
      const el = await fixture(html`
        <attached-app-users .users="${mockUsers}"></attached-app-users>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;

      // Always visible
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="DisplayName"]') as HTMLElement).hidden
      ).to.equal(false, 'DisplayName should be visible');
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="LoginId"]') as HTMLElement).hidden
      ).to.equal(false, 'LoginId should be visible');

      // Hidden on narrow
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="LoginType"]') as HTMLElement).hidden
      ).to.equal(true, 'LoginType should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="LanIdType"]') as HTMLElement).hidden
      ).to.equal(true, 'LanIdType should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="Team"]') as HTMLElement).hidden
      ).to.equal(true, 'Team should be hidden');
    });

    it('shows all columns on wide screens', async () => {
      mockMatchMedia(false);
      const el = await fixture(html`
        <attached-app-users .users="${mockUsers}"></attached-app-users>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;
      const cols = sr.querySelectorAll('vaadin-grid-sort-column');
      for (const col of Array.from(cols)) {
        expect((col as HTMLElement).hidden).to.equal(
          false,
          `${col.getAttribute('path')} should be visible on wide`
        );
      }
    });
  });

  // ── attached-databases ──
  describe('attached-databases', () => {
    const mockDatabases = [
      {
        ServerName: 'srv1',
        Name: 'db1',
        Type: 'Tag1',
        ArrayName: 'arr1',
        Id: 1,
      },
    ];

    it('hides secondary columns on narrow screens', async () => {
      mockMatchMedia(true);
      const el = await fixture(html`
        <attached-databases .databases="${mockDatabases}"></attached-databases>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;

      // Always visible
      expect(
        (sr.querySelector('vaadin-grid-column[path="ServerName"]') as HTMLElement).hidden
      ).to.equal(false, 'ServerName should be visible');
      expect(
        (sr.querySelector('vaadin-grid-column[path="Name"]') as HTMLElement).hidden
      ).to.equal(false, 'Name should be visible');

      // Hidden on narrow — Application Tag has no path, select by header
      expect(
        (sr.querySelector('vaadin-grid-column[header="Application Tag"]') as HTMLElement).hidden
      ).to.equal(true, 'Application Tag should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-column[path="ArrayName"]') as HTMLElement).hidden
      ).to.equal(true, 'ArrayName should be hidden');
    });
  });

  // ── attached-servers ──
  describe('attached-servers', () => {
    const mockServers = [
      { Name: 'srv1', OsName: 'Windows', ApplicationTags: 'tag1;tag2', Id: 1 },
    ];

    it('hides secondary columns on narrow screens', async () => {
      mockMatchMedia(true);
      const el = await fixture(html`
        <attached-servers .servers="${mockServers}"></attached-servers>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;

      // Always visible
      expect(
        (sr.querySelector('vaadin-grid-column[path="Name"]') as HTMLElement).hidden
      ).to.equal(false, 'Name should be visible');

      // Hidden on narrow
      expect(
        (sr.querySelector('vaadin-grid-column[path="OsName"]') as HTMLElement).hidden
      ).to.equal(true, 'OsName should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-column[header="Application Tags"]') as HTMLElement).hidden
      ).to.equal(true, 'Application Tags should be hidden');
    });
  });

  // ── env-deployments ──
  describe('env-deployments', () => {
    const mockBuilds = [
      {
        ComponentName: 'comp1',
        RequestDetails: 'req1',
        UpdateDate: '2024-01-01',
        State: 'Complete',
      },
    ];

    it('hides secondary columns on narrow screens', async () => {
      mockMatchMedia(true);
      const el = await fixture(html`
        <env-deployments .builds="${mockBuilds}"></env-deployments>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;

      // Always visible
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="ComponentName"]') as HTMLElement).hidden
      ).to.equal(false, 'ComponentName should be visible');
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="State"]') as HTMLElement).hidden
      ).to.equal(false, 'State should be visible');

      // Hidden on narrow
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="RequestDetails"]') as HTMLElement).hidden
      ).to.equal(true, 'RequestDetails should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-sort-column[path="UpdateDate"]') as HTMLElement).hidden
      ).to.equal(true, 'UpdateDate should be hidden');
    });
  });

  // ── component-deployment-results ──
  describe('component-deployment-results', () => {
    const mockResults = [
      {
        ComponentName: 'comp1',
        Status: 'Complete',
        Log: 'some log',
        StartedTime: '2024-01-01T10:00:00',
        CompletedTime: '2024-01-01T10:05:00',
        Id: 1,
      },
    ];

    it('hides secondary columns on narrow screens', async () => {
      mockMatchMedia(true);
      const el = await fixture(html`
        <component-deployment-results
          .resultItems="${mockResults}"
        ></component-deployment-results>
      `);
      await el.updateComplete;

      const sr = el.shadowRoot!;

      // Always visible
      expect(
        (sr.querySelector('vaadin-grid-column[header="Component Name"]') as HTMLElement).hidden
      ).to.equal(false, 'Component Name should be visible');
      expect(
        (sr.querySelector('vaadin-grid-column[header="Status"]') as HTMLElement).hidden
      ).to.equal(false, 'Status should be visible');

      // Hidden on narrow
      expect(
        (sr.querySelector('vaadin-grid-column[header="Timings"]') as HTMLElement).hidden
      ).to.equal(true, 'Timings should be hidden');
      expect(
        (sr.querySelector('vaadin-grid-column[header="Log"]') as HTMLElement).hidden
      ).to.equal(true, 'Log should be hidden');
    });
  });

});
