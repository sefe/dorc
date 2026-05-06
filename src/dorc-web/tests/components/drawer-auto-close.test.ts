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
    // Open via the real path so focus capture, body lock, and aria all run.
    (el as any)._openDrawer();
    expect(navbar.classList.contains('open')).to.equal(true, 'precondition');

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));

    expect(navbar.classList.contains('open')).to.equal(
      false,
      'Drawer should auto-close after navigation on mobile'
    );
  });

  it('does NOT close drawer on vaadin-router-location-changed when wide', async () => {
    mockMatchMedia(false);

    const { el, navbar } = await mountDorcApp(container);
    (el as any)._openDrawer();

    window.dispatchEvent(new CustomEvent('vaadin-router-location-changed'));

    expect(navbar.classList.contains('open')).to.equal(
      true,
      'Drawer should stay open on wide screens'
    );
  });

  it('closes drawer on Escape when narrow', async () => {
    mockMatchMedia(true);

    const { el, navbar } = await mountDorcApp(container);
    (el as any)._openDrawer();
    expect(navbar.classList.contains('open')).to.equal(true, 'precondition');

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

describe('Splitter listener survives reconnect', () => {
  let container: HTMLDivElement;

  beforeAll(async () => {
    await import('../../src/components/dorc-app.js');
  });

  beforeEach(() => {
    mockMatchMedia(false); // desktop layout — splitter is interactive
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(() => {
    container.remove();
  });

  /** Returns the splitter element from a mounted dorc-app. */
  function splitterOf(el: ReadyDorcApp): HTMLElement {
    const splitter = el.shadowRoot?.getElementById('splitter');
    if (!splitter) throw new Error('dorc-app rendered without #splitter');
    return splitter;
  }

  // We observe the `_splitterDragInProgress` flag rather than
  // body.style.user-select because WebKit silently drops setProperty calls
  // for the unprefixed `user-select`, making it an unreliable signal.
  it('still flips drag-in-progress on mousedown after disconnect/reconnect', async () => {
    const { el } = await mountDorcApp(container);

    splitterOf(el).dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect((el as any)._splitterDragInProgress).to.equal(
      true,
      'splitter mousedown should set _splitterDragInProgress on first mount'
    );

    // End the drag so cleanup runs.
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    document.body.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));

    // Disconnect…
    el.remove();
    // …reconnect to the same container. firstUpdated will not re-fire; the
    // re-attach path is connectedCallback's deferred updateComplete handler.
    container.appendChild(el);
    await el.updateComplete;

    (el as any)._splitterDragInProgress = false;
    splitterOf(el).dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect((el as any)._splitterDragInProgress).to.equal(
      true,
      'splitter mousedown should still fire after reconnect'
    );
  });
});
