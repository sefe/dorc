import '@vaadin/button';
import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/password-field';
import '@vaadin/text-area';
import '@vaadin/text-field';
import './hegs-json-viewer';
import { css, html, LitElement, nothing, type PropertyValues } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { live } from 'lit/directives/live.js';
import { ref } from 'lit/directives/ref.js';
import type { HegsJsonViewer } from './hegs-json-viewer';
import {
  ComponentType,
  PropertiesApi,
  PropertyValuesApi,
  RefDataProjectEnvironmentMappingsApi,
  TerraformSourceType,
  type ComponentApiModel,
  type PropertyApiModel,
  type PropertyResolutionTraceApiModel,
  type PropertyValueScopeOptionApiModel,
  type ProjectApiModel
} from '../apis/dorc-api';
import {
  findGenericScriptTemplate,
  GENERIC_SCRIPT_TEMPLATES,
  parseScriptPathValue,
  serializeScriptPathValue,
  type GenericScriptTemplate
} from '../helpers/generic-scripts';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';
import { defaultPropertyResolutionClientCache } from '../variable-resolution/property-resolution-client-cache';
import { parsePropertyTokens } from '../variable-resolution/property-token-parser';
import {
  applyDollarTokenCompletion,
  buildInsertableDollarTokens,
  detectActiveDollarCompletion,
  evaluateDollarValuePreview,
  inspectParameterBindingSyntax,
  inspectUnescapedDollars,
  lookupTrace,
  presentTrace
} from '../variable-resolution/resolution-presentation';

const CUSTOM_SCRIPT_OPTION = '__custom__';

export interface ParameterResolutionPreview {
  status: 'resolved' | 'warning' | 'unresolved' | 'masked';
  resolvedText: string;
  tokenNames: string[];
  traces: PropertyResolutionTraceApiModel[];
  detailMessage?: string;
}

const STATUS_PRIORITY: Record<
  ParameterResolutionPreview['status'],
  number
> = {
  resolved: 0,
  masked: 1,
  warning: 2,
  unresolved: 3
};

function promoteStatus(
  current: ParameterResolutionPreview['status'],
  next: ParameterResolutionPreview['status']
): ParameterResolutionPreview['status'] {
  return STATUS_PRIORITY[next] > STATUS_PRIORITY[current] ? next : current;
}

export function evaluateParameterPreview(
  rawText: string,
  tracesByProperty: Map<string, PropertyResolutionTraceApiModel>
): ParameterResolutionPreview | undefined {
  const segments = parsePropertyTokens(rawText, 'brace-only');
  const tokenNames = segments
    .filter(
      (
        seg
      ): seg is Extract<typeof seg, { kind: 'property' }> =>
        seg.kind === 'property'
    )
    .map(seg => seg.propertyName);
  if (tokenNames.length === 0) return undefined;

  let out = '';
  let status: ParameterResolutionPreview['status'] = 'resolved';
  const traces: PropertyResolutionTraceApiModel[] = [];
  const details: string[] = [];

  for (const seg of segments) {
    if (seg.kind !== 'property') {
      out += seg.rawText;
      continue;
    }
    const trace = lookupTrace(tracesByProperty, seg.propertyName);
    if (!trace) {
      status = promoteStatus(status, 'unresolved');
      out += seg.rawText;
      continue;
    }
    traces.push(trace);
    const presented = presentTrace(trace);
    if (presented.diagnosticDetail) {
      details.push(`${seg.propertyName}: ${presented.diagnosticDetail}`);
    }
    if (presented.severity === 'masked') {
      status = promoteStatus(status, 'masked');
      out += presented.effectiveDisplayValue;
      continue;
    }
    if (
      presented.severity === 'unresolved' ||
      !presented.hasEffectiveValue
    ) {
      status = promoteStatus(status, 'unresolved');
      out += presented.hasEffectiveValue
        ? presented.effectiveDisplayValue
        : seg.rawText;
      continue;
    }
    if (presented.severity === 'warning') {
      status = promoteStatus(status, 'warning');
    }
    out += presented.effectiveDisplayValue;
  }

  return {
    status,
    resolvedText: out,
    tokenNames,
    traces,
    detailMessage: details.join(' · ')
  };
}

@customElement('deployment-definition-editor')
export class DeploymentDefinitionEditor extends LitElement {
  @property({ attribute: false }) component: ComponentApiModel | undefined;
  @property({ attribute: false }) project: ProjectApiModel | undefined;
  @property({ type: Boolean }) readonly = false;
  @property({ type: Array }) powerShellVersions: string[] = [];
  @state() private previewEnvironmentName = '';
  @state() private availableProperties: PropertyApiModel[] = [];
  @state() private projectEnvironmentNames: string[] = [];
  @state() private activeParameterKey = '';
  @state() private openPickerParamKey = '';
  @state() private pickerFilterText = '';
  @state() private inlineCreateParamKey = '';
  @state() private inlineNewPropertyName = '';
  @state() private inlineNewPropertySecure = false;
  @state() private inlineNewPropertyDefaultValue = '';
  @state() private inlineValuePickerOpen = false;
  @state() private inlineValuePickerFilter = '';
  @state() private inlineScopeOptions: PropertyValueScopeOptionApiModel[] = [];
  @state() private inlineCreateFeedback = '';
  @state() private inlineCreateSubmitting = false;
  private inlineValueSelectionStart: number | null = null;
  private inlineValueSelectionEnd: number | null = null;
  private inlineCreatedProperty: PropertyApiModel | undefined;
  @state() private previewTracesByProperty = new Map<
    string,
    PropertyResolutionTraceApiModel
  >();
  private previewRequestGeneration = 0;

