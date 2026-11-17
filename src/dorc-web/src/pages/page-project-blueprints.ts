import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/icons';
import '@vaadin/number-field';
import '@vaadin/text-area';
import '@vaadin/text-field';
import {
  dialogFooterRenderer,
  dialogHeaderRenderer,
  dialogRenderer
} from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, nothing, PropertyValues } from 'lit';
import { customElement, property, query, state } from 'lit/decorators.js';
import {
  EnvironmentApiModel,
  EnvironmentApiModelTemplateApiModel,
  EnvironmentBlueprintCaptureFromEnvironmentRequestApiModel,
  EnvironmentBlueprintCloneRequestApiModel,
  EnvironmentBlueprintDefinitionApiModel,
  EnvironmentBlueprintHealthStatusApiModel,
  EnvironmentBlueprintImportRequestApiModel,
  EnvironmentBlueprintSchemaContextApiModel,
  EnvironmentTemplateApiModel,
  EphemeralEnvironmentsApi,
  RefDataEnvironmentsApi,
  RefDataProjectEnvironmentMappingsApi
} from '../apis/dorc-api';
import { confirmPrompt } from '../components/confirm-prompt';
import '../components/dorc-spinner';
import '../components/ephemeral-blueprint-editor-dialog';
import { EphemeralBlueprintEditorDialog } from '../components/ephemeral-blueprint-editor-dialog';
import '../components/ephemeral-environment-wizard-dialog';
import { EphemeralEnvironmentWizardDialog } from '../components/ephemeral-environment-wizard-dialog';
import { ErrorNotification } from '../components/notifications/error-notification';
import '../components/project-workspace-nav';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { PageElement } from '../helpers/page-element';
import { navigate } from '../router/router';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

@customElement('page-project-blueprints')
export class PageProjectBlueprints extends PageElement {
  static get styles() {
    return css`
      :host {
        display: flex;
        flex-direction: column;
        width: 100%;
        height: 100%;
        overflow: hidden;
        font-family: var(--lumo-font-family);
      }

      .workspace-toolbar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 8px 12px;
        padding: 10px var(--lumo-space-m);
        border-bottom: 1px solid var(--dorc-border-color);
        background: var(--dorc-bg-primary);
        flex-shrink: 0;
      }

      .toolbar-left {
        display: flex;
        align-items: center;
        gap: 12px;
        flex-wrap: wrap;
      }

      .toolbar-title {
        margin: 0;
        font-size: 1rem;
        font-weight: 700;
        color: var(--dorc-text-primary);
      }

      .stat-pill {
        display: inline-flex;
        align-items: center;
        gap: 5px;
        padding: 2px 9px;
        border-radius: 999px;
        font-size: 0.74rem;
        font-weight: 600;
        background: var(--dorc-bg-secondary);
        color: var(--dorc-text-secondary);
        border: 1px solid var(--dorc-border-color);
      }

      .stat-pill.healthy {
        background: rgba(21, 128, 61, 0.1);
        color: var(--dorc-Badge-green-text, #15803d);
        border-color: rgba(21, 128, 61, 0.25);
      }

      .stat-pill.drift {
        background: rgba(180, 83, 9, 0.12);
        color: #b45309;
        border-color: rgba(180, 83, 9, 0.3);
      }

      .toolbar-actions {
        display: inline-flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 8px;
      }

      .content-area {
        flex: 1;
        overflow-y: auto;
        padding: var(--lumo-space-m);
        display: flex;
        flex-direction: column;
        gap: 14px;
      }

      .blueprints-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(380px, 1fr));
        gap: 14px;
        align-items: stretch;
      }

      .blueprint-card {
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        gap: 12px;
        padding: 14px 16px;
        border-radius: 8px;
        border: 1px solid var(--dorc-border-color);
        background: var(--dorc-bg-secondary);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
        transition: border-color 0.15s ease;
      }

      .blueprint-card:hover {
        border-color: var(--dorc-link-color);
      }

      .blueprint-card.has-drift {
        border-left: 4px solid #d97706;
      }

      .card-top {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .card-header-row {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: 8px;
      }

      .card-title {
        font-size: 0.96rem;
        font-weight: 700;
        color: var(--dorc-text-primary);
        margin: 0;
        word-break: break-word;
      }

      .badges-row {
        display: flex;
        align-items: center;
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

      .badge-tier {
        background: rgba(9, 105, 218, 0.12);
        color: var(--dorc-link-color);
      }

      .badge-healthy {
        background: rgba(21, 128, 61, 0.12);
        color: var(--dorc-Badge-green-text, #15803d);
      }

      .badge-drift {
        background: rgba(180, 83, 9, 0.14);
        color: #b45309;
      }

      .badge-meta {
        background: var(--lumo-contrast-5pct);
        color: var(--dorc-text-secondary);
      }

      .card-desc {
        font-size: 0.8rem;
        color: var(--dorc-text-secondary);
        margin: 0;
        line-height: 1.4;
      }

      .drift-warnings-box {
        padding: 8px 10px;
        border-radius: 6px;
        background: rgba(245, 158, 11, 0.1);
        border: 1px solid rgba(245, 158, 11, 0.35);
        font-size: 0.75rem;
        color: var(--dorc-text-primary);
        display: flex;
        flex-direction: column;
        gap: 4px;
      }

      .drift-warning-item {
        display: flex;
        align-items: center;
        gap: 6px;
      }

      .components-summary {
        display: flex;
        flex-direction: column;
        gap: 6px;
        padding-top: 4px;
        border-top: 1px dashed var(--dorc-border-color);
      }

      .comp-group-label {
        font-size: 0.7rem;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--dorc-text-secondary);
      }

      .comp-pills {
        display: flex;
        flex-wrap: wrap;
        gap: 4px;
      }

      .comp-pill {
        font-size: 0.72rem;
        font-family: var(--lumo-font-family-monospace, monospace);
        padding: 2px 7px;
        border-radius: 4px;
        background: var(--dorc-bg-primary);
        border: 1px solid var(--dorc-border-color);
        color: var(--dorc-text-primary);
      }

      .card-footer {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 8px;
        padding-top: 8px;
        border-top: 1px solid var(--dorc-border-color);
      }

      .card-actions-left,
      .card-actions-right {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        flex-wrap: wrap;
      }

      .empty-state {
        padding: 36px 24px;
        text-align: center;
        border: 1px dashed var(--dorc-border-color);
        border-radius: 8px;
        background: var(--dorc-bg-secondary);
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 10px;
      }

      .empty-state h3 {
        margin: 0;
        font-size: 1rem;
        color: var(--dorc-text-primary);
      }

      .empty-state p {
        margin: 0;
        max-width: 560px;
        font-size: 0.84rem;
        color: var(--dorc-text-secondary);
      }

      .modal-form {
        display: flex;
        flex-direction: column;
        gap: 12px;
        width: 560px;
        max-width: 92vw;
        padding: 4px;
      }
    `;
  }

