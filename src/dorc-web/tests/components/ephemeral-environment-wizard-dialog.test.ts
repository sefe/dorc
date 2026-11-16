import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { page } from 'vitest/browser';
import {
  contrastRatio,
  expect,
  fixture,
  html,
  setTheme,
  settle
} from '../_helpers';
import type { Checkbox } from '@vaadin/checkbox';
import type { NumberField } from '@vaadin/number-field';
import type { TextField } from '@vaadin/text-field';
import type {
  ComponentApiModel,
  EnvironmentApiModel,
  EnvironmentTemplateApiModel,
  EphemeralEnvironmentProvisionResponseApiModel,
  EphemeralEnvironmentTierPreviewResponseApiModel,
  ProjectApiModel
} from '../../src/apis/dorc-api';
import type { EphemeralEnvironmentWizardDialog } from '../../src/components/ephemeral-environment-wizard-dialog';

const mocks = vi.hoisted(() => ({
  getProjects: vi.fn(),
  getEnvironments: vi.fn(),
  getComponents: vi.fn(),
  getTemplates: vi.fn(),
  createTemplate: vi.fn(),
  updateTemplate: vi.fn(),
  deleteTemplate: vi.fn(),
  confirmPrompt: vi.fn(),
  previewTierParameters: vi.fn(),
  provision: vi.fn(),
  navigate: vi.fn()
}));

vi.mock('../../src/router/router', () => ({ navigate: mocks.navigate }));
vi.mock('../../src/components/confirm-prompt', () => ({
  confirmPrompt: mocks.confirmPrompt
}));
vi.mock('../../src/apis/dorc-api', async importOriginal => ({
  ...(await importOriginal<Record<string, unknown>>()),
  RefDataProjectsApi: class {
    refDataProjectsGet = mocks.getProjects;
  },
  RefDataEnvironmentsApi: class {
    refDataEnvironmentsGet = mocks.getEnvironments;
  },
  RefDataComponentsApi: class {
    refDataComponentsGet = mocks.getComponents;
  },
  EphemeralEnvironmentsApi: class {
    ephemeralEnvironmentsTemplatesGet = mocks.getTemplates;
    ephemeralEnvironmentsTemplatesPost = mocks.createTemplate;
    ephemeralEnvironmentsTemplatesTemplateIdPut = mocks.updateTemplate;
    ephemeralEnvironmentsTemplatesTemplateIdDelete = mocks.deleteTemplate;
    ephemeralEnvironmentsPreviewTierParametersPost =
      mocks.previewTierParameters;
    ephemeralEnvironmentsProvisionPost = mocks.provision;
  }
}));

await import('../../src/router/style-registrations');
await import('../../src/components/ephemeral-environment-wizard-dialog');

const sampleProjects: ProjectApiModel[] = [
  { ProjectId: 10, ProjectName: 'Traveler' },
  { ProjectId: 20, ProjectName: 'Billing' }
];

const sampleEnvironments: EnvironmentApiModel[] = [
  {
    EnvironmentId: 101,
    EnvironmentName: 'Traveler-NonProd-Base',
    EnvironmentIsProd: false,
    IsEphemeral: false
  },
  {
    EnvironmentId: 102,
    EnvironmentName: 'Traveler-Prod',
    EnvironmentIsProd: true,
    IsEphemeral: false
  },
  {
    EnvironmentId: 103,
    EnvironmentName: 'Traveler-Existing-Eph',
    EnvironmentIsProd: false,
    IsEphemeral: true
  }
];

const sampleComponents: ComponentApiModel[] = [
  {
    ComponentId: 1,
    ComponentName: 'Aiven-Postgres',
    ComponentType: 'Terraform' as any,
    Children: [
      {
        ComponentId: 2,
        ComponentName: 'Kafka-Topic',
        ComponentType: 'Terraform' as any
      }
    ]
  },
  {
    ComponentId: 3,
    ComponentName: 'SmokeTests',
    ComponentType: 'PowerShell' as any
  }
];

