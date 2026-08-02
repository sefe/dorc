/**
 * Route table types.
 *
 * These describe the shape of `routes.ts` and the location object handed to
 * routed components. They are deliberately independent of the routing library
 * so that the route table reads the same regardless of what resolves it.
 */

export interface RouteMetadata {
  title: string;
  description: string;
}

/** Returned from a route `action` to send the browser somewhere else. */
export interface RouteRedirect {
  redirect: string;
}

/** The subset of resolution state a route `action` may read. */
export interface RouteResolveContext {
  /** Full pathname being resolved, e.g. `/environment/DEV1/components`. */
  pathname: string;
  /** Parameters captured so far, including those from ancestor routes. */
  params: RouteParams;
}

export type RouteParams = Record<string, string | string[] | undefined>;

/**
 * A route may render a component, redirect via an action, or do neither and
 * exist purely to group `children` under a shared path prefix.
 */
export interface AppRoute {
  /** Path segment, relative to the parent route. */
  path: string;
  /** Unique name, used by `urlForName` to build links. */
  name?: string;
  /** Custom element tag rendered when this route (or a descendant) matches. */
  component?: string;
  metadata?: RouteMetadata;
  action?: (context: RouteResolveContext) => RouteRedirect | undefined;
  children?: AppRoute[];
  /**
   * Populated by the resolver during route-table traversal — do not set this
   * by hand in the route table.
   */
  parent?: AppRoute | null;
}

/**
 * The location object assigned to every routed component, mirroring the shape
 * Vaadin Router provided so routed components did not need changing.
 */
export interface RouterLocation {
  baseUrl: string;
  hash: string;
  params: RouteParams;
  pathname: string;
  search: string;
  searchParams: URLSearchParams;
  /** The pathname that redirected here, if this location is a redirect target. */
  redirectFrom?: string;
  /** The route that rendered the component this location was assigned to. */
  route: AppRoute | null;
  /** The full matched chain, outermost first. */
  routes: AppRoute[];
}

/** A component in a resolved chain, paired with the route that produced it. */
export interface RouteChainEntry {
  route: AppRoute;
  component: string;
}

export interface RouteResolution {
  chain: RouteChainEntry[];
  params: RouteParams;
  pathname: string;
}