  @property({ type: String })
  project = '';

  @state() private loading = true;
  @state() private projectData: EnvironmentApiModelTemplateApiModel | undefined;
  @state() private templates: EnvironmentTemplateApiModel[] = [];
  @state() private schemaContext:
    EnvironmentBlueprintSchemaContextApiModel | undefined;
  @state() private nonProdEnvironments: EnvironmentApiModel[] = [];
  @state() private searchQuery = '';

  // Capture from environment dialog state
  @state() private captureDialogOpened = false;
  @state() private captureEnvId: number | null = null;
  @state() private captureName = '';
  @state() private captureDescription = '';
  @state() private captureDefaultTier = 'ephemeral';
  @state() private captureDefaultTtl = 240;
  @state() private captureMaxTtl = 1440;
  @state() private captureSaving = false;

  // Clone dialog state
  @state() private cloneDialogOpened = false;
  @state() private cloneSourceTemplate: EnvironmentTemplateApiModel | null =
    null;
  @state() private cloneName = '';
  @state() private cloneDescription = '';
  @state() private cloneSaving = false;

  // Import declarative JSON dialog state
  @state() private importDialogOpened = false;
  @state() private importJsonText = '';
  @state() private importSaving = false;

  @query('#blueprint-editor-dialog')
  private editorDialog!: EphemeralBlueprintEditorDialog;

  @query('#blueprint-spinup-wizard')
  private spinupWizard!: EphemeralEnvironmentWizardDialog;

