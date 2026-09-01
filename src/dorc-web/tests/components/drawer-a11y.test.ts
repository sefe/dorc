import { drawerShortcuts } from '../../src/components/drawer-shortcuts';
import {
  contrastRatio,
  expect,
  fixture,
  html,
  setTheme,
  type DorcTheme
} from '../_helpers';

// P2a — closes the live exclusion of keyboard and assistive-technology users from
// the drawer's shortcut feature, plus the bypass mechanism that exclusion made
// worse. Criteria referenced are from the remediation plan (see issue #890).

interface DrawerNavbar extends HTMLElement {
  updateComplete: Promise<unknown>;
  closeEnvDetail(e: CustomEvent): void;
}

/**
 * Shortcuts are added through the store now, not by imperative insertion — the
 * navbar renders whatever the store holds.
 */
async function addEnv(
  navbar: DrawerNavbar,
  env: { EnvironmentId?: number; EnvironmentName: string }
) {
  drawerShortcuts.add('environments', env);
  await settle(navbar);
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

async function mountNavbar(container: HTMLElement): Promise<DrawerNavbar> {
  const el = document.createElement('dorc-navbar') as DrawerNavbar;
  container.appendChild(el);
  await el.updateComplete;
  return el;
}

/**
 * Lit renders each shortcut component asynchronously, so the anchor and close
 * control do not exist in the tab until the component's own first update lands.
 * Await them before asserting — this is test timing, not app behaviour.
 */
async function settle(navbar: DrawerNavbar): Promise<void> {
  await navbar.updateComplete;
  const parts = Array.from(
    tabsOf(navbar).querySelectorAll('env-detail-tab, project-envs-tab, monitor-result-tab')
  ) as Array<Element & { updateComplete?: Promise<unknown> }>;
  await Promise.all(parts.map(p => p.updateComplete));
}

const tabsOf = (navbar: DrawerNavbar) => {
  const tabs = navbar.shadowRoot?.getElementById('tabs');
  if (!tabs) throw new Error('navbar rendered without #tabs');
  return tabs;
};

describe('P2a: drawer shortcut accessibility', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await registerRoutes();
    await import('../../src/components/dorc-navbar.js');
    await import('../../src/components/tabs/env-detail-tab.js');
    await import('../../src/components/tabs/monitor-result-tab.js');
  });

  beforeEach(() => {
    drawerShortcuts.clear();
    container = document.createElement('div');
    // dorc-app paints --dorc-bg-primary on its host; mirror that here so the
    // contrast helper measures against the real backdrop rather than throwing.
    container.style.background = 'var(--dorc-bg-primary)';
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
    drawerShortcuts.clear();
  });

  // ─── D-03 ───────────────────────────────────────────────────────────────
  // vaadin-tab activates a shortcut with `this.querySelector('a')` — a DESCENDANT
  // query. While the anchor lived in the component's shadow root it was invisible
  // to that lookup, so Enter selected the tab and never navigated. This asserts
  // the exact mechanism the vendor uses, not a proxy for it.
  describe('SC-4: keyboard activation', () => {
    it('exposes the anchor to vaadin-tab’s descendant lookup', async () => {
      const navbar = await mountNavbar(container);
      await addEnv(navbar, { EnvironmentId: 1, EnvironmentName: 'PROD-EMEA' });

      const tab = tabsOf(navbar).querySelector('vaadin-tab:has(env-detail-tab)');
      expect(tab, 'shortcut tab exists').to.exist;

      const anchor = tab!.querySelector('a');
      expect(anchor, 'vaadin-tab.querySelector("a") must find the shortcut link')
        .to.exist;
      expect(anchor!.getAttribute('href')).to.contain('PROD-EMEA');
    });

    it('gives the link the full, untruncated name as its accessible name', async () => {
      const navbar = await mountNavbar(container);
      const name = 'ENDUR MARKET RISK PRODUCTION EMEA';
      await addEnv(navbar, { EnvironmentId: 2, EnvironmentName: name });

      const anchor = tabsOf(navbar).querySelector(
        'env-detail-tab a'
      ) as HTMLElement;
      expect(anchor.textContent?.trim()).to.equal(name);
      expect(anchor.getAttribute('title'), 'title carries the full name').to.equal(
        name
      );
    });
  });

  // ─── D-04 ───────────────────────────────────────────────────────────────
  describe('SC-4: the close control is a real, named button', () => {
    it('is a focusable button naming which shortcut it closes', async () => {
      const navbar = await mountNavbar(container);
      await addEnv(navbar, { EnvironmentId: 3, EnvironmentName: 'UAT-01' });

      const close = tabsOf(navbar).querySelector(
        'env-detail-tab .shortcut-close'
      ) as HTMLElement;

      expect(close.localName, 'must be a button, not a bare icon').to.equal(
        'vaadin-button'
      );
      expect(close.getAttribute('aria-label')).to.equal(
        'Close UAT-01 shortcut'
      );

      close.focus();
      expect(
        navbar.shadowRoot?.activeElement,
        'close control must be focusable'
      ).to.equal(close);
    });

    it('gives each close control a distinct accessible name', async () => {
      const navbar = await mountNavbar(container);
      await addEnv(navbar, { EnvironmentId: 4, EnvironmentName: 'ALPHA' });
      await addEnv(navbar, { EnvironmentId: 5, EnvironmentName: 'BETA' });

      const labels = Array.from(
        tabsOf(navbar).querySelectorAll('env-detail-tab .shortcut-close')
      ).map(c => c.getAttribute('aria-label'));

      expect(new Set(labels).size, 'names must be unique in the drawer').to.equal(
        labels.length
      );
    });
  });

  // ─── SC-4a ──────────────────────────────────────────────────────────────
  describe('SC-4a: focus survives closing a shortcut', () => {
    it('moves focus to a sibling rather than dropping it on <body>', async () => {
      const navbar = await mountNavbar(container);
      const env = { EnvironmentId: 6, EnvironmentName: 'GAMMA' };
      await addEnv(navbar, env);

      const tab = tabsOf(navbar).querySelector(
        'vaadin-tab:has(env-detail-tab)'
      ) as HTMLElement;
      const close = tab.querySelector('.shortcut-close') as HTMLElement;
      close.focus();

      navbar.closeEnvDetail(
        new CustomEvent('close-env-detail', { detail: { Environment: env } })
      );
      await settle(navbar);

      const active = navbar.shadowRoot?.activeElement;
      expect(active, 'focus must land somewhere in the drawer').to.exist;
      expect(active).to.not.equal(document.body);
      expect(
        tab.isConnected,
        'the closed tab is gone, so focus cannot be on it'
      ).to.be.false;
    });
  });

  // ─── SC-11 ──────────────────────────────────────────────────────────────
  describe('SC-11: long names truncate instead of overflowing', () => {
    it('applies ellipsis to the shortcut label', async () => {
      const navbar = await mountNavbar(container);
      await addEnv(navbar, {
        EnvironmentId: 7,
        EnvironmentName: 'ENDUR MARKET RISK PRODUCTION EMEA'
      });

      const label = tabsOf(navbar).querySelector(
        'env-detail-tab .shortcut-label'
      ) as HTMLElement;
      const cs = getComputedStyle(label);

      expect(cs.textOverflow).to.equal('ellipsis');
      expect(cs.whiteSpace).to.equal('nowrap');
      expect(cs.overflow).to.equal('hidden');
      // Without min-width:0 a flex item refuses to shrink and ellipsis never engages.
      expect(cs.minWidth).to.equal('0px');
    });

    it('carries the build number in a title on monitor shortcuts', async () => {
      const el = await fixture(
        html`<monitor-result-tab
          .requestStatus=${{
            Id: 1234,
            EnvironmentName: 'PROD',
            BuildNumber: 'MyProduct_Main_20260811.3.1234567'
          }}
        ></monitor-result-tab>`
      );
      const anchor = el.querySelector('a') as HTMLElement;
      expect(anchor.getAttribute('title')).to.contain(
        'MyProduct_Main_20260811.3.1234567'
      );
    });
  });

  // ─── D-08 / SC-12 ───────────────────────────────────────────────────────
  // The close affordance was `color: lightblue` — 1.53:1 on the default light
  // background, effectively invisible. WCAG 1.4.11 requires 3:1 for UI components.
  describe('SC-12: close control contrast in both themes', () => {
    (['light', 'dark'] as DorcTheme[]).forEach(theme => {
      it(`meets 1.4.11's 3:1 in ${theme} theme`, async () => {
        setTheme(theme);
        const navbar = await mountNavbar(container);
        await addEnv(navbar, { EnvironmentId: 8, EnvironmentName: 'CONTRAST' });

        const close = tabsOf(navbar).querySelector(
          'env-detail-tab .shortcut-close'
        ) as HTMLElement;
        const ratio = contrastRatio(close, getComputedStyle(close).color);

        expect(ratio, `${theme} theme contrast`).to.be.at.least(3);
      });
    });
  });

  // ─── D-25a ──────────────────────────────────────────────────────────────
  describe('D-25a: the Audit disclosure exposes its state', () => {
    it('reflects expanded/collapsed via aria-expanded', async () => {
      const navbar = await mountNavbar(container);
      const auditTab = Array.from(
        tabsOf(navbar).querySelectorAll('vaadin-tab')
      ).find(t => t.textContent?.includes('Audit')) as HTMLElement;

      expect(auditTab, 'Audit tab exists').to.exist;
      expect(auditTab.getAttribute('aria-expanded')).to.equal('false');

      (auditTab.querySelector('a') as HTMLElement).click();
      await navbar.updateComplete;

      expect(auditTab.getAttribute('aria-expanded')).to.equal('true');
    });
  });
});

