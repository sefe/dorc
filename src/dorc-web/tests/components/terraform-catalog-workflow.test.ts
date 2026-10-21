import { from, of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { page } from 'vitest/browser';
import { expect, fixture, html, settle } from '../_helpers';
import type { ComboBox } from '@vaadin/combo-box';
import type { TextField } from '@vaadin/text-field';
import type { Checkbox } from '@vaadin/checkbox';
import type { Notification } from '@vaadin/notification';
import {
  TerraformParameterResolutionStatus,
  TerraformParameterType,
  ComponentType,
  TerraformSourceType
} from '../../src/apis/dorc-api';
import type { DeployFromTemplateDialog } from '../../src/components/deploy-from-template-dialog';
import type { PageModuleCatalog } from '../../src/pages/page-module-catalog';
import { overrideError } from '../../src/components/deploy-from-template-dialog';
import {
  resourceGroupNameFromArmId,
  sanitizeTerraformStateKeyPart
} from '../../src/helpers/terraform-identity';
import type {
  CloudResourceApiModel,
  ComponentApiModelTemplateApiModel,
  EnvironmentApiModelTemplateApiModel,
  TerraformTemplateInstantiateResponseApiModel,
  TerraformTemplateResolutionApiModel,
  TerraformTemplateManifest
} from '../../src/apis/dorc-api';

const mocks = vi.hoisted(() => ({
  projects: vi.fn(),
  environments: vi.fn(),
  components: vi.fn(),
  instantiate: vi.fn(),
  templates: vi.fn(),
  resolution: vi.fn(),
  saveConfiguration: vi.fn(),
  previewUpgrade: vi.fn(),
  upgrade: vi.fn(),
  cloudResources: vi.fn(),
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
  RefDataCloudResourcesApi: class {
    refDataCloudResourcesByEnvIdEnvIdGet = mocks.cloudResources;
  },
  TerraformApi: class {
    terraformTemplateInstantiatePost = mocks.instantiate;
    terraformTemplatesGet = mocks.templates;
    terraformTemplateResolutionGet = mocks.resolution;
    terraformTemplateConfigurationPut = mocks.saveConfiguration;
    terraformUpgradePreviewGet = mocks.previewUpgrade;
    terraformUpgradePost = mocks.upgrade;
  }
}));

await import('../../src/components/deploy-from-template-dialog');
await import('../../src/pages/page-module-catalog');
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
    b =>
      b.textContent?.trim() === label || b.getAttribute('aria-label') === label
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
  mocks.cloudResources.mockReturnValue(of([]));
  mocks.templates.mockReturnValue(of([template]));
  mocks.instantiate.mockReturnValue(of({ requestId: 73 }));
  mocks.resolution.mockReturnValue(
    of({
      ProjectName: project.ProjectName,
      EnvironmentName: environment.EnvironmentName,
      Parameters: [
        {
          Name: 'location',
          Required: true,
          Sensitive: false,
          Status: TerraformParameterResolutionStatus.Environment
        },
        {
          Name: 'enabled',
          Required: false,
          Sensitive: false,
          Status: TerraformParameterResolutionStatus.Unset
        },
        {
          Name: 'credential',
          Required: true,
          Sensitive: true,
          Status: TerraformParameterResolutionStatus.Environment
        }
      ]
    })
  );
});

