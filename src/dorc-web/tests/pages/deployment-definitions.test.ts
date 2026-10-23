import { vi } from 'vitest';
import { of, Subject, throwError } from 'rxjs';
import { expect, fixture, html, settle } from '../_helpers';
import { ComponentType, TerraformSourceType } from '../../src/apis/dorc-api';
import type { TextField } from '@vaadin/text-field';
import type { Tabs } from '@vaadin/tabs';
import type { DeploymentDefinitionHistory } from '../../src/components/deployment-definition-history';
import type { DeployEnv } from '../../src/components/deploy/deploy-env';
import type { HegsTree } from '../../src/components/deploy/component-tree/hegs-tree';

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  save: vi.fn(),
  history: vi.fn(),
  definitions: vi.fn(),
  projects: vi.fn(),
  deleteProject: vi.fn(),
  confirm: vi.fn(),
  editor: { value: '', change: () => {} }
}));
vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: mocks.confirm
}));

vi.mock('ace-builds', () => ({
  edit: () => ({
    renderer: { attachToShadowRoot() {} },
    session: { setMode() {}, setUseWorker() {} },
    setTheme() {},
    setOptions() {},
    setReadOnly() {},
    resize() {},
    destroy() {},
    on: (_name: string, callback: () => void) => {
      mocks.editor.change = callback;
    },
    setValue: (value: string) => {
      mocks.editor.value = value;
      mocks.editor.change();
    },
    getValue: () => mocks.editor.value
  })
}));

vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  RefDataApi: class {
    refDataIdGet = mocks.get;
    refDataPut = mocks.save;
  },
  PowerShellVersionsApi: class {
    powerShellVersionsGet = () => of([{ Value: '5.1' }, { Value: '7.4' }]);
  },
  RefDataProjectsApi: class {
    refDataProjectsGet = mocks.projects;
    refDataProjectsProjectIdDelete = mocks.deleteProject;
  },
  RefDataProjectEnvironmentMappingsApi: class {
    refDataProjectEnvironmentMappingsGet = () =>
      of({ Items: [{ EnvironmentId: 2, EnvironmentName: 'UT02' }] });
  },
  PropertiesApi: class {
    propertiesGet = () => of([]);
  },
  RequestApi: class {
    requestBuildDefinitionsGet = () => of([]);
    requestComponentsGet = () =>
      of([
        { Id: 10, Name: 'Parent', ParentId: 0, NumOfChildren: 1 },
        { Id: 11, Name: 'Nested script', ParentId: 10, NumOfChildren: 0 }
      ]);
  },
  DeploymentDefinitionsApi: class {
    deploymentDefinitionsHistoryGet = mocks.history;
    deploymentDefinitionsGet = mocks.definitions;
  }
}));
vi.mock('../../src/apis/dorc-api/apis', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  RefDataProjectsApi: class {
    refDataProjectsGet = mocks.projects;
    refDataProjectsProjectIdDelete = mocks.deleteProject;
  }
}));

await import('../../src/pages/page-project-ref-data');
await import('../../src/pages/page-deployment-definitions');
await import('../../src/pages/page-deploy');
const { PageProjectsList } = await import('../../src/pages/page-projects-list');

const reference = () => ({
  Project: {
    ProjectId: 7,
    ProjectName: 'Payments',
    TerraformGitRepoUrl: 'https://example.invalid/project.git',
    LeanIXUrl: 'https://example.invalid/inventory'
  },
  Revision: 'revision-1',
  CanEdit: true,
  CanDeploy: true,
  Components: [
    {
      ComponentId: 10,
      ComponentName: 'Parent',
      ComponentType: ComponentType.PowerShell,
      PSVersion: '5.1',
      IsEnabled: true,
      ScriptPath: 'parent.ps1',
      Children: [
        {
          ComponentId: 11,
          ComponentName: 'Nested script',
          ComponentType: ComponentType.PowerShell,
          ParentId: 10,
          ScriptPath: '{"Path":"nested.ps1","Arguments":{"Scope":"#{Scope}"}}',
          PSVersion: '7.4',
          IsEnabled: true,
          NonProdOnly: true,
          Children: []
        }
      ]
    }
  ]
});

const findHegsTreeButtons = (root: ParentNode): HTMLButtonElement[] => {
  const buttons: HTMLButtonElement[] = [];
  const visit = (node: ParentNode) => {
    for (const btn of node.querySelectorAll<HTMLButtonElement>(
      '.node-select-btn'
    )) {
      buttons.push(btn);
    }
    for (const el of node.querySelectorAll<HTMLElement>(
      'hegs-tree, hegs-tree-node'
    )) {
      if (el.shadowRoot) {
        visit(el.shadowRoot);
      }
    }
  };
  visit(root);
  return buttons;
};

