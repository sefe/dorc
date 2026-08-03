import { expect } from '../_helpers';
import { RouteResolver, isRedirect } from '../../src/router/route-resolution';
import type { AppRoute, RouteResolution } from '../../src/router/route-config';

// A miniature of the real route table: a layout root, flat routes, a named
// route with a parameter, a three-level nest, an action redirect, and the
// catch-all. Using a synthetic table keeps these tests focused on resolution
// behaviour rather than dragging in every routed component.
const buildRoutes = (): AppRoute[] => [
  {
    path: '',
    component: 'dorc-app',
    children: [
      {
        path: '/deploy',
        name: 'deploy',
        component: 'page-deploy',
        metadata: { title: 'Deploy', description: 'Deploy things' }
      },
      {
        path: '/about',
        name: 'about',
        action: () => ({ redirect: '/analytics' })
      },
      {
        path: '/analytics',
        name: 'analytics',
        component: 'page-analytics'
      },
      {
        path: '/project-envs/:id',
        name: 'project-envs',
        component: 'page-project-envs'
      },
      {
        path: '/project-envs/:id/bundles',
        name: 'project-bundles',
        component: 'page-project-bundles'
      },
      {
        path: '/environment/:id',
        name: 'environment',
        component: 'page-environment',
        children: [
          {
            path: '/components',
            component: 'page-environment-components',
            children: [
              {
                path: '',
                action: context => ({
                  redirect: `${context.pathname.replace(/\/+$/, '')}/servers`
                })
              },
              { path: '/servers', component: 'env-servers' },
              { path: '/databases', component: 'env-databases' }
            ]
          }
        ]
      },
      { path: '/', name: 'default', component: 'page-deploy' },
      { path: '/*notFound', name: 'not-found', component: 'page-not-found' }
    ]
  }
];

const componentsOf = (resolution: RouteResolution) =>
  resolution.chain.map(entry => entry.component);

const resolveChain = async (resolver: RouteResolver, path: string) => {
  const outcome = await resolver.resolve(path);
  if (isRedirect(outcome)) {
    throw new Error(`Expected a chain for "${path}", got a redirect`);
  }
  return outcome;
};