// ─── SC-28 ────────────────────────────────────────────────────────────────
// WCAG 2.4.1 Bypass Blocks (Level A). P2a gives every shortcut a focusable close
// button, roughly doubling the drawer's tab stops — so the bypass mechanism ships
// in the same phase that creates the burden, not in the gated one.
describe('SC-28: bypass blocks', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await registerRoutes();
    await import('../../src/components/dorc-app.js');
  });

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  it('offers a skip link as the first focusable element, targeting a main landmark', async () => {
    const app = document.createElement('dorc-app') as HTMLElement & {
      updateComplete: Promise<unknown>;
    };
    container.appendChild(app);
    await app.updateComplete;

    const root = app.shadowRoot!;
    const skip = root.querySelector('.skip-link') as HTMLAnchorElement;
    expect(skip, 'skip link exists').to.exist;
    expect(skip.textContent?.trim()).to.match(/skip to main content/i);

    // It must come before the drawer in the tab order, or it bypasses nothing.
    const focusables = Array.from(
      root.querySelectorAll<HTMLElement>('a[href], button, vaadin-button, [tabindex]')
    ).filter(el => !el.hasAttribute('disabled') && el.tabIndex !== -1);
    expect(focusables[0], 'skip link must be first in the tab order').to.equal(
      skip
    );

    const content = root.getElementById('page-content')!;
    expect(content.getAttribute('role'), 'skip target is a main landmark').to.equal(
      'main'
    );

    skip.click();
    expect(root.activeElement, 'skip link moves focus to content').to.equal(
      content
    );
  });
});
