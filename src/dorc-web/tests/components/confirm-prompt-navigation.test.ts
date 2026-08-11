import { expect } from '../_helpers';
import { AppRouter } from '../../src/router/router';
import type { AppRoute } from '../../src/router/route-config';
import { confirmPrompt } from '../../src/components/confirm-prompt';

// `confirmPrompt` appends its dialog to `document.body`, not to the component
// that raised it, so a navigation does not take it away with the page. That
// matters more than a stray dialog: the confirm dialog is modal, and
// `_enterModalState()` sets `document.body.style.pointerEvents = 'none'`
// (vaadin-overlay-stack-mixin.js). Only `_exitModalState()` clears it, and that
// runs when `opened` goes false — which nothing was doing.
//
// The 31 page-level `<vaadin-dialog>`s are not affected: DialogBaseMixin's
// disconnectedCallback schedules `opened = false` when the host is detached, so
// the router removing the page does exit modal state. The body-appended prompt
// is the one thing a route change cannot reach.
//
// Failure: on /servers a user clicks Delete, then presses browser Back rather
// than answering — the one escape route modality leaves open, since every
// in-page click is dead. The next page renders underneath a stale confirmation
// with the whole document still un-clickable.

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

const settle = async () => {
  for (let i = 0; i < 4; i++) {
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }
};

describe('confirmPrompt across a navigation', () => {
  let outlet: HTMLElement;
  let router: AppRouter;
  let startPath: string;

  beforeEach(async () => {
    startPath = window.location.pathname;
    outlet = document.createElement('div');
    document.body.appendChild(outlet);
    router = new AppRouter(outlet);
    await router.setRoutes(routes);
    await router.navigate('/first');
  });

  afterEach(() => {
    window.history.replaceState(null, '', startPath);
    outlet.remove();
    // Clean up after the defect itself: a leaked dialog would otherwise be the
    // one the next test finds.
    document.body
      .querySelectorAll('vaadin-confirm-dialog')
      .forEach(d => d.remove());
    document.body.style.pointerEvents = '';
  });

  it('leaves the page interactive after navigating away', async () => {
    const answer = confirmPrompt('Delete server FOO?');
    await settle();
    expect(
      document.body.style.pointerEvents,
      'modal while the prompt is up'
    ).to.equal('none');

    await router.navigate('/second');
    await settle();

    expect(
      document.body.style.pointerEvents,
      'page is interactive again'
    ).to.equal('');
    expect(
      document.body.querySelector('vaadin-confirm-dialog'),
      'no dialog left behind'
    ).to.equal(null);

    // Navigating away declines, so no caller is left awaiting forever.
    expect(await answer, 'resolves as a decline').to.equal(false);
  });

  it('still resolves normally when no navigation happens', async () => {
    const answer = confirmPrompt('Proceed?');
    await settle();

    const dialogs = document.body.querySelectorAll('vaadin-confirm-dialog');
    const dialog = dialogs[dialogs.length - 1] as HTMLElement & {
      opened: boolean;
    };
    expect(dialog, 'dialog is up').to.not.equal(null);

    // Cancel through the overlay's own path.
    dialog.dispatchEvent(new CustomEvent('cancel'));

    expect(await answer).to.equal(false);
  });
});