it('honours the linked Catalog component instead of selecting the first compatible component', async () => {
  const component = {
    ComponentType: ComponentType.Terraform,
    TerraformSourceType: TerraformSourceType.Catalog,
    TerraformTemplateName: template.Name,
    TerraformTemplateVersion: template.Version,
    IsEnabled: true
  };
  mocks.components.mockReturnValue(
    of({
      Items: [
        { ...component, ComponentId: 10, ComponentName: 'First component' },
        { ...component, ComponentId: 20, ComponentName: 'Linked component' }
      ]
    })
  );
  const host = await fixture<DeployFromTemplateDialog>(
    html`<deploy-from-template-dialog></deploy-from-template-dialog>`
  );
  host.open(template, {
    projectName: project.ProjectName,
    environmentName: environment.EnvironmentName,
    componentId: 20
  });
  await settle();
  await click(dialogElement(host), 'Continue');
  expect(mocks.resolution.mock.calls.at(-1)?.[0].componentId).to.equal(20);
  await click(dialogElement(host), 'Cancel');
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

const open = async (manifest = template) => {
  const host = await fixture<InstanceType<typeof DeployFromTemplateDialog>>(
    html`<deploy-from-template-dialog></deploy-from-template-dialog>`
  );
  host.open(manifest, {
    projectName: project.ProjectName,
    environmentName: environment.EnvironmentName
  });
  await settle();
  return { host, dialog: dialogElement(host) };
};

describe('catalog environment deployment workflow', () => {
  it('saves only explicit non-secret component/environment inputs without submitting a deployment', async () => {
    mocks.components.mockReturnValue(
      of({
        Items: [
          {
            ComponentId: 22,
            ComponentName: 'ApplicationStorage',
            ComponentType: 'Terraform',
            TerraformSourceType: 'Catalog',
            TerraformTemplateName: template.Name,
            TerraformTemplateVersion: template.Version
          }
        ]
      })
    );
    mocks.resolution.mockReturnValue(
      of({
        Parameters: [],
        SavedConfiguration: { Revision: 3, ParameterNames: [] }
      })
    );
    mocks.saveConfiguration.mockReturnValue(
      of({ Revision: 4, ParameterNames: ['enabled'] })
    );
    const { dialog } = await open();
    await click(dialog, 'Continue');
    await click(dialog, 'Override enabled for this request');
    await click(dialog, 'Override credential for this request');
    dialog.querySelector<TextField>('vaadin-password-field')!.value =
      'one-off-only';
    await settle();
    await click(dialog, 'Replace saved non-secret inputs');
    expect(mocks.saveConfiguration.mock.calls[0][0]).to.deep.equal({
      name: template.Name,
      version: template.Version,
      projectId: 7,
      componentId: 22,
      environmentName: environment.EnvironmentName,
      saveCatalogEnvironmentInputsApiModel: {
        ExpectedRevision: 3,
        Inputs: { enabled: 'false' }
      }
    });
    expect(mocks.instantiate.mock.calls).to.have.length(0);
  });

  it('requires impact confirmation before upgrading a shared pin and never submits a deployment automatically', async () => {
    mocks.components.mockReturnValue(
      of({
        Items: [
          {
            ComponentId: 22,
            ComponentName: 'ApplicationStorage',
            ComponentType: 'Terraform',
            TerraformSourceType: 'Catalog',
            TerraformTemplateName: template.Name,
            TerraformTemplateVersion: '0.9.0'
          }
        ]
      })
    );
    mocks.previewUpgrade.mockReturnValue(
      of({
        ComponentId: 22,
        Revision: 2,
        CurrentVersion: '0.9.0',
        TargetVersion: '1.0.0',
        Environments: [
          environment,
          { EnvironmentId: 10, EnvironmentName: 'Production' }
        ],
        InputChanges: ['Added required input'],
        ConfigurationRevisions: { '9': 1, '10': 0 }
      })
    );
    mocks.upgrade.mockReturnValue(of(undefined));
    const { dialog } = await open();
    (
      Array.from(dialog.querySelectorAll('vaadin-button')).find(button =>
        button.textContent?.includes('Review ApplicationStorage')
      ) as HTMLElement
    ).click();
    await settle();
    expect(dialog.textContent).to.contain('Production');
    expect(dialog.textContent).to.contain('Added required input');
    const upgrade = button(dialog, 'Upgrade shared pin only');
    expect((upgrade as HTMLElement & { disabled: boolean }).disabled).to.equal(
      true
    );
    const confirmation = Array.from(
      dialog.querySelectorAll<Checkbox>('vaadin-checkbox')
    ).find(checkbox => checkbox.label?.startsWith('I confirm'))!;
    confirmation.checked = true;
    await settle();
    await click(dialog, 'Upgrade shared pin only');
    expect(
      mocks.upgrade.mock.calls[0][0].catalogUpgradeRequestApiModel
    ).to.deep.equal({
      ExpectedRevision: 2,
      ExpectedVersion: '0.9.0',
      EnvironmentIds: [9, 10],
      ConfigurationRevisions: { '9': 1, '10': 0 },
      ConfirmImpact: true,
      PreserveExistingServiceOwnership: false
    });
    expect(mocks.instantiate.mock.calls).to.have.length(0);
  });

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
      SubmissionKey: mocks.instantiate.mock.calls[0][0].body.SubmissionKey,
      ProjectId: 7,
      ComponentName: 'storage-account',
      ParentComponentId: null,
      EnvironmentName: 'TestEnvironment',
      Parameters: {}
    });
    expect(mocks.navigate.mock.calls[0][0]).to.equal('/monitor-result/73');
  });

  it('previews the sanitized state key without changing the submitted component name', async () => {
    const { dialog } = await open();
    dialog.querySelector<TextField>(
      'vaadin-text-field[label="New component name"]'
    )!.value = 'Service_Bus 02';
    await settle();
    expect(dialog.querySelector('.identity-path')?.textContent).to.equal(
      'exampleproject/service_005fbus_002002/testenvironment.tfstate'
    );
    await click(dialog, 'Continue');
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.ComponentName).to.equal(
      'Service_Bus 02'
    );
  });

  it('blocks blank and fractional optional integer overrides but allows omission and an empty String', async () => {
    const manifest = {
      ...template,
      Parameters: [
        {
          Name: 'count',
          Type: TerraformParameterType.Number,
          Required: false,
          Sensitive: false,
          Integer: true
        },
        {
          Name: 'label',
          Type: TerraformParameterType.String,
          Required: false,
          Sensitive: false
        }
      ]
    };
    const { dialog } = await open(manifest);
    await click(dialog, 'Continue');
    await click(dialog, 'Override count for this request');
    await click(dialog, 'Continue');
    expect(dialog.querySelector('h2')?.textContent).to.contain('Inputs');
    expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
      'count: Must be a finite number'
    );
    dialog.querySelector<TextField>('vaadin-number-field')!.value = '2.5';
    await settle();
    await click(dialog, 'Continue');
    expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
      'count: Must be an integer'
    );
    await click(dialog, 'Remove request override for count');
    await click(dialog, 'Override label for this request');
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.Parameters).to.deep.equal({
      label: ''
    });
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
    await click(dialog, 'Override enabled for this request');
    await click(dialog, 'Override credential for this request');
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
    await click(dialog, 'Override location for this request');
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

  it('asks the server where inputs resolve from, names only, once the target is fixed', async () => {
    const { dialog } = await open();
    expect(mocks.resolution.mock.calls).to.have.length(0);
    await click(dialog, 'Continue');
    expect(mocks.resolution.mock.calls[0][0]).to.deep.equal({
      componentId: undefined,
      name: 'storage-account',
      version: '1.0.0',
      projectId: 7,
      environmentName: 'TestEnvironment'
    });
    expect(dialog.textContent).to.contain(
      'Resolved by DOrc for TestEnvironment, including environment/global properties and configured fallbacks.'
    );
    expect(dialog.textContent).to.contain('Not set');
    expect(dialog.textContent).not.to.contain('Missing in');
  });
  for (const staleTiming of [
    'before B request',
    'while B loads',
    'after B resolves'
  ]) {
    it(`ignores a deferred A resolution ${staleTiming} after switching environments`, async () => {
      let resolveA!: (value: TerraformTemplateResolutionApiModel) => void;
      let resolveB!: (value: TerraformTemplateResolutionApiModel) => void;
      mocks.environments.mockReturnValue(
        of({ Items: [environment, { EnvironmentName: 'EnvironmentB' }] })
      );
      mocks.resolution
        .mockReturnValueOnce(
          from(
            new Promise<TerraformTemplateResolutionApiModel>(resolve => {
              resolveA = resolve;
            })
          )
        )
        .mockReturnValueOnce(
          from(
            new Promise<TerraformTemplateResolutionApiModel>(resolve => {
              resolveB = resolve;
            })
          )
        );
      const stale = {
        Parameters: [
          {
            Name: 'credential',
            Required: true,
            Sensitive: true,
            Status: TerraformParameterResolutionStatus.Missing
          }
        ]
      } as TerraformTemplateResolutionApiModel;
      const current = {
        Parameters: [
          {
            Name: 'location',
            Required: true,
            Sensitive: false,
            Status: TerraformParameterResolutionStatus.Environment
          },
          {
            Name: 'credential',
            Required: true,
            Sensitive: true,
            Status: TerraformParameterResolutionStatus.Environment
          }
        ]
      } as TerraformTemplateResolutionApiModel;
      const { dialog } = await open();
      await click(dialog, 'Continue');
      await click(dialog, 'Back');
      dialog.querySelector<ComboBox>(
        'vaadin-combo-box[label="DOrc environment"]'
      )!.value = 'EnvironmentB';
      await settle();
      if (staleTiming === 'before B request') {
        resolveA(stale);
        await settle();
      }
      await click(dialog, 'Continue');
      expect(mocks.resolution.mock.calls).to.have.length(2);
      expect(mocks.resolution.mock.calls[1][0].environmentName).to.equal(
        'EnvironmentB'
      );
      if (staleTiming === 'while B loads') {
        resolveA(stale);
        await settle();
        expect(dialog.textContent).to.contain(
          'Checking EnvironmentB properties'
        );
      }
      resolveB(current);
      await settle();
      if (staleTiming === 'after B resolves') {
        resolveA(stale);
        await settle();
      }
      expect(dialog.textContent).to.contain(
        'Resolved by DOrc for EnvironmentB, including environment/global properties and configured fallbacks.'
      );
      expect(dialog.textContent).not.to.contain('Missing in EnvironmentB');
      await click(dialog, 'Continue');
      expect(dialog.querySelector('h2')?.textContent).to.contain('Review');
    });
  }
  it('marks a required input the environment cannot supply as missing and blocks until it is overridden', async () => {
    mocks.resolution.mockReturnValue(
      of({
        ProjectName: project.ProjectName,
        EnvironmentName: environment.EnvironmentName,
        Parameters: [
          {
            Name: 'location',
            Required: true,
            Sensitive: false,
            Status: TerraformParameterResolutionStatus.Default
          },
          {
            Name: 'enabled',
            Required: false,
            Sensitive: false,
            Status: TerraformParameterResolutionStatus.Unset
          },
          {
            Name: 'credential',
            Required: true,
            Sensitive: true,
            Status: TerraformParameterResolutionStatus.Missing
          }
        ]
      })
    );
    const { dialog } = await open();
    await click(dialog, 'Continue');
    const credential = Array.from(dialog.querySelectorAll('.param')).find(
      el => el.getAttribute('data-missing') === 'true'
    );
    expect(credential?.textContent).to.contain('credential');
    expect(credential?.querySelector('.badge')?.textContent?.trim()).to.equal(
      'Missing'
    );
    expect(dialog.textContent).to.contain('1 required input missing');
    await click(dialog, 'Continue');
    expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
      'credential: not set in TestEnvironment'
    );
    expect(dialog.querySelector('h2')?.textContent).to.contain('Inputs');
    await click(dialog, 'Override credential for this request');
    dialog.querySelector<TextField>('vaadin-password-field')!.value =
      'fictional-test-secret';
    await settle();
    expect(dialog.querySelector('[data-missing="true"]')).to.equal(null);
    await click(dialog, 'Continue');
    expect(dialog.querySelector('h2')?.textContent).to.contain('Review');
  });

  it('keeps the wizard usable when the resolution check fails', async () => {
    mocks.resolution.mockReturnValue(
      throwError(() => ({ response: 'resolution backend down' }))
    );
    const { dialog } = await open();
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Could not check TestEnvironment');
    expect(dialog.textContent).to.contain('resolution backend down');
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Review');
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

describe('input provenance and submission retries', () => {
  it('keeps mixed sources and sensitive defaults hidden consistently through review', async () => {
    const manifest = {
      ...template,
      Parameters: template.Parameters.map(parameter =>
        parameter.Name === 'credential'
          ? { ...parameter, Default: 'must-not-appear' }
          : parameter
      )
    };
    mocks.resolution.mockReturnValue(
      of({
        Parameters: [
          {
            Name: 'location',
            Status: TerraformParameterResolutionStatus.Environment
          },
          { Name: 'enabled', Status: TerraformParameterResolutionStatus.Unset },
          {
            Name: 'credential',
            Status: TerraformParameterResolutionStatus.Default
          }
        ]
      })
    );
    const { dialog } = await open(manifest);
    await click(dialog, 'Continue');
    const secret = dialog.querySelector('[data-sensitive="true"]')!;
    expect(secret.textContent).to.contain('Module default');
    expect(secret.textContent).not.to.contain('sensitive environment property');
    expect(dialog.textContent).not.to.contain('must-not-appear');
    expect(dialog.textContent).to.contain('DOrc-resolved: 1');
    expect(dialog.textContent).to.contain('Module default: 1');
    expect(dialog.textContent).to.contain('Not set: 1');
    await click(dialog, 'Continue');
    const review = dialog.querySelector('table')!.textContent!;
    expect(review).to.contain('Module default (sensitive, hidden)');
    expect(review).to.contain('Not set');
    expect(dialog.textContent).not.to.contain('must-not-appear');
    expect(dialog.textContent).not.to.contain('All inherited');
  });

  it('labels a failed resolution unchecked in cards, footer and review', async () => {
    mocks.resolution.mockReturnValue(throwError(() => new Error('offline')));
    const { dialog } = await open();
    await click(dialog, 'Continue');
    expect(dialog.textContent).to.contain('Not checked: 3');
    expect(
      dialog.querySelector('[data-sensitive="true"]')!.textContent
    ).to.contain('Not checked');
    await click(dialog, 'Continue');
    expect(dialog.querySelector('table')!.textContent).to.contain(
      'Not checked'
    );
    expect(dialog.textContent).not.to.contain('All inherited');
  });

  it('reuses a submission key after a lost response but changes it when inputs change', async () => {
    mocks.instantiate.mockReturnValue(
      throwError(() => new Error('response lost'))
    );
    const { dialog } = await open();
    await click(dialog, 'Continue');
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    await click(dialog, 'Submit plan request');
    const firstKey = mocks.instantiate.mock.calls[0][0].body.SubmissionKey;
    expect(firstKey).to.match(/^[0-9a-f-]{36}$/);
    expect(mocks.instantiate.mock.calls[1][0].body.SubmissionKey).to.equal(
      firstKey
    );
    await click(dialog, 'Back');
    await click(dialog, 'Override location for this request');
    dialog.querySelector<TextField>('.param-field vaadin-text-field')!.value =
      'westus';
    await settle();
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[2][0].body.SubmissionKey).not.to.equal(
      firstKey
    );
  });
});

describe('resource-group convenience fill', () => {
  const groupTemplate: TerraformTemplateManifest = {
    ...template,
    Parameters: [
      {
        Name: 'resource_group_name',
        Type: TerraformParameterType.String,
        Required: true,
        Sensitive: false
      }
    ]
  };
  const group: CloudResourceApiModel = {
    Name: 'Friendly label (collision-qualified)',
    ResourceType: 'azurerm_resource_group',
    Provider: 'Azure',
    ResourceIdentifier:
      '/subscriptions/11111111-2222-3333-4444-555555555555/resourceGroups/rg-real'
  };
  const resolution = (Status: TerraformParameterResolutionStatus) =>
    of({
      Parameters: [
        {
          Name: 'resource_group_name',
          Required: true,
          Sensitive: false,
          Status
        }
      ]
    });
  beforeEach(() => {
    mocks.cloudResources.mockReturnValue(of([group]));
    mocks.resolution.mockReturnValue(
      resolution(TerraformParameterResolutionStatus.Missing)
    );
  });
  const input = (dialog: ParentNode) =>
    Array.from(dialog.querySelectorAll<TextField>('vaadin-text-field')).find(
      field => field.label === 'resource_group_name'
    ) ?? null;

  it('uses the ARM group segment rather than the editable display name', async () => {
    const { dialog } = await open(groupTemplate);
    expect(mocks.cloudResources.mock.calls).to.have.length(0);
    await click(dialog, 'Continue');
    expect(mocks.cloudResources.mock.calls[0][0]).to.deep.equal({ envId: 9 });
    expect(input(dialog)?.value).to.equal('rg-real');
    expect(dialog.textContent).not.to.contain('Friendly label');
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.Parameters).to.deep.equal({
      resource_group_name: 'rg-real'
    });
  });

  for (const status of [
    TerraformParameterResolutionStatus.Environment,
    TerraformParameterResolutionStatus.Default
  ]) {
    it(`does not shadow a ${status} value or retrieve its contents`, async () => {
      mocks.resolution.mockReturnValue(resolution(status));
      const { dialog } = await open(groupTemplate);
      await click(dialog, 'Continue');
      expect(mocks.cloudResources.mock.calls).to.have.length(0);
      expect(input(dialog)).to.equal(null);
      await click(dialog, 'Continue');
      await click(dialog, 'Submit plan request');
      expect(mocks.instantiate.mock.calls[0][0].body.Parameters).to.deep.equal(
        {}
      );
    });
  }

  for (const resources of [
    [],
    [group, { ...group, ResourceIdentifier: null }],
    [{ ...group, ResourceIdentifier: null }],
    [{ ...group, ResourceIdentifier: 'friendly label' }],
    [{ ...group, ResourceType: 'storage_account' }]
  ]) {
    it(`leaves ambiguous or unusable groups unset: ${JSON.stringify(resources)}`, async () => {
      mocks.cloudResources.mockReturnValue(of(resources));
      const { dialog } = await open(groupTemplate);
      await click(dialog, 'Continue');
      expect(input(dialog)).to.equal(null);
      await click(dialog, 'Continue');
      expect(dialog.querySelector('[role="alert"]')?.textContent).to.contain(
        'resource_group_name: not set'
      );
      expect(mocks.instantiate.mock.calls).to.have.length(0);
    });
  }

  it('leaves the input inherited when resolution cannot be checked', async () => {
    mocks.resolution.mockReturnValue(
      throwError(() => new Error('unavailable'))
    );
    const { dialog } = await open(groupTemplate);
    await click(dialog, 'Continue');
    expect(mocks.cloudResources.mock.calls).to.have.length(0);
    expect(input(dialog)).to.equal(null);
  });

  it('preserves a local override entered while the cloud lookup is pending', async () => {
    const response = new Subject<CloudResourceApiModel[]>();
    mocks.cloudResources.mockReturnValue(response);
    const { dialog } = await open(groupTemplate);
    await click(dialog, 'Continue');
    await click(dialog, 'Override resource_group_name for this request');
    input(dialog)!.value = 'rg-local';
    await settle();
    response.next([group]);
    response.complete();
    await settle();
    expect(input(dialog)?.value).to.equal('rg-local');
  });

  it('ignores an old lookup even after switching back to its original environment', async () => {
    const stale = new Subject<CloudResourceApiModel[]>();
    mocks.environments.mockReturnValue(
      of({
        Items: [
          environment,
          { EnvironmentId: 10, EnvironmentName: 'OtherEnvironment' }
        ]
      })
    );
    mocks.cloudResources.mockReturnValueOnce(stale);
    const { dialog } = await open(groupTemplate);
    await click(dialog, 'Continue');
    await click(dialog, 'Back');
    const environments = dialog.querySelector<ComboBox>(
      'vaadin-combo-box[label="DOrc environment"]'
    )!;
    environments.value = 'OtherEnvironment';
    await settle();
    environments.value = environment.EnvironmentName;
    await settle();
    mocks.resolution.mockReturnValue(
      resolution(TerraformParameterResolutionStatus.Environment)
    );
    await click(dialog, 'Continue');
    stale.next([group]);
    stale.complete();
    await settle();
    expect(input(dialog)).to.equal(null);
    await click(dialog, 'Continue');
    await click(dialog, 'Submit plan request');
    expect(mocks.instantiate.mock.calls[0][0].body.Parameters).to.deep.equal(
      {}
    );
  });
});

