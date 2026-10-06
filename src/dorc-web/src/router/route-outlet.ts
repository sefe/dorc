/**
 * Renders a resolved route chain into the outlet element.
 *
 * Chains are rendered as nested light-DOM children — `<dorc-app>` contains
 * `<page-environment>`, which contains `<page-environment-components>`, and so
 * on — because the routed components project their child through a `<slot>`.
 *
 * Element reuse follows the same rule Vaadin Router used, and the app depends
 * on it: elements up to the first route that differs from the previous
 * navigation are kept (so `<dorc-app>` is not rebuilt on every navigation,
 * preserving its listeners and sidebar state), and only the diverging tail is
 * recreated.
 */

import type {
  AppRoute,
  RouteChainEntry,
  RouteParams,
  RouterLocation
} from './route-config';

/** A component that opted into the router's post-navigation callback. */
interface RoutedElement extends HTMLElement {
  location?: RouterLocation;
  onAfterEnter?: (location: RouterLocation) => void;
  onRouteUpdate?: (location: RouterLocation) => void;
}

interface RenderedEntry extends RouteChainEntry {
  element: RoutedElement;
}

export interface OutletRenderRequest {
  chain: RouteChainEntry[];
  params: RouteParams;
  pathname: string;
  search: string;
  hash: string;
  redirectFrom?: string;
}

export class RouteOutlet {
  private rendered: RenderedEntry[] = [];

  constructor(private readonly outlet: HTMLElement) {}

  render(request: OutletRenderRequest): void {
    const divergedAt = this.divergenceIndex(request.chain);

    const routes = request.chain.map(entry => entry.route);
    const locationFor = (route: AppRoute): RouterLocation => ({
      baseUrl: '',
      hash: request.hash,
      params: request.params,
      pathname: request.pathname,
      redirectFrom: request.redirectFrom,
      route,
      routes,
      search: request.search,
      searchParams: new URLSearchParams(request.search)
    });

    // Reused elements receive a lightweight update hook. Re-firing
    // onAfterEnter would repeat entry side effects on every sibling navigation.
    const reused = this.rendered.slice(0, divergedAt);
    for (const entry of reused) {
      entry.element.location = locationFor(entry.route);
    }

    this.removeFrom(divergedAt);

    const parentOf = (index: number): HTMLElement =>
      index === 0 ? this.outlet : reused[index - 1].element;

    let parent = parentOf(divergedAt);
    const added: RenderedEntry[] = [];

    for (let i = divergedAt; i < request.chain.length; i++) {
      const { route, component, path } = request.chain[i];
      const element = document.createElement(component) as RoutedElement;
      element.location = locationFor(route);
      parent.appendChild(element);
      added.push({ route, component, path, element });
      parent = element;
    }

    this.rendered = [...reused, ...added];

    // Fire after the whole chain is attached so a callback that inspects its
    // own subtree sees a complete tree.
    for (const entry of added) {
      entry.element.onAfterEnter?.(entry.element.location as RouterLocation);
    }
    for (const entry of reused) {
      entry.element.onRouteUpdate?.(entry.element.location as RouterLocation);
    }
  }

  /**
   * Index of the first chain position that differs from what is rendered.
   *
   * Compares route identity *and* the URL the route matched, matching Vaadin
   * Router (`dist/router.js`: `previousChain[i].route !== newChain[i].route ||
   * previousChain[i].path !== newChain[i].path && ...`, where the right-hand
   * side always holds because every resolution builds fresh elements).
   *
   * Both halves are load-bearing. Identity catches two routes that render the
   * same component but are different views. Matched path catches the same
   * route reached with different parameters — `/project-envs/A` versus
   * `/project-envs/B` — where reusing the element would leave the previous
   * project's data on screen under the new URL, because routed pages load in
   * `connectedCallback` / `firstUpdated` / `onAfterEnter`, none of which run
   * again for an element that was never detached.
   */
  private divergenceIndex(chain: RouteChainEntry[]): number {
    const shared = Math.min(this.rendered.length, chain.length);
    for (let i = 0; i < shared; i++) {
      const current = this.rendered[i];
      if (
        current.route !== chain[i].route ||
        current.path !== chain[i].path ||
        !current.element.isConnected
      ) {
        return i;
      }
    }
    return shared;
  }

  private removeFrom(index: number): void {
    // Removing the shallowest diverged element detaches its whole subtree.
    this.rendered[index]?.element.remove();
    this.rendered = this.rendered.slice(0, index);
  }
}