function clickButton(root: ParentNode, label: string) {
  const button = [
    ...root.querySelectorAll<HTMLElement>('vaadin-button, button')
  ].find(element => element.textContent?.trim() === label);
  expect(button, `button ${label}`).not.to.equal(undefined);
  button!.click();
}

beforeEach(() => {
  vi.clearAllMocks();
  history.replaceState(null, '', '/project-ref-data/7');
  mocks.get.mockReturnValue(of(reference()));
  mocks.save.mockImplementation(({ refDataApiModel }) =>
    of({ ...refDataApiModel, Revision: 'revision-2' })
  );
  mocks.history.mockReturnValue(of({ Items: [], TotalItems: 0 }));
  mocks.definitions.mockReturnValue(of({ Items: [], TotalItems: 0 }));
  mocks.projects.mockReturnValue(
    of([{ ProjectId: 7, ProjectName: 'Payments' }])
  );
  mocks.deleteProject.mockReturnValue(of('Project deleted'));
  mocks.confirm.mockResolvedValue(true);
});

afterEach(() => {
  vi.restoreAllMocks();
  history.replaceState(null, '', '/');
});

describe('unified reference-data editing', () => {
  it('reviews and saves project-default and mapped-environment backends with the project revision', async () => {
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    page.shadowRoot!.querySelector<HTMLDetailsElement>('.backends')!.open =
      true;
    await settle();
    clickButton(page.shadowRoot!, 'Add backend');
    await settle();
    const fillBackend = async (row: number, account: string) => {
      const values: Record<string, string> = {
        'State storage account': account,
        'State container': 'tfstate',
        'State subscription ID': '11111111-1111-1111-1111-111111111111'
      };
      for (const [label, value] of Object.entries(values)) {
        const field = [
          ...page
            .shadowRoot!.querySelectorAll('.backend-row')
            [row].querySelectorAll<TextField>('vaadin-text-field')
        ].find(field => field.label === label)!;
        field.value = value;
        field.dispatchEvent(new Event('change'));
        await settle();
      }
    };
    await fillBackend(0, 'projectstate');
    clickButton(page.shadowRoot!, 'Add backend');
    await settle();
    await fillBackend(1, 'utstate');
    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    expect(mocks.save.mock.calls).to.have.length(0);
    const reviewButton = [
      ...page.shadowRoot!.querySelectorAll<HTMLElement & { disabled: boolean }>(
        'vaadin-button'
      )
    ].find(button => button.textContent?.trim() === 'Review changes')!;
    expect(reviewButton.disabled, 'review disabled while previewing').to.equal(
      true
    );
    const diff = page.shadowRoot!.querySelector('hegs-json-diff')!;
    const added = [
      ...diff.shadowRoot!.querySelectorAll('.diff-cell.line-add')
    ].map(cell => cell.textContent ?? '');
    expect(added.join('\n')).to.contain('utstate');
    expect(diff.shadowRoot!.querySelectorAll('.fold').length).to.be.greaterThan(
      0
    );
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();
    const saved = mocks.save.mock.calls[0][0].refDataApiModel;
    expect(saved.Revision).to.equal('revision-1');
    expect(saved.Project.TerraformStateBackends).to.deep.equal([
      {
        EnvironmentId: null,
        StorageAccount: 'projectstate',
        ContainerName: 'tfstate',
        SubscriptionId: '11111111-1111-1111-1111-111111111111'
      },
      {
        EnvironmentId: 2,
        StorageAccount: 'utstate',
        ContainerName: 'tfstate',
        SubscriptionId: '11111111-1111-1111-1111-111111111111'
      }
    ]);
  });

  it('preserves backend edits when the server refuses an unsafe state move', async () => {
    const data = reference();
    mocks.get.mockReturnValue(
      of({
        ...data,
        Project: {
          ...data.Project,
          TerraformStateBackends: [
            {
              EnvironmentId: null,
              StorageAccount: 'existingstate',
              ContainerName: 'tfstate',
              SubscriptionId: '11111111-1111-1111-1111-111111111111'
            }
          ]
        }
      })
    );
    mocks.save.mockReturnValue(
      throwError(() => ({
        response:
          'Backend is bound to submitted requests; state migration requires operator review.'
      }))
    );
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    const field = [
      ...page.shadowRoot!.querySelectorAll<TextField>(
        '.backend-row vaadin-text-field'
      )
    ].find(field => field.label === 'State storage account')!;
    field.value = 'differentstate';
    field.dispatchEvent(new Event('change'));
    await settle();
    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();
    expect(page.shadowRoot!.textContent).to.include(
      'state migration requires operator review'
    );
    expect(
      mocks.save.mock.calls[0][0].refDataApiModel.Project
        .TerraformStateBackends[0].StorageAccount
    ).to.equal('differentstate');
    page.shadowRoot!.querySelector<Tabs>('vaadin-tabs')!.selected = 1;
    await settle();
    expect(
      JSON.parse(mocks.editor.value).Project.TerraformStateBackends[0]
        .StorageAccount
    ).to.equal('differentstate');
    expect(JSON.parse(mocks.editor.value).Revision).to.equal('revision-1');
  });

  it('preserves nested configuration and project-owned fields through a structured edit and reviewed save', async () => {
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    const treeButton = findHegsTreeButtons(page.shadowRoot!).find(button =>
      button.textContent?.includes('Nested script')
    );
    treeButton!.click();
    await settle();
    const editor = page.shadowRoot!.querySelector(
      'deployment-definition-editor'
    )!;
    const path = [
      ...editor.shadowRoot!.querySelectorAll<TextField>('vaadin-text-field')
    ].find(field => field.label === 'Script path or JSON path')!;
    path.value = '{"Path":"revised.ps1","Arguments":{"Scope":"#{Scope}"}}';
    path.dispatchEvent(new Event('change'));
    await settle();
    expect(page.shadowRoot!.querySelector('a[href^="/deploy?"]')).to.equal(
      null
    );
    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    expect(mocks.save.mock.calls).to.have.length(0);
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();
    const saved = mocks.save.mock.calls[0][0].refDataApiModel;
    expect(saved.Revision).to.equal('revision-1');
    expect(saved.Project.LeanIXUrl).to.equal(reference().Project.LeanIXUrl);
    expect(saved.Components[0].Children[0]).to.include({
      ParentId: 10,
      PSVersion: '7.4',
      NonProdOnly: true,
      ScriptPath: '{"Path":"revised.ps1","Arguments":{"Scope":"#{Scope}"}}'
    });
    expect(page.shadowRoot!.textContent).to.include('revision-2');
  });

  it('uses the loaded revision rather than a JSON-supplied concurrency token', async () => {
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    page.shadowRoot!.querySelector<Tabs>('vaadin-tabs')!.selected = 1;
    await settle();
    const json = {
      ...reference(),
      Revision: 'forged',
      Components: [{ ...reference().Components[0], ComponentName: 'Updated' }]
    };
    mocks.editor.value = JSON.stringify(json);
    mocks.editor.change();
    await settle();
    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();
    expect(mocks.save.mock.calls[0][0].refDataApiModel.Revision).to.equal(
      'revision-1'
    );
  });

  it('keeps invalid JSON and failed saves visible instead of discarding edits', async () => {
    mocks.save.mockReturnValue(
      throwError(() => ({ response: 'Revision conflict. Reload the project.' }))
    );
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    const tabs = page.shadowRoot!.querySelector<Tabs>('vaadin-tabs')!;
    tabs.selected = 1;
    await settle();
    mocks.editor.value = '{broken';
    mocks.editor.change();
    await settle();
    tabs.selected = 0;
    await settle();
    expect(tabs.selected).to.equal(1);
    expect(page.shadowRoot!.textContent).to.include(
      'Invalid reference-data JSON'
    );
    expect(mocks.save.mock.calls).to.have.length(0);
    mocks.editor.value = JSON.stringify({
      ...reference(),
      Project: { ...reference().Project, ProjectDescription: 'Keep this edit' }
    });
    mocks.editor.change();
    await settle();
    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();
    expect(
      page.shadowRoot!.querySelector('[role="alert"]')?.textContent
    ).to.include('Revision conflict');
    expect(mocks.editor.value).to.include('Keep this edit');
  });

  for (const source of [undefined, ...Object.values(TerraformSourceType)]) {
    it(`renders valid fields for ${source ?? 'PowerShell'} without changing hidden configuration`, async () => {
      const component = {
        ...reference().Components[0],
        ComponentType: source
          ? ComponentType.Terraform
          : ComponentType.PowerShell,
        TerraformSourceType: source,
        TerraformGitBranch: 'release',
        TerraformSubPath: 'infra',
        TerraformTemplateName: 'network',
        TerraformTemplateVersion: '1.0.0'
      };
      const editor = await fixture(
        html`<deployment-definition-editor
          .component=${component}
          .project=${reference().Project}
          .powerShellVersions=${['5.1', '7.4']}
        ></deployment-definition-editor>`
      );
      await settle();
      const labels = [
        ...editor.shadowRoot!.querySelectorAll<TextField>('vaadin-text-field')
      ].map(field => field.label);
      expect(labels.includes('Git ref')).to.equal(
        source === TerraformSourceType.Git
      );
      expect(labels.includes('Script path or JSON path')).to.equal(!source);
      expect(labels.includes('Shared folder path')).to.equal(
        source === TerraformSourceType.SharedFolder
      );
      expect(component.TerraformGitBranch).to.equal('release');
    });
  }

  it('shows a JSON script path in a multi-line field with a parsed preview and keeps the raw text', async () => {
    const raw =
      '{ "ScriptPath": "00 Generic\\\\DeployMSI.ps1", "GenericArguments": [ { "msiName": "Thing.msi" } ] }';
    const component = {
      ...reference().Components[0],
      ComponentType: ComponentType.PowerShell,
      ScriptPath: raw
    };
    const changes: string[] = [];
    const editor = await fixture(
      html`<deployment-definition-editor
        .component=${component}
        .project=${reference().Project}
        .powerShellVersions=${['5.1']}
        @definition-changed=${(e: CustomEvent) =>
          changes.push(e.detail.ScriptPath)}
      ></deployment-definition-editor>`
    );
    await settle();
    const root = editor.shadowRoot!;
    const area = root.querySelector('vaadin-text-area') as HTMLElement & {
      value: string;
      label: string;
    };
    expect(area.label).to.equal('Script path or JSON path');
    expect(area.value).to.equal(raw);
    expect(
      [...root.querySelectorAll<TextField>('vaadin-text-field')].some(
        f => f.label === 'Script path or JSON path'
      )
    ).to.equal(false);
    const viewer = root.querySelector('hegs-json-viewer') as HTMLElement & {
      data: unknown;
    };
    expect(viewer.data).to.deep.equal(JSON.parse(raw));
    expect(viewer.shadowRoot!.textContent).to.include('msiName');

    area.value = raw.replace('Thing.msi', 'Other.msi');
    area.dispatchEvent(new Event('change'));
    expect(changes).to.deep.equal([raw.replace('Thing.msi', 'Other.msi')]);
  });

  it('respects read-only capability and provides an unfiltered project timeline', async () => {
    mocks.get.mockReturnValue(of({ ...reference(), CanEdit: false }));
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    expect(page.shadowRoot!.textContent).to.include('Read-only');
    const editor = page.shadowRoot!.querySelector(
      'deployment-definition-editor'
    )!;
    expect(
      editor.shadowRoot!.querySelector<TextField>('vaadin-text-field')!.readonly
    ).to.equal(true);
    page.shadowRoot!.querySelector<Tabs>('vaadin-tabs')!.selected = 2;
    await settle();
    expect(mocks.history.mock.calls[0][0]).to.include({
      projectId: 7,
      componentId: undefined,
      page: 1
    });
  });
});

