import { expect } from '@open-wc/testing';

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

/** Wait for the element's shadow DOM to settle enough to have #dorcNavbar */
async function waitForNavbar(el: HTMLElement, timeoutMs = 5000): Promise<HTMLElement> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const navbar = el.shadowRoot?.getElementById('dorcNavbar');
    if (navbar) return navbar;
    await new Promise(r => setTimeout(r, 50));
  }
  throw new Error('Timed out waiting for #dorcNavbar');
}

describe('Drawer auto-close on mobile', () => {
  // Suppress uncaught errors from dorc-app's constructor API calls
  let origOnError: OnErrorEventHandler;
  let container: HTMLDivElement;

  // Pre-load the heavy dorc-app module before any test timers start
  before(async function () {
    this.timeout(30000);
    originalMatchMedia = window.matchMedia;
    origOnError = window.onerror;
    window.onerror = (msg, ...rest) => {
      const msgStr = String(msg);
      if (
        msgStr.includes('ajax error') ||
        msgStr.includes('AjaxError') ||
        msgStr.includes('Route')
      ) {
        return true;
      }
      if (origOnError) {
        return (origOnError as (...args: any[]) => any).call(window, msg, ...rest);
      }
      return false;
    };
    await import('./dorc-app.js');
  });

  after(() => {
    window.matchMedia = originalMatchMedia;
    window.onerror = origOnError;
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

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));
    await new Promise(r => setTimeout(r, 50));

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Drawer should auto-close after navigation on mobile'
    );
  });

  it('does NOT close drawer on vaadin-router-location-changed when wide', async () => {
    mockMatchMedia(false);

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));
    await new Promise(r => setTimeout(r, 50));

    expect(navbar.classList.contains('open')).to.equal(
      true,
      'Drawer should stay open on wide screens'
    );
  });

  it('closes drawer on Escape when narrow', async () => {
    mockMatchMedia(true);

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');
    (el as any)._drawerOpen = true;

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await new Promise(r => setTimeout(r, 50));

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Escape should close the drawer on mobile'
    );
  });

  describe('Drawer CSS (structural)', () => {
    it('dorc-app CSS contains mobile drawer styles', async () => {
      const mod = await import('./dorc-app.js');
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
