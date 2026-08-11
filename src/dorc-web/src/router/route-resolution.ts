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

        // `baseUrl` is everything the ancestors matched and `path` this
        // route's own segment; together they are the URL this route matched.
        //
        // The map is handed in per resolve rather than kept on the resolver:
        // universal-router spreads its context into every resolveRoute call,
        // and two overlapping resolves sharing one map read each other's paths
        // — a popstate landing mid-navigation is enough to cause it.
        const matched = context as unknown as {
          baseUrl?: string;
          path?: string;
          matchedPaths?: Map<AppRoute, string>;
        };
        const matchedPaths =
          matched.matchedPaths ?? new Map<AppRoute, string>();
        matchedPaths.set(route, `${matched.baseUrl ?? ''}${matched.path ?? ''}`);

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

        const leaf = (): RouteOutcome => ({
          chain: chainFor(route, matchedPaths),
          params: params as RouteParams,
          pathname: context.pathname
        });

        if (!route.component) {
          return undefined;
        }

        if (!route.children?.length) {
          return leaf();
        }

        // A route with children is a layout: descendants must get first refusal,
        // or `/environment/:id` would swallow `/environment/:id/metadata`.
        //
        // But it still terminates when the URL stops at it. `/environment/:id`
        // carries both a component and tabs, and the sidebar's per-environment
        // tab links to the bare form — resolving that to not-found is what
        // Vaadin Router did not do (`dist/router.js`: `if (isString(
        // route.component)) return commands.component(...)`, reached for
        // parents too).
        //
        // The guard is that this route consumed the whole pathname. Without it,
        // `/environment/:id/nosuchtab` would fall back to the layout instead of
        // not-found, which Vaadin Router also did not do.
        //
        // "Consumed the whole pathname" allows one trailing slash, so
        // `/environment/DEV1/` reaches the layout rather than not-found — which
        // is what Vaadin Router did, and what leaf routes here already do
        // (`/projects/` works). A second slash still does not match.
        //
        // `remaining` is the residue universal-router hands to the children, NOT
        // a suffix of `context.pathname`. path-to-regexp reports the matched
        // path *with* the trailing slash, and universal-router then strips the
        // leading character (`path.substr(1)`, universal-router.js:36-37), so
        // `consumed` is not a prefix of the pathname at all — only its *length*
        // lines up with the child-facing offset. Do not "tidy" this into
        // `pathname.startsWith(consumed)`: that reintroduces the 404.
        const consumed = `${matched.baseUrl ?? ''}${matched.path ?? ''}`;
        const remaining = context.pathname.slice(consumed.length);
        const stoppedHere = remaining === '' || remaining === '/';
        const next = (
          context as unknown as { next: () => Promise<RouteOutcome | null> }
        ).next;

        return next().then(
          result => result ?? (stoppedHere ? leaf() : undefined)
        );
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
    const matchedPaths = new Map<AppRoute, string>();
    try {
      const outcome = await this.router.resolve({
        pathname,
        matchedPaths
      } as never);
      if (outcome) {
        return outcome;
      }
    } catch {
      // Fall through to the not-found chain below.
    }
    return this.notFoundOutcome(pathname, matchedPaths);
  }

  /** Builds the path for a named route, e.g. `urlForName('environment', {id})`. */
  urlForName(name: string, params?: RouteParams): string {
    return this.urlBuilder(name, params);
  }

  private notFoundOutcome(
    pathname: string,
    matchedPaths: Map<AppRoute, string>
  ): RouteResolution {
    return {
      chain: this.notFound ? chainFor(this.notFound, matchedPaths) : [],
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