const sampleTemplate: EnvironmentTemplateApiModel = {
  Id: 55,
  ProjectId: 10,
  ProjectName: 'Traveler',
  Name: 'Traveler PR Blueprint',
  Description: 'Default PR template',
  BaseEnvironmentId: 101,
  DefaultTier: 'ephemeral',
  DefaultTtlMinutes: 120,
  MaxTtlMinutes: 480,
  ProvisionComponentIds: [1, 2],
  ValidationComponentIds: [3],
  TierParameterOverridesJson: JSON.stringify({
    ephemeral: { create_service: 'false' },
    qa: { create_service: 'true' }
  })
};

const samplePreview: EphemeralEnvironmentTierPreviewResponseApiModel = {
  EnvironmentTier: 'ephemeral',
  ComponentPreviews: [
    {
      ComponentId: 1,
      ComponentName: 'Aiven-Postgres',
      TemplateName: 'aiven-postgresql',
      IsSharedService: true,
      ResolvedParameters: {
        environment_tier: 'ephemeral',
        create_service: 'false',
        termination_protection: 'false'
      },
      ForcedInvariants: {
        termination_protection: 'false',
        service_termination_protection: 'false'
      }
    },
    {
      ComponentId: 2,
      ComponentName: 'Dedicated-Cosmos',
      IsSharedService: false,
      ResolvedParameters: {
        environment_tier: 'ephemeral',
        max_throughput: '400'
      },
      ForcedInvariants: {}
    },
    {
      ComponentId: 3,
      ComponentName: 'Dedicated-ServiceBus-SharedRG',
      IsSharedService: false,
      ResolvedParameters: {
        create_resource_group: 'false'
      },
      ForcedInvariants: {}
    }
  ]
};

function findButton(root: ParentNode, textMatch: string): HTMLElement {
  const found = Array.from(root.querySelectorAll('vaadin-button, button')).find(
    b =>
      b.textContent?.trim().includes(textMatch) ||
      b.getAttribute('title')?.includes(textMatch)
  );
  expect(found, `button "${textMatch}"`).not.to.equal(undefined);
  return found as HTMLElement;
}

describe('EphemeralEnvironmentWizardDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getProjects.mockReturnValue(of(sampleProjects));
    mocks.getEnvironments.mockReturnValue(of(sampleEnvironments));
    mocks.getComponents.mockReturnValue(of({ Items: sampleComponents }));
    mocks.getTemplates.mockReturnValue(of([sampleTemplate]));
    mocks.previewTierParameters.mockReturnValue(of(samplePreview));
  });

  it('opens wizard, filters out Prod and Ephemeral base environments, applies blueprint defaults, and renders live parameter preview', async () => {
    const el = await fixture<EphemeralEnvironmentWizardDialog>(html`
      <ephemeral-environment-wizard-dialog></ephemeral-environment-wizard-dialog>
    `);

    el.open({ projectName: 'Traveler', baseEnvironmentId: 101 });
    await settle(250);

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;
    expect(dialog).not.to.equal(null);

    // Non-prod filter: only Traveler-NonProd-Base should remain
    const nonProdEnvs = (
      el as unknown as { nonProdEnvironments: EnvironmentApiModel[] }
    ).nonProdEnvironments;
    expect(nonProdEnvs.length).to.equal(1);
    expect(nonProdEnvs[0].EnvironmentName).to.equal('Traveler-NonProd-Base');

    // Auto-generated environment name matches traveler-ephemeral-eph-*
    const envName = (el as unknown as { environmentName: string })
      .environmentName;
    expect(envName).to.match(/^traveler-ephemeral-eph-[a-z0-9]+$/);

    // Preview cards rendered with hegs-json-viewer and forced invariants
    const previewCards = dialog.querySelectorAll('.preview-comp-card');
    expect(previewCards.length).to.equal(3);
    expect(previewCards[0].textContent).to.contain('Shared Host Service');
    expect(previewCards[0].textContent).to.contain(
      'service_termination_protection = false'
    );
    expect(previewCards[1].textContent).to.contain('Dedicated Service');
    expect(previewCards[1].textContent).to.contain(
      'termination_protection = false'
    );
    expect(previewCards[2].textContent).to.contain(
      'Dedicated Service (Shared RG)'
    );
    expect(dialog.querySelector('hegs-json-viewer')).not.to.equal(null);
  });

  it('supports switching tiers, regenerating environment names, adding/removing ad-hoc overrides, and validating TTL cap', async () => {
    const el = await fixture<EphemeralEnvironmentWizardDialog>(html`
      <ephemeral-environment-wizard-dialog></ephemeral-environment-wizard-dialog>
    `);

    el.open({ projectName: 'Traveler' });
    await settle(250);

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;

    // Select QA tier card -> invalidates stale preview and disables Spin Up until preview resolves
    const qaCard = findButton(dialog, 'QA (qa)');
    qaCard.click();
    await el.updateComplete;
    expect(
      (el as unknown as { environmentName: string }).environmentName
    ).to.match(/^traveler-qa-eph-/);
    expect(
      (findButton(dialog, 'Spin Up Environment') as HTMLButtonElement).disabled
    ).to.equal(true);
    qaCard.click();
    await settle(250);

    // Regenerate environment name
    findButton(dialog, 'Regenerate short environment name').click();
    await el.updateComplete;

    // Add ad-hoc override and remove it
    const textFields = Array.from(
      dialog.querySelectorAll('vaadin-text-field')
    ) as TextField[];
    const adhocKeyField = textFields.find(
      f => f.getAttribute('label') === 'Ad-Hoc Override Parameter'
    )!;
    const adhocValField = textFields.find(
      f => f.getAttribute('label') === 'Override Value'
    )!;

    adhocKeyField.value = 'kafka_partitions';
    adhocKeyField.dispatchEvent(
      new CustomEvent('value-changed', {
        detail: { value: 'kafka_partitions' }
      })
    );
    adhocValField.value = '6';
    adhocValField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: '6' } })
    );
    findButton(dialog, 'Add Override').click();
    await el.updateComplete;
    expect(
      (findButton(dialog, 'Spin Up Environment') as HTMLButtonElement).disabled
    ).to.equal(true);
    await settle(250);

    expect(dialog.querySelector('.override-chip')?.textContent).to.contain(
      'kafka_partitions = 6'
    );

    const removeChipBtn = dialog.querySelector(
      '.override-chip button'
    ) as HTMLButtonElement;
    removeChipBtn.click();
    await el.updateComplete;
    expect(dialog.querySelector('.override-chip')).to.equal(null);

    // Sensitive ad-hoc override values render vaadin-password-field and are masked in override chips
    adhocKeyField.value = 'Postgres.admin_password';
    adhocKeyField.dispatchEvent(
      new CustomEvent('value-changed', {
        detail: { value: 'Postgres.admin_password' }
      })
    );
    await el.updateComplete;
    const passwordField = dialog.querySelector(
      'vaadin-password-field[label="Override Value"]'
    ) as HTMLElement;
    expect(passwordField).not.to.equal(null);
    expect(passwordField.getAttribute('data-sensitive')).to.equal('true');
    const innerInput = passwordField.querySelector(
      'input[slot="input"]'
    ) as HTMLInputElement;
    expect(innerInput.type).to.equal('password');
    (passwordField as unknown as { value: string }).value =
      'super-secret-value';
    passwordField.dispatchEvent(
      new CustomEvent('value-changed', {
        detail: { value: 'super-secret-value' }
      })
    );
    findButton(dialog, 'Add Override').click();
    await el.updateComplete;
    expect(dialog.querySelector('.override-chip')?.textContent).to.contain(
      'Postgres.admin_password = ***REDACTED***'
    );
    expect(dialog.querySelector('.override-chip')?.textContent).not.to.contain(
      'super-secret-value'
    );
    (
      dialog.querySelector('.override-chip button') as HTMLButtonElement
    ).click();
    await el.updateComplete;

    // Exceed max TTL cap (480m on sampleTemplate) -> warning banner and Spin Up button disabled
    const ttlField = Array.from(
      dialog.querySelectorAll('vaadin-number-field')
    ).find(
      f => f.getAttribute('label') === 'Custom TTL (minutes)'
    ) as NumberField;
    ttlField.value = '999';
    ttlField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: '999' } })
    );
    await el.updateComplete;

    const warningText =
      dialog
        .querySelector('.warning-banner')
        ?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(warningText).to.contain(
      'exceeds the Blueprint Template policy cap of 480 minutes'
    );
    const spinUpBtn = findButton(
      dialog,
      'Spin Up Environment'
    ) as HTMLButtonElement;
    expect(spinUpBtn.disabled).to.equal(true);
  });

  it('creates and edits Blueprint Templates in the inline Template Builder', async () => {
    const el = await fixture<EphemeralEnvironmentWizardDialog>(html`
      <ephemeral-environment-wizard-dialog></ephemeral-environment-wizard-dialog>
    `);

    el.open({ projectName: 'Traveler' });
    await settle(250);

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;

    // Open Edit Blueprint Template
    findButton(dialog, 'Edit Blueprint Template').click();
    await el.updateComplete;
    expect(dialog.querySelector('.builder-panel')).not.to.equal(null);

    // Toggle provision & validation component checkboxes and add a tier override
    const checkboxes = Array.from(
      dialog.querySelectorAll('.builder-panel vaadin-checkbox')
    ) as Checkbox[];
    expect(checkboxes.length).to.be.greaterThan(0);
    checkboxes[0].dispatchEvent(
      new CustomEvent('checked-changed', { detail: { value: false } })
    );
    checkboxes[0].dispatchEvent(
      new CustomEvent('checked-changed', { detail: { value: true } })
    );
    const valCheckbox = checkboxes[checkboxes.length - 1];
    valCheckbox.dispatchEvent(
      new CustomEvent('checked-changed', { detail: { value: false } })
    );
    valCheckbox.dispatchEvent(
      new CustomEvent('checked-changed', { detail: { value: true } })
    );

    const builderFields = Array.from(
      dialog.querySelectorAll('.builder-panel vaadin-text-field')
    ) as TextField[];
    const overrideKeyField = builderFields.find(f =>
      f.getAttribute('label')?.startsWith('Parameter Key')
    )!;
    const overrideValField = builderFields.find(
      f => f.getAttribute('label') === 'Parameter Value'
    )!;
    overrideKeyField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'aiven_plan' } })
    );
    overrideValField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: 'startup-4' } })
    );
    findButton(dialog, 'Set Override').click();
    await el.updateComplete;

    mocks.updateTemplate.mockReturnValueOnce(of(sampleTemplate));
    findButton(dialog, 'Save Blueprint Template').click();
    await settle();
    expect(mocks.updateTemplate.mock.calls).to.have.length(1);

    // Open New Blueprint Template and save via POST
    findButton(dialog, 'New Blueprint Template').click();
    await el.updateComplete;
    mocks.createTemplate.mockReturnValueOnce(
      of({ ...sampleTemplate, Id: 77, Name: 'New Blueprint' })
    );
    findButton(dialog, 'Save Blueprint Template').click();
    await settle();
    expect(mocks.createTemplate.mock.calls).to.have.length(1);

    // Delete Blueprint Template (cancel, error, and success paths)
    mocks.confirmPrompt.mockResolvedValueOnce(false);
    findButton(dialog, 'Delete Blueprint Template').click();
    await settle();
    expect(mocks.deleteTemplate.mock.calls).to.have.length(0);

    mocks.confirmPrompt.mockResolvedValueOnce(true);
    mocks.deleteTemplate.mockReturnValueOnce(
      throwError(() => new Error('Delete failed'))
    );
    findButton(dialog, 'Delete Blueprint Template').click();
    await settle();
    expect(mocks.deleteTemplate.mock.calls).to.have.length(1);

    mocks.confirmPrompt.mockResolvedValueOnce(true);
    mocks.deleteTemplate.mockReturnValueOnce(of(true));
    findButton(dialog, 'Delete Blueprint Template').click();
    await settle();
    expect(mocks.deleteTemplate.mock.calls).to.have.length(2);
  });

  it('submits provisioning request, dispatches environment-created, and navigates to monitor page', async () => {
    const el = await fixture<EphemeralEnvironmentWizardDialog>(html`
      <ephemeral-environment-wizard-dialog></ephemeral-environment-wizard-dialog>
    `);

    el.open({ projectName: 'Traveler' });
    await settle(250);

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;

    // Set optional build fields
    const buildNumField = Array.from(
      dialog.querySelectorAll('vaadin-text-field')
    ).find(
      f => f.getAttribute('label') === 'Build Number / Tag (Optional)'
    ) as TextField;
    buildNumField.dispatchEvent(
      new CustomEvent('value-changed', { detail: { value: '20261114.9' } })
    );
    await el.updateComplete;

    const provisionResp: EphemeralEnvironmentProvisionResponseApiModel = {
      Environment: {
        EnvironmentId: 900,
        EnvironmentName: 'traveler-ephemeral-eph-test',
        IsEphemeral: true,
        EphemeralState: 'Provisioning'
      },
      ProvisionRequestIds: [8001, 8002]
    };
    mocks.provision.mockReturnValueOnce(of(provisionResp));

    const createdEvents: CustomEvent[] = [];
    el.addEventListener('environment-created', e =>
      createdEvents.push(e as CustomEvent)
    );

    findButton(dialog, 'Spin Up Environment').click();
    await settle();

    expect(mocks.provision.mock.calls).to.have.length(1);
    expect(createdEvents.length).to.equal(1);
    expect(mocks.navigate.mock.calls[0][0]).to.equal(
      '/environment/traveler-ephemeral-eph-test/monitor'
    );

    // Also cover close(), validation guards, error handlers, manual name edit, TTL preset click, and fallback preview
    el.open();
    await settle(250);

    const internal = el as unknown as {
      safeParseJsonObject: (raw: string) => Record<string, unknown>;
      selectProject: (p: ProjectApiModel | undefined) => void;
      openEditTemplateBuilder: () => void;
      cancelTemplateBuilder: () => void;
      addBuilderTierOverride: () => void;
      saveBlueprintTemplate: () => void;
      addAdhocOverride: () => void;
      submitProvision: () => void;
      selectedProject: ProjectApiModel | undefined;
      selectedTemplate: EnvironmentTemplateApiModel | undefined;
      environmentName: string;
      builderName: string;
    };

    expect(internal.safeParseJsonObject('')).to.deep.equal({});
    expect(internal.safeParseJsonObject('{invalid')).to.deep.equal({});
    expect(internal.safeParseJsonObject('null')).to.deep.equal({});

    // Cover isSensitiveOverrideKey branches (: prefix, mixed-case redacted preview key, empty key, previewLoading)
    const sensHelper = el as unknown as {
      isSensitiveOverrideKey: (k: string) => boolean;
      previewData: unknown;
      previewLoading: boolean;
    };
    expect(sensHelper.isSensitiveOverrideKey('   ')).to.equal(false);
    sensHelper.previewData = {
      EnvironmentTier: 'dv',
      ComponentPreviews: [
        {
          ComponentName: 'Postgres',
          ResolvedParameters: {
            CustomCred: '***REDACTED***',
            replicas: '1'
          }
        }
      ]
    };
    expect(sensHelper.isSensitiveOverrideKey('Postgres:CustomCred')).to.equal(
      true
    );
    expect(sensHelper.isSensitiveOverrideKey('Postgres:replicas')).to.equal(
      false
    );
    sensHelper.previewLoading = true;
    expect(
      sensHelper.isSensitiveOverrideKey('Postgres:unknown_param')
    ).to.equal(true);
    sensHelper.previewLoading = false;
    (sensHelper as unknown as { previewError: string }).previewError =
      'Failed to load preview';
    expect(
      sensHelper.isSensitiveOverrideKey('Postgres:unknown_param')
    ).to.equal(true);
    (sensHelper as unknown as { previewError: string }).previewError = '';
    sensHelper.previewData = null;
    expect(
      sensHelper.isSensitiveOverrideKey('Postgres:unknown_param')
    ).to.equal(true);

    // Empty override keys are no-ops
    internal.addBuilderTierOverride();
    internal.addAdhocOverride();

    // Builder component ordering preserves projectComponents hierarchy order and handles unlisted IDs
    (
      el as unknown as {
        projectComponents: Array<{ ComponentId?: number }>;
        builderProvisionComponentIds: number[];
        toggleBuilderProvisionComponent: (id: number, checked: boolean) => void;
      }
    ).projectComponents = [
      { ComponentId: 10 },
      { ComponentId: 20 },
      { ComponentId: undefined }
    ];
    (
      el as unknown as { builderProvisionComponentIds: number[] }
    ).builderProvisionComponentIds = [99, 20];
    (
      el as unknown as {
        toggleBuilderProvisionComponent: (id: number, checked: boolean) => void;
      }
    ).toggleBuilderProvisionComponent(10, true);
    expect(
      (el as unknown as { builderProvisionComponentIds: number[] })
        .builderProvisionComponentIds
    ).to.deep.equal([10, 20, 99]);

    // Validation guards on saveBlueprintTemplate and submitProvision
    internal.builderName = '   ';
    internal.saveBlueprintTemplate();
    internal.environmentName = '   ';
    internal.submitProvision();
    // Clear selectedBaseEnvironmentId while template has BaseEnvironmentId -> getEffectiveBaseEnvironmentIdPayload returns 0
    (
      el as unknown as {
        selectedBaseEnvironmentId: number | null;
        getEffectiveBaseEnvironmentIdPayload: () => number | null;
      }
    ).selectedBaseEnvironmentId = null;
    expect(
      (
        el as unknown as {
          getEffectiveBaseEnvironmentIdPayload: () => number | null;
        }
      ).getEffectiveBaseEnvironmentIdPayload()
    ).to.equal(0);
    internal.selectedTemplate = undefined;
    (
      el as unknown as {
        deleteBlueprintTemplate: () => void;
      }
    ).deleteBlueprintTemplate();
    expect(
      (
        el as unknown as {
          getEffectiveBaseEnvironmentIdPayload: () => number | null;
        }
      ).getEffectiveBaseEnvironmentIdPayload()
    ).to.equal(null);
    internal.selectProject(undefined);
    internal.submitProvision();
    internal.openEditTemplateBuilder();

    // Fallback preview when ComponentPreviews is empty and error on preview/provision/load
    mocks.previewTierParameters.mockReturnValueOnce(
      of({ EnvironmentTier: 'dv', ComponentPreviews: [] })
    );
    internal.selectProject(sampleProjects[0]);
    await settle(250);

    mocks.previewTierParameters.mockReturnValueOnce(
      throwError(() => new Error('Preview error'))
    );
    (
      el as unknown as { fetchTierParameterPreview: () => void }
    ).fetchTierParameterPreview();
    await settle(50);

    mocks.provision.mockReturnValueOnce(
      throwError(() => new Error('Provision failed'))
    );
    internal.environmentName = 'traveler-err-env';
    internal.submitProvision();
    await settle(50);

    el.close();
    expect(el.opened).to.equal(false);
  });

  it('uses wide dialog theme without horizontal clipping, aligns form rows on desktop, and renders blueprint summary and empty-state guidance in light and dark themes', async () => {
    await page.viewport(1280, 900);
    const el = await fixture<EphemeralEnvironmentWizardDialog>(html`
      <ephemeral-environment-wizard-dialog></ephemeral-environment-wizard-dialog>
    `);

    el.open({ projectName: 'Traveler', baseEnvironmentId: 101 });
    await settle(250);

    const dialog = el.shadowRoot!.querySelector('vaadin-dialog')!;
    expect(dialog.getAttribute('theme')).to.contain('wide');

    const overlay = dialog.shadowRoot?.querySelector('vaadin-dialog-overlay');
    const overlayPart = overlay?.shadowRoot?.querySelector(
      '[part="overlay"]'
    ) as HTMLElement | null;
    const wizardContainer = dialog.querySelector(
      '.wizard-container'
    ) as HTMLElement;
    expect(wizardContainer).not.to.equal(null);
    expect(wizardContainer.scrollWidth).to.be.at.most(
      wizardContainer.clientWidth + 2
    );

    if (overlayPart) {
      const overlayRect = overlayPart.getBoundingClientRect();
      expect(overlayRect.width).to.be.greaterThan(900);
      const containerRect = wizardContainer.getBoundingClientRect();
      expect(containerRect.right).to.be.at.most(overlayRect.right + 2);

      const newTemplateBtn = findButton(dialog, 'New Blueprint Template');
      expect(newTemplateBtn.getBoundingClientRect().right).to.be.at.most(
        overlayRect.right + 2
      );
    }

    // Step 2 input fields align horizontally at top despite wrapping helper text
    const nameField = Array.from(
      dialog.querySelectorAll('vaadin-text-field')
    ).find(
      f => f.getAttribute('label') === 'Ephemeral Environment Name'
    ) as HTMLElement;
    const baseEnvCombo = Array.from(
      dialog.querySelectorAll('vaadin-combo-box')
    ).find(
      f => f.getAttribute('label') === 'Base Environment (Non-Prod Parent)'
    ) as HTMLElement;
    expect(
      Math.abs(
        nameField.getBoundingClientRect().top -
          baseEnvCombo.getBoundingClientRect().top
      )
    ).to.be.at.most(2);

    // Blueprint summary strip displays template metadata
    const summary = dialog.querySelector('.blueprint-summary') as HTMLElement;
    expect(summary).not.to.equal(null);
    const normalizedSummary = summary.textContent?.replace(/\s+/g, ' ');
    expect(normalizedSummary).to.contain('Traveler PR Blueprint');
    expect(normalizedSummary).to.contain('Provision Components: 2');

    // Verify WCAG contrast in both light and dark mode
    for (const theme of ['light', 'dark'] as const) {
      setTheme(theme);
      await settle(30);
      const sectionTitle = dialog.querySelector(
        '.section-title'
      ) as HTMLElement;
      expect(
        contrastRatio(sectionTitle, getComputedStyle(sectionTitle).color)
      ).to.be.at.least(4.5);
    }

    // Empty-state callout when project has 0 blueprint templates
    mocks.getTemplates.mockReturnValueOnce(of([]));
    const billingProject = sampleProjects[1];
    (
      el as unknown as { selectProject: (p: ProjectApiModel) => void }
    ).selectProject(billingProject);
    await settle(250);
    expect(dialog.querySelector('.blueprint-summary')).to.equal(null);
    expect(dialog.textContent).to.contain(
      'No Blueprint Templates are saved for'
    );
  });
});