  static styles = css`
    :host {
      display: block;
      min-width: 0;
      color: var(--dorc-text-primary);
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs) var(--lumo-space-m);
    }
    vaadin-text-field,
    vaadin-text-area,
    vaadin-combo-box {
      width: 100%;
      min-width: 0;
    }
    vaadin-text-area::part(input-field) {
      font-family: monospace;
    }
    .json-preview {
      font-size: var(--lumo-font-size-s);
      line-height: var(--lumo-line-height-s);
      border-radius: var(--lumo-border-radius-m);
      white-space: normal;
      overflow-wrap: anywhere;
      --indent-size: 1em;
    }
    .json-preview-label {
      display: block;
      margin-top: var(--lumo-space-xs);
      font-size: var(--lumo-font-size-s);
      font-weight: 500;
      color: var(--lumo-secondary-text-color);
    }
    .full {
      grid-column: 1 / -1;
    }
    .generic-script-card {
      grid-column: 1 / -1;
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      background: var(--dorc-bg-secondary);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
    }
    .generic-script-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--lumo-space-xs);
    }
    .generic-script-header strong {
      font-size: var(--lumo-font-size-s);
    }
    .generic-category-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: var(--lumo-font-size-xs);
      font-weight: 600;
      background: var(--dorc-chip-bg);
      color: var(--dorc-chip-text);
    }
    .generic-args-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs) var(--lumo-space-m);
    }
    .custom-arg-row {
      grid-column: 1 / -1;
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr) auto;
      gap: var(--lumo-space-xs);
      align-items: end;
    }
    .env-props {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
      margin: 0;
    }
    .flags {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s) var(--lumo-space-m);
      margin: var(--lumo-space-s) 0;
    }
    p {
      margin: var(--lumo-space-xs) 0;
      max-width: 75ch;
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-secondary-strong);
      overflow-wrap: anywhere;
    }
    code {
      overflow-wrap: anywhere;
    }
    a {
      color: var(--dorc-link-color);
    }
    .generic-variable-toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-xs) var(--lumo-space-m);
      align-items: flex-end;
      padding: var(--lumo-space-xs) 0;
      border-top: 1px dashed var(--dorc-border-color);
      border-bottom: 1px dashed var(--dorc-border-color);
    }
    .generic-variable-toolbar > * {
      flex: 1 1 200px;
    }
    .param-cell {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: 4px;
      min-width: 0;
    }
    .param-cell-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      font-size: var(--lumo-font-size-xs);
    }
    .param-action-btn {
      border: none;
      background: transparent;
      color: var(--dorc-link-color);
      font-size: var(--lumo-font-size-xs);
      cursor: pointer;
      padding: 0;
      text-decoration: underline;
    }
    .param-bind-suffix-btn {
      display: inline-flex;
      align-items: center;
      gap: 3px;
      padding: 2px 7px;
      border-radius: 5px;
      border: 1px solid var(--dorc-border-color, rgba(148, 163, 184, 0.35));
      background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.1));
      color: var(--dorc-link-color, #2563eb);
      font-family: var(--lumo-font-family-monospace, monospace);
      font-size: 11px;
      font-weight: 600;
      cursor: pointer;
      line-height: 1.3;
    }
    .param-bind-suffix-btn:hover {
      background: rgba(59, 130, 246, 0.2);
    }
    .param-picker-popover {
      border: 1px solid var(--dorc-border-color, rgba(148, 163, 184, 0.35));
      border-radius: 8px;
      padding: 8px;
      background: var(--lumo-base-color, #1e293b);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.22);
      display: flex;
      flex-direction: column;
      gap: 6px;
      z-index: 20;
    }
    .param-picker-list {
      max-height: 160px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .param-picker-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      padding: 4px 8px;
      border: none;
      border-radius: 5px;
      background: transparent;
      color: inherit;
      font-family: var(--lumo-font-family-monospace, monospace);
      font-size: var(--lumo-font-size-xs);
      text-align: left;
      cursor: pointer;
    }
    .param-picker-item:hover {
      background: rgba(59, 130, 246, 0.14);
    }
    .param-preview-badge {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: var(--lumo-font-size-xs);
      font-family: var(--lumo-font-family-monospace, monospace);
      background: var(--dorc-chip-bg, rgba(59, 130, 246, 0.12));
      color: var(--dorc-chip-text, inherit);
      overflow-wrap: anywhere;
    }
    .param-preview-badge[data-status='unresolved'],
    .param-preview-badge[data-status='warning'] {
      background: rgba(245, 158, 11, 0.16);
    }
    .param-syntax-hint {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: var(--lumo-font-size-xs);
      background: rgba(245, 158, 11, 0.14);
      border: 1px solid rgba(245, 158, 11, 0.35);
    }
    .inline-create-box {
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      padding: var(--lumo-space-xs) var(--lumo-space-s);
      background: var(--lumo-base-color, rgba(255, 255, 255, 0.04));
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs);
      align-items: end;
    }
    @media (max-width: 768px) {
      .fields,
      .generic-args-grid,
      .custom-arg-row,
      .inline-create-box {
        grid-template-columns: 1fr;
      }
    }
  `;

  private isApplyingInternalChange = false;
  private componentSelectionGeneration = 0;
  private projectEnvironmentsGeneration = 0;
  private inlineCreateGeneration = 0;

  connectedCallback() {
    super.connectedCallback();
    this.loadPropertiesCatalog();
    this.loadProjectEnvironments();
  }

  protected updated(changed: PropertyValues) {
    if (changed.has('project')) {
      this.previewEnvironmentName = '';
      this.loadProjectEnvironments();
    }
    if (changed.has('component')) {
      if (!this.isApplyingInternalChange) {
        this.componentSelectionGeneration++;
        this.inlineCreateGeneration++;
        this.inlineCreateSubmitting = false;
        this.inlineCreatedProperty = undefined;
        this.activeParameterKey = '';
        this.openPickerParamKey = '';
        this.pickerFilterText = '';
        this.inlineCreateParamKey = '';
        this.inlineCreateFeedback = '';
      }
      this.isApplyingInternalChange = false;
    }
    if (
      changed.has('component') ||
      changed.has('previewEnvironmentName') ||
      changed.has('inlineNewPropertyDefaultValue')
    ) {
      void this.refreshParameterResolutionPreview();
    }
    if (changed.has('previewEnvironmentName') && this.inlineCreateParamKey) {
      this.loadInlineScopeOptions();
    }
  }

  public loadInlineScopeOptions() {
    const api = new PropertyValuesApi(dorcApiConfiguration);
    if (typeof api.propertyValuesScopeOptionsGet !== 'function') {
      return;
    }
    api
      .propertyValuesScopeOptionsGet({
        propertyValueScope: this.previewEnvironmentName || ''
      })
      .subscribe({
        next: opts => {
          this.inlineScopeOptions = opts ?? [];
        },
        error: () => {
          this.inlineScopeOptions = [];
        }
      });
  }

  public loadPropertiesCatalog() {
    new PropertiesApi(dorcApiConfiguration).propertiesGet().subscribe({
      next: props => {
        this.availableProperties = [...(props ?? [])].sort((a, b) =>
          String(a.Name ?? '').localeCompare(String(b.Name ?? ''))
        );
      }
    });
  }

  public loadProjectEnvironments() {
    const requestGen = ++this.projectEnvironmentsGeneration;
    const projectName = this.project?.ProjectName;
    if (!projectName) {
      this.projectEnvironmentNames = [];
      return;
    }
    new RefDataProjectEnvironmentMappingsApi(dorcApiConfiguration)
      .refDataProjectEnvironmentMappingsGet({
        project: projectName,
        includeRead: true
      })
      .subscribe({
        next: res => {
          if (
            requestGen !== this.projectEnvironmentsGeneration ||
            this.project?.ProjectName !== projectName
          ) {
            return;
          }
          const names = (res?.Items ?? [])
            .map(item => item.EnvironmentName ?? '')
            .filter(Boolean);
          this.projectEnvironmentNames = names;
          if (!this.previewEnvironmentName && names.length > 0) {
            this.previewEnvironmentName = names[0];
          }
        }
      });
  }

  public async refreshParameterResolutionPreview() {
    const generation = ++this.previewRequestGeneration;
    const parsed = parseScriptPathValue(this.component?.ScriptPath);
    const tokenSet = new Set<string>();
    for (const arg of parsed.arguments) {
      for (const seg of parsePropertyTokens(arg.value, 'brace-only')) {
        if (seg.kind === 'property') {
          tokenSet.add(seg.propertyName);
        }
      }
    }
    if (
      this.inlineCreateParamKey &&
      !this.inlineNewPropertySecure &&
      this.inlineNewPropertyDefaultValue
    ) {
      for (const seg of parsePropertyTokens(
        this.inlineNewPropertyDefaultValue,
        'standard'
      )) {
        if (seg.kind === 'property') {
          tokenSet.add(seg.propertyName);
        }
      }
    }
    if (tokenSet.size === 0) {
      this.previewTracesByProperty = new Map();
      return;
    }
    try {
      const traces = await defaultPropertyResolutionClientCache.resolveProperties(
        {
          environmentName: this.previewEnvironmentName || undefined,
          projectName: this.project?.ProjectName || undefined,
          locationKind: 'deployment-definition'
        },
        Array.from(tokenSet)
      );
      if (generation === this.previewRequestGeneration) {
        this.previewTracesByProperty = traces;
      }
    } catch {
      if (generation === this.previewRequestGeneration) {
        this.previewTracesByProperty = new Map();
      }
    }
  }

