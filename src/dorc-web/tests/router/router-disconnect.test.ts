import { expect } from '../_helpers';
import { AppRouter, LOCATION_CHANGED_EVENT } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';

// `setRoutes` registers a click listener on `document` and a popstate listener
// on `window`, and until now there was no way to let go of them. The app builds
// one router for its lifetime so it never noticed; tests build one per test,
// and every router ever constructed stayed live.
//
// Two things follow, and both are assertions some test was already making about
// the wrong object: a discarded router intercepts the click first and calls
// `preventDefault()`, so the router under test never routes; and one
// `history.back()` fires LOCATION_CHANGED_EVENT once per router still alive.

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

describe('a disconnected router lets go', () => {
  let outlet: HTMLElement;
  let startPath: string;
  const routers: AppRouter[] = [];

  beforeEach(() => {
    startPath = window.location.pathname + window.location.search;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
  });

  afterEach(() => {
    for (const r of routers) r.disconnect();
    routers.length = 0;
    window.history.replaceState(null, '', startPath);
    outlet.remove();
  });

  const build = async () => {
    const router = new AppRouter(outlet);
    routers.push(router);
    await router.setRoutes(routes);
    return router;
  };

  it('stops handling clicks', async () => {
    const stale = await build();
    await stale.navigate('/second');
    stale.disconnect();

    const live = await build();
    await live.navigate('/first');
    await settle();

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
    anchor.remove();

    expect(live.location.pathname, 'the live router routed').to.equal(
      '/second'
    );
    expect(
      stale.location.pathname,
      'and the discarded one did not move'
    ).to.equal('/second');
  });

  it('stops responding to popstate', async () => {
    // Through Back, not `navigate()`. `navigate()` emits the event directly
    // from the router that was called, so it says nothing about who is still
    // *listening* — the whole point here. Back is the only path that reaches
    // the popstate handler.
    const stale = await build();
    stale.disconnect();
    const live = await build();
    await live.navigate('/first');
    await live.navigate('/second');
    await settle();

    let seen = 0;
    const count = () => {
      seen += 1;
    };
    window.addEventListener(LOCATION_CHANGED_EVENT, count);

    window.history.back();
    await new Promise(r => setTimeout(r, 300));
    window.removeEventListener(LOCATION_CHANGED_EVENT, count);

    expect(live.location.pathname, 'the live router went back').to.equal(
      '/first'
    );
    expect(seen, 'one event, not one per router ever built').to.equal(1);
  });
});
