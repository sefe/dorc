import { expect } from '../_helpers';

// ─── mockMatchMedia helper ───
let originalMatchMedia: typeof window.matchMedia;

function mockMatchMedia(matches: boolean) {
  const mql: MediaQueryList = {
    matches,
    media: '(max-width: 768px)',
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => true,
  };
  window.matchMedia = () => mql;
  return mql;
}

interface ReadyDorcApp extends HTMLElement {
  updateComplete: Promise<unknown>;
}

/**
 * Append dorc-app to a container, wait for its first update to settle, and
 * return both the host element and its #dorcNavbar shadow descendant.
 *
 * Uses element/Lit lifecycle promises rather than fixed sleeps so the test
 * doesn't get racy on slow CI runners.
 */
async function mountDorcApp(
  container: HTMLDivElement
): Promise<{ el: ReadyDorcApp; navbar: HTMLElement }> {
  const el = document.createElement('dorc-app') as ReadyDorcApp;
  container.appendChild(el);
  // updateComplete resolves after firstUpdated, when our event listeners are attached.
  await el.updateComplete;
  const navbar = el.shadowRoot?.getElementById('dorcNavbar');
  if (!navbar) throw new Error('dorc-app rendered without #dorcNavbar');
  return { el, navbar };
}

describe('Drawer auto-close on mobile', () => {
  let container: HTMLDivElement;

  // Pre-load the heavy dorc-app module before any test timers start.
  // Unhandled SUT-init errors are filtered by tests/_setup.ts.
  beforeAll(async () => {
    originalMatchMedia = window.matchMedia;
    await import('../../src/components/dorc-app.js');
  });

  afterAll(() => {
    window.matchMedia = originalMatchMedia;
  });

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
  });

  it('closes drawer on vaadin-router-location-changed when narrow', async () => {
    mockMatchMedia(true);

    const { el, navbar } = await mountDorcApp(container);
    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Drawer should auto-close after navigation on mobile'
    );
  });

  it('does NOT close drawer on vaadin-router-location-changed when wide', async () => {
    mockMatchMedia(false);

    const { el, navbar } = await mountDorcApp(container);
    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));

    expect(navbar.classList.contains('open')).to.equal(
      true,
      'Drawer should stay open on wide screens'
    );
  });

  it('closes drawer on Escape when narrow', async () => {
    mockMatchMedia(true);

    const { el, navbar } = await mountDorcApp(container);
    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Escape should close the drawer on mobile'
    );
  });

  describe('Drawer CSS (structural)', () => {
    it('dorc-app CSS contains mobile drawer styles', async () => {
      const mod = await import('../../src/components/dorc-app.js');
      const styles = mod.DorcApp.styles;
      const cssText = Array.isArray(styles)
        ? styles.map((s: any) => s.cssText ?? '').join('')
        : (styles as any)?.cssText ?? '';

      expect(cssText).to.include('max-width: 768px', 'Should have mobile breakpoint');
      expect(cssText).to.include('#dorcNavbar.open', 'Should have .open class rule');
      expect(cssText).to.include('position: fixed', 'Navbar should be fixed on mobile');
    });
  });
});
