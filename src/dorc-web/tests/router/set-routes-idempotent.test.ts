import { expect } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';

// `setRoutes()` replaced Vaadin Router's, whose contract was to replace the
// outlet's content. Building a fresh RouteOutlet per call breaks that: the new
// outlet's `rendered` list is empty, so it has nothing to diverge from and
// appends a second chain beside the first, leaving two live copies of the app
// in the DOM — both wired to the router, both responding to navigation.
//
// Only one call site exists today and it runs once, so this is a contract
// guard rather than a fix for a reachable path.

const first: AppRoute[] = [
  {
    path: '',
    component: 'div',
    children: [{ path: '/first', component: 'div' }]
  }
];

const second: AppRoute[] = [
  {
    path: '',
    component: 'section',
    children: [{ path: '/first', component: 'div' }]
  }
];

describe('setRoutes', () => {
  let outlet: HTMLElement;
  let startPath: string;
  // Describe-scoped so afterEach can let go of it. Each test still builds its
  // own — this file is specifically about constructing routers.
  let router: AppRouter;

  beforeEach(() => {
    startPath = window.location.pathname;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
  });

  afterEach(() => {
    // Let go of the document/window listeners. Without this every router
    // built in this file keeps handling clicks and popstate, so the
    // discarded one intercepts first and the router under test never
    // routes — the assertion would be about the previous test's object.
    router?.disconnect();
    window.history.replaceState(null, '', startPath);
    outlet.remove();
  });

  it('replaces the rendered chain instead of appending a second one', async () => {
    router = new AppRouter(outlet);
    await router.setRoutes(first);
    await router.navigate('/first');
    expect(outlet.children.length, 'one root rendered').to.equal(1);
    expect(outlet.children[0].tagName).to.equal('DIV');

    await router.setRoutes(second);

    expect(outlet.children.length, 'still one root').to.equal(1);
    expect(outlet.children[0].tagName, 'the new table won').to.equal('SECTION');
  });

  it('leaves the tree alone when the same table is installed again', async () => {
    router = new AppRouter(outlet);
    await router.setRoutes(first);
    await router.navigate('/first');
    const root = outlet.children[0];

    await router.setRoutes(first);

    expect(outlet.children.length, 'one root').to.equal(1);
    expect(outlet.children[0], 'same element kept').to.equal(root);
  });
});