  public insertVariableIntoParameter(
    baseScriptPath: string,
    currentArgs: Array<{ key: string; value: string }>,
    paramKey: string,
    propertyName: string,
    isJsonPath: boolean
  ) {
    const trimmedName = propertyName.trim();
    if (!trimmedName || !paramKey || !/^[A-Za-z0-9_.-]+$/.test(trimmedName)) {
      return;
    }
    const token = `\${${trimmedName}}`;
    this.setGenericArgument(
      baseScriptPath,
      currentArgs,
      paramKey,
      token,
      isJsonPath
    );
  }

  public openInlineVariableCreator(paramKey: string, suggestedName = '') {
    this.inlineCreateGeneration++;
    this.inlineCreateSubmitting = false;
    this.inlineCreatedProperty = undefined;
    this.inlineCreateParamKey = paramKey;
    this.activeParameterKey = paramKey;
    this.openPickerParamKey = '';
    this.inlineNewPropertyName = suggestedName;
    this.inlineNewPropertySecure = false;
    this.inlineNewPropertyDefaultValue = '';
    this.inlineValuePickerOpen = false;
    this.inlineValuePickerFilter = '';
    this.inlineValueSelectionStart = null;
    this.inlineValueSelectionEnd = null;
    this.inlineCreateFeedback = '';
    this.loadInlineScopeOptions();
  }

  public insertTokenIntoInlineValue(token: string) {
    if (!token) return;
    const current = this.inlineNewPropertyDefaultValue;
    const start =
      typeof this.inlineValueSelectionStart === 'number'
        ? this.inlineValueSelectionStart
        : current.length;
    const end =
      typeof this.inlineValueSelectionEnd === 'number'
        ? this.inlineValueSelectionEnd
        : start;
    const trigger = detectActiveDollarCompletion(current, end);
    const completed = applyDollarTokenCompletion(
      current,
      token,
      trigger,
      start,
      end
    );
    this.inlineNewPropertyDefaultValue = completed.nextValue;
    this.inlineValueSelectionStart = completed.nextCaretIndex;
    this.inlineValueSelectionEnd = completed.nextCaretIndex;
    this.inlineValuePickerOpen = false;
    this.inlineValuePickerFilter = '';
    const valueField = this.shadowRoot?.querySelector('#inlineNewPropertyValue') as
      | (HTMLElement & { inputElement?: HTMLInputElement })
      | null;
    const nativeInput = valueField?.inputElement;
    if (nativeInput) {
      nativeInput.focus();
      if (typeof nativeInput.setSelectionRange === 'function') {
        nativeInput.setSelectionRange(
          completed.nextCaretIndex,
          completed.nextCaretIndex
        );
      }
    } else {
      valueField?.focus();
    }
  }

  private renderInlineValueDollarHint() {
    if (this.inlineNewPropertySecure) return nothing;
    const inspection = inspectUnescapedDollars(
      this.inlineNewPropertyDefaultValue
    );
    const preview = evaluateDollarValuePreview(
      this.inlineNewPropertyDefaultValue,
      this.previewTracesByProperty,
      this.previewEnvironmentName || 'Default'
    );
    const dollarTokens = buildInsertableDollarTokens(
      this.inlineScopeOptions,
      this.availableProperties,
      this.inlineNewPropertyName
    );
    const filterLower = this.inlineValuePickerFilter.trim().toLowerCase();
    const filteredTokens = dollarTokens
      .filter(t =>
        !filterLower
          ? true
          : t.ValueOption.toLowerCase().includes(filterLower) ||
            String(t.SampleResolvedValue ?? '')
              .toLowerCase()
              .includes(filterLower)
      )
      .slice(0, 30);

    return html`
      ${
        this.inlineValuePickerOpen
          ? html`<div
              class="param-picker-popover full"
              role="dialog"
              aria-label="Insert $Variable$ token into initial value"
            >
              <vaadin-text-field
                placeholder="Search $ServerNames_&lt;Tag&gt;$, $EnvironmentName$, or $Variable$..."
                clear-button-visible
                theme="small"
                .value=${live(this.inlineValuePickerFilter)}
                @input=${(e: Event) => {
                  this.inlineValuePickerFilter = (
                    e.currentTarget as HTMLInputElement
                  ).value;
                }}
              ></vaadin-text-field>
              <div class="param-picker-list">
                ${filteredTokens.map(
                  opt => html`<button
                    type="button"
                    class="param-picker-item"
                    @click=${() => this.insertTokenIntoInlineValue(opt.ValueOption)}
                  >
                    <span>${opt.ValueOption}</span>
                    <span style="opacity:0.7">${opt.CategoryLabel}</span>
                  </button>`
                )}
              </div>
              <div style="display:flex; justify-content:flex-end;">
                <button
                  type="button"
                  class="param-action-btn"
                  @click=${() => {
                    this.inlineValuePickerOpen = false;
                  }}
                >
                  Close
                </button>
              </div>
            </div>`
          : nothing
      }
      ${
        inspection.hasBraceSyntax
          ? html`<div class="param-syntax-hint full" role="status">
              <span
                >Variable values use <code>$VariableName$</code> rather than
                <code>\${VariableName}</code>.</span
              >
              <button
                type="button"
                class="param-action-btn"
                @click=${() => {
                  this.inlineNewPropertyDefaultValue =
                    inspection.braceConvertedSuggestion;
                }}
              >
                Convert to $Variable$
              </button>
            </div>`
          : inspection.hasUnclosedDollar
            ? html`<div class="param-syntax-hint full" role="status">
                <span
                  >${
                    inspection.hasOtherPropertyTokens
                      ? 'Unescaped $ (such as E$) prevents other $Variable$ tokens from expanding.'
                      : 'Contains a literal $ (such as E$). Escape as $$ if combining with $Variable$ tokens.'
                  }</span
                >
                <button
                  type="button"
                  class="param-action-btn"
                  @click=${() => {
                    this.inlineNewPropertyDefaultValue =
                      inspection.escapedSuggestion;
                  }}
                >
                  Escape literal $ as $$
                </button>
              </div>`
            : nothing
      }
      ${
        preview
          ? html`<div
              class="param-preview-badge full"
              data-status=${preview.status}
              title=${preview.scopeSummary}
            >
              <span
                >${
                  this.previewEnvironmentName
                    ? `Value resolves in ${this.previewEnvironmentName}:`
                    : 'Value resolves (Default):'
                }</span
              >
              <strong>${preview.resolvedText}</strong>
            </div>`
          : nothing
      }
    `;
  }

