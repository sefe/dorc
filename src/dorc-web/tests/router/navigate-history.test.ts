import { expect } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';

// Vaadin Router only pushed a history entry when the URL actually changed
// (dist/router.js: `if (window.location.pathname !== pathname || ...)`).
// Without that guard, clicking the already-selected nav item stacks an
// identical entry and Back appears to do nothing until they are all popped.

const routes: AppRoute[] = [
  {
    path: '',
    component: 'div',
    children: [
      { path: '/first', component: 'div' },
      { path: '/second', component: 'div' },
      { path: '/*notFound', name: 'not-found', component: 'div' }
    ]
  }
];

describe('navigate history', () => {
  let outlet: HTMLElement;
  let router: AppRouter;
  let startLength: number;
  let startPath: string;

  beforeEach(() => {
    startPath = window.location.pathname;
    startLength = window.history.length;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
    router = new AppRouter(outlet);
    router.setRoutes(routes);
  });

  afterEach(() => {
    window.history.replaceState(null, '', startPath);
    outlet.remove();
  });

  it('does not stack an entry when the URL is unchanged', async () => {
    await router.navigate('/first');
    const afterFirst = window.history.length;

    await router.navigate('/first');
    await router.navigate('/first');

    expect(window.history.length, 'no entries added').to.equal(afterFirst);
  });

  it('still pushes when the URL changes', async () => {
    await router.navigate('/first');
    const afterFirst = window.history.length;

    await router.navigate('/second');

    expect(window.history.length, 'one entry added').to.equal(afterFirst + 1);
    expect(window.location.pathname).to.equal('/second');
    expect(startLength).to.be.a('number');
  });
});
