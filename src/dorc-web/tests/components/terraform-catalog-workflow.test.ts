import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { page } from 'vitest/browser';
import { expect, fixture, html, settle } from '../_helpers';
import type { ComboBox } from '@vaadin/combo-box';
import type { Checkbox } from '@vaadin/checkbox';
import type { TextField } from '@vaadin/text-field';
import type { Notification } from '@vaadin/notification';
import { TerraformParameterType } from '../../src/apis/dorc-api';
import type { DeployFromTemplateDialog } from '../../src/components/deploy-from-template-dialog';
import type {
  ComponentApiModelTemplateApiModel,
  EnvironmentApiModelTemplateApiModel,
  TerraformTemplateInstantiateResponseApiModel,
  TerraformTemplateManifest
} from '../../src/apis/dorc-api';

const mocks = vi.hoisted(() => ({
  projects: vi.fn(),
  environments: vi.fn(),
  components: vi.fn(),
  instantiate: vi.fn(),
  templates: vi.fn(),
  navigate: vi.fn()
}));

vi.mock('../../src/router/router', () => ({ navigate: mocks.navigate }));
vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  RefDataProjectsApi: class {
    refDataProjectsGet = mocks.projects;
  },
  RefDataProjectEnvironmentMappingsApi: class {
    refDataProjectEnvironmentMappingsGet = mocks.environments;
  },
  RefDataComponentsApi: class {
    refDataComponentsGet = mocks.components;
  },
  TerraformApi: class {
    terraformTemplateInstantiatePost = mocks.instantiate;
    terraformTemplatesGet = mocks.templates;
  }
}));

await import('../../src/components/deploy-from-template-dialog');
await import('../../src/pages/page-stock-modules');
await import('../../src/router/style-registrations');

const template: TerraformTemplateManifest = {
  Name: 'storage-account',
  Version: '1.0.0',
  Category: 'Storage',
  Outputs: [],
  Tags: [],
  RequiredProviders: {},
  RequiredTerraformVersion: '>= 1.5.0',
  Deprecated: false,
  Source: {
    Kind: 'git',
    Locator: 'https://example.com/modules.git',
    Ref: 'v1.0.0'
  },
  Parameters: [
    {
      Name: 'location',
      Type: TerraformParameterType.String,
      Required: true,
      Default: 'uksouth',
      Sensitive: false
    },
    {
      Name: 'enabled',
      Type: TerraformParameterType.Bool,
      Required: false,
      Sensitive: false
    },
    {
      Name: 'credential',
      Type: TerraformParameterType.String,
      Required: true,
      Sensitive: true
    }
  ]
};

const project = { ProjectId: 7, ProjectName: 'ExampleProject' };
const environment = { EnvironmentId: 9, EnvironmentName: 'TestEnvironment' };

function dialogElement(host: InstanceType<typeof DeployFromTemplateDialog>) {
  return host.shadowRoot!.querySelector('vaadin-dialog')!;
}

function button(root: ParentNode, label: string) {
  const found = Array.from(root.querySelectorAll('vaadin-button')).find(
    b => b.textContent?.trim() === label
  );
  expect(found, `button "${label}" exists`).not.to.equal(undefined);
  return found as HTMLElement;
}

async function click(root: ParentNode, label: string) {
  button(root, label).click();
  await settle();
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.projects.mockReturnValue(of([project]));
  mocks.environments.mockReturnValue(of({ Items: [environment] }));
  mocks.components.mockReturnValue(of({ Items: [] }));
  mocks.templates.mockReturnValue(of([template]));
  mocks.instantiate.mockReturnValue(of({ requestId: 73 }));
});

const browserErrors: string[] = [];
const captureBrowserError = (event: ErrorEvent) => {
  browserErrors.push(event.error?.stack ?? event.message);
};
beforeEach(() => {
  browserErrors.length = 0;
  window.addEventListener('error', captureBrowserError);
});
afterEach(() => {
  window.removeEventListener('error', captureBrowserError);
  expect(browserErrors).to.deep.equal([]);
});

