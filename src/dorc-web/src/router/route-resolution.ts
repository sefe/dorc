/**
 * Matches pathnames against the route table.
 *
 * Wraps `universal-router` — the resolver `@vaadin/router` was itself built on
 * — and reduces a match down to the chain of custom elements that should be
 * nested to render it, outermost first.
 */

import UniversalRouter from 'universal-router';
import generateUrls from 'universal-router/generate-urls';
import type {
  AppRoute,
  RouteChainEntry,
  RouteParams,
  RouteRedirect,
  RouteResolution
} from './route-config';

export type RouteOutcome = RouteRedirect | RouteResolution;

export const isRedirect = (outcome: RouteOutcome): outcome is RouteRedirect =>
  'redirect' in outcome;

/**
 * Walks a matched route back up its `parent` links, collecting the components
 * to nest. `parent` is populated by universal-router during traversal.
 *
 * `matchedPaths` carries the URL each route matched, recorded as the resolver
 * visited it. The outlet needs it to decide element reuse: two URLs differing
 * only in a parameter resolve to the same route objects, so identity alone
 * cannot tell them apart.
 */
const chainFor = (
  route: AppRoute,
  matchedPaths: Map<AppRoute, string>
): RouteChainEntry[] => {
  const chain: RouteChainEntry[] = [];
  for (let current: AppRoute | null | undefined = route; current; current = current.parent) {
    if (current.component) {
      chain.unshift({
        route: current,
        component: current.component,
        path: matchedPaths.get(current) ?? ''
      });
    }
  }
  return chain;
};

export class RouteResolver {
  private readonly router: UniversalRouter<RouteOutcome>;

  private readonly urlBuilder: (name: string, params?: RouteParams) => string;

  private readonly notFound: AppRoute | undefined;

  /**
   * URL matched per route for the resolve in flight, filled in as
   * universal-router visits each one. `resolve()` awaits a single traversal
   * before reading it, so only one resolve ever writes here at a time.
   */
  private readonly matchedPaths = new Map<AppRoute, string>();

  constructor(routes: AppRoute[]) {
    // universal-router sets `parent` lazily as it traverses, so a route that
    // has not been visited yet has no chain. Link them up front so `chainFor`
    // is correct from the first resolve — including the not-found fallback,
    // which may be needed before anything else has matched.
    linkParents(routes, null);

    this.notFound = findRouteByName(routes, 'not-found');

    this.router = new UniversalRouter<RouteOutcome>(routes as never, {
      resolveRoute: (context, params) => {
        const route = context.route as unknown as AppRoute;

        // `baseUrl` is everything the ancestors matched and `path` this route's
        // own segment; together they are the URL this route matched.
        const matched = context as unknown as {
          baseUrl?: string;
          path?: string;
        };
        this.matchedPaths.set(
          route,
          `${matched.baseUrl ?? ''}${matched.path ?? ''}`
        );

        if (typeof route.action === 'function') {
          const result = route.action({
            pathname: context.pathname,
            params: params as RouteParams
          });
          // An action that returns nothing falls through to the component,
          // matching Vaadin Router's behaviour.
          if (result) {
            return result;
          }
        }

        // A route with children is a layout: it contributes its component to
        // the chain but must not terminate resolution, so that descendants are
        // still matched. `chainFor` picks it up via the `parent` links.
        if (route.component && !route.children?.length) {
          return {
            chain: chainFor(route, this.matchedPaths),
            params: params as RouteParams,
            pathname: context.pathname
          };
        }

        return undefined;
      }
    });

    this.urlBuilder = generateUrls(this.router as never) as (
      name: string,
      params?: RouteParams
    ) => string;
  }

  /**
   * Resolves `pathname` to either a redirect or a chain of components.
   *
   * Never rejects: an unmatched pathname falls back to the `not-found` route,
   * so callers always have something to render.
   */
  async resolve(pathname: string): Promise<RouteOutcome> {
    this.matchedPaths.clear();
    try {
      const outcome = await this.router.resolve(pathname);
      if (outcome) {
        return outcome;
      }
    } catch {
      // Fall through to the not-found chain below.
    }
    return this.notFoundOutcome(pathname);
  }

  /** Builds the path for a named route, e.g. `urlForName('environment', {id})`. */
  urlForName(name: string, params?: RouteParams): string {
    return this.urlBuilder(name, params);
  }

  private notFoundOutcome(pathname: string): RouteResolution {
    return {
      chain: this.notFound ? chainFor(this.notFound, this.matchedPaths) : [],
      params: {},
      pathname
    };
  }
}

const linkParents = (routes: AppRoute[], parent: AppRoute | null): void => {
  for (const route of routes) {
    route.parent = parent;
    if (route.children) {
      linkParents(route.children, route);
    }
  }
};

const findRouteByName = (
  routes: AppRoute[],
  name: string
): AppRoute | undefined => {
  for (const route of routes) {
    if (route.name === name) {
      return route;
    }
    const found = route.children && findRouteByName(route.children, name);
    if (found) {
      return found;
    }
  }
  return undefined;
};
