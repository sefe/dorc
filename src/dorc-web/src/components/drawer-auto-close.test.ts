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

  it('removes open class when a link is clicked on narrow screens', async () => {
    mockMatchMedia(true);

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);

    // Wait for firstUpdated to attach the click handler
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');
    expect(navbar.classList.contains('open')).to.equal(true);

    const anchor = document.createElement('a');
    anchor.href = '/some-page';
    const clickEvent = new MouseEvent('click', { bubbles: true, composed: true });
    Object.defineProperty(clickEvent, 'composedPath', {
      value: () => [anchor, navbar, el.shadowRoot!, el, document.body, document],
    });

    navbar.dispatchEvent(clickEvent);

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Drawer should auto-close after link click on mobile'
    );
  });

  it('does NOT remove open class when a link is clicked on wide screens', async () => {
    mockMatchMedia(false);

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');

    const anchor = document.createElement('a');
    anchor.href = '/other-page';
    const clickEvent = new MouseEvent('click', { bubbles: true, composed: true });
    Object.defineProperty(clickEvent, 'composedPath', {
      value: () => [anchor, navbar, el.shadowRoot!, el, document.body, document],
    });

    navbar.dispatchEvent(clickEvent);

    expect(navbar.classList.contains('open')).to.equal(
      true,
      'Drawer should stay open on wide screens'
    );
  });

  it('does NOT remove open class when a non-link element is clicked on mobile', async () => {
    mockMatchMedia(true);

    const el = document.createElement('dorc-app');
    container.appendChild(el);
    const navbar = await waitForNavbar(el);
    await new Promise(r => setTimeout(r, 200));

    navbar.classList.add('open');

    const div = document.createElement('div');
    const clickEvent = new MouseEvent('click', { bubbles: true, composed: true });
    Object.defineProperty(clickEvent, 'composedPath', {
      value: () => [div, navbar, el.shadowRoot!, el, document.body, document],
    });

    navbar.dispatchEvent(clickEvent);

    expect(navbar.classList.contains('open')).to.equal(
      true,
      'Drawer should stay open when non-link clicked on mobile'
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
