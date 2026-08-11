import { expect, fixture, html, settle } from '../_helpers';
import '../../src/components/add-edit-project';

// The Source Control Type combo has no value binding. Its value came from a
// one-shot in `updated()` that queried `#proj-source-control` out of the shadow
// root and set `.value` the first time the element appeared.
//
// That worked while the form was slotted light DOM inside `<hegs-dialog>`,
// always mounted. Under `dialogRenderer` the form does not exist until the
// overlay opens, which happens in the dialog's own later update cycle — and the
// call site sets `.project` and calls `open()` in the same tick, so the host's
// `updated()` ran while the combo was still absent. Nothing scheduled another
// host update, so the field stayed blank.
//
// A blank field does not read as "not loaded", it reads as "not set" — beside
// URL labels that still say "GitHub API URL". A user who corrects the apparent
// omission silently rewrites SourceControlType on save.

type Project = {
  ProjectId?: number;
  ProjectName?: string;
  SourceControlType?: string;
  ArtefactsUrl?: string;
};

type Host = HTMLElement & {
  project: Project;
  open(): void;
};

const comboIn = (el: Host) =>
  el.shadowRoot?.getElementById('proj-source-control') as
    | (HTMLElement & { value: string })
    | null;

const openWith = async (el: Host, project: Project) => {
  // The call site's ordering: assign, then open, in one tick.
  el.project = project;
  el.open();
  await settle();
  await settle();
};

describe('project metadata dialog', () => {
  it('shows the source control type on the first open', async () => {
    const el = (await fixture(
      html`<add-edit-project></add-edit-project>`
    )) as unknown as Host;

    await openWith(el, {
      ProjectId: 1,
      ProjectName: 'Widgets',
      SourceControlType: 'GitHub'
    });

    const combo = comboIn(el);
    expect(combo, 'the form rendered').to.not.equal(null);
    expect(combo?.value, 'source control type is populated').to.equal('GitHub');
  });

  it('follows the project when reopened for a different one', async () => {
    const el = (await fixture(
      html`<add-edit-project></add-edit-project>`
    )) as unknown as Host;

    await openWith(el, {
      ProjectId: 1,
      ProjectName: 'Widgets',
      SourceControlType: 'GitHub'
    });
    (el as unknown as { dialogOpened: boolean }).dialogOpened = false;
    await settle();

    await openWith(el, {
      ProjectId: 2,
      ProjectName: 'Gadgets',
      SourceControlType: 'FileShare'
    });

    expect(
      comboIn(el)?.value,
      'shows the second project, not the first'
    ).to.equal('FileShare');
  });

  it('defaults to Azure DevOps for a project with none set', async () => {
    const el = (await fixture(
      html`<add-edit-project></add-edit-project>`
    )) as unknown as Host;

    await openWith(el, { ProjectId: 3, ProjectName: 'New' });

    expect(comboIn(el)?.value).to.equal('AzureDevOps');
  });
});
