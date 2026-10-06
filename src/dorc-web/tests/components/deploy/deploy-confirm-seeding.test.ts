import { expect, fixture, html, settle } from '../../_helpers';
import '../../../src/components/hegs-json-viewer';
import '@vaadin/confirm-dialog';

// The deploy confirmation moved from <hegs-dialog> to <vaadin-confirm-dialog>,
// and the two hide their content differently: the old one used
// `visibility: hidden`, the new one `display: none`.
//
// `deploy-env.checkDeployment()` reaches into the closed dialog and does
// `Object.assign(jsonViewer.data, ...)`, and `hegs-json-viewer` seeds `.data`
// from `this.innerText` in connectedCallback. `innerText` is layout-dependent,
// so the swap looks like it could leave `.data` undefined and make that assign
// throw — which would break the Deploy button outright.
//
// It does not: the HTML spec has `innerText` fall back to `textContent` when an
// element is not being rendered. This pins that, because the failure mode is
// silent at build time and fatal at runtime.

describe('deploy confirmation JSON viewer seeding', () => {
  it('seeds .data from its text content while the dialog is closed', async () => {
    const dialog = (await fixture(html`
      <vaadin-confirm-dialog .opened="${false}">
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
      </vaadin-confirm-dialog>
    `)) as unknown as HTMLElement;
    await settle();

    const viewer = dialog.querySelector('#jsonviewer') as HTMLElement & {
      data?: unknown;
    };

    expect(viewer, 'viewer present').to.not.equal(null);
    expect(viewer.data, '.data seeded from "{}"').to.deep.equal({});
  });

  it('survives the Object.assign that checkDeployment performs', async () => {
    const dialog = (await fixture(html`
      <vaadin-confirm-dialog .opened="${false}">
        <hegs-json-viewer id="jsonviewer">{}</hegs-json-viewer>
      </vaadin-confirm-dialog>
    `)) as unknown as HTMLElement;
    await settle();

    const viewer = dialog.querySelector('#jsonviewer') as HTMLElement & {
      data?: Record<string, unknown>;
    };

    expect(() =>
      Object.assign(viewer.data as object, { Id: 1, EnvironmentName: 'dev' })
    ).to.not.throw();
    expect(viewer.data?.EnvironmentName).to.equal('dev');
  });
});