const open = async () => {
  const host = await fixture<InstanceType<typeof DeployFromTemplateDialog>>(
    html`<deploy-from-template-dialog></deploy-from-template-dialog>`
  );
  host.open(template, {
    projectName: project.ProjectName,
    environmentName: environment.EnvironmentName
  });
  await settle();
  return { host, dialog: dialogElement(host) };
};

describe('catalog environment deployment workflow', () => {
  afterEach(async () => {
    for (const notification of document.querySelectorAll<Notification>(
      'vaadin-notification'
    )) {
      notification.close();
    }
    await settle(400);
  });
  it('keeps inherited inputs out of the request and requires review before submitting', async () => {
    const { dialog } = await open();
    expect(mocks.environments.mock.calls[0][0]).to.deep.equal({
      project: 'ExampleProject',
      includeRead: false
    });
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Inherited values');
    expect(dialog.querySelector('vaadin-password-field')).to.equal(null);
    expect(dialog.querySelector('a')?.getAttribute('href')).to.equal(
      '/environment/TestEnvironment/variables'
    );
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Review');
    expect(mocks.instantiate.mock.calls).to.have.length(0);
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body).to.deep.equal({
      ProjectId: 7,
      ComponentName: 'storage-account',
      ParentComponentId: null,
      EnvironmentName: 'TestEnvironment',
      Parameters: {}
    });
    expect(mocks.navigate.mock.calls[0][0]).to.equal('/monitor-result/73');
  });

  it('reuses an existing component and preserves its parent instead of creating a duplicate', async () => {
    mocks.components.mockReturnValue(
      of({
        Items: [
          {
            ComponentId: 22,
            ComponentName: 'ApplicationStorage',
            ParentId: 8,
            ComponentType: 'Terraform',
            TerraformSourceType: 'Catalog',
            IsEnabled: true,
            TerraformTemplateName: template.Name,
            TerraformTemplateVersion: template.Version
          },
          { ComponentId: 8, ComponentName: 'Infrastructure' }
        ]
      })
    );
    const { dialog } = await open();
    expect(
      dialog.querySelector('vaadin-combo-box[label="Existing component"]')
    ).not.to.equal(null);
    await click(dialog, 'Continue');
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('ApplicationStorage (reuse)');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.ComponentName).to.equal(
      'ApplicationStorage'
    );
    expect(mocks.instantiate.mock.calls[0][0].body.ParentComponentId).to.equal(
      8
    );
  });

  it('distinguishes an omitted Boolean from an explicit false and masks sensitive review values', async () => {
    const { dialog } = await open();
    await click(dialog, 'Continue');
    const overrides = Array.from(
      dialog.querySelectorAll<Checkbox>('vaadin-checkbox')
    );
    overrides.find(
      c => c.label === 'Override enabled for this request'
    )!.checked = true;
    overrides.find(
      c => c.label === 'Override credential for this request'
    )!.checked = true;
    await settle();
    dialog.querySelector<TextField>('vaadin-password-field')!.value =
      'fictional-test-secret';
    await settle();
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Sensitive override (hidden)');
    expect(dialog.textContent).not.to.contain('fictional-test-secret');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.Parameters).to.deep.equal({
      enabled: 'false',
      credential: 'fictional-test-secret'
    });
  });

  it('clears overrides on environment changes and ignores old project responses', async () => {
    mocks.projects.mockReturnValue(
      of([project, { ProjectId: 8, ProjectName: 'OtherProject' }])
    );
    mocks.environments.mockReturnValue(
      of({ Items: [environment, { EnvironmentName: 'OtherEnvironment' }] })
    );
    const { dialog } = await open();
    await click(dialog, 'Continue');
    dialog.querySelector<Checkbox>('vaadin-checkbox')!.checked = true;
    await settle();
    await click(dialog, 'Back');
    dialog.querySelector<ComboBox>(
      'vaadin-combo-box[label="DOrc environment"]'
    )!.value = 'OtherEnvironment';
    await settle();
    await click(dialog, 'Continue');
    expect(dialog.querySelector('vaadin-text-field')).to.equal(null);
    await click(dialog, 'Back');

    const staleEnvironments =
      new Subject<EnvironmentApiModelTemplateApiModel>();
    const staleComponents = new Subject<ComponentApiModelTemplateApiModel>();
    mocks.environments.mockReturnValueOnce(staleEnvironments);
    mocks.components.mockReturnValueOnce(staleComponents);
    const projects = dialog.querySelector<ComboBox>(
      'vaadin-combo-box[label="Project"]'
    )!;
    projects.selectedItem = { ProjectId: 8, ProjectName: 'OtherProject' };
    await settle();
    projects.selectedItem = project;
    await settle();
    staleEnvironments.next({
      Items: [{ EnvironmentName: 'StaleEnvironment' }]
    });
    staleEnvironments.complete();
    staleComponents.next({ Items: [] });
    staleComponents.complete();
    await settle();
    const environments = dialog.querySelector<ComboBox>(
      'vaadin-combo-box[label="DOrc environment"]'
    )!;
    expect(
      environments.items?.some(e => e.EnvironmentName === 'StaleEnvironment')
    ).to.equal(false);
  });

  it('blocks duplicate submission and dismissal until the response, then keeps errors visible', async () => {
    const response =
      new Subject<TerraformTemplateInstantiateResponseApiModel>();
    mocks.instantiate.mockReturnValue(response);
    const { host, dialog } = await open();
    await click(dialog, 'Continue');
    await click(dialog, 'Continue');
    button(dialog, 'Submit plan request').click();
    button(dialog, 'Submit plan request').click();
    await settle();
    await click(dialog, 'Cancel');
    expect(host.opened).to.equal(true);
    expect(mocks.instantiate.mock.calls).to.have.length(1);
    response.error({ response: 'Missing environment property credential' });
    await settle();
    expect(host.opened).to.equal(true);
    expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
      'Missing environment property credential'
    );
    expect(mocks.navigate.mock.calls).to.have.length(0);
  });

  it('does not pretend a component-only response created a deployment', async () => {
    mocks.instantiate.mockReturnValue(of({ component: { ComponentId: 22 } }));
    const { host, dialog } = await open();
    await click(dialog, 'Continue');
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(host.opened).to.equal(true);
    expect(dialog.textContent).to.contain('No deployment request ID');
    expect(mocks.navigate.mock.calls).to.have.length(0);
  });

  it('fits the target form on a narrow viewport', async () => {
    await page.viewport(375, 740);
    try {
      const { dialog } = await open();
      const form = dialog.querySelector('[aria-busy]') as HTMLElement;
      expect(form.getBoundingClientRect().width).to.be.greaterThan(0);
      expect(form.scrollWidth).to.be.at.most(form.clientWidth + 1);
    } finally {
      await page.viewport(1024, 768);
    }
  });
});

describe('module catalog discovery', () => {
  it('filters modules and hides deprecated versions unless requested', async () => {
    mocks.templates.mockReturnValue(
      of([template, { ...template, Version: '0.9.0', Deprecated: true }])
    );
    const host = await fixture(html`<page-stock-modules></page-stock-modules>`);
    await settle();
    const grid = host.shadowRoot!.querySelector(
      'vaadin-grid'
    ) as HTMLElement & { items: TerraformTemplateManifest[] };
    expect(grid.items).to.have.length(1);
    host.shadowRoot!.querySelector<Checkbox>('vaadin-checkbox')!.checked = true;
    await settle();
    expect(grid.items).to.have.length(2);
    host.shadowRoot!.querySelector<TextField>('vaadin-text-field')!.value =
      '0.9';
    await settle();
    expect(grid.items.map(t => t.Version)).to.deep.equal(['0.9.0']);
  });
});
