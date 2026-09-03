import { of } from 'rxjs';
import { vi } from 'vitest';
import { expect, fixture, html } from '../_helpers';

const { projectsGetSpy } = vi.hoisted(() => ({
  projectsGetSpy: vi.fn((request: { projectName: string }) =>
    of({
      ArtefactsUrl: `https://dev.azure.com/org/${request.projectName}/_git/repo`,
      ArtefactsSubPaths: request.projectName
    })
  )
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => {
  const actual = (await importOriginal()) as Record<string, unknown>;
  return {
    ...actual,
    RefDataProjectsApi: class {
      refDataProjectsProjectNameGet = projectsGetSpy;
    },
    RefDataEnvironmentsApi: class {
      refDataEnvironmentsGet = vi.fn(() => of([]));
    }
  };
});

await import('../../src/components/request-status-card');

type StatusCard = HTMLElement & {
  deployRequest: unknown;
  updateComplete: Promise<unknown>;
};

const controls = (el: StatusCard) =>
  el.shadowRoot!.querySelector('request-controls') as HTMLElement & {
    requestId: number;
    cancelable: boolean;
    canRestart: boolean;
    canPause: boolean;
    canResume: boolean;
  };

const viewLogButton = (el: StatusCard) =>
  el.shadowRoot!.querySelector('vaadin-button[aria-label="View Log"]');

describe('request-status-card with an undefined deployRequest', () => {
  beforeEach(() => {
    projectsGetSpy.mockClear();
  });

  it('renders without throwing before the request has loaded', async () => {
    const el = await fixture<StatusCard>(
      html`<request-status-card></request-status-card>`
    );

    expect(el.shadowRoot!.querySelector('request-controls')).to.not.be.null;
    expect(controls(el).requestId).to.equal(0);
    expect(controls(el).cancelable).to.be.false;
    expect(controls(el).canPause).to.be.false;
    expect(controls(el).canResume).to.be.false;
    expect(viewLogButton(el)).to.be.null;
  });

  it('does not look up the build link without a project', async () => {
    await fixture<StatusCard>(
      html`<request-status-card></request-status-card>`
    );

    expect(projectsGetSpy.mock.calls).to.have.length(0);
  });

  it('renders the request once it arrives', async () => {
    const el = await fixture<StatusCard>(
      html`<request-status-card></request-status-card>`
    );

    el.deployRequest = {
      Id: 42,
      Project: 'Bermuda',
      Status: 'Running',
      UserEditable: true,
      Log: 'boom'
    };
    await el.updateComplete;

    expect(controls(el).requestId).to.equal(42);
    expect(controls(el).cancelable).to.be.true;
    expect(viewLogButton(el)).to.not.be.null;
    expect(projectsGetSpy.mock.calls).to.have.length(1);
    expect(projectsGetSpy.mock.calls[0][0]).to.deep.equal({
      projectName: 'Bermuda'
    });
  });

  it('does not refetch the build link while the project is unchanged', async () => {
    const el = await fixture<StatusCard>(
      html`<request-status-card></request-status-card>`
    );

    el.deployRequest = { Id: 42, Project: 'Bermuda', Status: 'Running' };
    await el.updateComplete;
    el.deployRequest = { Id: 42, Project: 'Bermuda', Status: 'Complete' };
    await el.updateComplete;

    expect(projectsGetSpy.mock.calls).to.have.length(1);
  });
});
