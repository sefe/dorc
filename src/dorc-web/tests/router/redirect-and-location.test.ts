import { expect } from '../_helpers';
import { AppRouter, LOCATION_CHANGED_EVENT } from '../../src/router/router';
import { routes } from '../../src/router/routes';

// The redirect hop and the location object had no end-to-end coverage: four
// separate one-line mutations in `router.ts` survived the entire suite —
// `MAX_REDIRECTS` to 0, dropping the redirect's `history.replaceState`, blanking
// `search`/`searchParams` on `router.location`, and deleting the
// `LOCATION_CHANGED_EVENT` dispatch.
//
// The route this exercises is the single most-used redirect in the app: the
// environment Components tab is a path-less child whose action redirects to
// `/servers`. Every click on that tab goes through the hop.
//
// This drives the shipped route table and the real AppRouter, so the redirect,
// the address-bar rewrite, the query string and the event are all covered by the
// navigation itself rather than simulated.

const settle = () =>
  new Promise(resolve => requestAnimationFrame(() => resolve(null)));

describe('redirects and the location object', () => {
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

  const leaf = () => {
    let node: Element | null = outlet.firstElementChild;
    let last = node;
    while (node) {
      last = node;
      node = node.firstElementChild;
    }
    return last?.tagName.toLowerCase();
  };

  it('follows the Components tab redirect and renders the servers tab', async () => {
    await router.navigate('/environment/DEV1/components');
    await settle();

    expect(leaf(), 'rendered the redirect target').to.equal('env-servers');
  });

  it('rewrites the address bar to the redirect target', async () => {
    // Without the replaceState the view and the URL diverge: the servers tab is
    // on screen while the address bar still says /components, so reloading or
    // bookmarking lands somewhere the user was not.
    await router.navigate('/environment/DEV1/components');
    await settle();

    expect(window.location.pathname).to.equal(
      '/environment/DEV1/components/servers'
    );
    expect(router.location.pathname).to.equal(
      '/environment/DEV1/components/servers'
    );
    expect(router.location.redirectFrom, 'records where it came from').to.equal(
      '/environment/DEV1/components'
    );
  });

  it('carries the query string through the redirect', async () => {
    await router.navigate('/environment/DEV1/components?tab=1');
    await settle();

    expect(window.location.search, 'query preserved').to.equal('?tab=1');
    expect(router.location.search).to.equal('?tab=1');
    expect(router.location.searchParams.get('tab')).to.equal('1');
  });

  it('fires a location-changed event for each completed navigation', async () => {
    // dorc-app listens for this to close the mobile drawer. drawer-auto-close
    // dispatches the event by hand, so nothing checked that the router emits it.
    const seen: string[] = [];
    const listener = (e: Event) => {
      seen.push((e as CustomEvent).detail.location.pathname);
    };
    window.addEventListener(LOCATION_CHANGED_EVENT, listener);

    await router.navigate('/projects');
    await settle();
    await router.navigate('/servers');
    await settle();

    window.removeEventListener(LOCATION_CHANGED_EVENT, listener);

    expect(
      seen,
      'one event per navigation, with the final pathname'
    ).to.deep.equal(['/projects', '/servers']);
  });
});