describe('module catalog discovery', () => {
  it('distinguishes workload authorization from the Azure backend for both providers', async () => {
    const host = await fixture<InstanceType<typeof PageModuleCatalog>>(
      html`<page-module-catalog></page-module-catalog>`
    );
    await settle();
    const guidance =
      host.shadowRoot!.querySelector('.setup-body')!.textContent!;
    expect(guidance).to.contain('Storage Blob Data Contributor');
    expect(guidance).to.contain(
      'Aiven tokens authenticate the workload provider only'
    );
    expect(guidance).not.to.contain('target subscription only');
  });
  it('groups versions into one card whose selector defaults to the latest active version', async () => {
    mocks.templates.mockReturnValue(
      of([
        template,
        { ...template, Version: '0.9.0', Deprecated: true },
        {
          ...template,
          Name: 'legacy-module',
          Version: '0.1.0',
          Deprecated: true
        }
      ])
    );
    const host = await fixture<PageModuleCatalog>(
      html`<page-module-catalog></page-module-catalog>`
    );
    await settle();
    const cards = () =>
      Array.from(host.shadowRoot!.querySelectorAll('article.card'));
    // storage-account's two versions share one card; the fully-deprecated
    // legacy-module is hidden until the Deprecated filter is enabled.
    expect(cards()).to.have.length(1);
    const select = host.shadowRoot!.querySelector<HTMLSelectElement>(
      'select.version-select'
    )!;
    expect(select.value).to.equal('1.0.0');
    expect(Array.from(select.options).map(o => o.value)).to.deep.equal([
      '1.0.0',
      '0.9.0'
    ]);
    host
      .shadowRoot!.querySelector<HTMLElement>(
        'vaadin-button[aria-pressed][class="chip"]:last-of-type'
      )!
      .click();
    await settle();
    expect(cards()).to.have.length(2);
    host.shadowRoot!.querySelector<TextField>('vaadin-text-field')!.value =
      '0.9';
    await settle();
    // Matches the 0.9.0 version of storage-account but nothing on legacy-module.
    expect(cards()).to.have.length(1);
    expect(host.filteredGroups.map(g => g.name)).to.deep.equal([
      'storage-account'
    ]);
  });

  it('switches the card and open detail panel to the selected version', async () => {
    mocks.templates.mockReturnValue(
      of([template, { ...template, Version: '0.9.0', Deprecated: true }])
    );
    const host = await fixture<PageModuleCatalog>(
      html`<page-module-catalog></page-module-catalog>`
    );
    await settle();
    host
      .shadowRoot!.querySelector<HTMLElement>(
        'article.card vaadin-button[theme="tertiary"]'
      )!
      .click();
    await settle();
    const select = host.shadowRoot!.querySelector<HTMLSelectElement>(
      'select.version-select'
    )!;
    select.value = '0.9.0';
    select.dispatchEvent(new Event('change'));
    await settle();
    expect(host.filteredGroups[0].manifest.Version).to.equal('0.9.0');
    const card = host.shadowRoot!.querySelector('article.card')!;
    expect(card.textContent).to.contain('Deprecated');
    const detail = host.shadowRoot!.querySelector('.detail')!;
    expect(detail.textContent).to.contain('v0.9.0');
  });

  it('opens the detail panel inline next to its card and toggles it closed', async () => {
    const host = await fixture<PageModuleCatalog>(
      html`<page-module-catalog></page-module-catalog>`
    );
    await settle();
    expect(host.shadowRoot!.querySelector('.detail')).to.equal(null);
    const toggle = () =>
      host
        .shadowRoot!.querySelector<HTMLElement>(
          'article.card vaadin-button[theme="tertiary"]'
        )!
        .click();
    toggle();
    await settle();
    const detail = host.shadowRoot!.querySelector('.detail')!;
    // The panel renders inside the cards grid, immediately after its card.
    expect(detail.parentElement?.classList.contains('cards')).to.equal(true);
    expect(detail.previousElementSibling?.classList.contains('card')).to.equal(
      true
    );
    expect(detail.textContent).to.contain('credential');
    expect(detail.querySelectorAll('.badge-sensitive')).to.have.length(1);
    expect(detail.querySelectorAll('.badge-required')).to.have.length(2);
    button(detail, 'Use in code').click();
    await settle();
    expect(detail.querySelector('pre')?.textContent).to.equal(
      'TerraformSourceType = Catalog\nTerraformTemplateName = storage-account\nTerraformTemplateVersion = 1.0.0'
    );
    toggle();
    await settle();
    expect(host.shadowRoot!.querySelector('.detail')).to.equal(null);
  });
});