  protected override firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);
    const rawProject = location.pathname.split('/')[2] || '';
    this.project = decodeURIComponent(rawProject);
    this.loadAllData();
  }

  private showError(msg: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', msg);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private loadAllData() {
    if (!this.project) {
      this.loading = false;
      return;
    }

    this.loading = true;
    const mappingApi = new RefDataProjectEnvironmentMappingsApi(
      dorcApiConfiguration
    );
    mappingApi
      .refDataProjectEnvironmentMappingsGet({
        project: this.project,
        includeRead: true
      })
      .subscribe({
        next: (data: EnvironmentApiModelTemplateApiModel) => {
          this.projectData = data;
          const projectId = data.Project?.ProjectId;
          if (!projectId) {
            this.loading = false;
            return;
          }
          this.refreshBlueprintsAndSchema(projectId);
          this.loadNonProdEnvironments();
        },
        error: () => {
          this.loading = false;
          void navigate('not-found');
        }
      });
  }

  private loadNonProdEnvironments() {
    const envApi = new RefDataEnvironmentsApi(dorcApiConfiguration);
    envApi.refDataEnvironmentsGet({ env: '' }).subscribe({
      next: (envs: EnvironmentApiModel[]) => {
        this.nonProdEnvironments = (envs || [])
          .filter(e => !e.EnvironmentIsProd && !e.IsEphemeral)
          .sort((a, b) =>
            (a.EnvironmentName ?? '').localeCompare(b.EnvironmentName ?? '')
          );
      }
    });
  }

  private refreshBlueprintsAndSchema(projectId?: number) {
    const targetProjectId = projectId ?? this.projectData?.Project?.ProjectId;
    if (!targetProjectId) {
      return;
    }

    this.loading = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsTemplatesGet({ projectId: targetProjectId })
      .subscribe({
        next: (templates: EnvironmentTemplateApiModel[]) => {
          this.templates = templates || [];
          this.loading = false;
        },
        error: (err: unknown) => {
          this.loading = false;
          this.showError(
            retrieveErrorMessage(err, 'Failed to load blueprints.')
          );
        }
      });

    api
      .ephemeralEnvironmentsTemplatesSchemaContextGet({
        projectId: targetProjectId
      })
      .subscribe({
        next: (ctx: EnvironmentBlueprintSchemaContextApiModel) => {
          this.schemaContext = ctx;
        }
      });
  }

  private getHealthForTemplate(
    templateId: number | undefined
  ): EnvironmentBlueprintHealthStatusApiModel | undefined {
    if (!templateId || !this.schemaContext?.TemplateHealth) {
      return undefined;
    }
    return this.schemaContext.TemplateHealth.find(
      h => h.TemplateId === templateId
    );
  }

  private getComponentName(componentId: number): string {
    const match = (this.schemaContext?.Components || []).find(
      c => c.ComponentId === componentId
    );
    return match?.ComponentName || `Component #${componentId}`;
  }

  private getBaseEnvironmentName(
    template: EnvironmentTemplateApiModel
  ): string {
    if (template.BaseEnvironmentName) {
      return template.BaseEnvironmentName;
    }
    if (!template.BaseEnvironmentId) {
      return 'Ad-hoc / None';
    }
    const match = this.nonProdEnvironments.find(
      e => e.EnvironmentId === template.BaseEnvironmentId
    );
    return match?.EnvironmentName || `Env #${template.BaseEnvironmentId}`;
  }

  private countTierOverrides(rawJson: string | null | undefined): number {
    if (!rawJson || !rawJson.trim()) {
      return 0;
    }
    try {
      const parsed = JSON.parse(rawJson);
      if (!parsed || typeof parsed !== 'object') {
        return 0;
      }
      let total = 0;
      for (const val of Object.values(parsed)) {
        if (val && typeof val === 'object') {
          total += Object.keys(val as Record<string, unknown>).length;
        }
      }
      return total;
    } catch {
      return 0;
    }
  }

  private openCreateBlueprint = () => {
    const proj = this.projectData?.Project;
    if (!proj?.ProjectId) return;
    this.editorDialog.openCreate({
      projectId: proj.ProjectId,
      projectName: proj.ProjectName || this.project,
      nonProdEnvironments: this.nonProdEnvironments
    });
  };

  private openEditBlueprint(template: EnvironmentTemplateApiModel) {
    const proj = this.projectData?.Project;
    if (!proj?.ProjectId) return;
    this.editorDialog.openEdit({
      projectId: proj.ProjectId,
      projectName: proj.ProjectName || this.project,
      template,
      nonProdEnvironments: this.nonProdEnvironments
    });
  }

  private openSpinUpWithBlueprint(template: EnvironmentTemplateApiModel) {
    const projectName = this.projectData?.Project?.ProjectName || this.project;
    this.spinupWizard.open({
      projectName,
      baseEnvironmentId: template.BaseEnvironmentId ?? undefined,
      templateId: template.Id
    });
  }

  private openCloneBlueprintDialog(template: EnvironmentTemplateApiModel) {
    this.cloneSourceTemplate = template;
    this.cloneName = `${template.Name || 'Blueprint'} (Copy)`;
    this.cloneDescription = template.Description || '';
    this.cloneDialogOpened = true;
  }

  private submitCloneBlueprint = () => {
    if (!this.cloneSourceTemplate?.Id || !this.cloneName.trim()) {
      this.showError('New blueprint name is required.');
      return;
    }

    this.cloneSaving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const req: EnvironmentBlueprintCloneRequestApiModel = {
      NewName: this.cloneName.trim(),
      Description: this.cloneDescription.trim() || null
    };

    api
      .ephemeralEnvironmentsTemplatesTemplateIdClonePost({
        templateId: this.cloneSourceTemplate.Id,
        environmentBlueprintCloneRequestApiModel: req
      })
      .subscribe({
        next: (cloned: EnvironmentTemplateApiModel) => {
          this.cloneSaving = false;
          this.cloneDialogOpened = false;
          Notification.show(`Cloned blueprint '${cloned.Name}'.`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 4000
          });
          this.refreshBlueprintsAndSchema();
        },
        error: (err: unknown) => {
          this.cloneSaving = false;
          this.showError(
            retrieveErrorMessage(err, 'Failed to clone blueprint.')
          );
        }
      });
  };

  private openCaptureDialog = () => {
    const defaultEnv =
      this.projectData?.Items?.find(
        e => !e.EnvironmentIsProd && !e.IsEphemeral
      ) ?? this.nonProdEnvironments[0];
    this.captureEnvId = defaultEnv?.EnvironmentId ?? null;
    this.captureName = defaultEnv?.EnvironmentName
      ? `${defaultEnv.EnvironmentName} Ephemeral Blueprint`
      : `${this.project} Captured Blueprint`;
    this.captureDescription =
      'Captured from non-production base environment topology and configuration.';
    this.captureDefaultTier = 'ephemeral';
    this.captureDefaultTtl = 240;
    this.captureMaxTtl = 1440;
    this.captureDialogOpened = true;
  };

  private submitCaptureFromEnvironment = () => {
    const projectId = this.projectData?.Project?.ProjectId;
    if (!projectId || !this.captureEnvId || !this.captureName.trim()) {
      this.showError(
        'Project, Source Base Environment, and Blueprint Name are required.'
      );
      return;
    }

    this.captureSaving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const payload: EnvironmentBlueprintCaptureFromEnvironmentRequestApiModel = {
      ProjectId: projectId,
      BaseEnvironmentId: this.captureEnvId,
      Name: this.captureName.trim(),
      Description: this.captureDescription.trim() || null,
      DefaultTier: this.captureDefaultTier,
      DefaultTtlMinutes: this.captureDefaultTtl,
      MaxTtlMinutes: this.captureMaxTtl
    };

    api
      .ephemeralEnvironmentsTemplatesCaptureFromEnvironmentPost({
        environmentBlueprintCaptureFromEnvironmentRequestApiModel: payload
      })
      .subscribe({
        next: (created: EnvironmentTemplateApiModel) => {
          this.captureSaving = false;
          this.captureDialogOpened = false;
          Notification.show(
            `Captured blueprint '${created.Name}' from environment.`,
            {
              theme: 'success',
              position: 'bottom-start',
              duration: 4000
            }
          );
          this.refreshBlueprintsAndSchema(projectId);
        },
        error: (err: unknown) => {
          this.captureSaving = false;
          this.showError(
            retrieveErrorMessage(
              err,
              'Failed to capture blueprint from environment.'
            )
          );
        }
      });
  };

  private exportBlueprintJson(template: EnvironmentTemplateApiModel) {
    if (!template.Id) return;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsTemplatesTemplateIdExportGet({
        templateId: template.Id
      })
      .subscribe({
        next: (portable: EnvironmentBlueprintDefinitionApiModel) => {
          const jsonText = JSON.stringify(portable, null, 2);
          const slug = (template.Name || 'blueprint')
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
          const blob = new Blob([jsonText], { type: 'application/json' });
          const url = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = `${slug || 'blueprint'}.blueprint.json`;
          anchor.click();
          URL.revokeObjectURL(url);

          Notification.show(`Exported portable blueprint '${template.Name}'.`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 3500
          });
        },
        error: (err: unknown) => {
          this.showError(
            retrieveErrorMessage(err, 'Failed to export portable blueprint.')
          );
        }
      });
  }

  private openImportDialog = () => {
    this.importJsonText = '';
    this.importDialogOpened = true;
  };

  private handleImportFileSelected = (e: Event) => {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    file
      .text()
      .then(content => {
        this.importJsonText = content;
      })
      .catch(() => {
        this.showError('Could not read the selected blueprint file.');
      });
  };

  private submitImportBlueprint = () => {
    const raw = this.importJsonText.trim();
    if (!raw) {
      this.showError('Paste or upload a portable blueprint JSON definition.');
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      this.showError('Invalid JSON syntax in blueprint definition.');
      return;
    }

    let blueprints: EnvironmentBlueprintDefinitionApiModel[] = [];
    if (Array.isArray(parsed)) {
      blueprints = parsed as EnvironmentBlueprintDefinitionApiModel[];
    } else if (parsed && typeof parsed === 'object') {
      const obj = parsed as Record<string, unknown>;
      if (Array.isArray(obj.Blueprints)) {
        blueprints = obj.Blueprints as EnvironmentBlueprintDefinitionApiModel[];
      } else if (Array.isArray(obj.blueprints)) {
        blueprints = obj.blueprints as EnvironmentBlueprintDefinitionApiModel[];
      } else {
        blueprints = [obj as unknown as EnvironmentBlueprintDefinitionApiModel];
      }
    }

    const projectName = this.projectData?.Project?.ProjectName || this.project;
    blueprints = blueprints.map(bp => ({
      ...bp,
      ProjectName: bp.ProjectName || projectName
    }));

    this.importSaving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const req: EnvironmentBlueprintImportRequestApiModel = {
      ProjectId: this.projectData?.Project?.ProjectId ?? null,
      ProjectName: projectName,
      Blueprints: blueprints
    };

    api
      .ephemeralEnvironmentsTemplatesImportPost({
        environmentBlueprintImportRequestApiModel: req
      })
      .subscribe({
        next: res => {
          this.importSaving = false;
          this.importDialogOpened = false;
          Notification.show(
            `Imported blueprints (${res.CreatedCount ?? 0} created, ${res.UpdatedCount ?? 0} updated).`,
            {
              theme: 'success',
              position: 'bottom-start',
              duration: 4000
            }
          );
          this.refreshBlueprintsAndSchema();
        },
        error: (err: unknown) => {
          this.importSaving = false;
          this.showError(
            retrieveErrorMessage(err, 'Failed to import blueprint JSON.')
          );
        }
      });
  };

  private async deleteBlueprint(template: EnvironmentTemplateApiModel) {
    if (!template.Id) return;
    const confirmed = await confirmPrompt(
      `Delete Blueprint '${template.Name}'?`,
      {
        header: 'Delete Blueprint Template',
        confirmText: 'Delete Blueprint',
        warningTitle: 'Project-wide Blueprint Removal',
        warningMessage:
          'Active ephemeral environments referencing this blueprint will block deletion until torn down.'
      }
    );
    if (!confirmed) return;

    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    api
      .ephemeralEnvironmentsTemplatesTemplateIdDelete({
        templateId: template.Id
      })
      .subscribe({
        next: () => {
          Notification.show(`Deleted blueprint '${template.Name}'.`, {
            theme: 'success',
            position: 'bottom-start',
            duration: 3500
          });
          this.refreshBlueprintsAndSchema();
        },
        error: (err: unknown) => {
          this.showError(
            retrieveErrorMessage(err, 'Failed to delete blueprint.')
          );
        }
      });
  }

  private renderCaptureDialogHeader = () => html`
    <div style="font-weight: 700; font-size: 0.96rem;">
      Capture Blueprint from Non-Prod Environment
    </div>
  `;

  private renderCaptureDialogBody = () => html`
    <div class="modal-form">
      <div style="font-size: 0.8rem; color: var(--dorc-text-secondary);">
        Bootstrap a reusable Ephemeral Blueprint from an existing non-production
        environment's attached Catalog Terraform components and saved
        configuration.
      </div>
      <vaadin-combo-box
        label="Source Non-Prod Environment"
        item-label-path="EnvironmentName"
        item-value-path="EnvironmentId"
        .items=${this.nonProdEnvironments}
        .selectedItem=${
          this.nonProdEnvironments.find(
            e => e.EnvironmentId === this.captureEnvId
          ) ?? null
        }
        @selected-item-changed=${(
          e: CustomEvent<{ value: EnvironmentApiModel | null | undefined }>
        ) => {
          const env = e.detail.value;
          this.captureEnvId = env?.EnvironmentId ?? null;
          if (env?.EnvironmentName && !this.captureName) {
            this.captureName = `${env.EnvironmentName} Ephemeral Blueprint`;
          }
        }}
      ></vaadin-combo-box>
      <vaadin-text-field
        label="New Blueprint Name"
        required
        .value=${this.captureName}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.captureName = e.detail.value;
        }}
      ></vaadin-text-field>
      <vaadin-text-field
        label="Description"
        .value=${this.captureDescription}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.captureDescription = e.detail.value;
        }}
      ></vaadin-text-field>
      <div
        style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px;"
      >
        <vaadin-combo-box
          label="Default Tier"
          .items=${['ephemeral', 'xx', 'dv', 'in', 'qa', 'ut', 'pp', 'ff']}
          .value=${this.captureDefaultTier}
          @value-changed=${(e: CustomEvent<{ value: string }>) => {
            if (e.detail.value) {
              this.captureDefaultTier = e.detail.value;
            }
          }}
        ></vaadin-combo-box>
        <vaadin-number-field
          label="Default TTL (m)"
          min="15"
          max="10080"
          .value=${String(this.captureDefaultTtl)}
          @value-changed=${(e: CustomEvent<{ value: string }>) => {
            const parsed = Number.parseInt(e.detail.value, 10);
            if (!Number.isNaN(parsed) && parsed > 0) {
              this.captureDefaultTtl = parsed;
            }
          }}
        ></vaadin-number-field>
        <vaadin-number-field
          label="Max TTL Cap (m)"
          min="30"
          max="10080"
          .value=${String(this.captureMaxTtl)}
          @value-changed=${(e: CustomEvent<{ value: string }>) => {
            const parsed = Number.parseInt(e.detail.value, 10);
            if (!Number.isNaN(parsed) && parsed > 0) {
              this.captureMaxTtl = parsed;
            }
          }}
        ></vaadin-number-field>
      </div>
    </div>
  `;

  private renderCaptureDialogFooter = () => html`
    <div style="display: flex; justify-content: flex-end; gap: 8px;">
      <vaadin-button
        theme="tertiary"
        @click=${() => {
          this.captureDialogOpened = false;
        }}
      >
        Cancel
      </vaadin-button>
      <vaadin-button
        theme="primary"
        ?disabled=${this.captureSaving || !this.captureEnvId || !this.captureName.trim()}
        @click=${this.submitCaptureFromEnvironment}
      >
        <vaadin-icon icon="vaadin:magic" slot="prefix"></vaadin-icon>
        ${this.captureSaving ? 'Capturing...' : 'Capture Blueprint'}
      </vaadin-button>
    </div>
  `;

  private renderCloneDialogHeader = () => html`
    <div style="font-weight: 700; font-size: 0.96rem;">
      Clone Blueprint: ${this.cloneSourceTemplate?.Name ?? ''}
    </div>
  `;

  private renderCloneDialogBody = () => html`
    <div class="modal-form">
      <vaadin-text-field
        label="Cloned Blueprint Name"
        required
        .value=${this.cloneName}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.cloneName = e.detail.value;
        }}
      ></vaadin-text-field>
      <vaadin-text-field
        label="Description"
        .value=${this.cloneDescription}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.cloneDescription = e.detail.value;
        }}
      ></vaadin-text-field>
    </div>
  `;

  private renderCloneDialogFooter = () => html`
    <div style="display: flex; justify-content: flex-end; gap: 8px;">
      <vaadin-button
        theme="tertiary"
        @click=${() => {
          this.cloneDialogOpened = false;
        }}
      >
        Cancel
      </vaadin-button>
      <vaadin-button
        theme="primary"
        ?disabled=${this.cloneSaving || !this.cloneName.trim()}
        @click=${this.submitCloneBlueprint}
      >
        <vaadin-icon icon="vaadin:copy" slot="prefix"></vaadin-icon>
        ${this.cloneSaving ? 'Cloning...' : 'Clone Blueprint'}
      </vaadin-button>
    </div>
  `;

  private renderImportDialogHeader = () => html`
    <div style="font-weight: 700; font-size: 0.96rem;">
      Import Declarative Blueprint (GitOps JSON)
    </div>
  `;

  private renderImportDialogBody = () => html`
    <div class="modal-form">
      <div style="font-size: 0.8rem; color: var(--dorc-text-secondary);">
        Upload or paste a portable name-based blueprint JSON definition (<code
          >schemaVersion: "1.0"</code
        >). Component names and base environment names are automatically
        resolved and upserted by blueprint name.
      </div>
      <div>
        <input
          type="file"
          accept=".json,application/json"
          @change=${this.handleImportFileSelected}
        />
      </div>
      <vaadin-text-area
        label="Portable Blueprint JSON"
        style="width: 100%; min-height: 240px; font-family: monospace;"
        placeholder='{ "SchemaVersion": "1.0", "ProjectName": "...", "Name": "...", "ProvisionComponentNames": [...] }'
        .value=${this.importJsonText}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          this.importJsonText = e.detail.value;
        }}
      ></vaadin-text-area>
    </div>
  `;

  private renderImportDialogFooter = () => html`
    <div style="display: flex; justify-content: flex-end; gap: 8px;">
      <vaadin-button
        theme="tertiary"
        @click=${() => {
          this.importDialogOpened = false;
        }}
      >
        Cancel
      </vaadin-button>
      <vaadin-button
        theme="primary"
        ?disabled=${this.importSaving || !this.importJsonText.trim()}
        @click=${this.submitImportBlueprint}
      >
        <vaadin-icon icon="vaadin:upload" slot="prefix"></vaadin-icon>
        ${this.importSaving ? 'Importing...' : 'Apply Blueprint'}
      </vaadin-button>
    </div>
  `;

  render() {
    const projectName =
      this.projectData?.Project?.ProjectName || this.project || '';
    const q = this.searchQuery.trim().toLowerCase();
    const filteredTemplates = this.templates.filter(t => {
      if (!q) return true;
      return (
        (t.Name ?? '').toLowerCase().includes(q) ||
        (t.Description ?? '').toLowerCase().includes(q) ||
        (t.DefaultTier ?? '').toLowerCase().includes(q)
      );
    });

    const driftCount = (this.schemaContext?.TemplateHealth || []).filter(
      h => !h.IsHealthy
    ).length;
    const healthyCount = Math.max(0, this.templates.length - driftCount);

    return html`
      <project-workspace-nav
        .projectId=${this.projectData?.Project?.ProjectId}
        .projectName=${projectName}
        activeTab="blueprints"
      ></project-workspace-nav>

      <dorc-spinner
        style="--dorc-spinner-z-index: 1000"
        ?hidden=${!this.loading}
      ></dorc-spinner>

      <div class="workspace-toolbar">
        <div class="toolbar-left">
          <h2 class="toolbar-title">${projectName} Blueprints</h2>
          <span class="stat-pill">
            ${this.templates.length}
            ${this.templates.length === 1 ? 'Blueprint' : 'Blueprints'}
          </span>
          ${
            this.templates.length > 0
              ? html`
                  <span class="stat-pill healthy">
                    <vaadin-icon
                      icon="vaadin:check-circle"
                      style="width: 12px; height: 12px;"
                    ></vaadin-icon>
                    ${healthyCount} Healthy
                  </span>
                `
              : nothing
          }
          ${
            driftCount > 0
              ? html`
                  <span class="stat-pill drift">
                    <vaadin-icon
                      icon="vaadin:warning"
                      style="width: 12px; height: 12px;"
                    ></vaadin-icon>
                    ${driftCount} Drift
                    ${driftCount === 1 ? 'Warning' : 'Warnings'}
                  </span>
                `
              : nothing
          }
          <vaadin-text-field
            theme="small"
            clear-button-visible
            placeholder="Filter blueprints..."
            .value=${this.searchQuery}
            @value-changed=${(e: CustomEvent<{ value: string }>) => {
              this.searchQuery = e.detail.value;
            }}
          >
            <vaadin-icon icon="vaadin:search" slot="prefix"></vaadin-icon>
          </vaadin-text-field>
        </div>

        <div class="toolbar-actions">
          <vaadin-button theme="small" @click=${this.openImportDialog}>
            <vaadin-icon icon="vaadin:upload" slot="prefix"></vaadin-icon>
            Import JSON
          </vaadin-button>
          <vaadin-button theme="small" @click=${this.openCaptureDialog}>
            <vaadin-icon icon="vaadin:magic" slot="prefix"></vaadin-icon>
            Capture from Environment
          </vaadin-button>
          <vaadin-button
            theme="small primary"
            @click=${this.openCreateBlueprint}
          >
            <vaadin-icon icon="vaadin:plus" slot="prefix"></vaadin-icon>
            New Blueprint
          </vaadin-button>
        </div>
      </div>

      <div class="content-area">
        ${
          filteredTemplates.length === 0 && !this.loading
            ? html`
                <div class="empty-state">
                  <vaadin-icon
                    icon="vaadin:cubes"
                    style="width: 32px; height: 32px; color: var(--dorc-link-color);"
                  ></vaadin-icon>
                  <h3>No Ephemeral Blueprints Configured Yet</h3>
                  <p>
                    Create a schema-guided Blueprint to standardize how
                    temporary environments are provisioned for
                    <strong>${projectName}</strong>, capture one from an
                    existing Base Environment, or import a portable GitOps JSON
                    definition.
                  </p>
                  <div
                    style="display: flex; gap: 8px; flex-wrap: wrap; justify-content: center;"
                  >
                    <vaadin-button
                      theme="primary"
                      @click=${this.openCreateBlueprint}
                    >
                      <vaadin-icon
                        icon="vaadin:plus"
                        slot="prefix"
                      ></vaadin-icon>
                      New Blueprint
                    </vaadin-button>
                    <vaadin-button @click=${this.openCaptureDialog}>
                      <vaadin-icon
                        icon="vaadin:magic"
                        slot="prefix"
                      ></vaadin-icon>
                      Capture from Base Environment
                    </vaadin-button>
                    <vaadin-button
                      theme="tertiary"
                      @click=${this.openImportDialog}
                    >
                      <vaadin-icon
                        icon="vaadin:upload"
                        slot="prefix"
                      ></vaadin-icon>
                      Import JSON
                    </vaadin-button>
                  </div>
                </div>
              `
            : html`
                <div class="blueprints-grid">
                  ${filteredTemplates.map(tpl => {
                    const health = this.getHealthForTemplate(tpl.Id);
                    const isHealthy = health ? Boolean(health.IsHealthy) : true;
                    const warnings = health?.Warnings || [];
                    const provIds = tpl.ProvisionComponentIds || [];
                    const valIds = tpl.ValidationComponentIds || [];
                    const overrideCount = this.countTierOverrides(
                      tpl.TierParameterOverridesJson
                    );

                    return html`
                      <div
                        class="blueprint-card ${!isHealthy ? 'has-drift' : ''}"
                      >
                        <div class="card-top">
                          <div class="card-header-row">
                            <h3 class="card-title">${tpl.Name}</h3>
                            <div class="badges-row">
                              <span class="badge badge-tier">
                                Tier:
                                ${(tpl.DefaultTier || 'ephemeral').toUpperCase()}
                              </span>
                              ${
                                isHealthy
                                  ? html`
                                      <span class="badge badge-healthy">
                                        <vaadin-icon
                                          icon="vaadin:check"
                                          style="width: 10px; height: 10px;"
                                        ></vaadin-icon>
                                        Healthy
                                      </span>
                                    `
                                  : html`
                                      <span class="badge badge-drift">
                                        <vaadin-icon
                                          icon="vaadin:warning"
                                          style="width: 10px; height: 10px;"
                                        ></vaadin-icon>
                                        Drift (${warnings.length})
                                      </span>
                                    `
                              }
                            </div>
                          </div>

                          ${
                            tpl.Description
                              ? html`<p class="card-desc">
                                  ${tpl.Description}
                                </p>`
                              : nothing
                          }

                          <div class="badges-row">
                            <span class="badge badge-meta">
                              Base Env: ${this.getBaseEnvironmentName(tpl)}
                            </span>
                            <span class="badge badge-meta">
                              TTL: ${tpl.DefaultTtlMinutes || 240}m (Max
                              ${tpl.MaxTtlMinutes || 1440}m)
                            </span>
                            <span class="badge badge-meta">
                              ${overrideCount} Tier
                              ${overrideCount === 1 ? 'Override' : 'Overrides'}
                            </span>
                          </div>

                          ${
                            warnings.length > 0
                              ? html`
                                  <div class="drift-warnings-box">
                                    ${warnings.map(
                                      w => html`
                                        <div class="drift-warning-item">
                                          <vaadin-icon
                                            icon="vaadin:warning"
                                            style="width: 12px; height: 12px; color: #d97706; flex-shrink: 0;"
                                          ></vaadin-icon>
                                          <span>${w}</span>
                                        </div>
                                      `
                                    )}
                                  </div>
                                `
                              : nothing
                          }

                          <div class="components-summary">
                            <span class="comp-group-label">
                              Provisioning Components (${provIds.length})
                            </span>
                            <div class="comp-pills">
                              ${
                                provIds.length > 0
                                  ? provIds.map(
                                      id => html`
                                        <span class="comp-pill">
                                          ${this.getComponentName(id)}
                                        </span>
                                      `
                                    )
                                  : html`<span
                                      style="font-size: 0.74rem; color: var(--dorc-text-secondary);"
                                      >None selected</span
                                    >`
                              }
                            </div>
                            ${
                              valIds.length > 0
                                ? html`
                                    <span
                                      class="comp-group-label"
                                      style="margin-top: 4px;"
                                    >
                                      Validation Smoke Tests (${valIds.length})
                                    </span>
                                    <div class="comp-pills">
                                      ${valIds.map(
                                        id => html`
                                          <span class="comp-pill">
                                            ${this.getComponentName(id)}
                                          </span>
                                        `
                                      )}
                                    </div>
                                  `
                                : nothing
                            }
                          </div>
                        </div>

                        <div class="card-footer">
                          <div class="card-actions-left">
                            <vaadin-button
                              theme="small primary"
                              @click=${() => this.openSpinUpWithBlueprint(tpl)}
                            >
                              <vaadin-icon
                                icon="vaadin:play"
                                slot="prefix"
                              ></vaadin-icon>
                              Spin Up
                            </vaadin-button>
                            <vaadin-button
                              theme="small"
                              @click=${() => this.openEditBlueprint(tpl)}
                            >
                              <vaadin-icon
                                icon="vaadin:sliders"
                                slot="prefix"
                              ></vaadin-icon>
                              Edit Matrix
                            </vaadin-button>
                          </div>

                          <div class="card-actions-right">
                            <vaadin-button
                              theme="small tertiary"
                              title="Clone Blueprint"
                              @click=${() => this.openCloneBlueprintDialog(tpl)}
                            >
                              <vaadin-icon
                                icon="vaadin:copy"
                                slot="prefix"
                              ></vaadin-icon>
                              Clone
                            </vaadin-button>
                            <vaadin-button
                              theme="small tertiary"
                              title="Export Portable GitOps JSON"
                              @click=${() => this.exportBlueprintJson(tpl)}
                            >
                              <vaadin-icon
                                icon="vaadin:download"
                                slot="prefix"
                              ></vaadin-icon>
                              Export
                            </vaadin-button>
                            <vaadin-button
                              theme="small error tertiary"
                              title="Delete Blueprint"
                              @click=${() => this.deleteBlueprint(tpl)}
                            >
                              <vaadin-icon icon="vaadin:trash"></vaadin-icon>
                            </vaadin-button>
                          </div>
                        </div>
                      </div>
                    `;
                  })}
                </div>
              `
        }
      </div>

      <ephemeral-blueprint-editor-dialog
        id="blueprint-editor-dialog"
        @ephemeral-blueprint-saved=${() => this.refreshBlueprintsAndSchema()}
      ></ephemeral-blueprint-editor-dialog>

      <ephemeral-environment-wizard-dialog
        id="blueprint-spinup-wizard"
      ></ephemeral-environment-wizard-dialog>

      <vaadin-dialog
        aria-label="Capture Blueprint from Non-Prod Environment"
        .opened=${this.captureDialogOpened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.captureDialogOpened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderCaptureDialogHeader, [])}
        ${dialogRenderer(this.renderCaptureDialogBody, [
          this.nonProdEnvironments,
          this.captureEnvId,
          this.captureName,
          this.captureDescription,
          this.captureDefaultTier,
          this.captureDefaultTtl,
          this.captureMaxTtl
        ])}
        ${dialogFooterRenderer(this.renderCaptureDialogFooter, [
          this.captureSaving,
          this.captureEnvId,
          this.captureName
        ])}
      ></vaadin-dialog>

      <vaadin-dialog
        aria-label="Clone Blueprint"
        .opened=${this.cloneDialogOpened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.cloneDialogOpened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderCloneDialogHeader, [
          this.cloneSourceTemplate
        ])}
        ${dialogRenderer(this.renderCloneDialogBody, [
          this.cloneName,
          this.cloneDescription
        ])}
        ${dialogFooterRenderer(this.renderCloneDialogFooter, [
          this.cloneSaving,
          this.cloneName
        ])}
      ></vaadin-dialog>

      <vaadin-dialog
        aria-label="Import Declarative Blueprint"
        .opened=${this.importDialogOpened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.importDialogOpened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderImportDialogHeader, [])}
        ${dialogRenderer(this.renderImportDialogBody, [this.importJsonText])}
        ${dialogFooterRenderer(this.renderImportDialogFooter, [
          this.importSaving,
          this.importJsonText
        ])}
      ></vaadin-dialog>
    `;
  }
}