  public createInlineVariableForParameter() {
    if (this.inlineCreateSubmitting) return;
    const propName = this.inlineNewPropertyName.trim();
    const targetParam = this.inlineCreateParamKey || this.activeParameterKey;
    if (!propName || !targetParam) return;
    if (!/^[A-Za-z0-9_.-]+$/.test(propName)) {
      this.inlineCreateFeedback =
        'Variable name may only contain letters, digits, _, ., and -.';
      return;
    }

    const originatingGeneration = this.componentSelectionGeneration;
    const originatingInlineGeneration = ++this.inlineCreateGeneration;
    const initialParsed = parseScriptPathValue(this.component?.ScriptPath);
    const initialTargetValue =
      initialParsed.arguments.find(
        a => a.key.toLowerCase() === targetParam.toLowerCase()
      )?.value ?? '';
    const seedVal = this.inlineNewPropertyDefaultValue.trim();
    const targetScope = this.previewEnvironmentName || undefined;
    const createdByForm =
      this.inlineCreatedProperty?.Name?.toLowerCase() === propName.toLowerCase()
        ? this.inlineCreatedProperty
        : undefined;
    const preExistingCatalogProp = !createdByForm
      ? this.availableProperties.find(
          p => p.Name?.toLowerCase() === propName.toLowerCase()
        )
      : undefined;

    if (preExistingCatalogProp) {
      this.inlineCreateFeedback = `Variable \${${preExistingCatalogProp.Name}} already exists. Pick it from the parameter dropdown instead.`;
      return;
    }

    if (
      createdByForm &&
      Boolean(createdByForm.Secure) !== Boolean(this.inlineNewPropertySecure)
    ) {
      this.inlineCreateFeedback = `Variable \${${createdByForm.Name}} was already created as ${
        createdByForm.Secure ? 'secure' : 'non-secure'
      }.`;
      return;
    }

    const newProp: PropertyApiModel = createdByForm ?? {
      Name: propName,
      Secure: this.inlineNewPropertySecure,
      IsArray: false
    };

    const isStillCurrentAttempt = () =>
      this.componentSelectionGeneration === originatingGeneration &&
      this.inlineCreateGeneration === originatingInlineGeneration &&
      this.inlineCreateParamKey === targetParam;

    const finalizeParameterInsert = () => {
      if (!isStillCurrentAttempt()) return;
      this.inlineCreateSubmitting = false;
      this.inlineCreatedProperty = undefined;
      const latestParsed = parseScriptPathValue(this.component?.ScriptPath);
      const latestTemplate = findGenericScriptTemplate(latestParsed.scriptPath);
      const currentParamValue =
        latestParsed.arguments.find(
          a => a.key.toLowerCase() === targetParam.toLowerCase()
        )?.value ?? '';
      const isValidTarget = Boolean(
        latestTemplate?.parameters.some(
          p => p.name.toLowerCase() === targetParam.toLowerCase()
        ) ||
          latestParsed.arguments.some(
            a => a.key.toLowerCase() === targetParam.toLowerCase()
          )
      );
      if (
        isValidTarget &&
        latestParsed.scriptPath &&
        currentParamValue === initialTargetValue
      ) {
        this.insertVariableIntoParameter(
          latestTemplate?.scriptPath ?? latestParsed.scriptPath,
          latestParsed.arguments,
          targetParam,
          newProp.Name ?? propName,
          latestParsed.isJsonPath
        );
      }
      this.inlineCreateFeedback = `Created \${${newProp.Name ?? propName}}`;
      this.inlineCreateParamKey = '';
    };

    const saveInitialSeedValue = () => {
      new PropertyValuesApi(dorcApiConfiguration)
        .propertyValuesPost({
          propertyValueDto: [
            {
              Property: newProp,
              Value: seedVal,
              PropertyValueFilter: targetScope
            }
          ]
        })
        .subscribe({
          next: valResponses => {
            defaultPropertyResolutionClientCache.clear();
            void this.refreshParameterResolutionPreview();
            if (!isStillCurrentAttempt()) return;
            if (valResponses?.[0]?.Status === 'success') {
              finalizeParameterInsert();
            } else {
              this.inlineCreateSubmitting = false;
              this.inlineCreateFeedback =
                valResponses?.[0]?.Status ||
                `Created \${${propName}}, but initial value could not be saved.`;
            }
          },
          error: () => {
            if (!isStillCurrentAttempt()) return;
            this.inlineCreateSubmitting = false;
            this.inlineCreateFeedback = `Created \${${propName}}, but initial value could not be saved.`;
          }
        });
    };

    this.inlineCreateSubmitting = true;

    if (createdByForm) {
      if (seedVal) {
        saveInitialSeedValue();
      } else {
        finalizeParameterInsert();
      }
      return;
    }

    const propertiesApi = new PropertiesApi(dorcApiConfiguration);
    propertiesApi.propertiesGet().subscribe({
      next: existingList => {
        if (!isStillCurrentAttempt()) return;
        if (Array.isArray(existingList)) {
          this.availableProperties = [...existingList].sort((a, b) =>
            String(a.Name ?? '').localeCompare(String(b.Name ?? ''))
          );
        }
        const existingMatch = Array.isArray(existingList)
          ? existingList.find(
              p => p.Name?.toLowerCase() === propName.toLowerCase()
            )
          : undefined;
        if (existingMatch) {
          this.inlineCreateSubmitting = false;
          this.inlineCreateFeedback = `Variable \${${existingMatch.Name ?? propName}} already exists. Pick it from the parameter dropdown instead.`;
          return;
        }
        propertiesApi
          .propertiesPost({ propertyApiModel: [newProp] })
          .subscribe({
            next: responses => {
              if (responses?.[0]?.Status === 'success') {
                const returnedItem = responses[0]?.Item as
                  | PropertyApiModel
                  | undefined;
                const persistedProp: PropertyApiModel =
                  returnedItem && typeof returnedItem === 'object' && returnedItem.Name
                    ? returnedItem
                    : newProp;
                this.availableProperties = [
                  ...this.availableProperties.filter(
                    p => p.Name?.toLowerCase() !== propName.toLowerCase()
                  ),
                  persistedProp
                ].sort((a, b) =>
                  String(a.Name ?? '').localeCompare(String(b.Name ?? ''))
                );
                if (!isStillCurrentAttempt()) return;
                if (
                  returnedItem &&
                  typeof returnedItem === 'object' &&
                  typeof returnedItem.Secure === 'boolean' &&
                  Boolean(returnedItem.Secure) !== Boolean(newProp.Secure)
                ) {
                  this.inlineCreateSubmitting = false;
                  this.inlineCreateFeedback = `Variable \${${persistedProp.Name ?? propName}} already exists as ${
                    returnedItem.Secure ? 'secure' : 'non-secure'
                  }. Pick it from the parameter dropdown instead.`;
                  return;
                }
                this.inlineCreatedProperty = persistedProp;
                if (seedVal) {
                  saveInitialSeedValue();
                } else {
                  finalizeParameterInsert();
                }
              } else if (isStillCurrentAttempt()) {
                this.inlineCreateSubmitting = false;
                this.inlineCreateFeedback =
                  responses?.[0]?.Status || 'Could not create variable.';
              }
            },
            error: () => {
              if (isStillCurrentAttempt()) {
                this.inlineCreateSubmitting = false;
                this.inlineCreateFeedback = 'Could not create variable.';
              }
            }
          });
      },
      error: () => {
        if (!isStillCurrentAttempt()) return;
        this.inlineCreateSubmitting = false;
        this.inlineCreateFeedback = 'Could not verify existing variables.';
      }
    });
  }

  public toggleParameterPicker(paramKey: string) {
    if (this.openPickerParamKey === paramKey) {
      this.openPickerParamKey = '';
      this.pickerFilterText = '';
    } else {
      this.openPickerParamKey = paramKey;
      this.activeParameterKey = paramKey;
      this.pickerFilterText = '';
    }
  }