describe('override validation mirrors the manifest constraints', () => {
  const base = {
    Name: 'account_name',
    Type: TerraformParameterType.String,
    Required: true,
    Sensitive: false
  };

  it('rejects values that do not match the manifest pattern', () => {
    const p = { ...base, Pattern: '^[a-z0-9]{3,24}$' };
    expect(overrideError(p, 'Trading-Storage')).to.contain('pattern');
    expect(overrideError(p, 'tradingstorage')).to.equal(null);
  });

  it('requires a value only when the input is required', () => {
    expect(overrideError(base, '')).to.contain('Enter a value');
    expect(overrideError({ ...base, Required: false }, '')).to.equal(null);
  });

  it('enforces allowed values and numeric bounds', () => {
    expect(
      overrideError({ ...base, AllowedValues: ['S0', 'S1'] }, 'S2')
    ).to.contain('S0, S1');
    const n = {
      ...base,
      Type: TerraformParameterType.Number,
      Min: 1,
      Max: 4096
    };
    expect(overrideError(n, '0')).to.contain('at least 1');
    expect(overrideError(n, '5000')).to.contain('at most 4096');
    expect(overrideError(n, '10')).to.equal(null);
  });

  it('rejects explicitly blank optional scalars even when a default exists', () => {
    for (const Type of [
      TerraformParameterType.Number,
      TerraformParameterType.Bool
    ]) {
      for (const Default of [undefined, Type === 'Number' ? '2' : 'true']) {
        const p = { ...base, Type, Required: false, Default };
        for (const value of ['', ' ', '\t']) {
          expect(overrideError(p, value)).not.to.equal(null);
        }
      }
    }
    expect(
      overrideError({ ...base, Type: TerraformParameterType.Bool }, 'false')
    ).to.equal(null);
  });

  it('accepts whole numbers and exponents for Integer but rejects fractional values', () => {
    const p = { ...base, Type: TerraformParameterType.Number, Integer: true };
    for (const value of ['2', '2.0', '2e1', '20e-1', '-2', '0', '0e-1000']) {
      expect(overrideError(p, value)).to.equal(null);
    }
    for (const value of ['2.5', '2e-1', '2.0000000000000001', '1e-1000']) {
      expect(overrideError(p, value)).to.contain('integer');
    }
    expect(overrideError({ ...p, Integer: false }, '2.5')).to.equal(null);
  });

  it('defers an unparseable pattern to the server', () => {
    expect(overrideError({ ...base, Pattern: '(' }, 'anything')).to.equal(null);
  });

  it('aligns catalog card action buttons at the bottom across cards with different description lengths', async () => {
    mocks.templates.mockReturnValue(
      of([
        {
          ...template,
          Name: 'short-module',
          Description: 'Short description.'
        },
        {
          ...template,
          Name: 'long-module',
          Description:
            'Much longer description that wraps across multiple lines so the card in the same grid row is taller than its neighbour and the action buttons still align at the bottom.'
        }
      ])
    );
    const catalog = await fixture<PageModuleCatalog>(
      html`<page-module-catalog
        style="display:block;width:900px"
      ></page-module-catalog>`
    );
    await settle();
    const actions = Array.from(
      catalog.shadowRoot!.querySelectorAll<HTMLElement>('.card .card-actions')
    );
    expect(actions).to.have.length(2);
    expect(
      Math.max(
        parseFloat(getComputedStyle(actions[0]).marginTop),
        parseFloat(getComputedStyle(actions[1]).marginTop)
      )
    ).to.be.greaterThan(0);
    expect(
      Math.abs(
        actions[0].getBoundingClientRect().bottom -
          actions[1].getBoundingClientRect().bottom
      )
    ).to.be.lessThan(2);
  });

  describe('Terraform identity parity', () => {
    it('matches Monitor UTF-16 escaping and invariant ASCII case-folding', () => {
      const cases = [
        ['', 'unknown'],
        ['Prod EU', 'prod_0020eu'],
        ['Prod-EU', 'prod-eu'],
        ['Prod_EU', 'prod_005feu'],
        ['UT 02', 'ut_002002'],
        ['PROD', 'prod'],
        ['prod-eu-1', 'prod-eu-1'],
        ['a/b\\c.d', 'a_002fb_005cc_002ed'],
        ['ÉéİıK', '_00c9_00e9_0130_0131k'],
        ['🚀', '_d83d_de80']
      ];
      for (const [raw, expected] of cases) {
        expect(sanitizeTerraformStateKeyPart(raw), raw).to.equal(expected);
      }
    });

    it('extracts only a usable resource-group ARM identity', () => {
      const prefix =
        '/subscriptions/11111111-2222-3333-4444-555555555555/resourceGroups/';
      expect(resourceGroupNameFromArmId(`${prefix}rg-real`)).to.equal(
        'rg-real'
      );
      expect(
        resourceGroupNameFromArmId(`${prefix.toUpperCase()}RG-Real`)
      ).to.equal('RG-Real');
      for (const identity of [
        undefined,
        '',
        'friendly label',
        '/subscriptions/not-a-guid/resourceGroups/rg-real',
        `${prefix}`,
        `${prefix}rg-real/providers/Microsoft.Storage/storageAccounts/account`,
        `${prefix}rg-real?api-version=1`,
        `${prefix}rg-real.`,
        `${prefix}rg-real\n`
      ]) {
        expect(resourceGroupNameFromArmId(identity)).to.equal(null);
      }
    });
  });
});
