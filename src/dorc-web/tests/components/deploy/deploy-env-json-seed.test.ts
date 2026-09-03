import { expect, settle } from '../../_helpers';
import '../../../src/components/deploy/deploy-env';

// `checkDeployment` seeds the confirmation dialog with
// `Object.assign(jsonViewer.data, this.req.requestDto)`. That needs `data` to
// already be an object, and it is only an object because the template writes
// `<hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>` — the viewer parses
// its text content in `connectedCallback`, and empty text yields `undefined`.
//
// So deleting two characters from the template turns the Deploy button into a
// TypeError. deploy-confirm-seeding.test.ts describes exactly that failure but
// asserts it against a hand-built fixture, so the shipped element is not
// covered by it. This mounts the real component.

type Host = HTMLElement & { updateComplete: Promise<unknown> };

const viewerIn = (el: Host) =>
  el.shadowRoot?.getElementById('jsonviewer') as
    (HTMLElement & { data?: unknown }) | null;

describe('deploy-env confirmation seeding', () => {
  it('gives the json viewer an object to merge into', async () => {
    const el = document.createElement('deploy-env') as Host;
    document.body.appendChild(el);
    await settle();
    await settle();

    const viewer = viewerIn(el);
    expect(viewer, 'the viewer is in the template').to.not.equal(null);
    expect(
      typeof viewer?.data,
      'data is an object, so Object.assign into it works'
    ).to.equal('object');
    expect(viewer?.data, 'and it is not null').to.not.equal(null);

    // The operation checkDeployment performs. Throwing here is the failure the
    // seeded `{}` exists to prevent.
    expect(() =>
      Object.assign(viewer?.data as object, { Id: 1 })
    ).to.not.throw();

    el.remove();
  });
});