  private renderParameterVariableControls(
    baseScriptPath: string,
    currentArgs: Array<{ key: string; value: string }>,
    paramKey: string,
    currentVal: string,
    isJsonPath: boolean
  ) {
    const syntaxWarning = inspectParameterBindingSyntax(currentVal);
    const isPickerOpen =
      !this.readonly &&
      Boolean(paramKey) &&
      this.openPickerParamKey === paramKey;
    const filterLower = this.pickerFilterText.trim().toLowerCase();
    const filteredProps = this.availableProperties
      .filter(p =>
        !filterLower
          ? true
          : (p.Name ?? '').toLowerCase().includes(filterLower)
      )
      .slice(0, 30);

    return html`
      ${
        syntaxWarning
          ? html`<div class="param-syntax-hint" role="status">
              <span>${syntaxWarning.message}</span>
              ${
                !this.readonly && syntaxWarning.suggestedVariableName
                  ? html`<button
                      type="button"
                      class="param-action-btn"
                      @click=${() =>
                        this.insertVariableIntoParameter(
                          baseScriptPath,
                          currentArgs,
                          paramKey,
                          syntaxWarning.suggestedVariableName!,
                          isJsonPath
                        )}
                    >
                      Use \${${syntaxWarning.suggestedVariableName}}
                    </button>`
                  : nothing
              }
            </div>`
          : nothing
      }
      ${
        isPickerOpen
          ? html`<div
              class="param-picker-popover"
              role="dialog"
              aria-label="Bind variable to ${paramKey}"
            >
              <vaadin-text-field
                placeholder="Search variable for ${paramKey}..."
                clear-button-visible
                theme="small"
                .value=${live(this.pickerFilterText)}
                @input=${(e: Event) => {
                  this.pickerFilterText = (
                    e.currentTarget as HTMLInputElement
                  ).value;
                }}
              ></vaadin-text-field>
              <div class="param-picker-list">
                ${filteredProps.map(
                  prop => html`<button
                    type="button"
                    class="param-picker-item"
                    @click=${() => {
                      this.insertVariableIntoParameter(
                        baseScriptPath,
                        currentArgs,
                        paramKey,
                        prop.Name ?? '',
                        isJsonPath
                      );
                      this.openPickerParamKey = '';
                    }}
                  >
                    <span>\${${prop.Name}}</span>
                    <span style="opacity:0.7"
                      >${prop.Secure ? 'Secure' : 'Variable'}</span
                    >
                  </button>`
                )}
              </div>
              <div
                style="display:flex; justify-content:space-between; align-items:center; border-top:1px solid var(--dorc-border-color); padding-top:4px;"
              >
                <button
                  type="button"
                  class="param-action-btn"
                  @click=${() =>
                    this.openInlineVariableCreator(
                      paramKey,
                      this.pickerFilterText.trim()
                    )}
                >
                  + New variable for ${paramKey}
                </button>
                <button
                  type="button"
                  class="param-action-btn"
                  @click=${() => {
                    this.openPickerParamKey = '';
                  }}
                >
                  Close
                </button>
              </div>
            </div>`
          : nothing
      }
      <div class="param-cell-actions">
        ${this.renderParameterResolutionPreviewBadge(currentVal)}
        ${
          !this.readonly && paramKey
            ? html`<button
                type="button"
                class="param-action-btn"
                @click=${() => this.openInlineVariableCreator(paramKey)}
              >
                + Create variable for ${paramKey}
              </button>`
            : nothing
        }
      </div>
    `;
  }

  private renderParameterResolutionPreviewBadge(rawVal: string) {
    const preview = evaluateParameterPreview(
      rawVal,
      this.previewTracesByProperty
    );
    if (!preview) return nothing;
    const scopeSummary =
      preview.detailMessage ||
      preview.traces
        .map(
          t =>
            `${t.PropertyName}: ${t.ScopeName || 'Default'} (${t.Source ?? 'Unresolved'})`
        )
        .join(' · ');
    return html`<div
      class="param-preview-badge"
      data-status=${preview.status}
      title=${scopeSummary}
    >
      <span
        >${
          preview.status === 'unresolved'
            ? this.previewEnvironmentName
              ? `Not resolved in ${this.previewEnvironmentName}:`
              : 'Not resolved (Default):'
            : this.previewEnvironmentName
              ? `Resolves in ${this.previewEnvironmentName}:`
              : 'Resolves (Default):'
        }</span
      >
      <strong>${preview.resolvedText}</strong>
    </div>`;
  }

  private change(patch: Partial<ComponentApiModel>) {
    if (this.readonly || !this.component) return;
    this.isApplyingInternalChange = true;
    this.dispatchEvent(
      new CustomEvent('definition-changed', {
        detail: { ...this.component, ...patch },
        bubbles: true,
        composed: true
      })
    );
  }

  private text(
    label: string,
    key:
      | 'ComponentName'
      | 'ScriptPath'
      | 'TerraformGitBranch'
      | 'TerraformSubPath',
    helper = '',
    locked = false
  ) {
    const update = (event: Event) =>
      this.change({
        [key]: (event.currentTarget as HTMLInputElement).value
      });
    return html`<vaadin-text-field
      .label=${label}
      .value=${live(this.component?.[key] ?? '')}
      .readonly=${this.readonly || locked}
      .helperText=${helper}
      @input=${update}
      @change=${update}
    ></vaadin-text-field>`;
  }

  /**
   * A JSON path is an object with `ScriptPath` and `GenericArguments`, and a
   * single-line field shows a few dozen characters of it. The raw value stays
   * editable — nothing is reformatted on the way in, so an untouched value
   * round-trips byte-for-byte — and the viewer below renders what the
   * current text parses to. Malformed JSON shows as the raw string.
   */
  private renderScriptPathField(component: ComponentApiModel) {
    const raw = component.ScriptPath ?? '';
    const helper =
      'Existing paths and JSON argument definitions are preserved. Use secure property references for secrets.';
    if (
      !parseScriptPathValue(raw).isJsonPath &&
      this.shadowRoot?.activeElement?.tagName !== 'VAADIN-TEXT-AREA'
    ) {
      return this.text('Script path or JSON path', 'ScriptPath', helper);
    }
    let parsed: unknown = raw;
    try {
      parsed = JSON.parse(raw);
    } catch {
      /* preview the raw text */
    }
    const update = (event: Event) =>
      this.change({
        ScriptPath: (event.currentTarget as HTMLTextAreaElement).value
      });
    return html`<vaadin-text-area
        label="Script path or JSON path"
        .value=${live(raw)}
        .readonly=${this.readonly}
        .helperText=${helper}
        min-rows="2"
        max-rows="8"
        @input=${update}
        @change=${update}
      ></vaadin-text-area>
      <span class="json-preview-label">Parsed JSON path</span>
      <hegs-json-viewer
        class="json-preview"
        .data=${parsed}
        .resolutionContext=${{
          environmentName: this.previewEnvironmentName || undefined,
          projectName: this.project?.ProjectName || undefined,
          locationKind: 'deployment-definition' as const
        }}
        ${ref(element => (element as HegsJsonViewer | undefined)?.expandAll())}
      ></hegs-json-viewer>`;
  }

  private selectGenericTemplate(templatePath: string) {
    if (this.readonly || !this.component) return;
    this.componentSelectionGeneration++;
    this.inlineCreateGeneration++;
    this.inlineCreateSubmitting = false;
    this.inlineCreatedProperty = undefined;
    this.activeParameterKey = '';
    this.openPickerParamKey = '';
    this.pickerFilterText = '';
    this.inlineCreateParamKey = '';
    this.inlineCreateFeedback = '';
    if (!templatePath || templatePath === CUSTOM_SCRIPT_OPTION) {
      const parsed = parseScriptPathValue(this.component.ScriptPath);
      if (findGenericScriptTemplate(parsed.scriptPath)) {
        this.change({ ScriptPath: '' });
      }
      return;
    }
    const template = findGenericScriptTemplate(templatePath);
    if (!template) return;
    const parsed = parseScriptPathValue(this.component.ScriptPath);
    const nextScriptPath = serializeScriptPathValue(
      template.scriptPath,
      parsed.arguments
    );
    const nextName =
      !this.component.ComponentName ||
      GENERIC_SCRIPT_TEMPLATES.some(
        item => item.defaultStepName === this.component?.ComponentName
      )
        ? template.defaultStepName
        : this.component.ComponentName;
    this.change({
      ComponentName: nextName,
      ScriptPath: nextScriptPath
    });
  }

