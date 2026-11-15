import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/dialog';
import {
  dialogFooterRenderer,
  dialogHeaderRenderer,
  dialogRenderer
} from '@vaadin/dialog/lit';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { Notification } from '@vaadin/notification';
import '@vaadin/number-field';
import '@vaadin/password-field';
import '@vaadin/text-field';
import { css, html, LitElement, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { createRef, ref, Ref } from 'lit/directives/ref.js';
import type { Subscription } from 'rxjs';
import {
  ComponentApiModel,
  ComponentApiModelTemplateApiModel,
  EnvironmentApiModel,
  EnvironmentTemplateApiModel,
  EphemeralComponentParameterPreviewApiModel,
  EphemeralEnvironmentProvisionRequestApiModel,
  EphemeralEnvironmentProvisionResponseApiModel,
  EphemeralEnvironmentsApi,
  EphemeralEnvironmentTierPreviewResponseApiModel,
  ProjectApiModel,
  RefDataComponentsApi,
  RefDataEnvironmentsApi,
  RefDataProjectsApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { confirmPrompt } from './confirm-prompt';
import { navigate } from '../router/router';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import './hegs-json-viewer';
import { HegsJsonViewer } from './hegs-json-viewer';
import { ErrorNotification } from './notifications/error-notification';
import './notifications/error-notification';

export interface WizardTierCardInfo {
  tier: string;
  title: string;
  strategyBadge: string;
  strategyType: 'shared' | 'dedicated' | 'perf';
  summary: string;
  specs: string[];
}

export const WIZARD_TIER_CARDS: WizardTierCardInfo[] = [
  {
    tier: 'ephemeral',
    title: 'Ephemeral PR (ephemeral)',
    strategyBadge: 'Shared Host Services',
    strategyType: 'shared',
    summary:
      'Fastest spin-up (~30s). Reuses shared non-prod host clusters with isolated schemas, topics, and databases.',
    specs: [
      'create_service = false (shared Aiven PG/CH)',
      'traveler-unstable-dev Kafka (1 partition / 1h retention)',
      '400 RU/s Cosmos / Basic SKU'
    ]
  },
  {
    tier: 'dv',
    title: 'Dev (dv)',
    strategyBadge: 'Shared Host Services',
    strategyType: 'shared',
    summary:
      'Development integration tier backed by shared host services and lightweight topic/database isolation.',
    specs: [
      'create_service = false (shared Aiven PG/CH)',
      'traveler-unstable-dev Kafka (1 partition / 1h retention)',
      '400 RU/s Cosmos / Basic SKU'
    ]
  },
  {
    tier: 'qa',
    title: 'QA (qa)',
    strategyBadge: 'Dedicated Non-Prod Services',
    strategyType: 'dedicated',
    summary:
      'Dedicated non-production databases and multi-partition messaging for functional and integration testing.',
    specs: [
      'create_service = true (Aiven startup-4)',
      'traveler-non-prod Kafka (3 partitions / 24h retention)',
      '1000 RU/s Cosmos / Standard SKU'
    ]
  },
  {
    tier: 'ut',
    title: 'UAT (ut)',
    strategyBadge: 'Dedicated Non-Prod Services',
    strategyType: 'dedicated',
    summary:
      'Dedicated higher-memory non-prod instances for user acceptance and pre-release verification.',
    specs: [
      'create_service = true (Aiven startup-16)',
      'traveler-non-prod Kafka (3 partitions / 24h retention)',
      '1000 RU/s Cosmos / Standard SKU'
    ]
  },
  {
    tier: 'pp',
    title: 'Perf / Benchmark (pp)',
    strategyBadge: 'Perf / Load Benchmark',
    strategyType: 'perf',
    summary:
      'Production-like sizing with isolated resource group and high-throughput partitions for load benchmarking.',
    specs: [
      'create_service = true (business-8 / business-16)',
      'traveler-non-prod Kafka (6 partitions / 7d retention)',
      '4000 RU/s Cosmos / Premium SKU'
    ]
  }
];

const TTL_PRESETS = [
  { label: '1h', minutes: 60 },
  { label: '2h', minutes: 120 },
  { label: '4h', minutes: 240 },
  { label: '8h', minutes: 480 },
  { label: '24h', minutes: 1440 }
];

@customElement('ephemeral-environment-wizard-dialog')
export class EphemeralEnvironmentWizardDialog extends LitElement {
  @property({ type: Boolean }) opened = false;

  @state() private projects: ProjectApiModel[] = [];

  @state() private selectedProject: ProjectApiModel | undefined;

  @state() private nonProdEnvironments: EnvironmentApiModel[] = [];

  @state() private templates: EnvironmentTemplateApiModel[] = [];

  @state() private selectedTemplate: EnvironmentTemplateApiModel | undefined;

  @state() private projectComponents: ComponentApiModel[] = [];

  @state() private templateBuilderMode: 'none' | 'create' | 'edit' = 'none';

  @state() private builderName = '';

  @state() private builderDescription = '';

  @state() private builderDefaultTier = 'ephemeral';

  @state() private builderBaseEnvironmentId: number | null = null;

  @state() private builderDefaultTtlMinutes = 240;

  @state() private builderMaxTtlMinutes = 1440;

  @state() private builderProvisionComponentIds: number[] = [];

  @state() private builderValidationComponentIds: number[] = [];

  @state() private builderTierOverridesJson = '{}';

  @state() private builderOverrideKey = '';

  @state() private builderOverrideValue = '';

  @state() private builderSaving = false;

  @state() private environmentName = '';

  @state() private nameManuallyEdited = false;

  @state() private shortSuffix = 'a1b2';

  @state() private selectedBaseEnvironmentId: number | null = null;

  @state() private selectedTier = 'ephemeral';

  @state() private ttlMinutes = 240;

  @state() private runValidationComponents = true;

  @state() private buildNum = '';

  @state() private buildUrl = '';

  @state() private buildText = '';

  @state() private adhocOverrides: Record<string, string> = {};

  @state() private adhocKey = '';

  @state() private adhocValue = '';

  @state() private previewLoading = false;

  @state() private previewData:
    EphemeralEnvironmentTierPreviewResponseApiModel | undefined;

  @state() private previewError = '';

  @state() private isProvisioning = false;

  @state() private templatesLoading = false;

  @state() private templatesLoadFailed = false;

  private previewDebounceTimer: number | undefined;

  private previewSubscription?: Subscription;

  private templatesSubscription?: Subscription;

  private componentsSubscription?: Subscription;

  private previewRequestSeq = 0;

  private builderJsonViewerRef: Ref<HegsJsonViewer> = createRef();

  static get styles() {
    return css`
      :host {
        display: contents;
      }

      .wizard-container {
        display: flex;
        flex-direction: column;
        gap: 18px;
        width: min(920px, 90vw);
        max-height: 76vh;
        overflow-y: auto;
        padding: 4px 6px 12px 2px;
        box-sizing: border-box;
      }

      .section-card {
        background: var(--dorc-bg-secondary, #ffffff);
        border: 1px solid var(--dorc-border-color, #e5e7eb);
        border-radius: 8px;
        padding: 14px 16px;
        display: flex;
        flex-direction: column;
        gap: 12px;
      }

      .section-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        flex-wrap: wrap;
      }

      .section-title {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        font-size: 0.95rem;
        font-weight: 600;
        color: var(--dorc-text-primary, #111827);
        margin: 0;
      }

      .step-num {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 22px;
        height: 22px;
        border-radius: 50%;
        background: var(--lumo-primary-color);
        color: var(--lumo-primary-contrast-color, #ffffff);
        font-size: 0.75rem;
        font-weight: 700;
      }

      .form-grid-2 {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
        gap: 12px;
        align-items: end;
      }

      .form-grid-3 {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 12px;
        align-items: end;
      }

      .info-banner {
        display: flex;
        align-items: flex-start;
        gap: 8px;
        padding: 8px 12px;
        border-radius: 6px;
        background: var(--lumo-primary-color-10pct);
        color: var(--dorc-text-primary, #1f2937);
        font-size: 0.8rem;
        line-height: 1.4;
      }

      .info-banner vaadin-icon {
        width: 16px;
        height: 16px;
        color: var(--lumo-primary-color);
        flex-shrink: 0;
        margin-top: 2px;
      }

      .warning-banner {
        display: flex;
        align-items: flex-start;
        gap: 8px;
        padding: 8px 12px;
        border-radius: 6px;
        background: var(--lumo-error-color-10pct);
        color: var(--lumo-error-text-color);
        font-size: 0.8rem;
        line-height: 1.4;
      }

      /* Blueprint Builder Panel */
      .builder-panel {
        border: 1px dashed var(--lumo-primary-color);
        border-radius: 8px;
        padding: 14px;
        background: var(--lumo-primary-color-10pct);
        display: flex;
        flex-direction: column;
        gap: 12px;
      }

      .components-picker {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
        gap: 6px;
        max-height: 165px;
        overflow-y: auto;
        padding: 8px;
        border-radius: 6px;
        background: var(--dorc-bg-secondary, #ffffff);
        border: 1px solid var(--dorc-border-color, #d1d5db);
      }

      .comp-chip {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 0.78rem;
      }

      .comp-type-tag {
        font-size: 0.65rem;
        padding: 1px 5px;
        border-radius: 4px;
        background: var(--lumo-contrast-10pct);
        color: var(--dorc-text-secondary);
      }

      /* Tier Cards */
      .tier-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(245px, 1fr));
        gap: 10px;
      }

      .tier-card {
        display: flex;
        flex-direction: column;
        gap: 6px;
        padding: 12px;
        border-radius: 8px;
        border: 2px solid var(--dorc-border-color, #e5e7eb);
        background: var(--dorc-bg-secondary, #ffffff);
        cursor: pointer;
        text-align: left;
        transition:
          border-color 0.15s ease,
          box-shadow 0.15s ease,
          background 0.15s ease;
      }

      .tier-card:hover {
        border-color: var(--lumo-primary-color-50pct);
      }

      .tier-card.selected {
        border-color: var(--lumo-primary-color);
        background: var(--lumo-primary-color-10pct);
        box-shadow: 0 2px 6px rgba(0, 0, 0, 0.06);
      }

      .tier-card-top {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 6px;
      }

      .tier-card-title {
        font-weight: 700;
        font-size: 0.88rem;
        color: var(--dorc-text-primary, #111827);
      }

      .strategy-pill {
        font-size: 0.68rem;
        font-weight: 600;
        padding: 2px 7px;
        border-radius: 999px;
      }

      .strategy-shared {
        background: #e0f2fe;
        color: #0369a1;
      }

      .strategy-dedicated {
        background: #fef3c7;
        color: #92400e;
      }

      .strategy-perf {
        background: #fce7f3;
        color: #9d174d;
      }

      .tier-card-desc {
        margin: 0;
        font-size: 0.76rem;
        color: var(--dorc-text-secondary, #4b5563);
        line-height: 1.35;
      }

      .tier-spec-list {
        margin: 2px 0 0 0;
        padding-left: 16px;
        font-size: 0.73rem;
        color: var(--dorc-text-primary, #374151);
        line-height: 1.4;
      }

      /* TTL Pills */
      .ttl-row {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 10px;
      }

      .ttl-pills {
        display: inline-flex;
        flex-wrap: wrap;
        gap: 6px;
      }

      .ttl-pill {
        padding: 5px 12px;
        border-radius: 999px;
        border: 1px solid var(--dorc-border-color, #d1d5db);
        background: var(--dorc-bg-secondary, #ffffff);
        color: var(--dorc-text-primary, #111827);
        font-size: 0.8rem;
        font-weight: 600;
        cursor: pointer;
      }

      .ttl-pill.active {
        background: var(--lumo-primary-color);
        color: var(--lumo-primary-contrast-color, #ffffff);
        border-color: var(--lumo-primary-color);
      }

      .ttl-pill:disabled {
        opacity: 0.4;
        cursor: not-allowed;
      }

      .expiry-summary {
        display: flex;
        flex-wrap: wrap;
        justify-content: space-between;
        gap: 8px;
        font-size: 0.8rem;
        padding: 8px 12px;
        border-radius: 6px;
        background: var(--lumo-contrast-5pct);
        color: var(--dorc-text-primary);
      }

      /* Live Parameter Preview */
      .preview-list {
        display: flex;
        flex-direction: column;
        gap: 12px;
      }

      .preview-comp-card {
        border: 1px solid var(--dorc-border-color, #e5e7eb);
        border-radius: 6px;
        padding: 10px 12px;
        background: var(--lumo-contrast-5pct);
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .preview-comp-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 8px;
      }

      .preview-comp-title {
        font-weight: 600;
        font-size: 0.86rem;
        color: var(--dorc-text-primary);
      }

      .preview-badges {
        display: inline-flex;
        flex-wrap: wrap;
        gap: 6px;
      }

      .badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        border-radius: 999px;
        font-size: 0.7rem;
        font-weight: 600;
      }

      .badge-shared {
        background: #e0f2fe;
        color: #0369a1;
        border: 1px solid #7dd3fc;
      }

      .badge-dedicated {
        background: #fef3c7;
        color: #92400e;
        border: 1px solid #fcd34d;
      }

      .badge-safety {
        background: #dcfce7;
        color: #166534;
        border: 1px solid #86efac;
      }

      .json-viewer-box {
        background: var(--dorc-bg-secondary, #ffffff);
        border: 1px solid var(--dorc-border-color, #e5e7eb);
        border-radius: 6px;
        padding: 8px;
        max-height: 240px;
        overflow: auto;
      }

      .kv-row {
        display: flex;
        align-items: flex-end;
        gap: 8px;
        flex-wrap: wrap;
      }

      .override-chips {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
      }

      .override-chip {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 2px 8px;
        border-radius: 999px;
        background: var(--lumo-contrast-10pct);
        font-size: 0.75rem;
      }

      .override-chip button {
        border: none;
        background: transparent;
        cursor: pointer;
        color: var(--lumo-error-text-color);
        font-weight: 700;
        padding: 0 2px;
      }

      .wizard-footer {
        display: flex;
        align-items: center;
        justify-content: space-between;
        width: 100%;
        gap: 12px;
        flex-wrap: wrap;
      }

      .wizard-footer-right {
        display: flex;
        align-items: center;
        gap: 8px;
      }

      vaadin-text-field[data-sensitive='true']::part(value) {
        -webkit-text-security: disc;
      }
    `;
  }

  public open(context?: { projectName?: string; baseEnvironmentId?: number }) {
    this.shortSuffix = this.generateShortSuffix();
    this.nameManuallyEdited = false;
    this.buildNum = '';
    this.buildUrl = '';
    this.buildText = '';
    this.previewError = '';
    this.templateBuilderMode = 'none';
    this.opened = true;

    this.loadInitialData(context);
  }

  public close() {
    this.opened = false;
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    if (this.previewDebounceTimer !== undefined) {
      window.clearTimeout(this.previewDebounceTimer);
      this.previewDebounceTimer = undefined;
    }
    this.previewSubscription?.unsubscribe();
    this.previewSubscription = undefined;
    this.templatesSubscription?.unsubscribe();
    this.templatesSubscription = undefined;
    this.componentsSubscription?.unsubscribe();
    this.componentsSubscription = undefined;
  }

  private showError(msg: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', msg);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private generateShortSuffix(): string {
    return Math.random().toString(36).substring(2, 6);
  }

  private sanitizeSlug(input: string): string {
    return input
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 24);
  }

  private computeAutoEnvironmentName(
    projectName: string | undefined,
    tier: string
  ): string {
    const projSlug = this.sanitizeSlug(projectName || 'proj') || 'proj';
    const tierSlug = this.sanitizeSlug(tier || 'ephemeral') || 'eph';
    return `${projSlug}-${tierSlug}-eph-${this.shortSuffix}`;
  }

  private maybeUpdateAutoEnvironmentName() {
    if (!this.nameManuallyEdited) {
      this.environmentName = this.computeAutoEnvironmentName(
        this.selectedProject?.ProjectName ?? undefined,
        this.selectedTier
      );
    }
  }

  private regenerateEnvironmentName = () => {
    this.shortSuffix = this.generateShortSuffix();
    this.nameManuallyEdited = false;
    this.environmentName = this.computeAutoEnvironmentName(
      this.selectedProject?.ProjectName ?? undefined,
      this.selectedTier
    );
    this.schedulePreviewRefresh();
  };

  private loadInitialData(context?: {
    projectName?: string;
    baseEnvironmentId?: number;
  }) {
    const projectsApi = new RefDataProjectsApi(dorcApiConfiguration);
    projectsApi.refDataProjectsGet().subscribe({
      next: (projects: ProjectApiModel[]) => {
        const sorted = [...(projects || [])].sort((a, b) =>
          (a.ProjectName ?? '').localeCompare(b.ProjectName ?? '')
        );
        this.projects = sorted;

        if (context?.projectName) {
          const match = sorted.find(
            p =>
              p.ProjectName?.toLowerCase() ===
              context.projectName?.toLowerCase()
          );
          if (match) {
            this.selectProject(match);
            return;
          }
        }
        if (sorted.length > 0 && !this.selectedProject) {
          this.selectProject(sorted[0]);
        }
      },
      error: (err: unknown) => {
        const msg = retrieveErrorMessage(err, 'Failed to load DOrc projects.');
        this.showError(msg);
      }
    });

    const envApi = new RefDataEnvironmentsApi(dorcApiConfiguration);
    envApi.refDataEnvironmentsGet({ env: '' }).subscribe({
      next: (envs: EnvironmentApiModel[]) => {
        // Filter out production environments and ephemeral environments
        this.nonProdEnvironments = (envs || [])
          .filter(e => !e.EnvironmentIsProd && !e.IsEphemeral)
          .sort((a, b) =>
            (a.EnvironmentName ?? '').localeCompare(b.EnvironmentName ?? '')
          );

        const prevBaseId = this.selectedBaseEnvironmentId;
        if (context?.baseEnvironmentId) {
          this.selectedBaseEnvironmentId = context.baseEnvironmentId;
        } else if (
          !this.selectedTemplate &&
          !this.selectedBaseEnvironmentId &&
          this.nonProdEnvironments.length > 0
        ) {
          this.selectedBaseEnvironmentId =
            this.nonProdEnvironments[0].EnvironmentId ?? null;
        }
        if (this.selectedBaseEnvironmentId !== prevBaseId) {
          this.schedulePreviewRefresh(true);
        }
      }
    });
  }

  private selectProject(project: ProjectApiModel | undefined) {
    this.templatesSubscription?.unsubscribe();
    this.templatesSubscription = undefined;
    this.componentsSubscription?.unsubscribe();
    this.componentsSubscription = undefined;
    this.previewSubscription?.unsubscribe();
    this.previewSubscription = undefined;
    if (this.previewDebounceTimer !== undefined) {
      window.clearTimeout(this.previewDebounceTimer);
      this.previewDebounceTimer = undefined;
    }
    this.previewRequestSeq += 1;
    this.previewLoading = false;
    this.previewData = undefined;
    this.previewError = '';
    this.selectedProject = project;
    this.selectedTemplate = undefined;
    this.templates = [];
    this.templatesLoading = Boolean(project?.ProjectId);
    this.templatesLoadFailed = false;
    this.projectComponents = [];
    this.maybeUpdateAutoEnvironmentName();

    if (!project?.ProjectId) {
      return;
    }

    this.loadTemplatesForProject(project.ProjectId);

    if (project.ProjectName) {
      const compApi = new RefDataComponentsApi(dorcApiConfiguration);
      this.componentsSubscription = compApi
        .refDataComponentsGet({ id: project.ProjectName })
        .subscribe({
          next: (res: ComponentApiModelTemplateApiModel) => {
            this.projectComponents = this.flattenComponents(res?.Items || []);
          },
          error: () => {
            this.projectComponents = [];
          }
        });
    }
  }

  private flattenComponents(items: ComponentApiModel[]): ComponentApiModel[] {
    const acc: ComponentApiModel[] = [];
    const seen = new Set<number>();

    const walk = (nodes: ComponentApiModel[]) => {
      for (const node of nodes) {
        if (node.ComponentId && !seen.has(node.ComponentId)) {
          seen.add(node.ComponentId);
          acc.push(node);
        }
        if (node.Children && node.Children.length > 0) {
          walk(node.Children);
        }
      }
    };
    walk(items);
    return acc;
  }

  private loadTemplatesForProject(
    projectId: number,
    selectTemplateId?: number
  ) {
    this.templatesSubscription?.unsubscribe();
    this.previewSubscription?.unsubscribe();
    this.previewSubscription = undefined;
    this.previewRequestSeq++;
    this.previewData = undefined;
    this.previewLoading = true;
    this.templatesLoading = true;
    this.templatesLoadFailed = false;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    this.templatesSubscription = api
      .ephemeralEnvironmentsTemplatesGet({ projectId })
      .subscribe({
        next: (list: EnvironmentTemplateApiModel[]) => {
          this.templatesLoading = false;
          this.templates = list || [];
          if (selectTemplateId) {
            const found = this.templates.find(t => t.Id === selectTemplateId);
            if (found) {
              this.applySelectedTemplate(found);
              return;
            }
          }
          if (this.templates.length > 0) {
            this.applySelectedTemplate(this.templates[0]);
          } else {
            this.selectedTemplate = undefined;
            this.schedulePreviewRefresh(true);
          }
        },
        error: (err: unknown) => {
          this.templatesLoading = false;
          this.previewLoading = false;
          this.templatesLoadFailed = true;
          const msg = retrieveErrorMessage(
            err,
            'Failed to load Blueprint Templates.'
          );
          this.showError(msg);
        }
      });
  }

  private applySelectedTemplate(
    template: EnvironmentTemplateApiModel | undefined
  ) {
    const templateChanged = this.selectedTemplate?.Id !== template?.Id;
    this.selectedTemplate = template;
    if (template) {
      if (template.DefaultTier) {
        this.selectedTier = template.DefaultTier.toLowerCase();
      }
      this.selectedBaseEnvironmentId = template.BaseEnvironmentId ?? null;
      if (template.DefaultTtlMinutes && template.DefaultTtlMinutes > 0) {
        this.ttlMinutes = template.DefaultTtlMinutes;
      }
      this.maybeUpdateAutoEnvironmentName();
    }
    this.schedulePreviewRefresh(templateChanged);
  }

  private openNewTemplateBuilder = () => {
    this.templateBuilderMode = 'create';
    this.builderName = `${this.selectedProject?.ProjectName || 'Service'} Ephemeral Blueprint`;
    this.builderDescription =
      'Non-prod temporary environment blueprint with tier-weighted sizing';
    this.builderDefaultTier = this.selectedTier || 'ephemeral';
    this.builderBaseEnvironmentId =
      this.selectedBaseEnvironmentId ??
      this.nonProdEnvironments[0]?.EnvironmentId ??
      null;
    this.builderDefaultTtlMinutes = 240;
    this.builderMaxTtlMinutes = 1440;
    this.builderProvisionComponentIds = this.projectComponents
      .slice(0, 3)
      .map(c => c.ComponentId!)
      .filter(Boolean);
    this.builderValidationComponentIds = [];
    this.builderTierOverridesJson = JSON.stringify(
      {
        ephemeral: { create_service: 'false' },
        qa: { create_service: 'true' }
      },
      null,
      2
    );
  };

  private openEditTemplateBuilder = () => {
    const tpl = this.selectedTemplate;
    if (!tpl) {
      return;
    }
    this.templateBuilderMode = 'edit';
    this.builderName = tpl.Name || '';
    this.builderDescription = tpl.Description || '';
    this.builderDefaultTier = (tpl.DefaultTier || 'ephemeral').toLowerCase();
    this.builderBaseEnvironmentId = tpl.BaseEnvironmentId ?? null;
    this.builderDefaultTtlMinutes = tpl.DefaultTtlMinutes ?? 240;
    this.builderMaxTtlMinutes = tpl.MaxTtlMinutes ?? 1440;
    this.builderProvisionComponentIds = [...(tpl.ProvisionComponentIds || [])];
    this.builderValidationComponentIds = [
      ...(tpl.ValidationComponentIds || [])
    ];
    this.builderTierOverridesJson = tpl.TierParameterOverridesJson || '{}';
  };

  private cancelTemplateBuilder = () => {
    this.templateBuilderMode = 'none';
  };

  private orderBuilderComponentIds(ids: number[]): number[] {
    const rankById = new Map<number, number>();
    this.projectComponents.forEach((comp, idx) => {
      if (comp.ComponentId) {
        rankById.set(comp.ComponentId, idx);
      }
    });
    return [...ids].sort(
      (a, b) =>
        (rankById.get(a) ?? Number.MAX_SAFE_INTEGER) -
        (rankById.get(b) ?? Number.MAX_SAFE_INTEGER)
    );
  }

  private toggleBuilderProvisionComponent(compId: number, checked: boolean) {
    if (checked) {
      if (!this.builderProvisionComponentIds.includes(compId)) {
        this.builderProvisionComponentIds = this.orderBuilderComponentIds([
          ...this.builderProvisionComponentIds,
          compId
        ]);
      }
    } else {
      this.builderProvisionComponentIds =
        this.builderProvisionComponentIds.filter(id => id !== compId);
    }
  }

  private toggleBuilderValidationComponent(compId: number, checked: boolean) {
    if (checked) {
      if (!this.builderValidationComponentIds.includes(compId)) {
        this.builderValidationComponentIds = this.orderBuilderComponentIds([
          ...this.builderValidationComponentIds,
          compId
        ]);
      }
    } else {
      this.builderValidationComponentIds =
        this.builderValidationComponentIds.filter(id => id !== compId);
    }
  }

  private addBuilderTierOverride = () => {
    const key = this.builderOverrideKey.trim();
    const val = this.builderOverrideValue.trim();
    if (!key) {
      return;
    }
    const parsed = this.safeParseJsonObject(this.builderTierOverridesJson);
    const tierKey = this.builderDefaultTier || 'ephemeral';
    const currentTierMap =
      typeof parsed[tierKey] === 'object' && parsed[tierKey] !== null
        ? (parsed[tierKey] as Record<string, string>)
        : {};
    parsed[tierKey] = {
      ...currentTierMap,
      [key]: val
    };
    this.builderTierOverridesJson = JSON.stringify(parsed, null, 2);
    this.builderOverrideKey = '';
    this.builderOverrideValue = '';
    setTimeout(() => {
      this.builderJsonViewerRef.value?.expandAll();
    }, 0);
  };

  private safeParseJsonObject(raw: string): Record<string, unknown> {
    if (!raw || !raw.trim()) {
      return {};
    }
    try {
      const obj = JSON.parse(raw);
      return typeof obj === 'object' && obj !== null ? obj : {};
    } catch {
      return {};
    }
  }

  private saveBlueprintTemplate = () => {
    if (!this.selectedProject?.ProjectId || !this.builderName.trim()) {
      this.showError('Project and Blueprint Template Name are required.');
      return;
    }

    this.builderSaving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const payload: EnvironmentTemplateApiModel = {
      Id:
        this.templateBuilderMode === 'edit'
          ? this.selectedTemplate?.Id
          : undefined,
      ProjectId: this.selectedProject.ProjectId,
      ProjectName: this.selectedProject.ProjectName,
      Name: this.builderName.trim(),
      Description: this.builderDescription.trim() || null,
      BaseEnvironmentId: this.builderBaseEnvironmentId,
      DefaultTier: this.builderDefaultTier,
      DefaultTtlMinutes: this.builderDefaultTtlMinutes,
      MaxTtlMinutes: this.builderMaxTtlMinutes,
      ProvisionComponentIds: this.builderProvisionComponentIds,
      ValidationComponentIds: this.builderValidationComponentIds,
      TierParameterOverridesJson: this.builderTierOverridesJson || '{}'
    };

    const request$ =
      this.templateBuilderMode === 'edit' && this.selectedTemplate?.Id
        ? api.ephemeralEnvironmentsTemplatesTemplateIdPut({
            templateId: this.selectedTemplate.Id,
            environmentTemplateApiModel: payload
          })
        : api.ephemeralEnvironmentsTemplatesPost({
            environmentTemplateApiModel: payload
          });

    request$.subscribe({
      next: (saved: EnvironmentTemplateApiModel) => {
        this.builderSaving = false;
        this.templateBuilderMode = 'none';
        Notification.show(`Saved Blueprint Template '${saved.Name}'.`, {
          theme: 'success',
          position: 'bottom-start',
          duration: 4000
        });
        if (this.selectedProject?.ProjectId) {
          this.loadTemplatesForProject(
            this.selectedProject.ProjectId,
            saved.Id
          );
        }
      },
      error: (err: unknown) => {
        this.builderSaving = false;
        const msg = retrieveErrorMessage(
          err,
          'Failed to save Blueprint Template.'
        );
        this.showError(msg);
      }
    });
  };

  private deleteBlueprintTemplate = async () => {
    const tpl = this.selectedTemplate;
    const projectId = this.selectedProject?.ProjectId;
    if (!tpl?.Id || !projectId) {
      return;
    }

    const confirmed = await confirmPrompt(
      `Delete Blueprint Template '${tpl.Name}'?`,
      {
        header: 'Delete Blueprint Template',
        confirmText: 'Delete Template',
        warningTitle: 'Project-wide Blueprint Deletion',
        warningMessage:
          'This removes the blueprint template from the project. Active ephemeral environments referencing this template will prevent deletion until torn down.'
      }
    );
    if (!confirmed) {
      return;
    }

    this.builderSaving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsTemplatesTemplateIdDelete({ templateId: tpl.Id })
      .subscribe({
        next: () => {
          this.builderSaving = false;
          this.selectedTemplate = undefined;
          Notification.show(`Deleted Blueprint Template '${tpl.Name}'.`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 4000
          });
          this.loadTemplatesForProject(projectId);
        },
        error: (err: unknown) => {
          this.builderSaving = false;
          const msg = retrieveErrorMessage(
            err,
            'Failed to delete Blueprint Template.'
          );
          this.showError(msg);
        }
      });
  };

  private selectTier(tier: string) {
    const tierChanged = this.selectedTier !== tier;
    this.selectedTier = tier;
    this.maybeUpdateAutoEnvironmentName();
    this.schedulePreviewRefresh(tierChanged);
  }

  private addAdhocOverride = () => {
    const key = this.adhocKey.trim();
    if (!key) {
      return;
    }
    this.adhocOverrides = {
      ...this.adhocOverrides,
      [key]: this.adhocValue
    };
    this.adhocKey = '';
    this.adhocValue = '';
    this.schedulePreviewRefresh();
  };

  private removeAdhocOverride(key: string) {
    const next = { ...this.adhocOverrides };
    delete next[key];
    this.adhocOverrides = next;
    this.schedulePreviewRefresh();
  }

  private isSensitiveOverrideKey(key: string): boolean {
    const colonIdx = key.lastIndexOf(':');
    const dotIdx = colonIdx < 0 ? key.indexOf('.') : -1;
    const sepIdx = colonIdx >= 0 ? colonIdx : dotIdx;
    const compPrefix =
      colonIdx >= 0 ? key.slice(0, colonIdx).trim().toLowerCase() : '';
    const bareKey = (sepIdx >= 0 ? key.slice(sepIdx + 1) : key)
      .trim()
      .toLowerCase();
    if (!bareKey) {
      return false;
    }
    if (
      bareKey.includes('password') ||
      bareKey.includes('secret') ||
      bareKey.includes('token') ||
      bareKey.includes('apikey') ||
      bareKey.includes('api_key') ||
      bareKey.includes('private_key') ||
      bareKey.includes('connection_string') ||
      bareKey.endsWith('_key')
    ) {
      return true;
    }
    const matchingPreviews = (this.previewData?.ComponentPreviews ?? []).filter(
      cp => !compPrefix || (cp.ComponentName ?? '').toLowerCase() === compPrefix
    );
    const matchingEntries = matchingPreviews.flatMap(cp =>
      Object.entries(cp.ResolvedParameters ?? {}).filter(
        ([paramName]) => paramName.toLowerCase() === bareKey
      )
    );
    if (matchingEntries.some(([, val]) => val === '***REDACTED***')) {
      return true;
    }
    if (
      this.previewLoading ||
      !this.previewData ||
      Boolean(this.previewError)
    ) {
      return true;
    }
    return false;
  }

  private formatAdhocOverrideValue(key: string, value: string): string {
    return this.isSensitiveOverrideKey(key) ? '***REDACTED***' : value;
  }

  private schedulePreviewRefresh(invalidatePreview = false) {
    this.previewSubscription?.unsubscribe();
    this.previewSubscription = undefined;
    this.previewRequestSeq++;
    if (invalidatePreview) {
      this.previewData = undefined;
    }
    this.previewError = '';
    this.previewLoading = Boolean(this.selectedProject?.ProjectId);
    if (this.previewDebounceTimer !== undefined) {
      window.clearTimeout(this.previewDebounceTimer);
    }
    this.previewDebounceTimer = window.setTimeout(() => {
      this.fetchTierParameterPreview();
    }, 180);
  }

  private fetchTierParameterPreview() {
    this.previewSubscription?.unsubscribe();
    this.previewSubscription = undefined;
    const requestSeq = ++this.previewRequestSeq;

    if (!this.selectedProject?.ProjectId) {
      this.previewLoading = false;
      this.previewData = undefined;
      return;
    }

    this.previewLoading = true;
    this.previewError = '';

    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    this.previewSubscription = api
      .ephemeralEnvironmentsPreviewTierParametersPost({
        ephemeralEnvironmentTierPreviewRequestApiModel: {
          ProjectId: this.selectedProject.ProjectId,
          TemplateId: this.selectedTemplate?.Id ?? null,
          BaseEnvironmentId: this.getEffectiveBaseEnvironmentIdPayload(),
          EnvironmentName:
            this.environmentName.trim() || 'preview-ephemeral-env',
          EnvironmentTier: this.selectedTier,
          ParameterOverrides:
            Object.keys(this.adhocOverrides).length > 0
              ? this.adhocOverrides
              : null
        }
      })
      .subscribe({
        next: (res: EphemeralEnvironmentTierPreviewResponseApiModel) => {
          if (requestSeq !== this.previewRequestSeq) {
            return;
          }
          this.previewLoading = false;
          this.previewData = res;
        },
        error: (err: unknown) => {
          if (requestSeq !== this.previewRequestSeq) {
            return;
          }
          this.previewLoading = false;
          this.previewData = undefined;
          this.previewError = retrieveErrorMessage(
            err,
            'Unable to resolve tier parameter preview.'
          );
        }
      });
  }

  private getEffectiveBaseEnvironmentIdPayload(): number | null {
    if (this.selectedBaseEnvironmentId !== null) {
      return this.selectedBaseEnvironmentId;
    }
    return this.selectedTemplate?.BaseEnvironmentId ? 0 : null;
  }

  private getEffectiveMaxTtlMinutes(): number {
    return this.selectedTemplate?.MaxTtlMinutes &&
      this.selectedTemplate.MaxTtlMinutes > 0
      ? this.selectedTemplate.MaxTtlMinutes
      : 1440;
  }

  private isTtlValid(): boolean {
    const maxCap = this.getEffectiveMaxTtlMinutes();
    return this.ttlMinutes >= 1 && this.ttlMinutes <= maxCap;
  }

  private submitProvision = () => {
    if (!this.selectedProject?.ProjectId) {
      this.showError('Please select a Project.');
      return;
    }
    const envName = this.environmentName.trim();
    if (!envName) {
      this.showError('Please provide an Environment Name.');
      return;
    }
    if (!this.isTtlValid()) {
      this.showError(
        `TTL must be between 1 and ${this.getEffectiveMaxTtlMinutes()} minutes.`
      );
      return;
    }

    this.isProvisioning = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const provisionPayload: EphemeralEnvironmentProvisionRequestApiModel = {
      ProjectId: this.selectedProject.ProjectId,
      TemplateId: this.selectedTemplate?.Id ?? null,
      BaseEnvironmentId: this.getEffectiveBaseEnvironmentIdPayload(),
      EnvironmentName: envName,
      EnvironmentTier: this.selectedTier,
      TtlMinutes: this.ttlMinutes,
      RunValidationComponents: this.runValidationComponents,
      BuildNum: this.buildNum.trim() || undefined,
      BuildUrl: this.buildUrl.trim() || undefined,
      BuildText: this.buildText.trim() || undefined,
      ParameterOverrides:
        Object.keys(this.adhocOverrides).length > 0 ? this.adhocOverrides : null
    };
    api
      .ephemeralEnvironmentsProvisionPost({
        ephemeralEnvironmentProvisionRequestApiModel: provisionPayload
      })
      .subscribe({
        next: (resp: EphemeralEnvironmentProvisionResponseApiModel) => {
          this.isProvisioning = false;
          const state = resp.Environment?.EphemeralState || 'Provisioning';
          const reqIds = (resp.ProvisionRequestIds || []).join(', #') || 'N/A';

          Notification.show(
            `Ephemeral environment '${envName}' created (${state}) — provisioning request #${reqIds} submitted.`,
            {
              theme: 'success',
              position: 'bottom-start',
              duration: 6000
            }
          );

          this.dispatchEvent(
            new CustomEvent('environment-created', {
              detail: {
                environment: resp.Environment,
                provisionResponse: resp
              },
              bubbles: true,
              composed: true
            })
          );

          this.opened = false;
          if (resp.Environment?.EnvironmentName) {
            void navigate(
              `/environment/${encodeURIComponent(resp.Environment.EnvironmentName)}/monitor`
            );
          }
        },
        error: (err: unknown) => {
          this.isProvisioning = false;
          const msg = retrieveErrorMessage(
            err,
            'Failed to provision temporary ephemeral environment.'
          );
          this.showError(msg);
        }
      });
  };

  private renderDialogHeader = () => html`
    <div style="display: flex; align-items: center; gap: 8px;">
      <vaadin-icon
        icon="vaadin:stopwatch"
        style="color: var(--lumo-primary-color); width: 20px; height: 20px;"
      ></vaadin-icon>
      <span style="font-weight: 700; font-size: 1.05rem;">
        Spin Up Temporary Environment
      </span>
    </div>
  `;

  private renderComponentPreviewCard(
    comp: EphemeralComponentParameterPreviewApiModel
  ) {
    const isShared = Boolean(comp.IsSharedService);
    const paramsObj = comp.ResolvedParameters || {};
    const isSharedRg =
      !isShared &&
      String(paramsObj['create_resource_group'] ?? '').toLowerCase() ===
        'false';
    const serviceBadgeLabel = isShared
      ? 'Shared Host Service'
      : isSharedRg
        ? 'Dedicated Service (Shared RG)'
        : 'Dedicated Service';
    const forcedInvariantEntries = Object.entries(comp.ForcedInvariants ?? {});

    return html`
      <div class="preview-comp-card">
        <div class="preview-comp-header">
          <span class="preview-comp-title">
            ${comp.ComponentName || `Component #${comp.ComponentId}`}
            ${
              comp.TemplateName
                ? html`<span
                    style="font-weight: 400; color: var(--dorc-text-secondary); font-size: 0.78rem;"
                  >
                    (${comp.TemplateName})
                  </span>`
                : nothing
            }
          </span>
          <div class="preview-badges">
            <span
              class="badge ${isShared ? 'badge-shared' : 'badge-dedicated'}"
            >
              ${serviceBadgeLabel}
            </span>
            ${
              forcedInvariantEntries.length > 0
                ? forcedInvariantEntries.map(
                    ([invKey, invVal]) => html`
                      <span
                        class="badge badge-safety"
                        title="Forced non-prod safety invariant"
                      >
                        ${invKey} = ${invVal ?? 'false'}
                      </span>
                    `
                  )
                : html`
                    <span
                      class="badge badge-safety"
                      title="Forced non-prod safety invariant"
                    >
                      termination_protection = false
                    </span>
                  `
            }
          </div>
        </div>
        <div class="json-viewer-box">
          <hegs-json-viewer
            .data=${paramsObj}
            ${ref((el?: Element) => {
              if (el) {
                setTimeout(() => (el as HegsJsonViewer).expandAll(), 0);
              }
            })}
          ></hegs-json-viewer>
        </div>
      </div>
    `;
  }

  private renderDialogBody = () => {
    const maxTtlCap = this.getEffectiveMaxTtlMinutes();
    const ttlValid = this.isTtlValid();
    const expiryDate = new Date(Date.now() + this.ttlMinutes * 60 * 1000);
    const expiryUtcStr = expiryDate.toUTCString();
    const expiryLocalStr = expiryDate.toLocaleString();
    const builderJsonParsed = this.safeParseJsonObject(
      this.builderTierOverridesJson
    );
    const componentPreviews = this.previewData?.ComponentPreviews || [];

    return html`
      <div class="wizard-container">
        <!-- 1. Project & Blueprint Template Selector -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-num">1</span>
              Project &amp; Blueprint Template
            </h3>
            <div style="display: inline-flex; gap: 6px;">
              ${
                this.selectedTemplate && this.templateBuilderMode === 'none'
                  ? html`
                      <vaadin-button
                        theme="small tertiary"
                        @click=${this.openEditTemplateBuilder}
                      >
                        <vaadin-icon
                          icon="vaadin:edit"
                          slot="prefix"
                        ></vaadin-icon>
                        Edit Blueprint Template
                      </vaadin-button>
                      <vaadin-button
                        id="delete-blueprint-template-btn"
                        theme="small error tertiary"
                        ?disabled=${this.builderSaving}
                        @click=${this.deleteBlueprintTemplate}
                      >
                        <vaadin-icon
                          icon="vaadin:trash"
                          slot="prefix"
                        ></vaadin-icon>
                        Delete Blueprint Template
                      </vaadin-button>
                    `
                  : nothing
              }
              ${
                this.templateBuilderMode === 'none'
                  ? html`
                      <vaadin-button
                        theme="small"
                        @click=${this.openNewTemplateBuilder}
                      >
                        <vaadin-icon
                          icon="vaadin:plus"
                          slot="prefix"
                        ></vaadin-icon>
                        New Blueprint Template
                      </vaadin-button>
                    `
                  : html`
                      <vaadin-button
                        theme="small tertiary"
                        @click=${this.cancelTemplateBuilder}
                      >
                        Close Template Builder
                      </vaadin-button>
                    `
              }
            </div>
          </div>

          <div class="form-grid-2">
            <vaadin-combo-box
              label="Project"
              item-label-path="ProjectName"
              item-value-path="ProjectId"
              .items=${this.projects}
              .selectedItem=${this.selectedProject ?? null}
              @selected-item-changed=${(
                e: CustomEvent<{ value: ProjectApiModel | null | undefined }>
              ) => {
                if (
                  e.detail.value &&
                  e.detail.value.ProjectId !== this.selectedProject?.ProjectId
                ) {
                  this.selectProject(e.detail.value);
                }
              }}
            ></vaadin-combo-box>

            <vaadin-combo-box
              label="Blueprint Template"
              placeholder="Select a Blueprint Template (or create one)"
              item-label-path="Name"
              item-value-path="Id"
              clear-button-visible
              .items=${this.templates}
              .selectedItem=${this.selectedTemplate ?? null}
              @selected-item-changed=${(
                e: CustomEvent<{
                  value: EnvironmentTemplateApiModel | null | undefined;
                }>
              ) => {
                const next = e.detail.value ?? undefined;
                if (next?.Id !== this.selectedTemplate?.Id) {
                  this.applySelectedTemplate(next);
                }
              }}
            ></vaadin-combo-box>
          </div>

          ${
            this.templateBuilderMode !== 'none'
              ? html`
                  <div class="builder-panel">
                    <div class="section-header">
                      <strong style="font-size: 0.88rem;">
                        ${
                          this.templateBuilderMode === 'edit'
                            ? `Edit Blueprint Template: ${this.builderName}`
                            : 'Create New Blueprint Template'
                        }
                      </strong>
                    </div>

                    <div class="form-grid-3">
                      <vaadin-text-field
                        label="Template Name"
                        required
                        .value=${this.builderName}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          this.builderName = e.detail.value;
                        }}
                      ></vaadin-text-field>

                      <vaadin-combo-box
                        label="Default Tier"
                        .items=${['ephemeral', 'dv', 'qa', 'ut', 'pp']}
                        .value=${this.builderDefaultTier}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          if (e.detail.value) {
                            this.builderDefaultTier = e.detail.value;
                          }
                        }}
                      ></vaadin-combo-box>

                      <vaadin-combo-box
                        label="Default Non-Prod Base Environment"
                        item-label-path="EnvironmentName"
                        item-value-path="EnvironmentId"
                        .items=${this.nonProdEnvironments}
                        .selectedItem=${
                          this.nonProdEnvironments.find(
                            env =>
                              env.EnvironmentId ===
                              this.builderBaseEnvironmentId
                          ) ?? null
                        }
                        @selected-item-changed=${(
                          e: CustomEvent<{
                            value: EnvironmentApiModel | null | undefined;
                          }>
                        ) => {
                          const nextId = e.detail.value?.EnvironmentId ?? null;
                          if (nextId !== this.builderBaseEnvironmentId) {
                            this.builderBaseEnvironmentId = nextId;
                          }
                        }}
                      ></vaadin-combo-box>
                    </div>

                    <div class="form-grid-3">
                      <vaadin-text-field
                        label="Description"
                        .value=${this.builderDescription}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          this.builderDescription = e.detail.value;
                        }}
                      ></vaadin-text-field>

                      <vaadin-number-field
                        label="Default TTL (minutes)"
                        min="15"
                        max="10080"
                        .value=${String(this.builderDefaultTtlMinutes)}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          const v = Number.parseInt(e.detail.value, 10);
                          if (!Number.isNaN(v) && v > 0) {
                            this.builderDefaultTtlMinutes = v;
                          }
                        }}
                      ></vaadin-number-field>

                      <vaadin-number-field
                        label="Max TTL Policy Cap (minutes)"
                        min="30"
                        max="10080"
                        .value=${String(this.builderMaxTtlMinutes)}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          const v = Number.parseInt(e.detail.value, 10);
                          if (!Number.isNaN(v) && v > 0) {
                            this.builderMaxTtlMinutes = v;
                          }
                        }}
                      ></vaadin-number-field>
                    </div>

                    <div>
                      <div
                        style="font-size: 0.78rem; font-weight: 600; margin-bottom: 4px;"
                      >
                        Provision Components
                        (${this.builderProvisionComponentIds.length} selected)
                      </div>
                      <div class="components-picker">
                        ${this.projectComponents.map(
                          comp => html`
                            <label class="comp-chip">
                              <vaadin-checkbox
                                ?checked=${Boolean(
                                  comp.ComponentId &&
                                  this.builderProvisionComponentIds.includes(
                                    comp.ComponentId
                                  )
                                )}
                                @checked-changed=${(
                                  e: CustomEvent<{ value: boolean }>
                                ) => {
                                  if (comp.ComponentId) {
                                    this.toggleBuilderProvisionComponent(
                                      comp.ComponentId,
                                      Boolean(e.detail.value)
                                    );
                                  }
                                }}
                              ></vaadin-checkbox>
                              <span>${comp.ComponentName}</span>
                              <span class="comp-type-tag">
                                ${comp.ComponentType || 'PowerShell'}
                              </span>
                            </label>
                          `
                        )}
                      </div>
                    </div>

                    <div>
                      <div
                        style="font-size: 0.78rem; font-weight: 600; margin-bottom: 4px;"
                      >
                        Post-Provision Validation Components
                        (${this.builderValidationComponentIds.length} selected)
                      </div>
                      <div class="components-picker">
                        ${this.projectComponents.map(
                          comp => html`
                            <label class="comp-chip">
                              <vaadin-checkbox
                                ?checked=${Boolean(
                                  comp.ComponentId &&
                                  this.builderValidationComponentIds.includes(
                                    comp.ComponentId
                                  )
                                )}
                                @checked-changed=${(
                                  e: CustomEvent<{ value: boolean }>
                                ) => {
                                  if (comp.ComponentId) {
                                    this.toggleBuilderValidationComponent(
                                      comp.ComponentId,
                                      Boolean(e.detail.value)
                                    );
                                  }
                                }}
                              ></vaadin-checkbox>
                              <span>${comp.ComponentName}</span>
                            </label>
                          `
                        )}
                      </div>
                    </div>

                    <div>
                      <div
                        style="font-size: 0.78rem; font-weight: 600; margin-bottom: 4px;"
                      >
                        Tier Parameter Overrides Preview
                        (<code>TierParameterOverridesJson</code>)
                      </div>
                      <div class="kv-row" style="margin-bottom: 8px;">
                        <vaadin-text-field
                          label="Parameter Key (for ${this.builderDefaultTier})"
                          placeholder="e.g. aiven_plan"
                          .value=${this.builderOverrideKey}
                          @value-changed=${(
                            e: CustomEvent<{ value: string }>
                          ) => {
                            this.builderOverrideKey = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-text-field
                          label="Parameter Value"
                          placeholder="e.g. startup-4"
                          .value=${this.builderOverrideValue}
                          @value-changed=${(
                            e: CustomEvent<{ value: string }>
                          ) => {
                            this.builderOverrideValue = e.detail.value;
                          }}
                        ></vaadin-text-field>
                        <vaadin-button
                          theme="small"
                          @click=${this.addBuilderTierOverride}
                        >
                          Set Override
                        </vaadin-button>
                      </div>
                      <div class="json-viewer-box">
                        <hegs-json-viewer
                          ${ref(this.builderJsonViewerRef)}
                          .data=${builderJsonParsed}
                        ></hegs-json-viewer>
                      </div>
                    </div>

                    <div
                      style="display: flex; justify-content: flex-end; gap: 8px;"
                    >
                      <vaadin-button
                        theme="small tertiary"
                        @click=${this.cancelTemplateBuilder}
                      >
                        Cancel
                      </vaadin-button>
                      <vaadin-button
                        theme="small primary"
                        ?disabled=${this.builderSaving}
                        @click=${this.saveBlueprintTemplate}
                      >
                        <vaadin-icon
                          icon="vaadin:check"
                          slot="prefix"
                        ></vaadin-icon>
                        Save Blueprint Template
                      </vaadin-button>
                    </div>
                  </div>
                `
              : nothing
          }
        </div>

        <!-- 2. Environment Identity & Inheritance -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-num">2</span>
              Environment Identity &amp; Non-Prod Inheritance
            </h3>
          </div>

          <div class="form-grid-2">
            <div style="display: flex; align-items: flex-end; gap: 6px;">
              <vaadin-text-field
                style="flex: 1;"
                label="Ephemeral Environment Name"
                helper-text="Auto-generated non-prod identifier ({project}-{tier}-eph-{short})"
                required
                .value=${this.environmentName}
                @input=${(e: Event) => {
                  const target = e.target as HTMLInputElement;
                  this.environmentName = target.value;
                  this.nameManuallyEdited = true;
                  this.schedulePreviewRefresh();
                }}
              ></vaadin-text-field>
              <vaadin-button
                theme="icon"
                title="Regenerate short environment name"
                @click=${this.regenerateEnvironmentName}
              >
                <vaadin-icon icon="vaadin:refresh"></vaadin-icon>
              </vaadin-button>
            </div>

            <vaadin-combo-box
              label="Base Environment (Non-Prod Parent)"
              helper-text="Production environments are excluded by safety policy"
              item-label-path="EnvironmentName"
              item-value-path="EnvironmentId"
              clear-button-visible
              .items=${this.nonProdEnvironments}
              .selectedItem=${
                this.nonProdEnvironments.find(
                  env => env.EnvironmentId === this.selectedBaseEnvironmentId
                ) ?? null
              }
              @selected-item-changed=${(
                e: CustomEvent<{
                  value: EnvironmentApiModel | null | undefined;
                }>
              ) => {
                const nextId = e.detail.value?.EnvironmentId ?? null;
                if (nextId !== this.selectedBaseEnvironmentId) {
                  this.selectedBaseEnvironmentId = nextId;
                  this.schedulePreviewRefresh(true);
                }
              }}
            ></vaadin-combo-box>
          </div>

          <div class="info-banner">
            <vaadin-icon icon="vaadin:sitemap"></vaadin-icon>
            <span>
              <strong>Property &amp; Credential Inheritance:</strong> The
              temporary environment links to the selected Base Environment via
              <code>ParentId</code>. Cloud credentials, shared Aiven/Kafka host
              endpoints, and base configuration variables inherit automatically
              while tier-weighted overrides are scoped to this child
              environment.
            </span>
          </div>
        </div>

        <!-- 3. Tier Sizing Profile Selector -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-num">3</span>
              Tier Sizing Profile &amp; Isolation Strategy
            </h3>
          </div>

          <div
            class="tier-grid"
            role="radiogroup"
            aria-label="Tier Sizing Profile"
          >
            ${WIZARD_TIER_CARDS.map(card => {
              const isSelected = this.selectedTier === card.tier;
              const pillClass =
                card.strategyType === 'shared'
                  ? 'strategy-shared'
                  : card.strategyType === 'dedicated'
                    ? 'strategy-dedicated'
                    : 'strategy-perf';
              return html`
                <button
                  type="button"
                  class="tier-card ${isSelected ? 'selected' : ''}"
                  role="radio"
                  aria-checked=${isSelected ? 'true' : 'false'}
                  @click=${() => this.selectTier(card.tier)}
                >
                  <div class="tier-card-top">
                    <span class="tier-card-title">${card.title}</span>
                    <span class="strategy-pill ${pillClass}">
                      ${card.strategyBadge}
                    </span>
                  </div>
                  <p class="tier-card-desc">${card.summary}</p>
                  <ul class="tier-spec-list">
                    ${card.specs.map(spec => html`<li>${spec}</li>`)}
                  </ul>
                </button>
              `;
            })}
          </div>
        </div>

        <!-- 4. TTL Duration & Expiry Policy -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-num">4</span>
              TTL Duration &amp; Automated Reaper Policy
            </h3>
            <span
              style="font-size: 0.78rem; color: var(--dorc-text-secondary);"
            >
              Policy Cap: Max ${maxTtlCap} minutes
              (${Math.round((maxTtlCap / 60) * 10) / 10}h)
            </span>
          </div>

          <div class="ttl-row">
            <div class="ttl-pills">
              ${TTL_PRESETS.map(
                preset => html`
                  <button
                    type="button"
                    class="ttl-pill ${
                      this.ttlMinutes === preset.minutes ? 'active' : ''
                    }"
                    ?disabled=${preset.minutes > maxTtlCap}
                    @click=${() => {
                      this.ttlMinutes = preset.minutes;
                    }}
                  >
                    ${preset.label}
                  </button>
                `
              )}
            </div>

            <vaadin-number-field
              label="Custom TTL (minutes)"
              min="1"
              .max=${maxTtlCap}
              step="15"
              .value=${String(this.ttlMinutes)}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                const parsed = Number.parseInt(e.detail.value, 10);
                if (!Number.isNaN(parsed)) {
                  this.ttlMinutes = parsed;
                }
              }}
            ></vaadin-number-field>

            <vaadin-checkbox
              label="Run Validation / Smoke Test Components after provisioning"
              ?checked=${this.runValidationComponents}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => {
                this.runValidationComponents = Boolean(e.detail.value);
              }}
            ></vaadin-checkbox>
          </div>

          <div class="form-grid-2">
            <vaadin-text-field
              label="Build Number / Tag (Optional)"
              placeholder="e.g. 20261114.1 or v1.4.2-rc1"
              helper-text="Optional build number or tag for artifact-backed or validation components"
              clear-button-visible
              .value=${this.buildNum}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.buildNum = e.detail.value ?? '';
              }}
            ></vaadin-text-field>
            <vaadin-text-field
              label="Build Artifact URL (Optional)"
              placeholder="e.g. https://dev.azure.com/... or UNC/drop path"
              helper-text="Optional build artifact URI or drop location"
              clear-button-visible
              .value=${this.buildUrl}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.buildUrl = e.detail.value ?? '';
              }}
            ></vaadin-text-field>
          </div>
          <div class="form-grid-1" style="margin-top: 8px;">
            <vaadin-text-field
              label="Build Definition / Workflow Name (Optional)"
              placeholder="e.g. CI-Pipeline or deploy.yml"
              helper-text="Optional Azure DevOps build definition or GitHub Actions workflow name"
              clear-button-visible
              .value=${this.buildText}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.buildText = e.detail.value ?? '';
              }}
            ></vaadin-text-field>
          </div>
          ${
            !ttlValid
              ? html`
                  <div class="warning-banner">
                    <vaadin-icon icon="vaadin:warning"></vaadin-icon>
                    <span>
                      Requested TTL (${this.ttlMinutes}m) exceeds the Blueprint
                      Template policy cap of ${maxTtlCap} minutes.
                    </span>
                  </div>
                `
              : html`
                  <div class="expiry-summary">
                    <span>
                      <strong>Calculated Expiry:</strong> ${expiryUtcStr}
                      (Local: ${expiryLocalStr})
                    </span>
                    <span>
                      <vaadin-icon
                        icon="vaadin:automation"
                        style="width: 14px; height: 14px;"
                      ></vaadin-icon>
                      <code>Dorc.Monitor</code> reaper automatically queues
                      reverse-order <code>terraform destroy</code> upon expiry.
                    </span>
                  </div>
                `
          }
        </div>

        <!-- 5. Live Tier-Weighted Parameter Preview -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-num">5</span>
              Live Tier-Weighted Parameter Preview
            </h3>
            <vaadin-button
              theme="small tertiary"
              ?disabled=${this.previewLoading}
              @click=${() => this.fetchTierParameterPreview()}
            >
              <vaadin-icon icon="vaadin:refresh" slot="prefix"></vaadin-icon>
              Refresh Preview
            </vaadin-button>
          </div>

          <div class="kv-row">
            <vaadin-text-field
              label="Ad-Hoc Override Parameter"
              placeholder="e.g. kafka_partitions"
              .value=${this.adhocKey}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.adhocKey = e.detail.value;
              }}
            ></vaadin-text-field>
            ${
              this.isSensitiveOverrideKey(this.adhocKey)
                ? html`
                    <vaadin-password-field
                      label="Override Value"
                      placeholder="e.g. 3"
                      data-sensitive="true"
                      .value=${this.adhocValue}
                      @value-changed=${(e: CustomEvent<{ value: string }>) => {
                        this.adhocValue = e.detail.value;
                      }}
                    ></vaadin-password-field>
                  `
                : html`
                    <vaadin-text-field
                      label="Override Value"
                      placeholder="e.g. 3"
                      data-sensitive="false"
                      .value=${this.adhocValue}
                      @value-changed=${(e: CustomEvent<{ value: string }>) => {
                        this.adhocValue = e.detail.value;
                      }}
                    ></vaadin-text-field>
                  `
            }
            <vaadin-button theme="small" @click=${this.addAdhocOverride}>
              Add Override
            </vaadin-button>
          </div>

          ${
            Object.keys(this.adhocOverrides).length > 0
              ? html`
                  <div class="override-chips">
                    ${Object.entries(this.adhocOverrides).map(
                      ([k, v]) => html`
                        <span class="override-chip">
                          <code
                            >${k} = ${this.formatAdhocOverrideValue(k, v)}</code
                          >
                          <button
                            type="button"
                            title="Remove override"
                            @click=${() => this.removeAdhocOverride(k)}
                          >
                            &times;
                          </button>
                        </span>
                      `
                    )}
                  </div>
                `
              : nothing
          }
          ${
            this.previewError
              ? html`<div class="warning-banner">${this.previewError}</div>`
              : nothing
          }
          ${
            componentPreviews.length > 0
              ? html`
                  <div class="preview-list">
                    ${componentPreviews.map(comp =>
                      this.renderComponentPreviewCard(comp)
                    )}
                  </div>
                `
              : html`
                  <div class="json-viewer-box">
                    <div
                      style="font-size: 0.78rem; font-weight: 600; margin-bottom: 6px; color: var(--dorc-text-secondary);"
                    >
                      Tier Default Resolved Parameters
                      (${this.previewData?.EnvironmentTier || this.selectedTier})
                    </div>
                    <hegs-json-viewer
                      .data=${{
                        environment_tier:
                          this.previewData?.EnvironmentTier ||
                          this.selectedTier,
                        termination_protection: 'false',
                        service_termination_protection: 'false',
                        ...Object.fromEntries(
                          Object.entries(this.adhocOverrides).map(([k, v]) => [
                            k,
                            this.formatAdhocOverrideValue(k, v)
                          ])
                        )
                      }}
                      ${ref((el?: Element) => {
                        if (el) {
                          setTimeout(
                            () => (el as HegsJsonViewer).expandAll(),
                            0
                          );
                        }
                      })}
                    ></hegs-json-viewer>
                  </div>
                `
          }
        </div>
      </div>
    `;
  };

  private renderDialogFooter = () => {
    const canProvision =
      Boolean(this.selectedProject?.ProjectId) &&
      !this.templatesLoading &&
      !this.templatesLoadFailed &&
      !this.previewLoading &&
      !this.previewError &&
      Boolean(this.previewData) &&
      (this.templates.length === 0 || Boolean(this.selectedTemplate?.Id)) &&
      Boolean(this.environmentName.trim()) &&
      this.isTtlValid() &&
      !this.isProvisioning;

    return html`
      <div class="wizard-footer">
        <span style="font-size: 0.78rem; color: var(--dorc-text-secondary);">
          Non-production ephemeral plans auto-approve during provisioning &amp;
          teardown.
        </span>
        <div class="wizard-footer-right">
          <vaadin-button
            theme="tertiary"
            ?disabled=${this.isProvisioning}
            @click=${() => {
              this.opened = false;
            }}
          >
            Cancel
          </vaadin-button>
          <vaadin-button
            theme="primary"
            ?disabled=${!canProvision}
            @click=${this.submitProvision}
          >
            <vaadin-icon icon="vaadin:play" slot="prefix"></vaadin-icon>
            ${
              this.isProvisioning
                ? 'Spinning Up Environment...'
                : 'Spin Up Environment'
            }
          </vaadin-button>
        </div>
      </div>
    `;
  };

  render() {
    return html`
      <vaadin-dialog
        aria-label="Spin Up Temporary Environment"
        .opened=${this.opened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.opened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderDialogHeader, [])}
        ${dialogRenderer(this.renderDialogBody, [
          this.projects,
          this.selectedProject,
          this.nonProdEnvironments,
          this.templates,
          this.selectedTemplate,
          this.projectComponents,
          this.templateBuilderMode,
          this.builderName,
          this.builderDescription,
          this.builderDefaultTier,
          this.builderBaseEnvironmentId,
          this.builderDefaultTtlMinutes,
          this.builderMaxTtlMinutes,
          this.builderProvisionComponentIds,
          this.builderValidationComponentIds,
          this.builderTierOverridesJson,
          this.builderOverrideKey,
          this.builderOverrideValue,
          this.builderSaving,
          this.environmentName,
          this.selectedBaseEnvironmentId,
          this.selectedTier,
          this.ttlMinutes,
          this.runValidationComponents,
          this.buildNum,
          this.buildUrl,
          this.buildText,
          this.adhocOverrides,
          this.adhocKey,
          this.adhocValue,
          this.previewLoading,
          this.previewData,
          this.previewError
        ])}
        ${dialogFooterRenderer(this.renderDialogFooter, [
          this.selectedProject,
          this.templatesLoading,
          this.templatesLoadFailed,
          this.previewLoading,
          this.previewData,
          this.previewError,
          this.templates,
          this.environmentName,
          this.ttlMinutes,
          this.selectedTemplate,
          this.isProvisioning
        ])}
      ></vaadin-dialog>
    `;
  }
}
