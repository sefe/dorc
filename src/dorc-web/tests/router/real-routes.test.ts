import { expect } from '../_helpers';
import { RouteResolver, isRedirect } from '../../src/router/route-resolution';
import { routes } from '../../src/router/routes';

// The other router tests build miniature hand-written tables, which is what let
// a whole URL shape regress unnoticed: a route carrying both `component` and
// `children` — `/environment/:id` is one — could never terminate resolution, so
// its bare URL fell through every child and landed on not-found. The sidebar's
// per-environment tab links to exactly that URL.
//
// So this resolves the table the app actually ships. It is table-driven on
// purpose: adding a route means adding a line here, and the cost is one import.

const resolver = new RouteResolver(routes);

/** The component chain a pathname resolves to, or 'REDIRECT:<to>'. */
const resolve = async (pathname: string): Promise<string> => {
  const outcome = await resolver.resolve(pathname);
  if (isRedirect(outcome)) return `REDIRECT:${outcome.redirect}`;
  return outcome.chain.map(entry => entry.component).join(' > ');
};

const cases: [string, string][] = [
  ['/', 'dorc-app > page-deploy'],
  ['/projects', 'dorc-app > page-projects-list'],
  ['/projects/audit', 'dorc-app > page-projects-audit'],
  ['/environments', 'dorc-app > page-environments-list'],
  ['/servers', 'dorc-app > page-servers-list'],
  ['/variables', 'dorc-app > page-variables'],
  ['/variables/audit', 'dorc-app > page-variables-audit'],

  // The regression. A layout route with children still terminates when the URL
  // stops at it — this is what @vaadin/router did.
  ['/environment/DEV1', 'dorc-app > page-environment'],

  // …but only when it consumed the whole path. A bogus tab is still not-found,
  // rather than being swallowed by the parent.
  ['/environment/DEV1/nosuchtab', 'dorc-app > page-not-found'],

  ['/definitely/not/a/route', 'dorc-app > page-not-found']
];

describe('the shipped route table', () => {
  for (const [pathname, expected] of cases) {
    it(`resolves ${pathname}`, async () => {
      expect(await resolve(pathname)).to.equal(expected);
    });
  }

  it('resolves every environment tab', async () => {
    // Each tab is a child of the layout route, so all of them exercise the
    // path the parent must NOT swallow.
    const environment = routes
      .flatMap(route => route.children ?? [])
      .find(route => route.name === 'environment');
    const tabs = (environment?.children ?? [])
      .map(child => child.path)
      .filter((path): path is string => Boolean(path) && !path.includes('*'));

    expect(tabs.length, 'the layout has tabs to check').to.be.greaterThan(1);

    for (const tab of tabs) {
      const chain = await resolve(`/environment/DEV1${tab}`);
      // The Components tab is an index redirect rather than a component.
      if (chain.startsWith('REDIRECT:')) continue;
      expect(chain, `${tab} resolves through the layout`).to.contain(
        'dorc-app > page-environment'
      );
      expect(chain, `${tab} is not the not-found page`).to.not.contain(
        'page-not-found'
      );
    }
  });
});
