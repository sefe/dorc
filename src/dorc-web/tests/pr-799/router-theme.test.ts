import { expect, settle } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';
import { themeManager } from '../../src/theme/theme-manager';
import '@vaadin/dialog';
import '@vaadin/vaadin-lumo-styles/lumo.css';

const routes: AppRoute[] = [
  {
    path: '',
    component: 'div',
    children: [
      { path: '/first', component: 'div' },
      { path: '/second', component: 'div' },
      { path: '/*notFound', component: 'div' }
    ]
  }
];

describe('PR #799 router hash navigation', () => {
  let outlet: HTMLElement;
  let router: AppRouter;
  let startUrl: string;

  beforeEach(async () => {
    startUrl =
      window.location.pathname + window.location.search + window.location.hash;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
    router = new AppRouter(outlet);
    await router.setRoutes(routes);
  });

  afterEach(() => {
    router.disconnect();
    window.history.replaceState(null, '', startUrl);
    outlet.remove();
  });

  it('keeps hash changes on the same route', async () => {
    await router.navigate('/first?tab=activity#request-41');

    await router.navigate('/first?tab=activity#request-42');

    expect(window.location.hash).to.equal('#request-42');
    expect(router.location.hash).to.equal('#request-42');
    expect(router.location.searchParams.get('tab')).to.equal('activity');
  });
});

describe('PR #799 theme overlays', () => {
  let savedTheme: string | null;
  let savedAttribute: string | null;
  let dialog: HTMLElement;

  beforeEach(async () => {
    savedTheme = localStorage.getItem('dorc-theme');
    savedAttribute = document.documentElement.getAttribute('theme');
    dialog = document.createElement('vaadin-dialog');
    dialog.textContent = 'Theme inheritance check';
    document.body.appendChild(dialog);
    (dialog as HTMLElement & { opened: boolean }).opened = true;
    await settle();
  });

  afterEach(() => {
    (dialog as HTMLElement & { opened: boolean }).opened = false;
    dialog.remove();
    if (savedTheme === null) localStorage.removeItem('dorc-theme');
    else localStorage.setItem('dorc-theme', savedTheme);
    if (savedAttribute === null)
      document.documentElement.removeAttribute('theme');
    else document.documentElement.setAttribute('theme', savedAttribute);
    themeManager.apply(
      savedAttribute === 'dark' || savedTheme === 'dark' ? 'dark' : 'light'
    );
  });

  it('updates the root state and the dialog overlay through Lumo inheritance', async () => {
    const overlay = dialog.shadowRoot?.querySelector(
      'vaadin-dialog-overlay'
    ) as HTMLElement | null;
    expect(overlay, 'open dialog has an overlay').to.not.equal(null);
    const surface = overlay?.shadowRoot?.querySelector(
      '[part="overlay"]'
    ) as HTMLElement | null;
    expect(surface, 'dialog overlay has a rendered surface').to.not.equal(null);

    themeManager.apply('light');
    await settle();
    const lightBase = getComputedStyle(surface!).backgroundColor;

    themeManager.apply('dark');
    await settle();
    const darkBase = getComputedStyle(surface!).backgroundColor;

    expect(document.documentElement.getAttribute('theme')).to.equal('dark');
    expect(lightBase, 'light overlay has a visible Lumo surface').to.not.equal(
      'rgba(0, 0, 0, 0)'
    );
    expect(
      darkBase,
      'dark mode changes the inherited Lumo surface'
    ).to.not.equal(lightBase);
  });
});
