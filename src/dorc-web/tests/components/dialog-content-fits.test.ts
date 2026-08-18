import { expect, fixture, html, settle } from '../_helpers';
import '@vaadin/confirm-dialog';
import '@vaadin/dialog';
import '../../src/components/hegs-json-viewer';
import { confirmPrompt } from '../../src/components/confirm-prompt';

// The suite had NO geometry assertions at all — a grep for scrollWidth,
// clientWidth, offsetWidth or getBoundingClientRect across tests/ returned
// zero. Every dialog test asked whether data arrived and whether buttons
// worked; none asked whether the result fits. That is how the deploy
// confirmation shipped with its build text clipped and unreachable.
//
// Mechanism: content used to live in <hegs-dialog>, whose box was
// `position: absolute` with no width or max-width, so it grew to fit. The
// Vaadin overlays that replaced it constrain the width, and their content part
// is `overflow-x: visible` — so anything wider is simply cut off, with no
// scrollbar and no scrollable ancestor to reach it with.
//
// A string of repeated characters, deliberately: it has no spaces, hyphens or
// underscores, so it cannot wrap. Real build numbers and artefact paths do
// contain break opportunities, which is why this only bit on the longest ones
// and why a realistic-looking fixture would have passed.

const UNBREAKABLE = 'A'.repeat(120);

// The invariant is REACHABILITY, not fitting. Content wider than its box is
// fine as long as something can scroll to it — which is what `overflow: auto`
// buys, and precisely what the deleted `paper-dialog.size-position { overflow:
// auto }` rules used to provide. An earlier draft of this test asserted
// `scrollWidth <= clientWidth`, which is a different and wrong contract: a
// scrollable element keeps a larger scrollWidth by definition, so the fix
// would never have satisfied it.
const scrolls = (el: Element) => {
  const x = getComputedStyle(el).overflowX;
  return x === 'auto' || x === 'scroll';
};

/**
 * Elements whose content overflows horizontally with nothing able to scroll to
 * it. An overflowing node inside a scrollable ancestor is fine — the ancestor
 * is what the user scrolls — so the walk carries that down rather than
 * flagging every inner box, which is what the JSON tree's own ul/li do.
 */
const unreachable = (el: Element): Element[] => {
  const bad: Element[] = [];
  const walk = (node: Element, ancestorScrolls: boolean) => {
    const reachable = ancestorScrolls || scrolls(node);
    if (node.scrollWidth > node.clientWidth && !reachable) bad.push(node);
    const roots: (Element | ShadowRoot)[] = [node];
    const sr = (node as HTMLElement).shadowRoot;
    if (sr) roots.push(sr);
    for (const root of roots) {
      for (const child of Array.from(root.children)) walk(child, reachable);
    }
  };
  walk(el, false);
  return bad;
};

describe('dialog content stays inside the dialog', () => {
  it('the deploy confirmation does not clip a long build number', async () => {
    const host = (await fixture(html`
      <vaadin-confirm-dialog
        header="New deployment"
        confirm-text="Deploy"
        cancel-button-visible
        opened
      >
        Please confirm you want to submit this deployment request?
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
      </vaadin-confirm-dialog>
    `)) as HTMLElement;
    await settle(300);

    const viewer = host.querySelector('#jsonviewer') as HTMLElement & {
      data: Record<string, unknown>;
      expand(path: string): void;
    };
    // Exactly what deploy-env does with the request DTO.
    Object.assign(viewer.data, {
      Project: 'Endur',
      EnvironmentName: 'Endur DV 18',
      Components: [{ ComponentName: 'Core', BuildNumber: UNBREAKABLE }]
    });
    viewer.expand('**');
    await settle(300);

    expect(viewer.clientWidth, 'the viewer was laid out').to.be.greaterThan(0);
    expect(
      viewer.scrollWidth,
      'the fixture is only meaningful if the content really is too wide'
    ).to.be.greaterThan(viewer.clientWidth);
    expect(
      scrolls(viewer),
      'the viewer scrolls, so the clipped build number can be reached'
    ).to.equal(true);
  });

  it('a plain dialog does not clip it either', async () => {
    // page-project-bundles and page-scripts-list put the same viewer in a
    // <vaadin-dialog>, which constrains width the same way.
    const host = (await fixture(html`
      <vaadin-dialog opened></vaadin-dialog>
    `)) as HTMLElement & { renderer: unknown };
    const viewer = document.createElement('hegs-json-viewer') as HTMLElement & {
      data: Record<string, unknown>;
      expand(path: string): void;
    };
    viewer.textContent = '{}';
    host.appendChild(viewer);
    await settle(300);

    Object.assign(viewer.data, { Path: UNBREAKABLE });
    viewer.expand('**');
    await settle(300);

    expect(
      unreachable(viewer).map(e => e.tagName.toLowerCase()),
      'nothing overflows without something able to scroll to it'
    ).to.deep.equal([]);

    viewer.remove();
  });

  it('confirmPrompt does not clip a long server name', async () => {
    // The measured difference that scopes this whole class of bug:
    // vaadin-dialog's overlay [part="content"] is `overflow-x: auto`, so the
    // twelve paper-dialog conversions are covered by the framework.
    // vaadin-confirm-dialog's is `visible` — nothing scrolls — so its content
    // must wrap instead. Every confirmPrompt message interpolates an
    // identifier the user did not choose: `Delete server ${name}?` gets an
    // FQDN, `Remove Access from ${accessControl?.Name}?` a domain-qualified
    // group. Neither dots nor backslashes are break opportunities in CSS, so
    // those do not wrap on their own.
    const fqdn = 'uk.lon.dv18.endur.application.server.node.internal.example';
    const answer = confirmPrompt(`Delete server ${fqdn}?`);
    await settle(300);

    const dialog = document.body.querySelector(
      'vaadin-confirm-dialog'
    ) as HTMLElement;
    expect(dialog, 'the prompt opened').to.not.equal(null);
    const text = dialog.querySelector('div') as HTMLElement;

    expect(
      unreachable(text),
      'the message wraps rather than running past the edge of a dialog ' +
        'that cannot scroll'
    ).to.deep.equal([]);

    // Settle the promise so the dialog tears itself down.
    (
      dialog.shadowRoot?.querySelector(
        'vaadin-confirm-dialog-overlay'
      ) as HTMLElement | null
    )?.dispatchEvent(
      new CustomEvent('cancel', { bubbles: true, composed: true })
    );
    dialog.remove();
    void answer;
  });
});
