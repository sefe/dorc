/**
 * Application router.
 *
 * Owns the browser-history integration: it resolves the current URL through
 * {@link RouteResolver}, renders the result through {@link RouteOutlet}, and
 * intercepts in-app link clicks so they navigate without a full page load.
 */

import './style-registrations';
import { RouteResolver, isRedirect } from './route-resolution';
import { RouteOutlet } from './route-outlet';
import type { AppRoute, RouteParams, RouterLocation } from './route-config';

// NB: this module must not import `./routes`. The route table imports every
// routed component, and those components import this module for `urlForName`
// and `navigate`. Importing it here would close that cycle and leave `routes`
// in its temporal dead zone while this module initialises. The route table is
// injected via `setRoutes` instead, as it was under Vaadin Router.

/** Fired on `window` after every completed navigation. */
export const LOCATION_CHANGED_EVENT = 'dorc-router-location-changed';

/** Guards against a redirect loop in a misconfigured route table. */
const MAX_REDIRECTS = 5;

const EMPTY_LOCATION: RouterLocation = {
  baseUrl: '',
  hash: '',
  params: {},
  pathname: '',
  route: null,
  routes: [],
  search: '',
  searchParams: new URLSearchParams()
};

export class AppRouter {
  private resolver: RouteResolver | undefined;

  private outlet: RouteOutlet | undefined;

  private listening = false;

  /** The location of the most recently completed navigation. */
  location: RouterLocation = EMPTY_LOCATION;

  constructor(private readonly outletElement: HTMLElement | null) {}

  /**
   * Installs the route table, renders the current URL, and starts listening for
   * link clicks and history changes.
   */
  async setRoutes(routeTable: AppRoute[]): Promise<void> {
    this.resolver = new RouteResolver(routeTable);

    // Reuse the outlet across calls. A fresh RouteOutlet starts with an empty
    // `rendered` list, so it would append a second chain alongside the first
    // rather than replacing it — Vaadin Router's setRoutes() replaced the
    // outlet's content. Reusing it lets the normal divergence check do that:
    // a new route table shares no route identities, so the whole chain
    // diverges and the old root is detached.
    if (this.outletElement && !this.outlet) {
      this.outlet = new RouteOutlet(this.outletElement);
    }

    if (!this.listening) {
      this.listening = true;
      window.addEventListener('popstate', this.onPopState);
      document.addEventListener('click', this.onClick);
    }

    await this.renderCurrentUrl();
  }

  /**
   * Navigates to `path`, pushing (or replacing) a history entry.
   *
   * `path` may be absolute (`/projects`) or relative to the document root
   * (`project-envs/Foo`); both forms occur in the existing call sites.
   */
  async navigate(path: string, options?: { replace?: boolean }): Promise<void> {
    const url = new URL(path, window.location.origin);
    const target = url.pathname + url.search + url.hash;

    // Only touch history when the URL actually changes, matching Vaadin Router
    // (`if (window.location.pathname !== pathname || ...)`). Without this,
    // clicking the already-selected nav item stacks an identical entry and Back
    // appears to do nothing until they are all popped.
    const unchanged =
      window.location.pathname === url.pathname &&
      window.location.search === url.search &&
      window.location.hash === url.hash;

    if (!unchanged) {
      if (options?.replace) {
        window.history.replaceState(null, '', target);
      } else {
        window.history.pushState(null, '', target);
      }
    }

    await this.renderCurrentUrl();
  }

  /** Builds the path for a named route, e.g. `urlForName('environment', {id})`. */
  urlForName(name: string, params?: RouteParams): string {
    if (!this.resolver) {
      // Matches the error Vaadin Router raised when a link rendered before the
      // route table was installed.
      throw new Error(`Route "${name}" not found`);
    }
    return this.resolver.urlForName(name, params);
  }

  private readonly onPopState = (): void => {
    void this.renderCurrentUrl();
  };

  private readonly onClick = (event: MouseEvent): void => {
    const anchor = this.routableAnchor(event);
    if (!anchor) {
      return;
    }
    event.preventDefault();
    void this.navigate(anchor.href);
  };

  /**
   * Returns the anchor a click should be routed through, or undefined if the
   * browser should handle it. Uses the composed path so links inside shadow
   * roots — which is where most of this app's navigation lives — are seen.
   */
  private routableAnchor(event: MouseEvent): HTMLAnchorElement | undefined {
    if (
      event.defaultPrevented ||
      event.button !== 0 ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey
    ) {
      return undefined;
    }

    const anchor = event
      .composedPath()
      .find(
        (node): node is HTMLAnchorElement =>
          node instanceof HTMLAnchorElement && Boolean(node.getAttribute('href'))
      );

    if (
      !anchor ||
      anchor.target ||
      anchor.hasAttribute('download') ||
      anchor.getAttribute('rel') === 'external'
    ) {
      return undefined;
    }

    if (new URL(anchor.href).origin !== window.location.origin) {
      return undefined;
    }

    return anchor;
  }

  private async renderCurrentUrl(): Promise<void> {
    const { outlet, resolver } = this;
    if (!outlet || !resolver) {
      return;
    }

    let pathname = window.location.pathname;
    let redirectFrom: string | undefined;

    for (let hop = 0; hop <= MAX_REDIRECTS; hop++) {
      const outcome = await resolver.resolve(pathname);

      if (!isRedirect(outcome)) {
        this.location = {
          baseUrl: '',
          hash: window.location.hash,
          params: outcome.params,
          pathname: outcome.pathname,
          redirectFrom,
          route: outcome.chain.at(-1)?.route ?? null,
          routes: outcome.chain.map(entry => entry.route),
          search: window.location.search,
          searchParams: new URLSearchParams(window.location.search)
        };

        outlet.render({
          chain: outcome.chain,
          params: outcome.params,
          pathname: outcome.pathname,
          search: window.location.search,
          hash: window.location.hash,
          redirectFrom
        });

        window.dispatchEvent(
          new CustomEvent(LOCATION_CHANGED_EVENT, {
            detail: { location: this.location }
          })
        );
        return;
      }

      redirectFrom = pathname;
      pathname = outcome.redirect;
      window.history.replaceState(
        null,
        '',
        pathname + window.location.search + window.location.hash
      );
    }

    console.error(
      `Router: too many redirects while resolving "${window.location.pathname}"`
    );
  }
}

export const router = new AppRouter(
  document.querySelector<HTMLElement>('#outlet')
);

export const urlForName = (name: string, params?: RouteParams): string =>
  router.urlForName(name, params);

/** Imperative navigation. Replaces `Router.go(...)` from the previous router. */
export const navigate = (path: string): Promise<void> => router.navigate(path);
