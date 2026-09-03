import { contrastRatio, expect, setTheme, type DorcTheme } from '../_helpers';

// P3 — the surfaces around the shortcut feature: the app shell's splitter and
// collapsed sidebar, the contrast token, and the reduced-motion position.

interface ReadyApp extends HTMLElement {
  updateComplete: Promise<unknown>;
}

// dorc-app branches on a (max-width: 768px) media query, and the test runner's
// viewport can match it — which would put every assertion below on the mobile
// modal path instead of the desktop one they are about. Force desktop.
let originalMatchMedia: typeof window.matchMedia;

function forceDesktop() {
  originalMatchMedia = window.matchMedia;
  window.matchMedia = () =>
    ({
      matches: false,
      media: '(max-width: 768px)',
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => true
    }) as MediaQueryList;
}

async function mountApp(container: HTMLElement): Promise<ReadyApp> {
  const el = document.createElement('dorc-app') as ReadyApp;
  container.appendChild(el);
  await el.updateComplete;
  return el;
}

const rootOf = (app: ReadyApp) => app.shadowRoot!;

describe('P3: app shell accessibility', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    const { router } = await import('../../src/router/router.js');
    const { routes } = await import('../../src/router/routes.js');
    await (
      router as unknown as {
        setRoutes(r: unknown, skipRender?: boolean): Promise<unknown>;
      }
    ).setRoutes(routes, true);
    await import('../../src/components/dorc-app.js');
  });

  beforeEach(() => forceDesktop());
  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => container.remove());

  // ─── D-24 ───────────────────────────────────────────────────────────────
  describe('SC-24: the splitter is operable without a mouse', () => {
    it('exposes separator semantics with a readable current value', async () => {
      const app = await mountApp(container);
      const splitter = rootOf(app).getElementById('splitter')!;

      expect(splitter.getAttribute('role')).to.equal('separator');
      expect(splitter.getAttribute('aria-orientation')).to.equal('vertical');
      expect(splitter.getAttribute('aria-label')).to.be.a('string').and.not
        .empty;
      expect(splitter.getAttribute('aria-valuemin')).to.equal('200');
      expect(splitter.getAttribute('aria-valuemax')).to.equal('1000');
      expect(splitter.getAttribute('aria-valuenow')).to.equal('300');
      expect(
        (splitter as HTMLElement).tabIndex,
        'must be reachable by keyboard'
      ).to.equal(0);
    });

    it('resizes with the arrow keys and clamps at both ends', async () => {
      const app = await mountApp(container);
      const splitter = rootOf(app).getElementById('splitter')!;
      const press = async (key: string, shiftKey = false) => {
        splitter.dispatchEvent(
          new KeyboardEvent('keydown', { key, shiftKey, bubbles: true })
        );
        await app.updateComplete;
      };

      await press('ArrowRight');
      expect(splitter.getAttribute('aria-valuenow')).to.equal('310');

      await press('ArrowLeft', true);
      expect(splitter.getAttribute('aria-valuenow')).to.equal('260');

      await press('Home');
      expect(splitter.getAttribute('aria-valuenow'), 'clamps to min').to.equal(
        '200'
      );

      await press('ArrowLeft');
      expect(
        splitter.getAttribute('aria-valuenow'),
        'cannot go below min'
      ).to.equal('200');

      await press('End');
      expect(splitter.getAttribute('aria-valuenow'), 'clamps to max').to.equal(
        '1000'
      );
    });

    // WCAG 2.5.7 Dragging Movements (AA, new in 2.2). A keyboard path does NOT
    // discharge this — it requires a single-pointer alternative to the drag, for
    // head-pointer, eye-gaze and tremor users who are on a pointer device.
    it('offers a single-pointer alternative that needs no dragging', async () => {
      const app = await mountApp(container);
      const splitter = rootOf(app).getElementById('splitter')!;

      const before = splitter.getAttribute('aria-valuenow');
      splitter.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await app.updateComplete;

      expect(
        splitter.getAttribute('aria-valuenow'),
        'a plain click must change the width'
      ).to.not.equal(before);
    });

    it('does not apply the click preset after a mouse drag', async () => {
      const app = await mountApp(container);
      const splitter = rootOf(app).getElementById('splitter')!;

      splitter.dispatchEvent(
        new MouseEvent('mousedown', { bubbles: true, clientX: 300 })
      );
      document.body.dispatchEvent(
        new MouseEvent('mousemove', { bubbles: true, clientX: 420 })
      );
      document.body.dispatchEvent(
        new MouseEvent('mouseup', { bubbles: true, clientX: 420 })
      );
      splitter.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await app.updateComplete;

      expect(splitter.getAttribute('aria-valuenow')).to.equal('420');
    });

    it('matches the subtle production divider when idle', async () => {
      setTheme('light');
      const app = await mountApp(container);
      const splitter = rootOf(app).getElementById('splitter')!;
      const line = getComputedStyle(splitter, '::before');
      const dragTarget = getComputedStyle(splitter, '::after');

      expect(getComputedStyle(splitter).width).to.equal('2px');
      expect(line.width).to.equal('2px');
      expect(line.backgroundColor).to.equal('rgb(245, 246, 248)');
      expect(dragTarget.width).to.equal('12px');
      expect(dragTarget.left).to.equal('-5px');
    });

    (['light', 'dark'] as DorcTheme[]).forEach(theme => {
      it(`is visible while resizing in ${theme} theme`, async () => {
        setTheme(theme);
        const app = await mountApp(container);
        app.style.background = 'var(--dorc-bg-primary)';
        const splitter = rootOf(app).getElementById('splitter')!;
        app.setAttribute('resizing', '');

        const line = getComputedStyle(splitter, '::before').backgroundColor;
        const ratio = contrastRatio(splitter, line);

        expect(ratio, `${theme} splitter contrast`).to.be.at.least(3);
      });
    });
  });

  // ─── D-39 ───────────────────────────────────────────────────────────────
  // Collapsing the sidebar on desktop only set width:0 inside an overflow:hidden
  // host. Every link stayed in the tab order inside a zero-width clipped box.
  describe('SC-29: a collapsed desktop sidebar is not focusable', () => {
    it('inerts and hides the drawer when collapsed, and restores it', async () => {
      const app = await mountApp(container);
      const navbar = rootOf(app).getElementById('dorcNavbar')!;
      const menuBtn = rootOf(app).querySelector('.menu-btn') as HTMLElement;

      expect(navbar.hasAttribute('inert'), 'expanded: reachable').to.be.false;

      menuBtn.click();
      await app.updateComplete;

      expect(navbar.hasAttribute('inert'), 'collapsed: not focusable').to.be
        .true;
      expect(
        navbar.getAttribute('aria-hidden'),
        'collapsed: hidden from AT'
      ).to.equal('true');

      menuBtn.click();
      await app.updateComplete;

      expect(navbar.hasAttribute('inert'), 'expanded again').to.be.false;
      expect(navbar.hasAttribute('aria-hidden')).to.be.false;
    });

    it('makes a collapsed drawer reachable when the splitter resizes it', async () => {
      const app = await mountApp(container);
      const navbar = rootOf(app).getElementById('dorcNavbar')!;
      const menuBtn = rootOf(app).querySelector('.menu-btn') as HTMLElement;
      const splitter = rootOf(app).getElementById('splitter')!;

      menuBtn.click();
      await app.updateComplete;
      expect(navbar.hasAttribute('inert'), 'precondition: collapsed').to.be
        .true;

      splitter.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })
      );
      await app.updateComplete;

      expect(navbar.hasAttribute('inert'), 'resized: reachable').to.be.false;
      expect(navbar.hasAttribute('aria-hidden')).to.be.false;
      expect(menuBtn.getAttribute('aria-expanded')).to.equal('true');
    });
  });

  // ─── D-33 ───────────────────────────────────────────────────────────────
  describe('SC-25: the mascot stays inside the header', () => {
    it('is no taller than the header', async () => {
      const app = await mountApp(container);
      const header = rootOf(app).getElementById('header')!;
      const mascot = rootOf(app).querySelector('.mascot') as HTMLElement;

      expect(
        mascot.getBoundingClientRect().height,
        'mascot must not bleed past the header'
      ).to.be.at.most(header.getBoundingClientRect().height);
    });
  });

  // ─── D-23a ──────────────────────────────────────────────────────────────
  describe('SC: in-scope secondary text meets 1.4.3', () => {
    (['light', 'dark'] as DorcTheme[]).forEach(theme => {
      it(`header user-info reaches 4.5:1 in ${theme} theme`, async () => {
        setTheme(theme);
        const app = await mountApp(container);
        const info = rootOf(app).querySelector('.user-info') as HTMLElement;

        const ratio = contrastRatio(info, getComputedStyle(info).color);
        expect(ratio, `${theme} user-info contrast`).to.be.at.least(4.5);
      });
    });
  });
});

// ─── D-34 ─────────────────────────────────────────────────────────────────
describe('SC-26: reduced motion', () => {
  it('suppresses the shared spinner animation under prefers-reduced-motion', async () => {
    const { DorcSpinner } =
      await import('../../src/components/dorc-spinner.js');

    const css = (
      DorcSpinner as unknown as {
        styles: { cssText: string } | Array<{ cssText: string }>;
      }
    ).styles;
    const text = Array.isArray(css)
      ? css.map(s => s.cssText).join('\n')
      : css.cssText;

    const block = text.match(
      /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{([\s\S]*?)\}\s*\}/
    );
    expect(block, 'reduced-motion block must exist').to.exist;
    expect(block![1]).to.match(/animation:\s*none/);
  });
});
