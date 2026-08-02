import { expect } from '../_helpers';
import { RouteOutlet } from '../../src/router/route-outlet';
import type {
  AppRoute,
  RouteChainEntry,
  RouterLocation
} from '../../src/router/route-config';

// Plain custom elements standing in for routed pages. They project children
// through a slot, the way the real routed components do, and record the
// onAfterEnter calls the router makes.
const entered: string[] = [];

class RoutedStub extends HTMLElement {
  location?: RouterLocation;

  onAfterEnter(location: RouterLocation) {
    entered.push(`${this.tagName.toLowerCase()}:${location.pathname}`);
  }

  connectedCallback() {
    if (!this.shadowRoot) {
      this.attachShadow({ mode: 'open' }).innerHTML = '<slot></slot>';
    }
  }
}

for (const tag of ['stub-layout', 'stub-parent', 'stub-a', 'stub-b']) {
  if (!customElements.get(tag)) {
    customElements.define(tag, class extends RoutedStub {});
  }
}

const route = (component: string): AppRoute => ({ path: '/', component });

// Stable route identities — divergence is decided by route identity, so these
// must be shared across renders the way the real route table is.
const layoutRoute = route('stub-layout');
const parentRoute = route('stub-parent');
const routeA = route('stub-a');
const routeB = route('stub-b');

const chainOf = (...routes: AppRoute[]): RouteChainEntry[] =>
  routes.map(r => ({ route: r, component: r.component as string }));

describe('RouteOutlet', () => {
  let host: HTMLElement;
  let outlet: RouteOutlet;

  const render = (routes: AppRoute[], pathname = '/') =>
    outlet.render({
      chain: chainOf(...routes),
      params: {},
      pathname,
      search: '',
      hash: ''
    });

  beforeEach(() => {
    entered.length = 0;
    host = document.createElement('div');
    document.body.appendChild(host);
    outlet = new RouteOutlet(host);
  });

  afterEach(() => {
    host.remove();
  });

  it('nests the chain as light-DOM children', () => {
    render([layoutRoute, parentRoute, routeA]);

    const layout = host.firstElementChild;
    expect(layout?.tagName.toLowerCase()).to.equal('stub-layout');
    const parent = layout?.firstElementChild;
    expect(parent?.tagName.toLowerCase()).to.equal('stub-parent');
    expect(parent?.firstElementChild?.tagName.toLowerCase()).to.equal('stub-a');
  });

  it('assigns each element a location carrying its own route', () => {
    render([layoutRoute, routeA], '/somewhere');

    const layout = host.firstElementChild as RoutedStub;
    const leaf = layout.firstElementChild as RoutedStub;
    expect(layout.location?.route).to.equal(layoutRoute);
    expect(leaf.location?.route).to.equal(routeA);
    expect(leaf.location?.pathname).to.equal('/somewhere');
    expect(leaf.location?.routes).to.deep.equal([layoutRoute, routeA]);
  });

  it('reuses unchanged ancestors across navigations', () => {
    render([layoutRoute, parentRoute, routeA]);
    const layoutBefore = host.firstElementChild;
    const parentBefore = layoutBefore?.firstElementChild;

    render([layoutRoute, parentRoute, routeB]);

    expect(host.firstElementChild).to.equal(layoutBefore);
    expect(host.firstElementChild?.firstElementChild).to.equal(parentBefore);
  });

  it('replaces only the diverging tail', () => {
    render([layoutRoute, parentRoute, routeA]);
    const leafBefore = host.querySelector('stub-a');

    render([layoutRoute, parentRoute, routeB]);

    expect(host.querySelector('stub-a')).to.equal(null);
    expect(leafBefore?.isConnected).to.equal(false);
    expect(host.querySelector('stub-b')).to.not.equal(null);
  });

  it('recreates the subtree when an ancestor route changes', () => {
    render([layoutRoute, parentRoute, routeA]);
    const parentBefore = host.querySelector('stub-parent');

    render([layoutRoute, routeA]);

    expect(host.querySelector('stub-parent')).to.equal(null);
    expect(parentBefore?.isConnected).to.equal(false);
    expect(host.firstElementChild?.firstElementChild?.tagName.toLowerCase()).to.equal(
      'stub-a'
    );
  });

  it('refreshes the location of reused elements', () => {
    render([layoutRoute, routeA], '/first');
    const layout = host.firstElementChild as RoutedStub;
    expect(layout.location?.pathname).to.equal('/first');

    render([layoutRoute, routeB], '/second');
    expect(layout.location?.pathname).to.equal('/second');
  });

  it('calls onAfterEnter only for newly rendered elements', () => {
    render([layoutRoute, parentRoute, routeA], '/one');
    expect(entered).to.deep.equal([
      'stub-layout:/one',
      'stub-parent:/one',
      'stub-a:/one'
    ]);

    entered.length = 0;
    render([layoutRoute, parentRoute, routeB], '/two');
    // The layout and parent are reused, so only the new leaf is entered.
    expect(entered).to.deep.equal(['stub-b:/two']);
  });

  it('has the whole chain attached before onAfterEnter fires', () => {
    // A callback that inspects its own subtree must see a complete tree.
    const seen: (string | undefined)[] = [];
    class Inspecting extends RoutedStub {
      override onAfterEnter() {
        seen.push(this.firstElementChild?.tagName.toLowerCase());
      }
    }
    if (!customElements.get('stub-inspecting')) {
      customElements.define('stub-inspecting', Inspecting);
    }
    const inspectingRoute = route('stub-inspecting');

    render([inspectingRoute, routeA]);
    expect(seen).to.deep.equal(['stub-a']);
  });

  it('grows the chain without disturbing existing ancestors', () => {
    render([layoutRoute, parentRoute]);
    const parentBefore = host.querySelector('stub-parent');

    render([layoutRoute, parentRoute, routeA]);

    expect(host.querySelector('stub-parent')).to.equal(parentBefore);
    expect(parentBefore?.firstElementChild?.tagName.toLowerCase()).to.equal(
      'stub-a'
    );
  });
});