describe('definition history and discovery', () => {
  it('deletes using the project revision acknowledged before confirmation, not a changed row', async () => {
    let confirm!: (answer: boolean) => void;
    mocks.confirm.mockReturnValue(
      new Promise<boolean>(resolve => {
        confirm = resolve;
      })
    );
    vi.spyOn(PageProjectsList.prototype, 'render').mockReturnValue(html``);
    const page = await fixture(html`<page-projects-list></page-projects-list>`);
    await settle();
    const project = {
      ProjectId: 7,
      ProjectName: 'Payments',
      Revision: 'before-confirmation'
    };
    page.dispatchEvent(
      new CustomEvent('delete-project', { detail: { Project: project } })
    );
    project.Revision = 'changed-while-confirming';
    confirm(true);
    await settle();
    expect(mocks.deleteProject.mock.calls[0][0]).to.deep.equal({
      projectId: 7,
      expectedRevision: 'before-confirmation'
    });
  });
  it('does not substitute another current definition for a detached historical component', async () => {
    history.replaceState(
      null,
      '',
      '/project-ref-data/7?componentId=99&tab=history'
    );
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();
    expect(page.shadowRoot!.textContent).to.include('no longer attached');
    expect(
      page.shadowRoot!.querySelector('.tree button[aria-pressed="true"]')
    ).to.equal(null);
    expect(mocks.history.mock.calls[0][0]).to.include({
      projectId: 7,
      componentId: 99
    });
  });
  it('retains a deep-linked project while projects load and selects only the requested nested component', async () => {
    const projects = new Subject<unknown>();
    mocks.projects.mockReturnValue(projects);
    history.replaceState(null, '', '/deploy?projectId=7&componentId=11');
    const page = await fixture(html`<page-deploy></page-deploy>`);
    await settle();
    projects.next([{ ProjectId: 7, ProjectName: 'Payments' }]);
    await settle();
    const deploy = page.shadowRoot!.querySelector<DeployEnv>('deploy-env')!;
    expect(deploy.project?.ProjectId).to.equal(7);
    expect(deploy.preselectedComponentId).to.equal(11);
    expect(deploy.envName).to.equal('UT02');
    const tree = deploy.shadowRoot!.querySelector<HegsTree>('hegs-tree')!;
    expect(tree.getCheckedComponents().map(node => node.data.id)).to.deep.equal(
      [11]
    );
  });
  it('ignores a previous scope response and labels legacy evidence honestly', async () => {
    const stale = new Subject<unknown>();
    mocks.history.mockReturnValueOnce(stale).mockReturnValueOnce(
      of({
        Items: [
          {
            Id: 'legacy-1',
            EventType: 'Legacy audit',
            Summary: 'Submitted JSON',
            IsLegacy: true,
            ProjectId: 8
          }
        ],
        TotalItems: 1
      })
    );
    const historyElement = await fixture<DeploymentDefinitionHistory>(
      html`<deployment-definition-history
        .projectId=${7}
      ></deployment-definition-history>`
    );
    await settle();
    historyElement.projectId = 8;
    await settle();
    stale.next({ Items: [{ Summary: 'Wrong project' }], TotalItems: 1 });
    await settle();
    expect(historyElement.shadowRoot!.textContent).to.include(
      'not proof of a successful'
    );
    expect(historyElement.shadowRoot!.textContent).not.to.include(
      'Wrong project'
    );
  });

  it('pages definitions on the server and links by project and component identity', async () => {
    mocks.definitions.mockReturnValue(
      of({
        Items: [
          {
            ProjectId: 7,
            ProjectName: 'Payments',
            Component: {
              ComponentId: 11,
              ComponentName: 'Network',
              ComponentType: ComponentType.Terraform
            },
            Revision: 'r1',
            CanDeploy: true
          }
        ],
        TotalItems: 51
      })
    );
    history.replaceState(null, '', '/deployment-definitions?projectId=7');
    const page = await fixture(
      html`<page-deployment-definitions></page-deployment-definitions>`
    );
    await settle();
    expect(mocks.definitions.mock.calls[0][0]).to.include({
      projectId: 7,
      page: 1,
      limit: 50
    });
    expect(
      page.shadowRoot!.querySelector(
        'a[href="/deploy?projectId=7&componentId=11"]'
      )
    ).not.to.equal(null);
    clickButton(page.shadowRoot!, 'Next');
    await settle();
    expect(mocks.definitions.mock.calls[1][0]).to.include({
      projectId: 7,
      page: 2
    });
  });

  it('hides secondary project and source columns on narrow viewports', async () => {
    const originalMatchMedia = window.matchMedia;
    window.matchMedia = () =>
      ({
        matches: true,
        media: '(max-width: 768px)',
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => true
      }) as MediaQueryList;
    try {
      const page = await fixture(
        html`<page-deployment-definitions></page-deployment-definitions>`
      );
      await settle();
      const cols = [
        ...page.shadowRoot!.querySelectorAll<HTMLElement>('vaadin-grid-column')
      ];
      expect(cols.map(col => col.hidden)).to.deep.equal([
        false,
        true,
        true,
        false
      ]);
    } finally {
      window.matchMedia = originalMatchMedia;
    }
  });

  it('clones a sibling step with pre-populated parent, PowerShell version and script path, and supports tree resize and collapse', async () => {
    history.replaceState(null, '', '/project-ref-data/7?componentId=11');
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();

    // Clone Child Apply (which has ParentId 10, PSVersion 7.5, ScriptPath deploy/child.ps1)
    clickButton(page.shadowRoot!, 'Clone sibling');
    await settle();

    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();

    const saved = mocks.save.mock.calls[0][0].refDataApiModel;
    const children = saved.Components[0].Children;
    expect(children).to.have.length(2);
    expect(children[1]).to.include({
      ComponentName: 'Nested script (copy)',
      ParentId: 10,
      ComponentType: ComponentType.PowerShell,
      PSVersion: '7.4',
      ScriptPath: '{"Path":"nested.ps1","Arguments":{"Scope":"#{Scope}"}}',
      IsEnabled: true,
      StopOnFailure: true,
      NonProdOnly: true
    });

    // Resize tree splitter via keyboard
    const splitter =
      page.shadowRoot!.querySelector<HTMLElement>('.tree-splitter')!;
    expect(splitter).not.to.equal(null);
    splitter.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })
    );
    await settle();
    const workspace =
      page.shadowRoot!.querySelector<HTMLElement>('.workspace')!;
    expect(workspace.getAttribute('style')).to.include(
      '--tree-pane-width: 300px'
    );

    // Collapse and expand parent node in hegs-tree
    clickButton(page.shadowRoot!, 'Collapse all');
    await settle();
    expect(findHegsTreeButtons(page.shadowRoot!)).to.have.length(1);
    clickButton(page.shadowRoot!, 'Expand all');
    await settle();
    expect(findHegsTreeButtons(page.shadowRoot!)).to.have.length(3);
  });

  it('adds a predefined 00 Generic script and a reusable script component from the script catalog dialog and switches workspace tabs', async () => {
    mocks.definitions.mockReturnValue(
      of({
        Items: [
          {
            ProjectId: 7,
            ProjectName: 'Payments',
            Component: {
              ComponentId: 42,
              ComponentName: 'Database Backup',
              ComponentType: ComponentType.PowerShell,
              ScriptPath: 'common/backup-db.ps1',
              PSVersion: '7.4',
              IsEnabled: true,
              StopOnFailure: true,
              NonProdOnly: false
            }
          }
        ],
        TotalItems: 1
      })
    );
    history.replaceState(null, '', '/project-ref-data/7?componentId=11');
    const page = await fixture(
      html`<page-project-ref-data></page-project-ref-data>`
    );
    await settle();

    clickButton(page.shadowRoot!, 'Script catalog');
    await settle();
    const dialog = page.shadowRoot!.querySelector('.script-catalog-dialog')!;
    expect(dialog).not.to.equal(null);
    expect(dialog.textContent).to.include('Deploy MSI Package');
    expect(dialog.textContent).to.include('Database Backup');

    const msiField = [
      ...dialog.querySelectorAll<TextField>('vaadin-text-field')
    ].find(field => field.label === 'MSI file name (msiName)')!;
    msiField.value = 'Payments.Service.msi';
    msiField.dispatchEvent(new Event('input'));
    await settle();

    clickButton(page.shadowRoot!, 'Add "Deploy MSI Package" as sibling');
    await settle();

    clickButton(page.shadowRoot!, 'Script catalog');
    await settle();
    clickButton(page.shadowRoot!, 'Insert sibling');
    await settle();

    clickButton(page.shadowRoot!, 'Review changes');
    await settle();
    clickButton(page.shadowRoot!, 'Confirm save');
    await settle();

    const saved = mocks.save.mock.calls[0][0].refDataApiModel;
    expect(saved.Components[0].Children[1]).to.include({
      ComponentName: 'Deploy MSI',
      ParentId: 10,
      ScriptPath:
        '{"ScriptPath":"00 Generic\\\\DeployMSI.ps1","GenericArguments":[{"msiName":"Payments.Service.msi"}]}',
      PSVersion: '5.1'
    });
    expect(saved.Components[0].Children[2]).to.include({
      ComponentName: 'Database Backup',
      ParentId: 10,
      ScriptPath: 'common/backup-db.ps1',
      PSVersion: '7.4'
    });

    // Switch to project-source and terraform-state workspace tabs
    history.replaceState(null, '', '/project-ref-data/7?tab=project-source');
    window.dispatchEvent(new CustomEvent('dorc-router-location-changed'));
    await settle();
    expect(
      page.shadowRoot!.querySelector<HTMLElement>('.project-source-pane')
        ?.hidden
    ).to.equal(false);

    history.replaceState(null, '', '/project-ref-data/7?tab=terraform-state');
    window.dispatchEvent(new CustomEvent('dorc-router-location-changed'));
    await settle();
    expect(
      page.shadowRoot!.querySelector<HTMLElement>('.terraform-state-pane')
        ?.hidden
    ).to.equal(false);
  });
});