describe('RouteResolver', () => {
  let resolver: RouteResolver;

  beforeEach(() => {
    resolver = new RouteResolver(buildRoutes());
  });

  it('nests a top-level route inside the layout component', async () => {
    const resolution = await resolveChain(resolver, '/deploy');
    expect(componentsOf(resolution)).to.deep.equal(['dorc-app', 'page-deploy']);
  });

  it('resolves the root path to the default route', async () => {
    const resolution = await resolveChain(resolver, '/');
    expect(componentsOf(resolution)).to.deep.equal(['dorc-app', 'page-deploy']);
  });

  it('builds the full chain for a three-level nested route', async () => {
    const resolution = await resolveChain(
      resolver,
      '/environment/DEV1/components/servers'
    );
    expect(componentsOf(resolution)).to.deep.equal([
      'dorc-app',
      'page-environment',
      'page-environment-components',
      'env-servers'
    ]);
  });

  it('exposes ancestor route parameters at the leaf', async () => {
    const resolution = await resolveChain(
      resolver,
      '/environment/DEV1/components/servers'
    );
    expect(resolution.params.id).to.equal('DEV1');
  });

  it('decodes URL-encoded parameters', async () => {
    const resolution = await resolveChain(resolver, '/project-envs/My%20Project');
    expect(resolution.params.id).to.equal('My Project');
  });

  it('prefers the longer path when two routes share a prefix', async () => {
    const bundles = await resolveChain(resolver, '/project-envs/Foo/bundles');
    expect(componentsOf(bundles)).to.deep.equal([
      'dorc-app',
      'page-project-bundles'
    ]);
  });

  it('returns a redirect for an action route', async () => {
    const outcome = await resolver.resolve('/about');
    expect(isRedirect(outcome)).to.equal(true);
    if (isRedirect(outcome)) {
      expect(outcome.redirect).to.equal('/analytics');
    }
  });

  it('redirects a nested index route to its default child', async () => {
    const outcome = await resolver.resolve('/environment/DEV1/components');
    expect(isRedirect(outcome)).to.equal(true);
    if (isRedirect(outcome)) {
      expect(outcome.redirect).to.equal('/environment/DEV1/components/servers');
    }
  });

  it('does not double the slash when the index path has a trailing slash', async () => {
    const outcome = await resolver.resolve('/environment/DEV1/components/');
    expect(isRedirect(outcome)).to.equal(true);
    if (isRedirect(outcome)) {
      expect(outcome.redirect).to.equal('/environment/DEV1/components/servers');
    }
  });

  it('falls back to the not-found route for an unmatched path', async () => {
    const resolution = await resolveChain(resolver, '/no/such/page');
    expect(componentsOf(resolution)).to.deep.equal([
      'dorc-app',
      'page-not-found'
    ]);
  });

  it('keeps the layout in the chain when falling back to not-found', async () => {
    // Regression guard: the not-found chain is built from `parent` links, which
    // universal-router only populates as it traverses. A fallback taken before
    // anything else matched must still be nested under the layout.
    const fresh = new RouteResolver(buildRoutes());
    const resolution = await resolveChain(fresh, '/first/request/is/unmatched');
    expect(componentsOf(resolution)).to.deep.equal([
      'dorc-app',
      'page-not-found'
    ]);
  });

  it('carries route metadata on the leaf route', async () => {
    const resolution = await resolveChain(resolver, '/deploy');
    expect(resolution.chain.at(-1)?.route.metadata?.title).to.equal('Deploy');
  });

  describe('urlForName', () => {
    it('builds a path for a parameterless route', () => {
      expect(resolver.urlForName('deploy')).to.equal('/deploy');
    });

    it('substitutes route parameters', () => {
      expect(resolver.urlForName('environment', { id: 'DEV1' })).to.equal(
        '/environment/DEV1'
      );
    });

    it('encodes parameter values', () => {
      expect(resolver.urlForName('project-envs', { id: 'My Project' })).to.equal(
        '/project-envs/My%20Project'
      );
    });

    it('throws for an unknown route name', () => {
      expect(() => resolver.urlForName('nope')).to.throw(/nope/);
    });
  });

  // The outlet decides element reuse on the matched path as well as route
  // identity, so resolution has to report what each route actually matched.
  // Without it, `/project-envs/A` and `/project-envs/B` are indistinguishable
  // and the page keeps showing project A.
  describe('matched paths', () => {
    it('differ when only a route parameter differs', async () => {
      const resolver = new RouteResolver(buildRoutes());

      const a = (await resolver.resolve('/project-envs/A')) as RouteResolution;
      const b = (await resolver.resolve('/project-envs/B')) as RouteResolution;

      expect(a.chain.at(-1)?.path).to.equal('/project-envs/A');
      expect(b.chain.at(-1)?.path).to.equal('/project-envs/B');
    });

    it('stay stable on an ancestor when only the child route changes', async () => {
      const resolver = new RouteResolver(buildRoutes());

      // Paths the fixture table actually matches — `/environment/:id` only has
      // a `/components` child, so `/metadata` fell through to not-found and
      // both assertions compared undefined with undefined.
      const metadata = (await resolver.resolve(
        '/environment/DEV1/components/servers'
      )) as RouteResolution;
      const servers = (await resolver.resolve(
        '/environment/DEV1/components/databases'
      )) as RouteResolution;

      const parentOf = (r: RouteResolution) =>
        r.chain.find(e => e.component === 'page-environment')?.path;

      expect(parentOf(metadata), 'ancestor actually resolved').to.equal(
        '/environment/DEV1'
      );
      expect(parentOf(metadata), 'parent matched path').to.equal(
        parentOf(servers)
      );
      expect(
        metadata.chain.at(-1)?.path,
        'leaf matched path differs'
      ).to.not.equal(servers.chain.at(-1)?.path);
    });

    // The matched paths are collected during traversal. Holding them on the
    // resolver instead of per resolve meant two overlapping resolutions wrote
    // into the same map and read each other's results — a popstate landing
    // while a click-driven navigation is still in flight is enough to do it.
    it('does not cross-contaminate between overlapping resolves', async () => {
      const resolver = new RouteResolver(buildRoutes());

      // A nested path, so the traversal has enough await points for the two
      // resolutions to interleave — a flat route finishes in one tick and
      // would pass even with the resolver-level map.
      const [first, second] = (await Promise.all([
        resolver.resolve('/environment/AAA/components/servers'),
        resolver.resolve('/environment/BBB/components/servers')
      ])) as RouteResolution[];

      const environmentPath = (r: RouteResolution) =>
        r.chain.find(e => e.component === 'page-environment')?.path;

      expect(environmentPath(first)).to.equal('/environment/AAA');
      expect(environmentPath(second)).to.equal('/environment/BBB');
    });
  });
});
