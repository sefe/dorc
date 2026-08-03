import { expect, fixture, html } from '../_helpers';

// ─── mockMatchMedia helper (same pattern as grid-column-hiding.test.ts) ───
let originalMatchMedia: typeof window.matchMedia;

function saveMM() {
  originalMatchMedia = window.matchMedia;
}
function restoreMM() {
  window.matchMedia = originalMatchMedia;
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
  // The four property-driven components below are MIGRATED to the §3.4
  // list-row idiom (post-pilot batch): narrow mode is CONTAINER-driven and
  // collapses to a single list column. Coverage re-established against the
  // new mechanism per the test-migration obligation (HLPS §3.1).
  const migratedListViews: Array<{
    tag: string;
    prop: string;
    items: unknown[];
    primaryText: string;
  }> = [
    {
      tag: 'attached-app-users',
      prop: 'users',
      items: [
        {
          DisplayName: 'User1',
          LoginId: 'u1',
          LoginType: 'AD',
          LanId: 'lan1',
          LanIdType: 'Domain',
          Team: 'TeamA',
        },
      ],
      primaryText: 'User1',
    },
    {
      tag: 'attached-databases',
      prop: 'databases',
      items: [{ ServerName: 'srv1', Name: 'db1', Type: 'Tag1', ArrayName: 'arr1', Id: 1 }],
      primaryText: 'db1',
    },
    {
      tag: 'attached-servers',
      prop: 'servers',
      items: [{ Name: 'srv1', OsName: 'Windows', ApplicationTags: 'tag1;tag2', Id: 1 }],
      primaryText: 'srv1',
    },
  ];

  const settleFrames = () =>
    new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

  for (const view of migratedListViews) {
    describe(`${view.tag} (migrated: list-row idiom)`, () => {
      it('collapses to the list column when its container is narrow', async () => {
        const wrapper = (await fixture(html`<div style="width: 375px"></div>`)) as HTMLElement;
        const el = document.createElement(view.tag) as HTMLElement & {
          updateComplete: Promise<unknown>;
        } & Record<string, unknown>;
        el[view.prop] = view.items;
        wrapper.appendChild(el);
        await el.updateComplete;
        await settleFrames();
        await settleFrames();

        const sr = el.shadowRoot!;
        const wideColumns = Array.from(
          sr.querySelectorAll(
            'vaadin-grid-column[header], vaadin-grid-column[path], vaadin-grid-sort-column'
          )
        ) as HTMLElement[];
        wideColumns.forEach(col => {
          expect(col.hidden).to.equal(
            true,
            `${col.getAttribute('header') ?? col.getAttribute('path')} hidden at narrow width`
          );
        });
        const listColumn = sr.querySelector('vaadin-grid-column[flex-grow="1"]') as HTMLElement;
        expect(listColumn, 'list column present').to.exist;
        expect(listColumn.hidden).to.equal(false, 'list column visible');
        const primary = sr.querySelector('.dorc-list-row__primary');
        expect(primary, 'list row rendered').to.exist;
        expect(primary!.textContent).to.contain(view.primaryText);
      });
    });
  }


  // ── component-deployment-results ──
  // MIGRATED to the §3.4 list-row idiom (IS S-005): narrow mode is
  // CONTAINER-driven via NarrowListController, not matchMedia, and collapses
  // to a single list column instead of hiding a subset. Coverage
  // re-established against the new mechanism per the test-migration
  // obligation (HLPS §3.1).
  describe('component-deployment-results (migrated: list-row idiom)', () => {
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

    const settle = () =>
      new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    it('collapses to the single list column when its CONTAINER is narrow', async () => {
      const wrapper = (await fixture(html`
        <div style="width: 375px">
          <component-deployment-results
            .resultItems="${mockResults}"
          ></component-deployment-results>
        </div>
      `)) as HTMLElement;
      const el = wrapper.querySelector(
        'component-deployment-results'
      ) as HTMLElement & { updateComplete: Promise<unknown> };
      await el.updateComplete;
      await settle();
      await settle();

      const sr = el.shadowRoot!;
      const wideColumns = Array.from(
        sr.querySelectorAll('vaadin-grid-column[header], vaadin-grid-column[path]')
      ) as HTMLElement[];
      // every wide column hidden; the (header-less, path-less) list column visible
      wideColumns.forEach(col => {
        expect(col.hidden).to.equal(
          true,
          `${col.getAttribute('header') ?? col.getAttribute('path')} should be hidden at narrow width`
        );
      });
      const listColumn = sr.querySelector(
        'vaadin-grid-column[flex-grow="1"]'
      ) as HTMLElement;
      expect(listColumn.hidden).to.equal(false, 'list column visible');

      // list row renders primary + chip from the row template
      const rowBody = sr.querySelector('.dorc-list-row__primary');
      expect(rowBody, 'list row primary line rendered').to.exist;
      expect(rowBody!.textContent).to.contain('comp1');
      const chip = sr.querySelector('.dorc-list-row__chip');
      expect(chip, 'status chip rendered').to.exist;
      expect(chip!.textContent!.trim()).to.equal('Complete');
    });

    it('restores the wide columns when the container widens (SC10 crossing)', async () => {
      const wrapper = (await fixture(html`
        <div style="width: 375px">
          <component-deployment-results
            .resultItems="${mockResults}"
          ></component-deployment-results>
        </div>
      `)) as HTMLElement;
      const el = wrapper.querySelector(
        'component-deployment-results'
      ) as HTMLElement & { updateComplete: Promise<unknown> };
      await el.updateComplete;
      await settle();
      await settle();

      wrapper.style.width = '1100px';
      await settle();
      await settle();
      await el.updateComplete;

      const sr = el.shadowRoot!;
      expect(
        (sr.querySelector('vaadin-grid-column[header="Component Name"]') as HTMLElement)
          .hidden
      ).to.equal(false, 'Component Name restored at wide width');
      expect(
        (
          sr.querySelector('vaadin-grid-column[flex-grow="1"]') as HTMLElement
        ).hidden
      ).to.equal(true, 'list column hidden at wide width');
    });
  });

});