  private setGenericArgument(
    baseScriptPath: string,
    currentArgs: Array<{ key: string; value: string }>,
    paramName: string,
    paramValue: string,
    isJsonPath: boolean
  ) {
    const updated = [...currentArgs];
    const existingIndex = updated.findIndex(
      item => item.key.toLowerCase() === paramName.toLowerCase()
    );
    if (existingIndex >= 0) {
      updated[existingIndex] = { key: paramName, value: paramValue };
    } else if (paramValue.trim().length > 0) {
      updated.push({ key: paramName, value: paramValue });
    }
    this.change({
      ScriptPath: serializeScriptPathValue(baseScriptPath, updated, isJsonPath)
    });
  }

  private renderPowerShellGenericSection(component: ComponentApiModel) {
    const parsed = parseScriptPathValue(component.ScriptPath);
    const template: GenericScriptTemplate | undefined =
      findGenericScriptTemplate(parsed.scriptPath);
    const templateItems = [
      {
        label: 'Custom project script path',
        value: CUSTOM_SCRIPT_OPTION
      },
      ...GENERIC_SCRIPT_TEMPLATES.map(item => ({
        label: `${item.name} (${item.scriptPath})`,
        value: item.scriptPath
      }))
    ];
    const selectedTemplateValue = template
      ? template.scriptPath
      : CUSTOM_SCRIPT_OPTION;
    const knownParamNames = new Set(
      (template?.parameters ?? []).map(p => p.name.toLowerCase())
    );
    const extraArgs = parsed.arguments.filter(
      arg => !knownParamNames.has(arg.key.toLowerCase())
    );

    return html`
      <div class="full">
        <vaadin-combo-box
          label="Predefined generic script (project-scripts / 00 Generic)"
          .items=${templateItems}
          item-label-path="label"
          item-value-path="value"
          .value=${live(selectedTemplateValue)}
          .disabled=${this.readonly}
          helper-text="Choose a reusable 00 Generic PowerShell script to configure its GenericArguments, or leave as Custom project script path."
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            const value =
              event.detail?.value ??
              (event.currentTarget as HTMLInputElement).value;
            if (value && value !== selectedTemplateValue) {
              this.selectGenericTemplate(value);
            }
          }}
          @input=${(event: Event) =>
            this.selectGenericTemplate(
              (event.currentTarget as HTMLInputElement).value
            )}
          @change=${(event: Event) =>
            this.selectGenericTemplate(
              (event.currentTarget as HTMLInputElement).value
            )}
        ></vaadin-combo-box>
      </div>
      ${
        template || parsed.isJsonPath
          ? html`
              <div
                class="generic-script-card"
                role="region"
                aria-label="Generic script arguments"
              >
                <div class="generic-script-header">
                  <strong>
                    ${
                      template
                        ? `${template.name} — ${template.scriptPath}`
                        : `JSON ScriptPath arguments — ${parsed.scriptPath}`
                    }
                  </strong>
                  ${
                    template
                      ? html`<span class="generic-category-badge"
                          >${template.category}</span
                        >`
                      : nothing
                  }
                </div>
                ${template ? html`<p>${template.description}</p>` : nothing}
                <div
                  class="generic-variable-toolbar"
                  role="group"
                  aria-label="Parameter variable tools and preview"
                >
                  <vaadin-combo-box
                    id="paramPreviewEnvCombo"
                    label="Preview resolution for environment"
                    placeholder="Select or type environment (e.g. STP UT 01)..."
                    allow-custom-value
                    clear-button-visible
                    theme="small"
                    .items=${this.projectEnvironmentNames}
                    .value=${live(this.previewEnvironmentName)}
                    @value-changed=${(event: CustomEvent<{ value: string }>) => {
                      const nextEnv = event.detail?.value ?? '';
                      if (nextEnv !== this.previewEnvironmentName) {
                        this.previewEnvironmentName = nextEnv;
                      }
                    }}
                    @custom-value-set=${(event: CustomEvent<string>) => {
                      this.previewEnvironmentName = event.detail ?? '';
                    }}
                  ></vaadin-combo-box>
                  <span class="env-props">
                    Click <code>${'{x}'}</code> inside any parameter field to
                    bind an existing <code>${'${VariableName}'}</code> or create
                    a new variable.
                  </span>
                </div>
                ${
                  this.inlineCreateFeedback
                    ? html`<p class="env-props" role="status">
                        ${this.inlineCreateFeedback}
                      </p>`
                    : nothing
                }
                ${
                  this.inlineCreateParamKey && !this.readonly
                    ? html`
                        <div
                          class="inline-create-box"
                          role="region"
                          aria-label="Create variable inline"
                        >
                          <vaadin-text-field
                            id="inlineNewPropertyName"
                            label="New variable name (for ${this.inlineCreateParamKey})"
                            placeholder="e.g. STP_DAS_Paper_DestinationPath"
                            theme="small"
                            .value=${live(this.inlineNewPropertyName)}
                            @input=${(e: Event) => {
                              this.inlineNewPropertyName = (
                                e.currentTarget as HTMLInputElement
                              ).value;
                            }}
                          ></vaadin-text-field>
                          ${
                            this.inlineNewPropertySecure
                              ? html`<vaadin-password-field
                                  id="inlineNewPropertyValue"
                                  label="Initial secret value (${
                                    this.previewEnvironmentName || 'default'
                                  })"
                                  placeholder="Optional initial secret value..."
                                  theme="small"
                                  .value=${live(
                                    this.inlineNewPropertyDefaultValue
                                  )}
                                  @input=${(e: Event) => {
                                    this.inlineNewPropertyDefaultValue = (
                                      e.currentTarget as HTMLInputElement
                                    ).value;
                                  }}
                                ></vaadin-password-field>`
                              : html`<vaadin-text-field
                                  id="inlineNewPropertyValue"
                                  label="Initial value (${
                                    this.previewEnvironmentName || 'default'
                                  })"
                                  placeholder="Optional initial value (use $Variable$ or $$)..."
                                  theme="small"
                                  .value=${live(
                                    this.inlineNewPropertyDefaultValue
                                  )}
                                  @blur=${(e: Event) => {
                                    const target = e.currentTarget as HTMLElement & {
                                      inputElement?: HTMLInputElement;
                                    };
                                    const nativeInput = target.inputElement;
                                    if (nativeInput) {
                                      this.inlineValueSelectionStart =
                                        nativeInput.selectionStart;
                                      this.inlineValueSelectionEnd =
                                        nativeInput.selectionEnd;
                                    }
                                  }}
                                  @input=${(e: Event) => {
                                    const target = e.currentTarget as HTMLElement & {
                                      value: string;
                                      inputElement?: HTMLInputElement;
                                    };
                                    this.inlineNewPropertyDefaultValue =
                                      target.value;
                                    const caret =
                                      target.inputElement?.selectionStart ??
                                      target.value.length;
                                    if (target.inputElement) {
                                      this.inlineValueSelectionStart =
                                        target.inputElement.selectionStart;
                                      this.inlineValueSelectionEnd =
                                        target.inputElement.selectionEnd;
                                    }
                                    const activeCompletion =
                                      detectActiveDollarCompletion(
                                        target.value,
                                        caret
                                      );
                                    if (activeCompletion) {
                                      this.inlineValuePickerOpen = true;
                                      this.inlineValuePickerFilter =
                                        activeCompletion.queryText;
                                    } else if (this.inlineValuePickerOpen) {
                                      this.inlineValuePickerOpen = false;
                                      this.inlineValuePickerFilter = '';
                                    }
                                  }}
                                  @keydown=${(e: KeyboardEvent) => {
                                    if (e.ctrlKey && e.key === ' ') {
                                      e.preventDefault();
                                      this.inlineValuePickerOpen = true;
                                    } else if (
                                      e.key === 'Escape' &&
                                      this.inlineValuePickerOpen
                                    ) {
                                      e.preventDefault();
                                      e.stopPropagation();
                                      this.inlineValuePickerOpen = false;
                                    }
                                  }}
                                >
                                  <button
                                    slot="suffix"
                                    type="button"
                                    class="param-bind-suffix-btn"
                                    title="Explore $ tokens (or type $ / Ctrl+Space)"
                                    @click=${() => {
                                      this.inlineValuePickerOpen =
                                        !this.inlineValuePickerOpen;
                                    }}
                                  >
                                    $x$
                                  </button>
                                </vaadin-text-field>`
                          }
                          ${this.renderInlineValueDollarHint()}
                          <vaadin-checkbox
                            id="inlineNewPropertySecure"
                            label="Secure"
                            .checked=${this.inlineNewPropertySecure}
                            @change=${(e: Event) => {
                              this.inlineNewPropertySecure = (
                                e.currentTarget as HTMLInputElement
                              ).checked;
                            }}
                          ></vaadin-checkbox>
                          <div style="display:flex; gap:6px;">
                            <vaadin-button
                              id="confirmInlineVariableBtn"
                              theme="small primary"
                              ?disabled=${this.inlineCreateSubmitting}
                              @click=${() =>
                                this.createInlineVariableForParameter()}
                              >Create & insert</vaadin-button
                            >
                            <vaadin-button
                              id="cancelInlineVariableBtn"
                              theme="small tertiary"
                              @click=${() => {
                                this.inlineCreateGeneration++;
                                this.inlineCreateSubmitting = false;
                                this.inlineCreatedProperty = undefined;
                                this.inlineCreateParamKey = '';
                              }}
                              >Cancel</vaadin-button
                            >
                          </div>
                        </div>
                      `
                    : nothing
                }
                ${
                  template && template.parameters.length > 0
                    ? html`
                        <div class="generic-args-grid">
                          ${template.parameters.map(param => {
                            const currentVal =
                              parsed.arguments.find(
                                a =>
                                  a.key.toLowerCase() ===
                                  param.name.toLowerCase()
                              )?.value ?? '';
                            const updateParam = (event: Event) =>
                              this.setGenericArgument(
                                template.scriptPath,
                                parsed.arguments,
                                param.name,
                                (event.currentTarget as HTMLInputElement).value,
                                parsed.isJsonPath
                              );
                            const suffixBtn = !this.readonly
                              ? html`<button
                                  type="button"
                                  slot="suffix"
                                  class="param-bind-suffix-btn"
                                  title="Bind variable (\${VariableName}) to ${param.name}"
                                  aria-label="Bind variable to ${param.name}"
                                  @click=${(e: Event) => {
                                    e.stopPropagation();
                                    this.toggleParameterPicker(param.name);
                                  }}
                                >
                                  ${'{x}'}
                                </button>`
                              : nothing;
                            return html`<div class="param-cell">
                              ${
                                param.options
                                  ? html`<vaadin-combo-box
                                      .label=${param.label}
                                      .items=${
                                        currentVal &&
                                        !param.options.includes(currentVal)
                                          ? [...param.options, currentVal]
                                          : param.options
                                      }
                                      allow-custom-value
                                      .value=${live(currentVal)}
                                      .readonly=${this.readonly}
                                      .helperText=${param.helperText ?? ''}
                                      ?required=${Boolean(param.required)}
                                      @focus=${() => {
                                        this.activeParameterKey = param.name;
                                      }}
                                      @custom-value-set=${(
                                        event: CustomEvent<string>
                                      ) => {
                                        const customVal = event.detail ?? '';
                                        if (customVal !== currentVal) {
                                          this.setGenericArgument(
                                            template.scriptPath,
                                            parsed.arguments,
                                            param.name,
                                            customVal,
                                            parsed.isJsonPath
                                          );
                                        }
                                      }}
                                      @value-changed=${(
                                        event: CustomEvent<{ value: string }>
                                      ) => {
                                        const nextVal =
                                          event.detail?.value ??
                                          (
                                            event.currentTarget as HTMLInputElement
                                          ).value;
                                        if (nextVal !== currentVal) {
                                          this.setGenericArgument(
                                            template.scriptPath,
                                            parsed.arguments,
                                            param.name,
                                            nextVal,
                                            parsed.isJsonPath
                                          );
                                        }
                                      }}
                                      @input=${updateParam}
                                      @change=${updateParam}
                                      >${suffixBtn}</vaadin-combo-box
                                    >`
                                  : html`<vaadin-text-field
                                      .label=${param.label}
                                      .placeholder=${param.placeholder ?? ''}
                                      .value=${live(currentVal)}
                                      .readonly=${this.readonly}
                                      .helperText=${param.helperText ?? ''}
                                      ?required=${Boolean(param.required)}
                                      @focus=${() => {
                                        this.activeParameterKey = param.name;
                                      }}
                                      @input=${updateParam}
                                      @change=${updateParam}
                                      >${suffixBtn}</vaadin-text-field
                                    >`
                              }
                              ${this.renderParameterVariableControls(
                                template.scriptPath,
                                parsed.arguments,
                                param.name,
                                currentVal,
                                parsed.isJsonPath
                              )}
                            </div>`;
                          })}
                        </div>
                      `
                    : nothing
                }
                ${extraArgs.map((arg, extraIdx) => {
                  const rawIndex = parsed.arguments.indexOf(arg);
                  const updateKey = (event: Event) => {
                    const nextArgs = [...parsed.arguments];
                    nextArgs[rawIndex] = {
                      key: (event.currentTarget as HTMLInputElement).value,
                      value: arg.value
                    };
                    this.change({
                      ScriptPath: serializeScriptPathValue(
                        template?.scriptPath ?? parsed.scriptPath,
                        nextArgs,
                        true
                      )
                    });
                  };
                  const updateValue = (event: Event) => {
                    const nextArgs = [...parsed.arguments];
                    nextArgs[rawIndex] = {
                      key: arg.key,
                      value: (event.currentTarget as HTMLInputElement).value
                    };
                    this.change({
                      ScriptPath: serializeScriptPathValue(
                        template?.scriptPath ?? parsed.scriptPath,
                        nextArgs,
                        true
                      )
                    });
                  };
                  return html`<div class="custom-arg-row">
                    <vaadin-text-field
                      label="Additional argument name"
                      .value=${live(arg.key)}
                      .readonly=${this.readonly}
                      @input=${updateKey}
                      @change=${updateKey}
                    ></vaadin-text-field>
                    <div class="param-cell">
                      <vaadin-text-field
                        label="Argument value"
                        .value=${live(arg.value)}
                        .readonly=${this.readonly}
                        @focus=${() => {
                          this.activeParameterKey = arg.key;
                        }}
                        @input=${updateValue}
                        @change=${updateValue}
                      >
                        ${
                          !this.readonly && arg.key
                            ? html`<button
                                type="button"
                                slot="suffix"
                                class="param-bind-suffix-btn"
                                title="Bind variable (\${VariableName}) to ${arg.key}"
                                aria-label="Bind variable to ${arg.key}"
                                @click=${(e: Event) => {
                                  e.stopPropagation();
                                  this.toggleParameterPicker(arg.key);
                                }}
                              >
                                ${'{x}'}
                              </button>`
                            : nothing
                        }
                      </vaadin-text-field>
                      ${this.renderParameterVariableControls(
                        template?.scriptPath ?? parsed.scriptPath,
                        parsed.arguments,
                        arg.key,
                        arg.value,
                        true
                      )}
                    </div>
                    <vaadin-button
                      theme="tertiary"
                      .disabled=${this.readonly}
                      aria-label="Remove argument ${extraIdx + 1}"
                      @click=${() => {
                        const nextArgs = parsed.arguments.filter(
                          (_, idx) => idx !== rawIndex
                        );
                        this.change({
                          ScriptPath: serializeScriptPathValue(
                            template?.scriptPath ?? parsed.scriptPath,
                            nextArgs
                          )
                        });
                      }}
                      >Remove</vaadin-button
                    >
                  </div>`;
                })}
                ${
                  template?.requiredEnvironmentProperties?.length
                    ? html`<p class="env-props">
                        Consumes DOrc environment properties:
                        <code
                          >${template.requiredEnvironmentProperties.join(
                            ', '
                          )}</code
                        >
                      </p>`
                    : nothing
                }
              </div>
            `
          : nothing
      }
    `;
  }

  render() {
    const component = this.component;
    if (!component)
      return html`<p>Select a component to inspect its definition.</p>`;
    const terraform = component.ComponentType === ComponentType.Terraform;
    const catalog =
      terraform &&
      component.TerraformSourceType === TerraformSourceType.Catalog;
    const sourceType =
      component.TerraformSourceType ?? TerraformSourceType.SharedFolder;
    return html`
      <div class="fields">
        ${this.text('Component name', 'ComponentName', catalog ? 'State identity is stable. Catalog renames require an operator-reviewed migration.' : 'Unique component name.', catalog)}
        <vaadin-combo-box
          label="Execution type"
          .items=${Object.values(ComponentType)}
          .value=${live(component.ComponentType ?? ComponentType.PowerShell)}
          .disabled=${this.readonly || Boolean(component.ComponentId)}
          helper-text="An existing execution identity is not converted to another type here."
          @value-changed=${(event: CustomEvent<{ value: string }>) => {
            const value =
              event.detail?.value ??
              (event.currentTarget as HTMLInputElement).value;
            if (
              (value === ComponentType.PowerShell ||
                value === ComponentType.Terraform) &&
              value !== (component.ComponentType ?? ComponentType.PowerShell)
            ) {
              this.change({ ComponentType: value });
            }
          }}
          @input=${(event: Event) => {
            const value = (event.currentTarget as HTMLInputElement).value;
            if (
              value === ComponentType.PowerShell ||
              value === ComponentType.Terraform
            ) {
              this.change({ ComponentType: value });
            }
          }}
          @change=${(event: Event) => {
            const value = (event.currentTarget as HTMLInputElement).value;
            if (
              value === ComponentType.PowerShell ||
              value === ComponentType.Terraform
            ) {
              this.change({ ComponentType: value });
            }
          }}
        ></vaadin-combo-box>
        ${
          terraform
            ? html`
                <vaadin-combo-box
                  label="Terraform source"
                  .items=${Object.values(TerraformSourceType).filter(source => catalog || source !== TerraformSourceType.Catalog)}
                  .value=${live(sourceType)}
                  .disabled=${this.readonly || catalog}
                  @value-changed=${(event: CustomEvent<{ value: string }>) => {
                    const value =
                      event.detail?.value ??
                      (event.currentTarget as HTMLInputElement).value;
                    const source = Object.values(TerraformSourceType).find(
                      item => item === value
                    );
                    if (source && source !== sourceType) {
                      this.change({ TerraformSourceType: source });
                    }
                  }}
                  @input=${(event: Event) => {
                    const value = (event.currentTarget as HTMLInputElement)
                      .value;
                    const source = Object.values(TerraformSourceType).find(
                      item => item === value
                    );
                    if (source) this.change({ TerraformSourceType: source });
                  }}
                  @change=${(event: Event) => {
                    const value = (event.currentTarget as HTMLInputElement)
                      .value;
                    const source = Object.values(TerraformSourceType).find(
                      item => item === value
                    );
                    if (source) this.change({ TerraformSourceType: source });
                  }}
                ></vaadin-combo-box>
                ${this.text('Source subpath', 'TerraformSubPath', 'Relative path inside the selected source.')}
              `
            : html`
                <vaadin-combo-box
                  label="PowerShell version"
                  .items=${this.powerShellVersions}
                  .value=${live(component.PSVersion ?? '')}
                  .disabled=${this.readonly}
                  @value-changed=${(event: CustomEvent<{ value: string }>) => {
                    const value =
                      event.detail?.value ??
                      (event.currentTarget as HTMLInputElement).value;
                    if (value && value !== (component.PSVersion ?? '')) {
                      this.change({ PSVersion: value });
                    }
                  }}
                  @input=${(event: Event) =>
                    this.change({
                      PSVersion: (event.currentTarget as HTMLInputElement).value
                    })}
                  @change=${(event: Event) =>
                    this.change({
                      PSVersion: (event.currentTarget as HTMLInputElement).value
                    })}
                ></vaadin-combo-box>
                ${this.renderPowerShellGenericSection(component)}
              `
        }
        ${
          !terraform || sourceType === TerraformSourceType.SharedFolder
            ? html`<div class="full">
                ${
                  terraform
                    ? this.text(
                        'Shared folder path',
                        'ScriptPath',
                        'Existing paths and JSON argument definitions are preserved. Use secure property references for secrets.'
                      )
                    : this.renderScriptPathField(component)
                }
              </div>`
            : nothing
        }
        ${
          terraform && sourceType === TerraformSourceType.Git
            ? html`
                ${this.text('Git ref', 'TerraformGitBranch', 'The requested ref is recorded separately from the commit actually planned.')}
                <div class="full">
                  <p>
                    Repository inherited from this project:
                    <code
                      >${this.project?.TerraformGitRepoUrl || 'Not configured'}</code
                    >. Edit project source settings below or in Advanced JSON.
                  </p>
                </div>
              `
            : nothing
        }
        ${
          catalog
            ? html`
                <div class="full">
                  <p>
                    Catalog module:
                    <strong
                      >${component.TerraformTemplateName}@${component.TerraformTemplateVersion}</strong
                    >.
                  </p>
                  <p>
                    Use the module catalog to plan this version or explicitly
                    upgrade this component. Editing JSON cannot bypass upgrade
                    checks.
                  </p>
                </div>
              `
            : nothing
        }
        ${
          terraform &&
          (sourceType === TerraformSourceType.AzureArtifact ||
            sourceType === TerraformSourceType.GitHubArtifact)
            ? html`
                <div class="full">
                  <p>
                    Artifact source inherited from this project:
                    <code
                      >${this.project?.ArtefactsUrl || 'Not configured'}</code
                    >. Select a concrete build or run when deploying; history
                    records the submitted and executed source.
                  </p>
                </div>
              `
            : nothing
        }
      </div>
      <div class="flags">
        <vaadin-checkbox
          label="Enabled"
          .checked=${component.IsEnabled !== false}
          .disabled=${this.readonly}
          @change=${(event: Event) => this.change({ IsEnabled: (event.currentTarget as HTMLInputElement).checked })}
        ></vaadin-checkbox>
        <vaadin-checkbox
          label="Stop on failure"
          .checked=${component.StopOnFailure === true}
          .disabled=${this.readonly}
          @change=${(event: Event) => this.change({ StopOnFailure: (event.currentTarget as HTMLInputElement).checked })}
        ></vaadin-checkbox>
        ${
          !terraform
            ? html`<vaadin-checkbox
                label="Non-production only"
                .checked=${component.NonProdOnly === true}
                .disabled=${this.readonly}
                @change=${(event: Event) => this.change({ NonProdOnly: (event.currentTarget as HTMLInputElement).checked })}
              ></vaadin-checkbox>`
            : nothing
        }
      </div>
    `;
  }
}
