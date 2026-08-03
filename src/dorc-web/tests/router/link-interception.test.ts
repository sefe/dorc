import { expect } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';

// Every in-app navigation goes through the document click handler, and none of
// it was covered: deleting the `event.defaultPrevented` check or the
// `anchor.target` check left the whole suite green. The first would hijack the
// navbar's `href="#"` Audit parent tab, which relies on preventDefault(); the
// second would client-side-route the `target="_blank"` script links in the
// deployment-results views, so "open in new tab" would silently stop working.
//
// The signal is whether the router navigated — i.e. whether the URL changed
// through the router's own `navigate()`. (`defaultPrevented` cannot be used: an
// anchor that handles its own click sets it before the document listener runs,
// so it reads the same for "the app handled it" and "the router handled it".)
//
// A dispatched click still runs the browser's default action for an anchor in
// Firefox, which really does navigate — that tore the test iframe out from
// under the runner in CI while passing locally on chromium. So a listener
// registered after the router's suppresses the default action for every click
// here. It cannot mask the behaviour under test: the router navigates by
// calling history.pushState itself, not by letting the click through.

const routes: AppRoute[] = [
  {
    path: '',
    component: 'div',
    children: [
      { path: '/first', component: 'div' },
      { path: '/second', component: 'div' },
      { path: '/*notFound', name: 'not-found', component: 'div' }
    ]
  }
];

describe('link interception', () => {
  let outlet: HTMLElement;
  let host: HTMLElement;
  let router: AppRouter;
  let startPath: string;

  const suppressDefault = (event: Event) => event.preventDefault();

  beforeEach(async () => {
    startPath = window.location.pathname;
    outlet = document.createElement('div');
    host = document.createElement('div');
    document.body.append(outlet, host);
    router = new AppRouter(outlet);
    await router.setRoutes(routes);
    // Registered after the router's own listener, so it runs second and only
    // stops the browser from following the link for real.
    document.addEventListener('click', suppressDefault);
    await router.navigate('/first');
  });

  afterEach(() => {
    document.removeEventListener('click', suppressDefault);
    window.history.replaceState(null, '', startPath);
    outlet.remove();
    host.remove();
  });

  /** Clicks an anchor and reports whether the router claimed the navigation. */
  const clickAnchor = async (
    configure: (anchor: HTMLAnchorElement) => void,
    init: MouseEventInit = {}
  ): Promise<boolean> => {
    const anchor = document.createElement('a');
    anchor.href = '/second';
    anchor.textContent = 'go';
    configure(anchor);
    host.appendChild(anchor);

    anchor.dispatchEvent(
      new MouseEvent('click', {
        bubbles: true,
        composed: true,
        cancelable: true,
        button: 0,
        ...init
      })
    );
    await new Promise(resolve => requestAnimationFrame(() => resolve(null)));
    anchor.remove();

    const routed = window.location.pathname === '/second';
    if (routed) await router.navigate('/first');
    return routed;
  };

  it('routes a plain in-app link', async () => {
    expect(await clickAnchor(() => {})).to.equal(true);
  });

  it('leaves a link the app already handled alone', async () => {
    // The navbar's parent tabs preventDefault in their own click handler. The
    // router must respect that rather than navigating on top of it.
    expect(
      await clickAnchor(anchor => {
        anchor.addEventListener('click', e => e.preventDefault());
      })
    ).to.equal(false);
  });

  it('leaves target="_blank" to the browser', async () => {
    expect(await clickAnchor(anchor => (anchor.target = '_blank'))).to.equal(
      false
    );
  });

  it('leaves a download link to the browser', async () => {
    expect(
      await clickAnchor(anchor => anchor.setAttribute('download', ''))
    ).to.equal(false);
  });

  it('leaves rel="external" to the browser', async () => {
    expect(
      await clickAnchor(anchor => anchor.setAttribute('rel', 'external'))
    ).to.equal(false);
  });

  it('leaves a cross-origin link to the browser', async () => {
    expect(
      await clickAnchor(anchor => (anchor.href = 'https://example.com/second'))
    ).to.equal(false);
  });

  it('leaves modified clicks to the browser', async () => {
    // Ctrl/Cmd/Shift-click and middle-click are how users open a new tab or
    // window; routing them in-page takes that away.
    for (const init of [
      { metaKey: true },
      { ctrlKey: true },
      { shiftKey: true },
      { altKey: true },
      { button: 1 }
    ]) {
      expect(
        await clickAnchor(() => {}, init),
        `${JSON.stringify(init)} not routed`
      ).to.equal(false);
    }
  });

  it('routes a link inside a shadow root', async () => {
    // Most of this app's navigation is rendered inside component shadow roots,
    // which is why the handler walks the composed path rather than the target.
    const wrapper = document.createElement('div');
    const shadow = wrapper.attachShadow({ mode: 'open' });
    const anchor = document.createElement('a');
    anchor.href = '/second';
    shadow.appendChild(anchor);
    host.appendChild(wrapper);

    anchor.dispatchEvent(
      new MouseEvent('click', {
        bubbles: true,
        composed: true,
        cancelable: true,
        button: 0
      })
    );
    await new Promise(resolve => requestAnimationFrame(() => resolve(null)));
    wrapper.remove();

    expect(
      window.location.pathname,
      'routed from inside the shadow root'
    ).to.equal('/second');
  });
});
