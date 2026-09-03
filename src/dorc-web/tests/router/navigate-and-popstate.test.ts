import { expect } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute, RouteRedirect } from '../../src/router/route-config';

// Three contracts in `router.ts` that a one-line change removed silently:
//
//  - `navigate()` builds its history entry from `url.pathname + url.search +
//    url.hash`. Reduced to `url.pathname`, the whole suite stayed green — and
//    several call sites pass the row identity in the query string
//    (`/daemons/audit?daemonId=…`, `/projects/audit?projectId=…`), so the audit
//    page would open on nothing.
//  - the `popstate` subscription. Every other router test navigates through
//    `navigate()` or a link click, both of which re-render directly, so nothing
//    went through the listener: Back would change the URL and leave the view.
//  - `preventDefault()` on an intercepted link. link-interception.test.ts asks
//    "did the router navigate", and the router navigates by calling pushState
//    itself — so removing preventDefault leaves that file green while the
//    browser *also* follows the href, turning every in-app link into a full
//    page load. Detecting it needs a listener registered before the router's.

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

const settle = () =>
  new Promise(resolve => requestAnimationFrame(() => resolve(null)));

// popstate arrives on the browser's own schedule, so poll rather than guess a
// delay — a fixed wait is the usual source of cross-browser flake here.
const waitFor = async (predicate: () => boolean, timeout = 2000) => {
  const deadline = Date.now() + timeout;
  while (!predicate() && Date.now() < deadline) {
    await new Promise(r => setTimeout(r, 20));
  }
};

describe('navigate, popstate and link default actions', () => {
  let outlet: HTMLElement;
  let router: AppRouter;
  let startPath: string;

  beforeEach(async () => {
    startPath = window.location.pathname + window.location.search;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
    router = new AppRouter(outlet);
    await router.setRoutes(routes);
  });

  afterEach(() => {
    // Let go of the document/window listeners. Without this every router
    // built in this file keeps handling clicks and popstate, so the
    // discarded one intercepts first and the router under test never
    // routes — the assertion would be about the previous test's object.
    router.disconnect();
    window.history.replaceState(null, '', startPath);
    outlet.remove();
  });

  it('keeps the query string when navigating', async () => {
    await router.navigate('/second?daemonId=42');
    await settle();

    expect(window.location.search, 'query survives navigate()').to.equal(
      '?daemonId=42'
    );
    expect(router.location.searchParams.get('daemonId')).to.equal('42');
  });

  it('re-renders when the user presses Back', async () => {
    await router.navigate('/first');
    await router.navigate('/second');
    await settle();
    expect(window.location.pathname).to.equal('/second');

    // popstate is the only path that reaches the router here — history.back()
    // does not call navigate().
    window.history.back();
    await waitFor(() => router.location.pathname === '/first');
    await settle();

    expect(window.location.pathname, 'URL went back').to.equal('/first');
    expect(
      router.location.pathname,
      'and the router re-resolved, so the view follows'
    ).to.equal('/first');
  });

  it('suppresses the browser navigation it is replacing', async () => {
    await router.navigate('/first');

    // Registered after `setRoutes` installed the router's own document
    // listener, so it runs second in the same bubble phase and sees the flag
    // the router set. Adding `true` here to make it a capture listener would
    // put it *before* the router and read defaultPrevented as false — the
    // ordering is the whole mechanism, not an incidental detail.
    let preventedAfterRouter: boolean | undefined;
    const sample = (e: Event) => {
      preventedAfterRouter = e.defaultPrevented;
    };
    document.addEventListener('click', sample);

    const anchor = document.createElement('a');
    anchor.href = '/second';
    outlet.appendChild(anchor);
    anchor.dispatchEvent(
      new MouseEvent('click', {
        bubbles: true,
        composed: true,
        cancelable: true,
        button: 0
      })
    );
    await settle();
    document.removeEventListener('click', sample);
    anchor.remove();

    expect(
      preventedAfterRouter,
      'the router cancelled the browser default, or the page reloads too'
    ).to.equal(true);
  });

  it('does not render an older navigation after a newer one completes', async () => {
    router.disconnect();
    window.history.replaceState(null, '', '/second');

    let resolveFirst: ((outcome: RouteRedirect) => void) | undefined;
    let firstStarted: (() => void) | undefined;
    const started = new Promise<void>(resolve => {
      firstStarted = resolve;
    });
    const delayedAction = (() => {
      firstStarted?.();
      return new Promise<RouteRedirect>(resolve => {
        resolveFirst = resolve;
      });
    }) as unknown as AppRoute['action'];
    const delayedRoutes: AppRoute[] = [
      {
        path: '',
        component: 'div',
        children: [
          { path: '/first', action: delayedAction },
          { path: '/second', component: 'div' },
          { path: '/*notFound', name: 'not-found', component: 'div' }
        ]
      }
    ];

    router = new AppRouter(outlet);
    await router.setRoutes(delayedRoutes);

    const olderNavigation = router.navigate('/first');
    await started;
    await router.navigate('/second');
    resolveFirst?.({ redirect: '/obsolete' });
    await olderNavigation;

    expect(window.location.pathname).to.equal('/second');
    expect(router.location.pathname).to.equal('/second');
  });
});
