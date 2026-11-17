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
import '@vaadin/text-field';
import { css, html, LitElement, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { ref } from 'lit/directives/ref.js';
import type { Subscription } from 'rxjs';
import {
  EnvironmentApiModel,
  EnvironmentBlueprintComponentSchemaApiModel,
  EnvironmentBlueprintParameterSchemaApiModel,
  EnvironmentBlueprintSchemaContextApiModel,
  EnvironmentTemplateApiModel,
  EphemeralEnvironmentsApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import './hegs-json-viewer';
import { HegsJsonViewer } from './hegs-json-viewer';
import { ErrorNotification } from './notifications/error-notification';
import './notifications/error-notification';

const MATRIX_TIERS = [
  { key: 'ephemeral', label: 'Ephemeral (PR)' },
  { key: 'xx', label: 'Temporary (xx)' },
  { key: 'dv', label: 'Dev (dv)' },
  { key: 'in', label: 'Integration (in)' },
  { key: 'qa', label: 'QA (qa)' },
  { key: 'ut', label: 'UAT (ut)' },
  { key: 'pp', label: 'Pre-Prod (pp)' },
  { key: 'ff', label: 'Fix-on-Fail (ff)' }
];

@customElement('ephemeral-blueprint-editor-dialog')
export class EphemeralBlueprintEditorDialog extends LitElement {
  @property({ type: Boolean }) opened = false;

  @state() private mode: 'create' | 'edit' = 'create';

  @state() private projectId: number | null = null;

  @state() private projectName = '';

  @state() private editingTemplateId: number | undefined = undefined;

  @state() private nonProdEnvironments: EnvironmentApiModel[] = [];

  @state() private schemaContext:
    EnvironmentBlueprintSchemaContextApiModel | undefined;

  @state() private schemaLoading = false;

  @state() private name = '';

  @state() private description = '';

  @state() private defaultTier = 'ephemeral';

  @state() private baseEnvironmentId: number | null = null;

  @state() private defaultTtlMinutes = 240;

  @state() private maxTtlMinutes = 1440;

  @state() private provisionComponentIds: number[] = [];

  @state() private validationComponentIds: number[] = [];

  @state() private componentFilter = '';

  @state() private activeTierTab = 'ephemeral';

  @state() private activeComponentId: number | null = null;

  @state() private tierOverridesMap: Record<string, Record<string, string>> =
    {};

  @state() private customParamKey = '';

  @state() private customParamValue = '';

  @state() private showPortablePreview = false;

  @state() private saving = false;

  private schemaSubscription?: Subscription;

  static get styles() {
    return css`
      :host {
        display: contents;
      }

      .editor-container {
        display: flex;
        flex-direction: column;
        gap: 16px;
        width: min(1080px, 92vw);
        max-width: 100%;
        padding: 4px;
        box-sizing: border-box;
      }

      .section-card {
        background: var(--dorc-bg-secondary, #f5f6f8);
        border: 1px solid var(--dorc-border-color, #dde2e8);
        border-radius: 8px;
        padding: 14px 16px;
        display: flex;
        flex-direction: column;
        gap: 12px;
        box-sizing: border-box;
      }

      .section-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px 12px;
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

      .step-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 22px;
        height: 22px;
        border-radius: 50%;
        background: var(--lumo-primary-color, #1676f3);
        color: var(--lumo-primary-contrast-color, #ffffff);
        font-size: 0.75rem;
        font-weight: 700;
      }

      .form-grid-3 {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr));
        gap: 12px 16px;
        align-items: start;
      }

      .form-grid-2 {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 320px), 1fr));
        gap: 12px 16px;
        align-items: start;
      }

      .components-box {
        display: flex;
        flex-direction: column;
        gap: 6px;
        background: var(--dorc-bg-primary, #ffffff);
        border: 1px solid var(--dorc-border-color, #d1d5db);
        border-radius: 6px;
        padding: 10px 12px;
        max-height: 210px;
        overflow-y: auto;
      }

      .comp-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        font-size: 0.8rem;
        padding: 3px 4px;
        border-radius: 4px;
      }

      .comp-row:hover {
        background: var(--lumo-contrast-5pct);
      }

      .comp-row-left {
        display: flex;
        align-items: center;
        gap: 6px;
        min-width: 0;
      }

      .badge {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 7px;
        border-radius: 999px;
        font-size: 0.68rem;
        font-weight: 600;
        white-space: nowrap;
      }

      .badge-catalog {
        background: #e0f2fe;
        color: #0369a1;
        border: 1px solid #7dd3fc;
      }

      .badge-ps {
        background: var(--lumo-contrast-10pct);
        color: var(--dorc-text-secondary);
      }

      .badge-warn {
        background: #fef3c7;
        color: #92400e;
        border: 1px solid #fcd34d;
      }

      .badge-lock {
        background: #dcfce7;
        color: #166534;
        border: 1px solid #86efac;
      }

      .badge-sensitive {
        background: #fce7f3;
        color: #9d174d;
        border: 1px solid #f9a8d4;
      }

      .tier-tabs,
      .comp-tabs {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
      }

      .tab-pill {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 5px 11px;
        border-radius: 999px;
        border: 1px solid var(--dorc-border-color, #d1d5db);
        background: var(--dorc-bg-primary, #ffffff);
        color: var(--dorc-text-primary, #111827);
        font-size: 0.78rem;
        font-weight: 600;
        cursor: pointer;
      }

      .tab-pill.active {
        background: var(--lumo-primary-color);
        color: var(--lumo-primary-contrast-color, #ffffff);
        border-color: var(--lumo-primary-color);
      }

      .tab-count {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 16px;
        height: 16px;
        padding: 0 4px;
        border-radius: 999px;
        font-size: 0.68rem;
        background: rgba(0, 0, 0, 0.12);
      }

      .tab-pill.active .tab-count {
        background: rgba(255, 255, 255, 0.25);
      }

      .matrix-table-wrap {
        background: var(--dorc-bg-primary, #ffffff);
        border: 1px solid var(--dorc-border-color, #dde2e8);
        border-radius: 6px;
        overflow-x: auto;
      }

      table.matrix-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.8rem;
      }

      table.matrix-table th,
      table.matrix-table td {
        padding: 8px 10px;
        border-bottom: 1px solid var(--dorc-border-color, #e5e7eb);
        text-align: left;
        vertical-align: middle;
      }

      table.matrix-table th {
        background: var(--lumo-contrast-5pct);
        font-weight: 600;
        font-size: 0.75rem;
        color: var(--dorc-text-secondary);
      }

      tr.overridden-row {
        background: rgba(22, 118, 243, 0.05);
      }

      .param-name {
        font-family: monospace;
        font-weight: 600;
        color: var(--dorc-text-primary);
      }

      .param-desc {
        font-size: 0.72rem;
        color: var(--dorc-text-secondary);
        margin-top: 2px;
      }

      .default-val-code {
        font-family: monospace;
        font-size: 0.75rem;
        padding: 2px 6px;
        border-radius: 4px;
        background: var(--lumo-contrast-5pct);
        color: var(--dorc-text-primary);
      }

      .json-viewer-box {
        background: #2a2f3a;
        border-radius: 6px;
        padding: 10px 12px;
        max-height: 240px;
        overflow: auto;
      }

      .editor-footer {
        display: flex;
        align-items: center;
        justify-content: space-between;
        width: 100%;
        gap: 12px;
        flex-wrap: wrap;
      }

      .editor-footer-right {
        display: flex;
        align-items: center;
        gap: 8px;
      }
    `;
  }

  public openCreate(options: {
    projectId: number;
    projectName: string;
    nonProdEnvironments: EnvironmentApiModel[];
  }) {
    this.mode = 'create';
    this.projectId = options.projectId;
    this.projectName = options.projectName;
    this.editingTemplateId = undefined;
    this.nonProdEnvironments = options.nonProdEnvironments || [];
    this.name = `${options.projectName || 'Service'} Ephemeral Blueprint`;
    this.description =
      'Non-prod temporary environment blueprint with tier-weighted sizing';
    this.defaultTier = 'ephemeral';
    this.activeTierTab = 'ephemeral';
    this.baseEnvironmentId = this.nonProdEnvironments[0]?.EnvironmentId ?? null;
    this.defaultTtlMinutes = 240;
    this.maxTtlMinutes = 1440;
    this.provisionComponentIds = [];
    this.validationComponentIds = [];
    this.tierOverridesMap = {
      ephemeral: { create_service: 'false' },
      qa: { create_service: 'true' }
    };
    this.componentFilter = '';
    this.customParamKey = '';
    this.customParamValue = '';
    this.showPortablePreview = false;
    this.opened = true;
    this.loadSchemaContext(true);
  }

  public openEdit(options: {
    projectId: number;
    projectName: string;
    template: EnvironmentTemplateApiModel;
    nonProdEnvironments: EnvironmentApiModel[];
  }) {
    const tpl = options.template;
    this.mode = 'edit';
    this.projectId = options.projectId;
    this.projectName = options.projectName;
    this.editingTemplateId = tpl.Id;
    this.nonProdEnvironments = options.nonProdEnvironments || [];
    this.name = tpl.Name || '';
    this.description = tpl.Description || '';
    this.defaultTier = (tpl.DefaultTier || 'ephemeral').toLowerCase();
    this.activeTierTab = this.defaultTier;
    this.baseEnvironmentId = tpl.BaseEnvironmentId ?? null;
    this.defaultTtlMinutes = tpl.DefaultTtlMinutes ?? 240;
    this.maxTtlMinutes = tpl.MaxTtlMinutes ?? 1440;
    this.provisionComponentIds = [...(tpl.ProvisionComponentIds || [])];
    this.validationComponentIds = [...(tpl.ValidationComponentIds || [])];
    this.tierOverridesMap = this.parseTierOverridesJson(
      tpl.TierParameterOverridesJson
    );
    this.componentFilter = '';
    this.customParamKey = '';
    this.customParamValue = '';
    this.showPortablePreview = false;
    this.opened = true;
    this.loadSchemaContext(false);
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.schemaSubscription?.unsubscribe();
    this.schemaSubscription = undefined;
  }

  private showError(msg: string) {
    const notification = new ErrorNotification();
    notification.setAttribute('errorMessage', msg);
    this.shadowRoot?.appendChild(notification);
    notification.open();
  }

  private parseTierOverridesJson(
    raw: string | null | undefined
  ): Record<string, Record<string, string>> {
    if (!raw || !raw.trim()) {
      return {};
    }
    try {
      const parsed = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null) {
        return {};
      }
      const result: Record<string, Record<string, string>> = {};
      for (const [tier, entries] of Object.entries(parsed)) {
        if (typeof entries === 'object' && entries !== null) {
          const inner: Record<string, string> = {};
          for (const [k, v] of Object.entries(
            entries as Record<string, unknown>
          )) {
            if (v !== null && v !== undefined) {
              inner[k] = String(v);
            }
          }
          result[tier.toLowerCase()] = inner;
        }
      }
      return result;
    } catch {
      return {};
    }
  }

  private serializeTierOverridesJson(): string {
    const cleaned: Record<string, Record<string, string>> = {};
    for (const [tier, entries] of Object.entries(this.tierOverridesMap)) {
      const nonEmpty = Object.entries(entries).filter(
        ([k, v]) => k.trim().length > 0 && v !== undefined
      );
      if (nonEmpty.length > 0) {
        cleaned[tier] = Object.fromEntries(nonEmpty);
      }
    }
    return JSON.stringify(cleaned, null, 2);
  }

  private loadSchemaContext(autoSelectDefaults: boolean) {
    if (!this.projectId) {
      return;
    }
    this.schemaSubscription?.unsubscribe();
    this.schemaLoading = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    this.schemaSubscription = api
      .ephemeralEnvironmentsTemplatesSchemaContextGet({
        projectId: this.projectId,
        baseEnvironmentId: this.baseEnvironmentId ?? undefined
      })
      .subscribe({
        next: (ctx: EnvironmentBlueprintSchemaContextApiModel) => {
          this.schemaLoading = false;
          this.schemaContext = ctx;
          const components = ctx.Components || [];
          if (
            autoSelectDefaults &&
            this.provisionComponentIds.length === 0 &&
            components.length > 0
          ) {
            const catalogIds = components
              .filter(c => c.IsCatalogTerraform && c.IsEnabled && c.ComponentId)
              .map(c => c.ComponentId!);
            this.provisionComponentIds =
              catalogIds.length > 0
                ? catalogIds
                : components
                    .slice(0, 3)
                    .map(c => c.ComponentId!)
                    .filter(Boolean);
          }
          this.ensureActiveComponentSelection();
        },
        error: (err: unknown) => {
          this.schemaLoading = false;
          const msg = retrieveErrorMessage(
            err,
            'Failed to load component manifest schema context.'
          );
          this.showError(msg);
        }
      });
  }

  private ensureActiveComponentSelection() {
    const selectedComponents = this.getSelectedProvisionComponents();
    if (
      this.activeComponentId !== null &&
      selectedComponents.some(c => c.ComponentId === this.activeComponentId)
    ) {
      return;
    }
    const firstWithManifest = selectedComponents.find(
      c => (c.Parameters || []).length > 0
    );
    this.activeComponentId =
      firstWithManifest?.ComponentId ??
      selectedComponents[0]?.ComponentId ??
      null;
  }

  private getSelectedProvisionComponents(): EnvironmentBlueprintComponentSchemaApiModel[] {
    const all = this.schemaContext?.Components || [];
    return all.filter(
      c => c.ComponentId && this.provisionComponentIds.includes(c.ComponentId)
    );
  }

  private toggleProvisionComponent(compId: number, checked: boolean) {
    if (checked) {
      if (!this.provisionComponentIds.includes(compId)) {
        this.provisionComponentIds = [...this.provisionComponentIds, compId];
      }
    } else {
      this.provisionComponentIds = this.provisionComponentIds.filter(
        id => id !== compId
      );
    }
    this.ensureActiveComponentSelection();
  }

  private toggleValidationComponent(compId: number, checked: boolean) {
    if (checked) {
      if (!this.validationComponentIds.includes(compId)) {
        this.validationComponentIds = [...this.validationComponentIds, compId];
      }
    } else {
      this.validationComponentIds = this.validationComponentIds.filter(
        id => id !== compId
      );
    }
  }

  private getOverrideValueForParameter(
    compName: string | null | undefined,
    paramName: string
  ): { value: string | undefined; isScoped: boolean } {
    const tierMap = this.tierOverridesMap[this.activeTierTab] || {};
    if (compName) {
      const scopedKey = `${compName}:${paramName}`;
      if (scopedKey in tierMap) {
        return { value: tierMap[scopedKey], isScoped: true };
      }
    }
    if (paramName in tierMap) {
      return { value: tierMap[paramName], isScoped: false };
    }
    return { value: undefined, isScoped: true };
  }

  private setParameterOverride(
    compName: string | null | undefined,
    paramName: string,
    newValue: string
  ) {
    const currentTierMap = {
      ...(this.tierOverridesMap[this.activeTierTab] || {})
    };
    const key = compName ? `${compName}:${paramName}` : paramName;
    if (!newValue.trim()) {
      delete currentTierMap[key];
    } else {
      currentTierMap[key] = newValue;
    }
    this.tierOverridesMap = {
      ...this.tierOverridesMap,
      [this.activeTierTab]: currentTierMap
    };
  }

  private clearParameterOverride(
    compName: string | null | undefined,
    paramName: string
  ) {
    const currentTierMap = {
      ...(this.tierOverridesMap[this.activeTierTab] || {})
    };
    if (compName) {
      delete currentTierMap[`${compName}:${paramName}`];
    }
    delete currentTierMap[paramName];
    this.tierOverridesMap = {
      ...this.tierOverridesMap,
      [this.activeTierTab]: currentTierMap
    };
  }

  private addCustomTierOverride = () => {
    const rawKey = this.customParamKey.trim();
    if (!rawKey) {
      return;
    }
    const activeComp = (this.schemaContext?.Components || []).find(
      c => c.ComponentId === this.activeComponentId
    );
    const qualifiedKey =
      rawKey.includes(':') || !activeComp?.ComponentName
        ? rawKey
        : `${activeComp.ComponentName}:${rawKey}`;

    const currentTierMap = {
      ...(this.tierOverridesMap[this.activeTierTab] || {}),
      [qualifiedKey]: this.customParamValue
    };
    this.tierOverridesMap = {
      ...this.tierOverridesMap,
      [this.activeTierTab]: currentTierMap
    };
    this.customParamKey = '';
    this.customParamValue = '';
  };

  private buildPortablePreviewObject() {
    const allComps = this.schemaContext?.Components || [];
    const compNameById = new Map<number, string>();
    for (const c of allComps) {
      if (c.ComponentId && c.ComponentName) {
        compNameById.set(c.ComponentId, c.ComponentName);
      }
    }
    const baseEnv = this.nonProdEnvironments.find(
      e => e.EnvironmentId === this.baseEnvironmentId
    );
    return {
      SchemaVersion: '1.0',
      ProjectName: this.projectName,
      Name: this.name.trim(),
      Description: this.description.trim() || null,
      DefaultTier: this.defaultTier,
      BaseEnvironmentName: baseEnv?.EnvironmentName || null,
      DefaultTtlMinutes: this.defaultTtlMinutes,
      MaxTtlMinutes: this.maxTtlMinutes,
      ProvisionComponentNames: this.provisionComponentIds.map(
        id => compNameById.get(id) || `Component#${id}`
      ),
      ValidationComponentNames: this.validationComponentIds.map(
        id => compNameById.get(id) || `Component#${id}`
      ),
      TierParameterOverrides: this.tierOverridesMap
    };
  }

  private saveBlueprint = () => {
    if (!this.projectId || !this.name.trim()) {
      this.showError('Project and Blueprint Name are required.');
      return;
    }

    this.saving = true;
    const api = new EphemeralEnvironmentsApi(dorcApiConfiguration);
    const payload: EnvironmentTemplateApiModel = {
      Id: this.mode === 'edit' ? this.editingTemplateId : undefined,
      ProjectId: this.projectId,
      ProjectName: this.projectName,
      Name: this.name.trim(),
      Description: this.description.trim() || null,
      BaseEnvironmentId: this.baseEnvironmentId,
      DefaultTier: this.defaultTier,
      DefaultTtlMinutes: this.defaultTtlMinutes,
      MaxTtlMinutes: this.maxTtlMinutes,
      ProvisionComponentIds: this.provisionComponentIds,
      ValidationComponentIds: this.validationComponentIds,
      TierParameterOverridesJson: this.serializeTierOverridesJson()
    };

    const req$ =
      this.mode === 'edit' && this.editingTemplateId
        ? api.ephemeralEnvironmentsTemplatesTemplateIdPut({
            templateId: this.editingTemplateId,
            environmentTemplateApiModel: payload
          })
        : api.ephemeralEnvironmentsTemplatesPost({
            environmentTemplateApiModel: payload
          });

    req$.subscribe({
      next: (saved: EnvironmentTemplateApiModel) => {
        this.saving = false;
        this.opened = false;
        Notification.show(`Saved Blueprint '${saved.Name}'.`, {
          theme: 'success',
          position: 'bottom-start',
          duration: 4000
        });
        this.dispatchEvent(
          new CustomEvent('blueprint-saved', {
            detail: { template: saved },
            bubbles: true,
            composed: true
          })
        );
      },
      error: (err: unknown) => {
        this.saving = false;
        const msg = retrieveErrorMessage(err, 'Failed to save Blueprint.');
        this.showError(msg);
      }
    });
  };

  private renderParameterRow(
    comp: EnvironmentBlueprintComponentSchemaApiModel,
    param: EnvironmentBlueprintParameterSchemaApiModel
  ) {
    const paramName = param.Name || '';
    const { value: currentOverride, isScoped } =
      this.getOverrideValueForParameter(comp.ComponentName, paramName);
    const hasOverride = currentOverride !== undefined;
    const tierSuggestions = param.TierSuggestedValues || {};
    const resolvedDefault =
      tierSuggestions[this.activeTierTab] ??
      param.BaseEnvironmentValue ??
      param.DefaultValue ??
      '—';
    const allowedValues = param.AllowedValues || [];
    const isBool =
      (param.Type || '').toLowerCase() === 'bool' ||
      (param.Type || '').toLowerCase() === 'boolean';

    return html`
      <tr class=${hasOverride ? 'overridden-row' : ''}>
        <td>
          <div class="param-name">
            ${paramName}
            ${
              param.Required
                ? html`<span
                    style="color: var(--lumo-error-text-color);"
                    title="Required parameter"
                    >*</span
                  >`
                : nothing
            }
          </div>
          ${
            param.Description
              ? html`<div class="param-desc">${param.Description}</div>`
              : nothing
          }
        </td>
        <td>
          <span class="badge badge-ps">${param.Type || 'string'}</span>
        </td>
        <td>
          <code class="default-val-code">${resolvedDefault}</code>
        </td>
        <td style="min-width: 240px;">
          ${
            param.IsLockedSafetyInvariant
              ? html`
                  <span class="badge badge-lock">
                    Locked = false (Non-Prod Safety Invariant)
                  </span>
                `
              : param.Sensitive
                ? html`
                    <span class="badge badge-sensitive">
                      Inherited via Base Env Secure Properties
                    </span>
                  `
                : allowedValues.length > 0
                  ? html`
                      <vaadin-combo-box
                        theme="small"
                        style="width: 100%;"
                        clear-button-visible
                        placeholder="Use tier default (${resolvedDefault})"
                        .items=${allowedValues}
                        .value=${currentOverride ?? ''}
                        @value-changed=${(
                          e: CustomEvent<{ value: string }>
                        ) => {
                          const nextVal = e.detail.value ?? '';
                          if (nextVal !== (currentOverride ?? '')) {
                            this.setParameterOverride(
                              comp.ComponentName,
                              paramName,
                              nextVal
                            );
                          }
                        }}
                      ></vaadin-combo-box>
                    `
                  : isBool
                    ? html`
                        <vaadin-combo-box
                          theme="small"
                          style="width: 100%;"
                          clear-button-visible
                          placeholder="Use tier default (${resolvedDefault})"
                          .items=${['true', 'false']}
                          .value=${currentOverride ?? ''}
                          @value-changed=${(
                            e: CustomEvent<{ value: string }>
                          ) => {
                            const nextVal = e.detail.value ?? '';
                            if (nextVal !== (currentOverride ?? '')) {
                              this.setParameterOverride(
                                comp.ComponentName,
                                paramName,
                                nextVal
                              );
                            }
                          }}
                        ></vaadin-combo-box>
                      `
                    : html`
                        <vaadin-text-field
                          theme="small"
                          style="width: 100%;"
                          clear-button-visible
                          placeholder="Use tier default (${resolvedDefault})"
                          .value=${currentOverride ?? ''}
                          @value-changed=${(
                            e: CustomEvent<{ value: string }>
                          ) => {
                            const nextVal = e.detail.value ?? '';
                            if (nextVal !== (currentOverride ?? '')) {
                              this.setParameterOverride(
                                comp.ComponentName,
                                paramName,
                                nextVal
                              );
                            }
                          }}
                        ></vaadin-text-field>
                      `
          }
        </td>
        <td style="width: 110px; text-align: right;">
          ${
            hasOverride
              ? html`
                  <vaadin-button
                    theme="small tertiary error"
                    title=${
                      isScoped
                        ? `Clear ${comp.ComponentName}:${paramName}`
                        : `Clear global ${paramName}`
                    }
                    @click=${() =>
                      this.clearParameterOverride(
                        comp.ComponentName,
                        paramName
                      )}
                  >
                    Reset
                  </vaadin-button>
                `
              : html`<span
                  style="font-size: 0.72rem; color: var(--dorc-text-secondary);"
                  >Inherited</span
                >`
          }
        </td>
      </tr>
    `;
  }

  private renderDialogHeader = () => html`
    <div style="display: flex; align-items: center; gap: 8px;">
      <vaadin-icon
        icon="vaadin:cubes"
        style="color: var(--lumo-primary-color); width: 20px; height: 20px;"
      ></vaadin-icon>
      <span style="font-weight: 700; font-size: 1.05rem;">
        ${
          this.mode === 'edit'
            ? `Edit Ephemeral Blueprint: ${this.name}`
            : `New Ephemeral Blueprint (${this.projectName})`
        }
      </span>
    </div>
  `;

  private renderDialogBody = () => {
    const allComponents = this.schemaContext?.Components || [];
    const filterTerm = this.componentFilter.trim().toLowerCase();
    const filteredComponents = filterTerm
      ? allComponents.filter(
          c =>
            (c.ComponentName || '').toLowerCase().includes(filterTerm) ||
            (c.TerraformTemplateName || '').toLowerCase().includes(filterTerm)
        )
      : allComponents;

    const selectedProvisionComponents = this.getSelectedProvisionComponents();
    const activeComponent =
      selectedProvisionComponents.find(
        c => c.ComponentId === this.activeComponentId
      ) ?? selectedProvisionComponents[0];
    const activeParameters = activeComponent?.Parameters || [];
    const activeTierMap = this.tierOverridesMap[this.activeTierTab] || {};

    return html`
      <div class="editor-container">
        <!-- Section 1: Identity & Governance -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-badge">1</span>
              Blueprint Identity, Base Environment &amp; TTL Governance
            </h3>
          </div>

          <div class="form-grid-3">
            <vaadin-text-field
              label="Blueprint Name"
              required
              .value=${this.name}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.name = e.detail.value;
              }}
            ></vaadin-text-field>

            <vaadin-combo-box
              label="Default Sizing Tier"
              .items=${MATRIX_TIERS.map(t => t.key)}
              .value=${this.defaultTier}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                if (e.detail.value) {
                  this.defaultTier = e.detail.value;
                }
              }}
            ></vaadin-combo-box>

            <vaadin-combo-box
              label="Default Non-Prod Base Environment"
              item-label-path="EnvironmentName"
              item-value-path="EnvironmentId"
              clear-button-visible
              .items=${this.nonProdEnvironments}
              .selectedItem=${
                this.nonProdEnvironments.find(
                  env => env.EnvironmentId === this.baseEnvironmentId
                ) ?? null
              }
              @selected-item-changed=${(
                e: CustomEvent<{
                  value: EnvironmentApiModel | null | undefined;
                }>
              ) => {
                const nextId = e.detail.value?.EnvironmentId ?? null;
                if (nextId !== this.baseEnvironmentId) {
                  this.baseEnvironmentId = nextId;
                  this.loadSchemaContext(false);
                }
              }}
            ></vaadin-combo-box>
          </div>

          <div class="form-grid-3">
            <vaadin-text-field
              label="Description"
              .value=${this.description}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.description = e.detail.value;
              }}
            ></vaadin-text-field>

            <vaadin-number-field
              label="Default TTL (minutes)"
              min="15"
              max="10080"
              .value=${String(this.defaultTtlMinutes)}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                const v = Number.parseInt(e.detail.value, 10);
                if (!Number.isNaN(v) && v > 0) {
                  this.defaultTtlMinutes = v;
                }
              }}
            ></vaadin-number-field>

            <vaadin-number-field
              label="Max TTL Policy Cap (minutes)"
              min="30"
              max="10080"
              .value=${String(this.maxTtlMinutes)}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                const v = Number.parseInt(e.detail.value, 10);
                if (!Number.isNaN(v) && v > 0) {
                  this.maxTtlMinutes = v;
                }
              }}
            ></vaadin-number-field>
          </div>
        </div>

        <!-- Section 2: Component Topology -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-badge">2</span>
              Component Topology (Provisioning &amp; Smoke Validation)
            </h3>
            <vaadin-text-field
              theme="small"
              placeholder="Filter components..."
              clear-button-visible
              .value=${this.componentFilter}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.componentFilter = e.detail.value ?? '';
              }}
            >
              <vaadin-icon icon="vaadin:search" slot="prefix"></vaadin-icon>
            </vaadin-text-field>
          </div>

          <div class="form-grid-2">
            <div>
              <div
                style="font-size: 0.8rem; font-weight: 600; margin-bottom: 6px;"
              >
                Provisioning Components (${this.provisionComponentIds.length}
                selected)
              </div>
              <div class="components-box">
                ${filteredComponents.map(comp => {
                  const id = comp.ComponentId ?? 0;
                  const checked = this.provisionComponentIds.includes(id);
                  return html`
                    <div class="comp-row">
                      <label class="comp-row-left">
                        <vaadin-checkbox
                          ?checked=${checked}
                          @checked-changed=${(
                            e: CustomEvent<{ value: boolean }>
                          ) => {
                            if (id) {
                              this.toggleProvisionComponent(
                                id,
                                Boolean(e.detail.value)
                              );
                            }
                          }}
                        ></vaadin-checkbox>
                        <span>${comp.ComponentName}</span>
                      </label>
                      <div
                        style="display: flex; gap: 4px; align-items: center;"
                      >
                        ${
                          comp.IsCatalogTerraform
                            ? html`<span class="badge badge-catalog">
                                ${comp.TerraformTemplateName || 'Catalog TF'}
                                ${
                                  comp.TerraformTemplateVersion
                                    ? `@${comp.TerraformTemplateVersion}`
                                    : ''
                                }
                              </span>`
                            : html`<span class="badge badge-ps">
                                ${comp.ComponentType || 'PowerShell'}
                              </span>`
                        }
                        ${
                          comp.IsEnabled === false
                            ? html`<span class="badge badge-warn"
                                >Disabled</span
                              >`
                            : nothing
                        }
                      </div>
                    </div>
                  `;
                })}
              </div>
            </div>

            <div>
              <div
                style="font-size: 0.8rem; font-weight: 600; margin-bottom: 6px;"
              >
                Post-Provision Validation / Smoke Tests
                (${this.validationComponentIds.length} selected)
              </div>
              <div class="components-box">
                ${filteredComponents.map(comp => {
                  const id = comp.ComponentId ?? 0;
                  const checked = this.validationComponentIds.includes(id);
                  return html`
                    <div class="comp-row">
                      <label class="comp-row-left">
                        <vaadin-checkbox
                          ?checked=${checked}
                          @checked-changed=${(
                            e: CustomEvent<{ value: boolean }>
                          ) => {
                            if (id) {
                              this.toggleValidationComponent(
                                id,
                                Boolean(e.detail.value)
                              );
                            }
                          }}
                        ></vaadin-checkbox>
                        <span>${comp.ComponentName}</span>
                      </label>
                      <span class="badge badge-ps">
                        ${comp.ComponentType || 'PowerShell'}
                      </span>
                    </div>
                  `;
                })}
              </div>
            </div>
          </div>
        </div>

        <!-- Section 3: Schema-Guided Component & Tier Parameter Matrix -->
        <div class="section-card">
          <div class="section-header">
            <h3 class="section-title">
              <span class="step-badge">3</span>
              Schema-Guided Component &amp; Tier Parameter Matrix
            </h3>
            <vaadin-button
              theme="small tertiary"
              @click=${() => {
                this.showPortablePreview = !this.showPortablePreview;
              }}
            >
              <vaadin-icon icon="vaadin:code" slot="prefix"></vaadin-icon>
              ${
                this.showPortablePreview
                  ? 'Hide Portable Spec Preview'
                  : 'View Portable Spec (GitOps JSON)'
              }
            </vaadin-button>
          </div>

          <!-- Tier Tabs -->
          <div>
            <div
              style="font-size: 0.75rem; font-weight: 600; color: var(--dorc-text-secondary); margin-bottom: 6px;"
            >
              1. Select Target Tier Profile
            </div>
            <div class="tier-tabs">
              ${MATRIX_TIERS.map(tier => {
                const count = Object.keys(
                  this.tierOverridesMap[tier.key] || {}
                ).length;
                return html`
                  <button
                    type="button"
                    class="tab-pill ${
                      this.activeTierTab === tier.key ? 'active' : ''
                    }"
                    @click=${() => {
                      this.activeTierTab = tier.key;
                    }}
                  >
                    <span>${tier.label}</span>
                    ${
                      count > 0
                        ? html`<span class="tab-count">${count}</span>`
                        : nothing
                    }
                  </button>
                `;
              })}
            </div>
          </div>

          <!-- Component Tabs -->
          <div>
            <div
              style="font-size: 0.75rem; font-weight: 600; color: var(--dorc-text-secondary); margin-bottom: 6px;"
            >
              2. Select Provisioning Component
            </div>
            <div class="comp-tabs">
              ${selectedProvisionComponents.map(comp => {
                const isActive =
                  activeComponent?.ComponentId === comp.ComponentId;
                const paramCount = (comp.Parameters || []).length;
                return html`
                  <button
                    type="button"
                    class="tab-pill ${isActive ? 'active' : ''}"
                    @click=${() => {
                      this.activeComponentId = comp.ComponentId ?? null;
                    }}
                  >
                    <span>${comp.ComponentName}</span>
                    <span class="tab-count">${paramCount}</span>
                  </button>
                `;
              })}
            </div>
          </div>

          <!-- Matrix Table -->
          ${
            activeComponent && activeParameters.length > 0
              ? html`
                  <div class="matrix-table-wrap">
                    <table class="matrix-table">
                      <thead>
                        <tr>
                          <th>Parameter</th>
                          <th>Type</th>
                          <th>
                            Base / Tier Default
                            (<code>${this.activeTierTab}</code>)
                          </th>
                          <th>
                            Tier Override Value
                            (<code>${this.activeTierTab}</code>)
                          </th>
                          <th style="text-align: right;">Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        ${activeParameters.map(param =>
                          this.renderParameterRow(activeComponent, param)
                        )}
                      </tbody>
                    </table>
                  </div>
                `
              : html`
                  <div
                    style="padding: 12px; border-radius: 6px; background: var(--dorc-bg-primary); border: 1px solid var(--dorc-border-color); font-size: 0.8rem; color: var(--dorc-text-secondary);"
                  >
                    ${
                      selectedProvisionComponents.length === 0
                        ? 'Select one or more Provisioning Components in Step 2 above to configure schema-guided tier parameters.'
                        : 'Selected component does not expose a Catalog Terraform manifest schema. You can still add custom tier parameter overrides below.'
                    }
                  </div>
                `
          }

          <!-- Custom Override Adder + Active Tier Summary -->
          <div
            style="display: flex; align-items: flex-end; gap: 8px; flex-wrap: wrap;"
          >
            <vaadin-text-field
              theme="small"
              label="Custom Parameter Key (${this.activeTierTab})"
              placeholder="e.g. kafka_partitions or Comp:param"
              style="flex: 1 1 240px;"
              .value=${this.customParamKey}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.customParamKey = e.detail.value;
              }}
            ></vaadin-text-field>
            <vaadin-text-field
              theme="small"
              label="Override Value"
              placeholder="e.g. 3"
              style="flex: 1 1 200px;"
              .value=${this.customParamValue}
              @value-changed=${(e: CustomEvent<{ value: string }>) => {
                this.customParamValue = e.detail.value;
              }}
            ></vaadin-text-field>
            <vaadin-button theme="small" @click=${this.addCustomTierOverride}>
              Set Custom Override
            </vaadin-button>
          </div>

          ${
            Object.keys(activeTierMap).length > 0
              ? html`
                  <div
                    style="display: flex; flex-wrap: wrap; gap: 6px; align-items: center;"
                  >
                    <span
                      style="font-size: 0.75rem; font-weight: 600; color: var(--dorc-text-secondary);"
                    >
                      Active <code>${this.activeTierTab}</code> Overrides:
                    </span>
                    ${Object.entries(activeTierMap).map(
                      ([k, v]) => html`
                        <span class="badge badge-catalog">
                          <code>${k} = ${v}</code>
                          <button
                            type="button"
                            style="border: none; background: transparent; cursor: pointer; color: #0369a1; font-weight: 700;"
                            title="Remove override"
                            @click=${() => {
                              const next = { ...activeTierMap };
                              delete next[k];
                              this.tierOverridesMap = {
                                ...this.tierOverridesMap,
                                [this.activeTierTab]: next
                              };
                            }}
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
            this.showPortablePreview
              ? html`
                  <div class="json-viewer-box">
                    <hegs-json-viewer
                      .data=${this.buildPortablePreviewObject()}
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
              : nothing
          }
        </div>
      </div>
    `;
  };

  private renderDialogFooter = () => html`
    <div class="editor-footer">
      <span style="font-size: 0.78rem; color: var(--dorc-text-secondary);">
        Overrides are validated against Catalog Terraform manifests and non-prod
        safety invariants on save.
      </span>
      <div class="editor-footer-right">
        <vaadin-button
          theme="tertiary"
          ?disabled=${this.saving}
          @click=${() => {
            this.opened = false;
          }}
        >
          Cancel
        </vaadin-button>
        <vaadin-button
          theme="primary"
          ?disabled=${this.saving || !this.name.trim()}
          @click=${this.saveBlueprint}
        >
          <vaadin-icon icon="vaadin:check" slot="prefix"></vaadin-icon>
          ${this.saving ? 'Saving Blueprint...' : 'Save Blueprint'}
        </vaadin-button>
      </div>
    </div>
  `;

  render() {
    return html`
      <vaadin-dialog
        aria-label="Ephemeral Blueprint Editor"
        theme="wide"
        draggable
        .opened=${this.opened}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          this.opened = e.detail.value;
        }}
        ${dialogHeaderRenderer(this.renderDialogHeader, [
          this.mode,
          this.name,
          this.projectName
        ])}
        ${dialogRenderer(this.renderDialogBody, [
          this.schemaContext,
          this.schemaLoading,
          this.componentFilter,
          this.provisionComponentIds,
          this.validationComponentIds,
          this.activeComponentId,
          this.activeTierTab,
          this.tierOverridesMap,
          this.name,
          this.description,
          this.defaultTier,
          this.baseEnvironmentId,
          this.nonProdEnvironments,
          this.defaultTtlMinutes,
          this.maxTtlMinutes,
          this.customParamKey,
          this.customParamValue,
          this.showPortablePreview,
          this.projectName
        ])}
        ${dialogFooterRenderer(this.renderDialogFooter, [
          this.saving,
          this.name
        ])}
      ></vaadin-dialog>
    `;
  }
}
