import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/number-field';
import '@vaadin/password-field';
import '@vaadin/checkbox';
import '@vaadin/radio-group';
import '@vaadin/radio-group/vaadin-radio-button';
import '@vaadin/dialog';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';
import { dialogRenderer, dialogFooterRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, LitElement, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { forkJoin } from 'rxjs';
import { navigate } from '../router/router';
import {
  CloudResourceApiModel,
  ComponentApiModel,
  ComponentType,
  EnvironmentApiModel,
  ProjectApiModel,
  RefDataCloudResourcesApi,
  RefDataComponentsApi,
  RefDataProjectsApi,
  RefDataProjectEnvironmentMappingsApi,
  TerraformApi,
  TerraformParameterResolutionStatus,
  TerraformSourceType,
  TerraformTemplateManifest,
  TerraformTemplateParameter,
  TerraformReadinessApiModel,
  CatalogEnvironmentInputsApiModel,
  CatalogUpgradePreviewApiModel
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import {
  resourceGroupNameFromArmId,
  sanitizeTerraformStateKeyPart
} from '../helpers/terraform-identity';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

export interface TemplateDeploymentContext {
  projectName?: string;
  environmentName?: string;
  componentId?: number;
}

const STEPS = [
  { title: 'Target', hint: 'Project, environment and component' },
  { title: 'Inputs', hint: 'Override only what should differ' },
  { title: 'Review & submit', hint: 'Generates a plan, applies nothing' }
] as const;

// Inspect decimal digits rather than IEEE-754 rounding (e.g. 2.0000000000000001).
const isWholeNumber = (value: string): boolean => {
  const [coefficient, exponent = '0'] = value.toLowerCase().split('e');
  const fractionLength = coefficient.split('.')[1]?.length ?? 0;
  const digits = coefficient.replace(/[-.]/g, '');
  const significant = digits.replace(/0+$/, '');
  return (
    significant.length === 0 ||
    Number(exponent) + digits.length - significant.length >= fractionLength
  );
};

/**
 * Validates one override against the manifest's constraints. Mirrors the
 * server-side ParameterValidator so a typo is caught before the request is
 * submitted; the server remains the authority.
 */
export const overrideError = (
  p: TerraformTemplateParameter,
  value: string
): string | null => {
  if (value === '') {
    if (p.Required) return 'Enter a value, or remove this request override.';
    if (p.Type !== 'Number' && p.Type !== 'Bool') return null;
  }
  if (p.AllowedValues?.length && !p.AllowedValues.includes(value)) {
    return `Choose one of: ${p.AllowedValues.join(', ')}.`;
  }
  if (p.Type === 'Number') {
    const number = Number(value);
    if (
      !/^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$/.test(value) ||
      value.trim() !== value ||
      !Number.isFinite(number)
    )
      return 'Must be a finite number.';
    if (p.Integer && !isWholeNumber(value)) return 'Must be an integer.';
    if (p.Min != null && number < p.Min) return `Must be at least ${p.Min}.`;
    if (p.Max != null && number > p.Max) return `Must be at most ${p.Max}.`;
  }
  if (p.Type === 'Bool' && value !== 'true' && value !== 'false')
    return 'Must be true or false.';
  if (p.Pattern) {
    try {
      if (!new RegExp(p.Pattern).test(value)) {
        return `Must match the pattern ${p.Pattern}.`;
      }
    } catch {
      // An unparseable manifest pattern is validated server-side instead.
    }
  }
  return null;
};

@customElement('deploy-from-template-dialog')
export class DeployFromTemplateDialog extends LitElement {
  static styles = css`
    :host {
      display: contents;
    }
    .wizard {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-l);
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .rail {
      flex: 1 1 220px;
      max-width: 280px;
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-l);
      padding: var(--lumo-space-m);
      background: var(--dorc-bg-secondary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-l);
      box-sizing: border-box;
    }
    @media (max-width: 768px) {
      .rail {
        max-width: none;
      }
    }
    .module {
      display: flex;
      align-items: center;
      gap: var(--lumo-space-s);
    }
    .module-icon {
      width: 36px;
      height: 36px;
      border-radius: var(--lumo-border-radius-m);
      background: var(--lumo-primary-color-10pct);
      color: var(--lumo-primary-text-color);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .module-name {
      font-weight: 700;
    }
    .module-version {
      font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    ol.steps {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .step {
      display: flex;
      gap: var(--lumo-space-s);
      align-items: flex-start;
      padding: var(--lumo-space-s);
      border-radius: var(--lumo-border-radius-m);
      border: 1px solid transparent;
    }
    .step[data-status='current'] {
      background: var(--dorc-bg-primary);
      border-color: var(--dorc-border-color);
    }
    .step-marker {
      width: 26px;
      height: 26px;
      border-radius: 999px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: var(--lumo-font-size-s);
      font-weight: 700;
      flex-shrink: 0;
      box-sizing: border-box;
      border: 2px solid var(--lumo-contrast-30pct);
      color: var(--dorc-text-secondary-strong);
    }
    .step[data-status='current'] .step-marker {
      background: var(--lumo-primary-color);
      border-color: var(--lumo-primary-color);
      color: var(--lumo-primary-contrast-color);
    }
    .step[data-status='done'] .step-marker {
      background: var(--lumo-success-color);
      border-color: var(--lumo-success-color);
      color: var(--lumo-success-contrast-color, #fff);
    }
    .step-marker vaadin-icon {
      width: 14px;
      height: 14px;
    }
    .step-title {
      font-weight: 600;
      font-size: var(--lumo-font-size-s);
    }
    .step[data-status='upcoming'] .step-title {
      color: var(--dorc-text-secondary-strong);
    }
    .step-hint {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
      line-height: 1.4;
    }
    .identity {
      margin-top: auto;
      padding: var(--lumo-space-s) var(--lumo-space-m);
      background: var(--dorc-bg-primary);
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    .identity-label {
      font-weight: 700;
      letter-spacing: 0.08em;
      text-transform: uppercase;
    }
    .identity-path {
      font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
      color: var(--dorc-text-primary);
      font-size: var(--lumo-font-size-s);
    }
    .content {
      flex: 999 1 360px;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-m);
    }
    h2 {
      margin: 0;
      font-size: var(--lumo-font-size-xl);
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .lead {
      margin: 0;
      color: var(--dorc-text-secondary-strong);
      line-height: 1.5;
    }
    .lead a {
      color: var(--dorc-link-color);
      font-weight: 600;
      text-decoration: none;
    }
    .alert {
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border-radius: var(--lumo-border-radius-m);
      background: var(--lumo-error-color-10pct);
      color: var(--lumo-error-text-color);
      display: flex;
      gap: var(--lumo-space-s);
      align-items: flex-start;
    }
    .notice {
      padding: var(--lumo-space-s) var(--lumo-space-m);
      border-radius: var(--lumo-border-radius-m);
      background: var(--dorc-warning-bg);
      color: var(--dorc-warning-text);
    }
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-s);
    }
    .form vaadin-combo-box,
    .form vaadin-text-field {
      width: 100%;
    }
    .group {
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-s);
    }
    .group-head {
      display: flex;
      align-items: center;
      gap: var(--lumo-space-s);
      font-size: var(--lumo-font-size-xs);
      font-weight: 700;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--dorc-text-secondary-strong);
    }
    .group-head.required {
      color: var(--lumo-error-text-color);
    }
    .group-head .rule {
      flex: 1;
      height: 1px;
      background: var(--dorc-border-color);
    }
    .group-head .count {
      font-weight: 500;
      letter-spacing: 0;
      text-transform: none;
      color: var(--dorc-text-secondary-strong);
    }
    .params {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 260px), 1fr));
      gap: var(--lumo-space-s);
    }
    .param {
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
      background: var(--dorc-bg-primary);
      min-width: 0;
    }
    .param[data-overridden='true'] {
      border-color: var(--lumo-primary-color);
      box-shadow: 0 0 0 3px var(--lumo-primary-color-10pct);
    }
    .param[data-invalid='true'],
    .param[data-missing='true'] {
      border-color: var(--lumo-error-color);
      box-shadow: 0 0 0 3px var(--lumo-error-color-10pct);
    }
    .param[data-sensitive='true'] {
      background: var(--dorc-warning-bg);
    }
    .param[data-sensitive='true'][data-overridden='false'] {
      border-color: var(--dorc-warning-text);
    }
    .param-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--lumo-space-s);
      flex-wrap: wrap;
    }
    .param-name {
      font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
      font-weight: 600;
      font-size: var(--lumo-font-size-s);
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .param-name vaadin-icon {
      width: 14px;
      height: 14px;
      color: var(--dorc-warning-text);
    }
    .badge {
      font-size: 11px;
      font-weight: 700;
      border-radius: 999px;
      padding: 2px 9px;
      white-space: nowrap;
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    .badge vaadin-icon {
      width: 10px;
      height: 10px;
    }
    .badge-inherit {
      background: var(--lumo-success-color-10pct);
      color: var(--lumo-success-text-color);
    }
    .badge-default {
      background: var(--lumo-contrast-10pct);
      color: var(--dorc-text-secondary-strong);
    }
    .badge-override {
      background: var(--lumo-primary-color-10pct);
      color: var(--lumo-primary-text-color);
    }
    .badge-invalid {
      background: var(--lumo-error-color-10pct);
      color: var(--lumo-error-text-color);
    }
    .badge-sensitive {
      background: var(--dorc-bg-primary);
      color: var(--dorc-warning-text);
      border: 1px solid var(--dorc-warning-text);
    }
    .param-desc {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
      line-height: 1.4;
    }
    .param-source {
      display: flex;
      align-items: center;
      gap: var(--lumo-space-s);
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-secondary-strong);
      flex-wrap: wrap;
    }
    .param-source vaadin-button,
    .param-field vaadin-button {
      margin-left: auto;
    }
    .param-field {
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
    }
    .param-field vaadin-text-field,
    .param-field vaadin-number-field,
    .param-field vaadin-password-field,
    .param-field vaadin-combo-box {
      width: 100%;
    }
    .field-error {
      font-size: var(--lumo-font-size-xs);
      color: var(--lumo-error-text-color);
    }
    .field-ok {
      font-size: var(--lumo-font-size-xs);
      color: var(--lumo-success-text-color);
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    .summary {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 170px), 1fr));
      gap: var(--lumo-space-s);
    }
    .summary-card {
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      display: flex;
      flex-direction: column;
      gap: 2px;
      background: var(--dorc-bg-primary);
      min-width: 0;
    }
    .summary-label {
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--dorc-text-secondary-strong);
    }
    .summary-value {
      font-weight: 600;
    }
    .summary-sub {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--lumo-font-size-s);
    }
    th {
      text-align: left;
      padding: 8px var(--lumo-space-s);
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--dorc-text-secondary-strong);
      border-bottom: 1px solid var(--dorc-border-color);
    }
    td {
      padding: 8px var(--lumo-space-s);
      border-bottom: 1px solid var(--dorc-border-color);
      vertical-align: top;
    }
    td.name {
      font-family: var(--dorc-mono-font, ui-monospace, Menlo, monospace);
      font-weight: 600;
    }
    td.muted {
      color: var(--dorc-text-secondary-strong);
    }
    tr[data-overridden='true'] td {
      background: var(--lumo-primary-color-10pct);
    }
    .footer-status {
      flex: 1;
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-secondary-strong);
      display: inline-flex;
      align-items: center;
      gap: 6px;
      min-width: 0;
    }
    .footer-status.invalid {
      color: var(--lumo-error-text-color);
    }
    .footer-status vaadin-icon {
      width: 14px;
      height: 14px;
    }
  `;

  @property({ type: Boolean }) opened = false;
  @property({ attribute: false }) template: TerraformTemplateManifest | null =
    null;
  @state() private projects: ProjectApiModel[] = [];
  @state() private environments: EnvironmentApiModel[] = [];
  @state() private components: ComponentApiModel[] = [];
  @state() private selectedProject: ProjectApiModel | null = null;
  @state() private selectedComponent: ComponentApiModel | null = null;
  @state() private selectedEnvironmentName = '';
  @state() private componentName = '';
  @state() private createNew = true;
  @state() private paramValues: Record<string, string> = {};
  @state() private step = 0;
  @state() private projectsLoading = false;
  @state() private targetLoading = false;
  @state() private submitting = false;
  @state() private error: string | null = null;
  /**
   * Where each input resolves from for the chosen target, by parameter name.
   * Null until the Inputs step has asked the server; never carries values.
   */
  @state() private resolution: Record<
    string,
    TerraformParameterResolutionStatus
  > | null = null;
  @state() private resolutionLoading = false;
  @state() private resolutionError: string | null = null;
  @state() private readiness: TerraformReadinessApiModel | null = null;
  @state() private savedConfiguration: CatalogEnvironmentInputsApiModel | null =
    null;
  private submissionIdentity: { payload: string; key: string } | null = null;
  @state() private upgradePreview: CatalogUpgradePreviewApiModel | null = null;
  @state() private upgradeImpactConfirmed = false;
  @state() private preserveExistingService = false;
  @state() private secureInputNames: string[] = [];

  private projectsApi = new RefDataProjectsApi(dorcApiConfiguration);
  private componentsApi = new RefDataComponentsApi(dorcApiConfiguration);
  private envMappingsApi = new RefDataProjectEnvironmentMappingsApi(
    dorcApiConfiguration
  );
  private terraformApi = new TerraformApi(dorcApiConfiguration);
  private cloudResourcesApi = new RefDataCloudResourcesApi(
    dorcApiConfiguration
  );
  private requestToken = 0;
  private resolutionToken = 0;
  private context: TemplateDeploymentContext = {};

  open(
    template: TerraformTemplateManifest,
    context: TemplateDeploymentContext = {}
  ) {
    if (this.submitting) return;
    const token = ++this.requestToken;
    this.template = template;
    this.secureInputNames = [];
    this.upgradePreview = null;
    this.context = context;
    this.componentName = template.Name;
    this.selectedProject = null;
    this.selectedComponent = null;
    this.selectedEnvironmentName = '';
    this.projects = [];
    this.environments = [];
    this.components = [];
    this.createNew = true;
    this.paramValues = {};
    this.step = 0;
    this.error = null;
    this.clearResolution();
    this.targetLoading = false;
    this.projectsLoading = true;
    this.opened = true;
    this.projectsApi.refDataProjectsGet().subscribe({
      next: data => {
        if (token !== this.requestToken || !this.opened) return;
        this.projects = [...(data ?? [])].sort((a, b) =>
          (a.ProjectName ?? '').localeCompare(b.ProjectName ?? '')
        );
        this.projectsLoading = false;
        const project = this.projects.find(
          p => p.ProjectName === context.projectName
        );
        if (project) this.onProjectChange(project);
      },
      error: err => {
        if (token !== this.requestToken || !this.opened) return;
        this.projectsLoading = false;
        this.error =
          retrieveErrorMessage(err) ??
          'Failed to load projects. Close and try again.';
      }
    });
  }

  private onProjectChange(project: ProjectApiModel | null) {
    if (this.selectedProject === project) return;
    const token = ++this.requestToken;
    this.selectedProject = project;
    this.upgradePreview = null;
    this.selectedComponent = null;
    this.selectedEnvironmentName = '';
    this.environments = [];
    this.components = [];
    this.paramValues = {};
    this.createNew = true;
    this.error = null;
    this.clearResolution();
    this.targetLoading = !!project?.ProjectName;
    if (!project?.ProjectName) return;
    forkJoin({
      environments: this.envMappingsApi.refDataProjectEnvironmentMappingsGet({
        project: project.ProjectName,
        includeRead: false
      }),
      components: this.componentsApi.refDataComponentsGet({
        id: project.ProjectName
      })
    }).subscribe({
      next: data => {
        if (token !== this.requestToken || !this.opened) return;
        this.environments = [...(data.environments.Items ?? [])].sort((a, b) =>
          (a.EnvironmentName ?? '').localeCompare(b.EnvironmentName ?? '')
        );
        this.components = data.components.Items ?? [];
        this.targetLoading = false;
        this.createNew = this.reusableComponents.length === 0;
        this.selectedComponent = this.reusableComponents[0] ?? null;
        if (
          this.context.componentId &&
          project.ProjectName === this.context.projectName
        ) {
          this.createNew = false;
          this.selectedComponent =
            this.reusableComponents.find(
              component => component.ComponentId === this.context.componentId
            ) ?? null;
          if (!this.selectedComponent) {
            this.error =
              'The linked component is unavailable, disabled or pinned to another module version. Reload its definition or explicitly select an upgrade; no replacement component has been selected.';
          }
        }
        const environment = this.environments.find(
          e => e.EnvironmentName === this.context.environmentName
        );
        this.selectedEnvironmentName = environment?.EnvironmentName ?? '';
      },
      error: err => {
        if (token !== this.requestToken || !this.opened) return;
        this.targetLoading = false;
        this.error =
          retrieveErrorMessage(err) ??
          'Failed to load project targets. Select the project again.';
      }
    });
  }

  private get reusableComponents() {
    return this.components.filter(
      c =>
        c.ComponentType === ComponentType.Terraform &&
        c.TerraformSourceType === TerraformSourceType.Catalog &&
        c.TerraformTemplateName?.toLowerCase() ===
          this.template?.Name.toLowerCase() &&
        c.TerraformTemplateVersion === this.template?.Version &&
        c.IsEnabled !== false
    );
  }

  private get upgradeCandidates() {
    return this.components.filter(
      component =>
        component.ComponentType === ComponentType.Terraform &&
        component.TerraformSourceType === TerraformSourceType.Catalog &&
        component.TerraformTemplateName === this.template?.Name &&
        component.TerraformTemplateVersion !== this.template?.Version &&
        component.IsEnabled !== false
    );
  }

  private get targetComponentName() {
    return this.createNew
      ? this.componentName.trim()
      : (this.selectedComponent?.ComponentName ?? '');
  }

  private get parameters(): TerraformTemplateParameter[] {
    return (this.template?.Parameters ?? []).map(parameter => ({
      ...parameter,
      Sensitive:
        parameter.Sensitive || this.secureInputNames.includes(parameter.Name)
    }));
  }

  private clearResolution() {
    this.resolutionToken += 1;
    this.resolution = null;
    this.readiness = null;
    this.savedConfiguration = null;
    this.resolutionLoading = false;
    this.resolutionError = null;
  }

  /**
   * Prefills resource_group_name from the environment's Cloud tab when the
   * template declares that input and the environment carries exactly one
   * resource-group ARM identity. Only unresolved inputs are eligible; inherited
   * values and defaults are never fetched or shadowed. The value lands as an
   * editable override; failure or ambiguity leaves the input unchanged.
   */
  private prefillFromEnvironmentCloudResources() {
    const paramName = 'resource_group_name';
    const environmentName = this.selectedEnvironmentName;
    const status = this.resolution?.[paramName];
    const envId = this.environments.find(
      e => e.EnvironmentName === environmentName
    )?.EnvironmentId;
    if (
      !envId ||
      (status !== TerraformParameterResolutionStatus.Missing &&
        status !== TerraformParameterResolutionStatus.Unset) ||
      !this.parameters.some(p => p.Name === paramName) ||
      this.paramValues[paramName] !== undefined
    ) {
      return;
    }
    const token = this.requestToken;
    const resolutionToken = this.resolutionToken;
    this.cloudResourcesApi
      .refDataCloudResourcesByEnvIdEnvIdGet({ envId })
      .subscribe({
        next: (resources: CloudResourceApiModel[]) => {
          if (
            token !== this.requestToken ||
            resolutionToken !== this.resolutionToken ||
            !this.opened ||
            environmentName !== this.selectedEnvironmentName ||
            this.paramValues[paramName] !== undefined
          ) {
            return;
          }
          const isResourceGroup = (r: CloudResourceApiModel) => {
            const type = (r.ResourceType ?? '')
              .toLowerCase()
              .replace(/[^a-z0-9]/g, '');
            return type === 'resourcegroup' || type === 'azurermresourcegroup';
          };
          const groups = (resources ?? []).filter(isResourceGroup);
          const groupName =
            groups.length === 1
              ? resourceGroupNameFromArmId(groups[0].ResourceIdentifier)
              : null;
          if (groupName) {
            this.paramValues = {
              ...this.paramValues,
              [paramName]: groupName
            };
          }
        },
        error: () => {
          // Prefill is a convenience; the user can always type the name.
        }
      });
  }

  /**
   * Asks the server where each input would resolve from for the chosen
   * target. Names and statuses only: the point is to show "Missing" before
   * submission, not to pull environment values into the browser.
   */
  private loadResolution() {
    if (!this.template || !this.selectedProject?.ProjectId) return;
    const token = ++this.resolutionToken;
    this.resolutionLoading = true;
    this.resolutionError = null;
    this.terraformApi
      .terraformTemplateResolutionGet({
        name: this.template.Name,
        version: this.template.Version,
        projectId: this.selectedProject.ProjectId,
        environmentName: this.selectedEnvironmentName,
        componentId: this.createNew
          ? undefined
          : (this.selectedComponent?.ComponentId ?? undefined)
      })
      .subscribe({
        next: data => {
          if (token !== this.resolutionToken || !this.opened) return;
          this.resolution = Object.fromEntries(
            (data?.Parameters ?? []).map(r => [r.Name, r.Status])
          );
          this.secureInputNames = (data?.Parameters ?? [])
            .filter(parameter => parameter.Sensitive)
            .map(parameter => parameter.Name);
          this.readiness = data?.Readiness ?? null;
          this.savedConfiguration = data?.SavedConfiguration ?? null;
          this.resolutionLoading = false;
          this.prefillFromEnvironmentCloudResources();
        },
        error: err => {
          if (token !== this.resolutionToken || !this.opened) return;
          this.resolutionLoading = false;
          this.resolutionError =
            retrieveErrorMessage(err) ??
            'The environment could not be checked.';
        }
      });
  }

  private statusOf(
    p: TerraformTemplateParameter
  ): TerraformParameterResolutionStatus | null {
    return this.resolution?.[p.Name] ?? null;
  }

  private sourceLabel(p: TerraformTemplateParameter): string {
    if (this.resolutionLoading) return 'Checking';
    if (this.resolutionError) return 'Not checked';
    switch (this.statusOf(p)) {
      case TerraformParameterResolutionStatus.Environment:
        return 'DOrc-resolved';
      case TerraformParameterResolutionStatus.Default:
        return 'Module default';
      case TerraformParameterResolutionStatus.SavedConfiguration:
        return 'Saved component configuration';
      case TerraformParameterResolutionStatus.Missing:
        return 'Missing';
      case TerraformParameterResolutionStatus.Unset:
        return 'Not set';
      default:
        return 'Not checked';
    }
  }

  private get inputSourceSummary(): string {
    const counts = new Map<string, number>();
    for (const parameter of this.parameters) {
      const label =
        parameter.Name in this.paramValues
          ? 'Override'
          : this.sourceLabel(parameter);
      counts.set(label, (counts.get(label) ?? 0) + 1);
    }
    return (
      [...counts].map(([label, count]) => `${label}: ${count}`).join(' · ') ||
      'No inputs'
    );
  }

  /** Required inputs the target cannot supply and the user has not overridden. */
  private get missingInputs(): string[] {
    return this.parameters
      .filter(
        p =>
          !(p.Name in this.paramValues) &&
          this.statusOf(p) === TerraformParameterResolutionStatus.Missing
      )
      .map(p => p.Name);
  }

  /** Overrides that currently fail validation, by parameter name. */
  private get invalidOverrides(): string[] {
    return this.parameters
      .filter(
        p =>
          p.Name in this.paramValues &&
          overrideError(p, this.paramValues[p.Name]) !== null
      )
      .map(p => p.Name);
  }

  private get stateIdentity(): string | null {
    const project = this.selectedProject?.ProjectName;
    const component = this.targetComponentName;
    const environment = this.selectedEnvironmentName;
    if (!project || !component || !environment) return null;
    return `${[project, component, environment].map(sanitizeTerraformStateKeyPart).join('/')}.tfstate`;
  }

  private targetError(): string | null {
    if (this.projectsLoading || this.targetLoading)
      return 'Wait for the project targets to load.';
    if (!this.selectedProject?.ProjectId) return 'Select a project.';
    if (
      !this.environments.some(
        e => e.EnvironmentName === this.selectedEnvironmentName
      )
    )
      return 'Select a mapped environment you can deploy to.';
    if (!this.targetComponentName)
      return 'Choose an existing component or enter a new component name.';
    if (
      this.createNew &&
      this.components.some(
        c =>
          c.ComponentName?.toLowerCase() ===
          this.targetComponentName.toLowerCase()
      )
    )
      return 'That component already exists. Reuse it, or choose a different name for separate infrastructure.';
    return null;
  }

  private inputError(): string | null {
    const missing = this.missingInputs;
    if (missing.length) {
      return `${missing.join(', ')}: not set in ${this.selectedEnvironmentName}. Override ${missing.length === 1 ? 'it' : 'them'} for this request, or add the environment propert${missing.length === 1 ? 'y' : 'ies'}.`;
    }
    for (const p of this.parameters) {
      // Omitted inputs are resolved on the server, never fetched into the browser.
      if (!(p.Name in this.paramValues)) continue;
      const message = overrideError(p, this.paramValues[p.Name]);
      if (message) return `${p.Name}: ${message}`;
    }
    return null;
  }

  private setOverride(parameter: TerraformTemplateParameter, enabled: boolean) {
    if (enabled) {
      this.paramValues = {
        ...this.paramValues,
        [parameter.Name]: parameter.Sensitive
          ? ''
          : (parameter.Default ?? (parameter.Type === 'Bool' ? 'false' : ''))
      };
    } else {
      const values = { ...this.paramValues };
      delete values[parameter.Name];
      this.paramValues = values;
    }
    this.error = null;
  }

  private setParamValue(name: string, value: string) {
    this.paramValues = { ...this.paramValues, [name]: value };
  }

  render() {
    return html`
      <vaadin-dialog
        .opened=${this.opened}
        .noCloseOnEsc=${this.submitting}
        .noCloseOnOutsideClick=${this.submitting}
        @opened-changed=${(e: CustomEvent<{ value: boolean }>) => {
          if (!e.detail.value && !this.submitting) this.close();
        }}
        header-title="Plan deployment"
        theme="wide"
        ${dialogRenderer(this.bodyRenderer, [
          this.template,
          this.projects,
          this.environments,
          this.components,
          this.selectedProject,
          this.selectedComponent,
          this.selectedEnvironmentName,
          this.componentName,
          this.createNew,
          this.paramValues,
          this.step,
          this.projectsLoading,
          this.targetLoading,
          this.submitting,
          this.error,
          this.resolution,
          this.resolutionLoading,
          this.resolutionError,
          this.readiness,
          this.savedConfiguration,
          this.secureInputNames,
          this.upgradePreview,
          this.upgradeImpactConfirmed,
          this.preserveExistingService
        ])}
        ${dialogFooterRenderer(this.footerRenderer, [
          this.template,
          this.paramValues,
          this.step,
          this.submitting,
          this.projectsLoading,
          this.targetLoading,
          this.resolution,
          this.resolutionLoading,
          this.resolutionError,
          this.selectedEnvironmentName,
          this.secureInputNames
        ])}
      ></vaadin-dialog>
    `;
  }

  private bodyRenderer = () => html`
    <div class="wizard" aria-busy=${this.submitting}>
      ${this.railRenderer()}
      <div class="content">
        <div role="status" aria-live="polite">
          <h2>${STEPS[this.step].title}</h2>
        </div>
        ${
          this.error
            ? html`<div class="alert" role="alert">
                <vaadin-icon
                  icon="vaadin:exclamation-circle-o"
                  style="width:16px;height:16px;flex-shrink:0;margin-top:2px"
                ></vaadin-icon>
                <span>${this.error}</span>
              </div>`
            : nothing
        }
        ${this.step === 0 ? this.targetRenderer() : this.step === 1 ? this.inputsRenderer() : this.reviewRenderer()}
        ${this.step > 0 ? this.readinessRenderer() : nothing}
      </div>
    </div>
  `;

  private railRenderer() {
    const status = (index: number) =>
      index < this.step ? 'done' : index === this.step ? 'current' : 'upcoming';
    const summaries = [
      this.step > 0
        ? `${this.selectedProject?.ProjectName} · ${this.selectedEnvironmentName} · ${this.createNew ? 'new' : 'reuse'} ${this.targetComponentName}`
        : STEPS[0].hint,
      this.step > 1
        ? this.inputSourceSummary
        : this.step === 1 && this.resolution
          ? this.inputSourceSummary
          : STEPS[1].hint,
      STEPS[2].hint
    ];
    const identity = this.stateIdentity;
    return html`
      <aside class="rail" aria-label="Progress">
        <div class="module">
          <div class="module-icon">
            <vaadin-icon
              icon="vaadin:puzzle-piece"
              style="width:18px;height:18px"
            ></vaadin-icon>
          </div>
          <div style="min-width:0">
            <div class="module-name">${this.template?.Name}</div>
            <div class="module-version">v${this.template?.Version}</div>
          </div>
        </div>
        <ol class="steps">
          ${STEPS.map(
            (s, i) => html`
              <li
                class="step"
                data-status=${status(i)}
                aria-current=${i === this.step ? 'step' : 'false'}
              >
                <div class="step-marker">
                  ${
                    status(i) === 'done'
                      ? html`<vaadin-icon icon="vaadin:check"></vaadin-icon>`
                      : i + 1
                  }
                </div>
                <div style="min-width:0">
                  <div class="step-title">${s.title}</div>
                  <div class="step-hint">${summaries[i]}</div>
                </div>
              </li>
            `
          )}
        </ol>
        ${
          identity
            ? html`<div class="identity">
                <span class="identity-label">State identity</span>
                <span class="identity-path">${identity}</span>
                <span
                  >${this.createNew ? 'A new component gets its own state.' : 'Reusing this component updates the same infrastructure.'}</span
                >
              </div>`
            : nothing
        }
      </aside>
    `;
  }

  private readinessRenderer() {
    if (!this.readiness)
      return html`<p class="notice">
        Target readiness not checked. Runner settings and cloud access must be
        verified before approval.
      </p>`;
    return html`<section class="notice" aria-label="Target readiness">
      <h3>Target readiness — configuration only</h3>
      <p>
        Workload: ${this.readiness.WorkloadTarget || 'Unknown or not disclosed'}
      </p>
      <p>Azure target: ${this.readiness.AzureTargetSource}</p>
      <p>
        Azure credentials (workload and state):
        ${this.readiness.AzureCredentialMode}
      </p>
      <p>Aiven credentials: ${this.readiness.AivenCredentialMode}</p>
      <p>
        Target identifiers above are non-secret configuration, not verified
        access. Secret and unrecognised target values remain hidden. One-off
        overrides may change the target; review the final plan.
      </p>
      <p>Backend: ${this.readiness.BackendStatus}</p>
      <p>${this.readiness.AccessStatus}</p>
      ${(this.readiness.Warnings ?? []).map(warning => html`<p>${warning}</p>`)}
      ${
        (this.readiness.InvalidInputs ?? []).filter(
          name => !(name in this.paramValues)
        ).length
          ? html`<p>
              Missing or invalid inherited inputs:
              ${(this.readiness.InvalidInputs ?? []).filter(name => !(name in this.paramValues)).join(', ')}.
              Supply valid overrides or correct the environment configuration.
            </p>`
          : nothing
      }
    </section>`;
  }

  private targetRenderer = () => html`
    <p class="lead">
      A module becomes a project component. Pick the mapped environment it
      should run in, then reuse an existing component to update its
      infrastructure or create a new one for separate infrastructure.
    </p>
    ${this.upgradeRenderer()}
    <div class="form">
      <vaadin-combo-box
        label="Project"
        item-label-path="ProjectName"
        item-value-path="ProjectId"
        .items=${this.projects}
        .selectedItem=${this.selectedProject ?? undefined}
        .disabled=${this.projectsLoading}
        @selected-item-changed=${(
          e: CustomEvent<{ value: ProjectApiModel | null }>
        ) => this.onProjectChange(e.detail.value ?? null)}
        helper-text="Requires project ownership or administrator access."
        required
      ></vaadin-combo-box>
      ${
        this.projectsLoading || this.targetLoading
          ? html`<div role="status" class="lead">Loading project targets…</div>`
          : nothing
      }
      <vaadin-combo-box
        label="DOrc environment"
        item-label-path="EnvironmentName"
        item-value-path="EnvironmentName"
        .items=${this.environments}
        .value=${this.selectedEnvironmentName}
        .disabled=${!this.selectedProject || this.targetLoading}
        @value-changed=${(e: CustomEvent<{ value: string }>) => {
          const name = e.detail.value ?? '';
          if (name !== this.selectedEnvironmentName) {
            this.selectedEnvironmentName = name;
            this.paramValues = {};
            this.clearResolution();
          }
        }}
        helper-text="Only mapped environments you can deploy to are listed. Inputs and state belong to this target."
        required
      ></vaadin-combo-box>
      ${
        this.selectedProject && !this.targetLoading && !this.environments.length
          ? html`<div class="notice" role="status">
              No deployable environments. Map the project to an environment and
              obtain deployment access first.
            </div>`
          : nothing
      }
      ${
        this.reusableComponents.length
          ? html`
              <vaadin-radio-group
                label="Component"
                .value=${this.createNew ? 'create' : 'reuse'}
                @value-changed=${(e: CustomEvent<{ value: string }>) =>
                  (this.createNew = e.detail.value === 'create')}
              >
                <vaadin-radio-button
                  value="reuse"
                  label="Reuse an existing component pinned to this version"
                ></vaadin-radio-button>
                <vaadin-radio-button
                  value="create"
                  label="Create a separate component"
                ></vaadin-radio-button>
              </vaadin-radio-group>
            `
          : nothing
      }
      ${
        this.createNew
          ? html`
              <vaadin-text-field
                label="New component name"
                .value=${this.componentName}
                @value-changed=${(e: CustomEvent<{ value: string }>) => (this.componentName = e.detail.value ?? '')}
                helper-text="Keep this identity stable for subsequent deployments. Component names must be unique."
                maxlength="64"
                required
              ></vaadin-text-field>
            `
          : html`
              <vaadin-combo-box
                label="Existing component"
                item-label-path="ComponentName"
                .items=${this.reusableComponents}
                .selectedItem=${this.selectedComponent ?? undefined}
                @selected-item-changed=${(
                  e: CustomEvent<{ value: ComponentApiModel | null }>
                ) => (this.selectedComponent = e.detail.value ?? null)}
                helper-text="Enabled components pinned to this exact template version."
                required
              ></vaadin-combo-box>
            `
      }
    </div>
  `;

  private inputsRenderer = () => {
    const required = this.parameters.filter(p => p.Required);
    const optional = this.parameters.filter(p => !p.Required);
    return html`
      <p class="lead">
        Values come from
        <strong>${this.selectedEnvironmentName}</strong> environment properties,
        then module defaults. Explicit saved component/environment inputs take
        precedence over inherited values; request overrides take precedence over
        saved inputs. Override only what should differ for this request.
        Inherited values, including secrets, are never loaded into this form.
        <a
          href="/environment/${encodeURIComponent(this.selectedEnvironmentName)}/variables"
          target="_blank"
          rel="noopener"
          >Manage environment variables</a
        >
      </p>
      ${
        !this.createNew && this.selectedComponent?.ComponentId
          ? html` <section
              class="notice"
              aria-label="Saved component configuration"
            >
              <h3>Repeat-deployment inputs</h3>
              <p>
                Saved for this component in ${this.selectedEnvironmentName}:
                ${(this.savedConfiguration?.ParameterNames ?? []).join(', ') || 'None'}.
                Saving replaces this list with the current non-sensitive
                overrides. Omitted saved inputs are removed. Secret overrides
                remain one-off. Submitting a plan alone does not save
                configuration.
              </p>
              <vaadin-button
                .disabled=${this.submitting || this.resolutionLoading || !this.savedConfiguration}
                @click=${() => this.saveConfiguration()}
                >Replace saved non-secret inputs</vaadin-button
              >
            </section>`
          : nothing
      }
      ${
        this.resolutionError
          ? html`<div class="notice" role="status">
              Could not check ${this.selectedEnvironmentName} properties
              (${this.resolutionError}). Required values will be checked on
              submission.
            </div>`
          : nothing
      }
      ${
        required.length
          ? html`<div class="group">
              <div class="group-head required">
                Required <span class="rule"></span>
                <span class="count">${required.length}</span>
              </div>
              <div class="params">${required.map(p => this.paramCard(p))}</div>
            </div>`
          : nothing
      }
      ${
        optional.length
          ? html`<div class="group">
              <div class="group-head">
                Optional <span class="rule"></span>
                <span class="count">${optional.length}</span>
              </div>
              <div class="params">${optional.map(p => this.paramCard(p))}</div>
            </div>`
          : nothing
      }
      ${
        !this.parameters.length
          ? html`<p class="lead">This module takes no inputs.</p>`
          : nothing
      }
    `;
  };

  private paramCard(p: TerraformTemplateParameter) {
    const overridden = p.Name in this.paramValues;
    const message = overridden
      ? overrideError(p, this.paramValues[p.Name])
      : null;
    const status = this.statusOf(p);
    const missing =
      !overridden && status === TerraformParameterResolutionStatus.Missing;
    const env = this.selectedEnvironmentName;
    const lock = html`<vaadin-icon icon="vaadin:lock"></vaadin-icon>`;
    // Labels are built as single strings so the rendered text never carries
    // template line breaks (tests and screen readers both read textContent).
    const badge = overridden
      ? message
        ? html`<span class="badge badge-invalid">Needs a value</span>`
        : html`<span class="badge badge-override">Override</span>`
      : html`<span
          class="badge ${missing ? 'badge-invalid' : p.Sensitive ? 'badge-sensitive' : 'badge-default'}"
          >${p.Sensitive ? lock : nothing}${this.sourceLabel(p)}</span
        >`;
    const source = this.resolutionLoading
      ? 'Checking input sources; values are not loaded into this form.'
      : this.resolutionError || status === null
        ? 'Input source not checked. The server validates inputs on submission.'
        : missing
          ? `No environment property named ${p.Name} in ${env}. Override it for this request, or add the property.`
          : status === TerraformParameterResolutionStatus.Unset
            ? `Not set in ${env} and no module default; the module runs without it.`
            : status === TerraformParameterResolutionStatus.SavedConfiguration
              ? 'Explicitly saved for this component and environment; snapshotted when a request is created.'
              : status === TerraformParameterResolutionStatus.Environment
                ? `Resolved by DOrc for ${env}, including environment/global properties and configured fallbacks.`
                : status === TerraformParameterResolutionStatus.Default
                  ? `No environment property in ${env}; the module default applies.`
                  : 'Input source not checked.';
    return html`
      <div
        class="param"
        data-overridden=${overridden ? 'true' : 'false'}
        data-invalid=${message ? 'true' : 'false'}
        data-missing=${missing ? 'true' : 'false'}
        data-sensitive=${p.Sensitive ? 'true' : 'false'}
      >
        <div class="param-head">
          <span class="param-name">
            ${p.Sensitive ? html`<vaadin-icon icon="vaadin:lock"></vaadin-icon>` : nothing}
            ${p.Name}
          </span>
          ${badge}
        </div>
        ${p.Description ? html`<div class="param-desc">${p.Description}</div>` : nothing}
        ${
          overridden
            ? html`
                <div class="param-field">
                  ${this.paramRenderer(p)}
                  ${
                    message
                      ? html`<span class="field-error">${message}</span>`
                      : html`<span class="field-ok">
                          <vaadin-icon
                            icon="vaadin:check"
                            style="width:10px;height:10px"
                          ></vaadin-icon>
                          Will be sent with this request
                        </span>`
                  }
                  <vaadin-button
                    theme="tertiary small"
                    aria-label="Remove request override for ${p.Name}"
                    @click=${() => this.setOverride(p, false)}
                    >Remove request override</vaadin-button
                  >
                </div>
              `
            : html`
                <div class="param-source">
                  <span>${source}</span>
                  <vaadin-button
                    theme="tertiary small"
                    aria-label="Override ${p.Name} for this request"
                    @click=${() => this.setOverride(p, true)}
                    >Override</vaadin-button
                  >
                </div>
              `
        }
      </div>
    `;
  }

  private paramRenderer(p: TerraformTemplateParameter) {
    const value = this.paramValues[p.Name] ?? '';
    const changed = (e: CustomEvent<{ value: string }>) =>
      this.setParamValue(p.Name, e.detail.value ?? '');
    if (p.Sensitive)
      return html` <vaadin-password-field
        .label=${p.Name}
        .value=${value}
        @value-changed=${changed}
        ?required=${p.Required}
        autocomplete="new-password"
      ></vaadin-password-field>`;
    if (p.AllowedValues?.length)
      return html` <vaadin-combo-box
        .label=${p.Name}
        .items=${p.AllowedValues}
        .value=${value}
        @value-changed=${changed}
        ?required=${p.Required}
      ></vaadin-combo-box>`;
    if (p.Type === 'Bool')
      return html` <vaadin-checkbox
        .label=${p.Name}
        .checked=${value === 'true'}
        @checked-changed=${(e: CustomEvent<{ value: boolean }>) =>
          this.setParamValue(p.Name, e.detail.value ? 'true' : 'false')}
      ></vaadin-checkbox>`;
    if (p.Type === 'Number')
      return html` <vaadin-number-field
        .label=${p.Name}
        .value=${value}
        @value-changed=${changed}
        ?required=${p.Required}
        .min=${p.Min ?? undefined}
        .max=${p.Max ?? undefined}
      ></vaadin-number-field>`;
    return html` <vaadin-text-field
      .label=${p.Name}
      .value=${value}
      @value-changed=${changed}
      ?required=${p.Required}
      pattern=${ifDefined(p.Pattern ?? undefined)}
    ></vaadin-text-field>`;
  }

  private reviewRenderer = () => html`
    <p class="lead">
      Submitting creates a deployment request and generates a plan. It does
      <strong>not</strong> apply changes: review the plan in the deployment
      result and confirm it explicitly before Terraform applies it.
    </p>
    ${
      this.template?.Deprecated
        ? html`<div class="notice" role="alert">
            This template version is deprecated. Review its suitability before
            proceeding.
          </div>`
        : nothing
    }
    <div class="summary">
      <div class="summary-card">
        <span class="summary-label">Project</span>
        <span class="summary-value">${this.selectedProject?.ProjectName}</span>
      </div>
      <div class="summary-card">
        <span class="summary-label">Environment</span>
        <span class="summary-value">${this.selectedEnvironmentName}</span>
      </div>
      <div class="summary-card">
        <span class="summary-label">Component</span>
        <span class="summary-value"
          >${`${this.targetComponentName} (${this.createNew ? 'create' : 'reuse'})`}</span
        >
        <span class="summary-sub">
          ${this.createNew ? 'New component, new state' : 'Updates the existing infrastructure'}
        </span>
      </div>
      <div class="summary-card">
        <span class="summary-label">Template</span>
        <span class="summary-value">${this.template?.Name}</span>
        <span class="summary-sub">v${this.template?.Version}</span>
      </div>
    </div>
    <table aria-label="Inputs">
      <thead>
        <tr>
          <th>Input</th>
          <th>Value for this request</th>
        </tr>
      </thead>
      <tbody>
        ${this.parameters.map(
          p => html`
            <tr
              data-overridden=${p.Name in this.paramValues ? 'true' : 'false'}
            >
              <td class="name">${p.Name}</td>
              ${
                p.Name in this.paramValues
                  ? html`<td>
                      ${p.Sensitive ? 'Sensitive override (hidden)' : this.paramValues[p.Name] || '(empty override)'}
                    </td>`
                  : html`<td class="muted">
                      ${this.sourceLabel(p)}${p.Sensitive ? ' (sensitive, hidden)' : ''}
                    </td>`
              }
            </tr>
          `
        )}
      </tbody>
    </table>
  `;

  private footerRenderer = () => {
    const invalid = this.step === 1 ? this.invalidOverrides.length : 0;
    const missing = this.step === 1 ? this.missingInputs.length : 0;
    const problems = [
      missing
        ? `${missing} required input${missing === 1 ? '' : 's'} missing in ${this.selectedEnvironmentName}`
        : '',
      invalid
        ? `${invalid} override${invalid === 1 ? '' : 's'} need${invalid === 1 ? 's' : ''} a value`
        : ''
    ].filter(Boolean);
    const status =
      this.step === 1
        ? problems.length
          ? html`<span class="footer-status invalid">
              <vaadin-icon icon="vaadin:exclamation-circle-o"></vaadin-icon>
              ${problems.join(' · ')}
            </span>`
          : this.resolutionLoading
            ? html`<span class="footer-status"
                >Checking ${this.selectedEnvironmentName} properties…</span
              >`
            : html`<span class="footer-status">
                ${this.inputSourceSummary}
              </span>`
        : html`<span class="footer-status"></span>`;
    return html`
      <vaadin-button
        theme="tertiary"
        @click=${() => this.close()}
        .disabled=${this.submitting}
        >Cancel</vaadin-button
      >
      ${status}
      ${
        this.step > 0
          ? html` <vaadin-button
              @click=${() => {
                this.step -= 1;
                this.error = null;
              }}
              .disabled=${this.submitting}
              >Back</vaadin-button
            >`
          : nothing
      }
      <vaadin-button
        theme="primary"
        @click=${() => this.advance()}
        .disabled=${this.submitting || this.projectsLoading || this.targetLoading}
      >
        ${this.submitting ? 'Submitting…' : this.step === 2 ? 'Submit plan request' : 'Continue'}
        ${
          this.submitting || this.step === 2
            ? nothing
            : html`<vaadin-icon
                icon="vaadin:arrow-right"
                slot="suffix"
              ></vaadin-icon>`
        }
      </vaadin-button>
    `;
  };

  private advance() {
    if (this.submitting) return;
    this.error =
      this.targetError() ?? (this.step > 0 ? this.inputError() : null);
    if (this.error) return;
    if (this.step < 2) {
      this.step += 1;
      if (this.step === 1 && !this.resolution && !this.resolutionLoading) {
        this.loadResolution();
      }
    } else {
      this.submit();
    }
  }

  private close() {
    if (this.submitting) return;
    this.opened = false;
    this.requestToken += 1;
    this.clearResolution();
    this.paramValues = {};
    this.error = null;
    this.submissionIdentity = null;
  }

  private upgradeRenderer() {
    if (!this.upgradeCandidates.length) return nothing;
    return html`<section class="notice">
      <h3>Upgrade an existing component</h3>
      <p>
        Keep component and state identity. The shared version pin affects every
        mapped environment.
      </p>
      ${this.upgradeCandidates.map(
        component =>
          html`<vaadin-button
            .disabled=${this.submitting}
            @click=${() => this.previewUpgrade(component)}
            >Review ${component.ComponentName}:
            ${component.TerraformTemplateVersion} →
            ${this.template?.Version}</vaadin-button
          >`
      )}
      ${
        this.upgradePreview
          ? html`
              <p>
                Version ${this.upgradePreview.CurrentVersion} →
                ${this.upgradePreview.TargetVersion}
              </p>
              <p>
                Affected environments:
                ${(this.upgradePreview.Environments ?? []).map(environment => environment.EnvironmentName).join(', ') || 'None currently mapped'}.
              </p>
              <ul>
                ${(this.upgradePreview.InputChanges ?? []).map(change => html`<li>${change}</li>`)}
              </ul>
              <vaadin-checkbox
                .checked=${this.upgradeImpactConfirmed}
                @checked-changed=${(event: CustomEvent<{ value: boolean }>) => {
                  this.upgradeImpactConfirmed = event.detail.value;
                }}
                label="I confirm the impact on all listed environments. Nothing will be deployed; each environment needs a fresh plan and approval."
              ></vaadin-checkbox>
              ${
                this.upgradePreview.RequiresExistingServiceOwnership
                  ? html`<vaadin-checkbox
                      .checked=${this.preserveExistingService}
                      @checked-changed=${(
                        event: CustomEvent<{ value: boolean }>
                      ) => {
                        this.preserveExistingService = event.detail.value;
                      }}
                      label="Explicitly save create_service=false in every mapped environment to preserve externally managed services. This does not import or adopt resources."
                    ></vaadin-checkbox>`
                  : nothing
              }
              <vaadin-button
                .disabled=${
                  this.submitting ||
                  !this.upgradeImpactConfirmed ||
                  (!!this.upgradePreview.RequiresExistingServiceOwnership &&
                    !this.preserveExistingService)
                }
                @click=${() => this.upgradeComponent()}
                >Upgrade shared pin only</vaadin-button
              >
            `
          : nothing
      }
    </section>`;
  }

  private previewUpgrade(component: ComponentApiModel) {
    if (
      !this.template ||
      !this.selectedProject?.ProjectId ||
      !component.ComponentId ||
      this.submitting
    )
      return;
    this.submitting = true;
    this.error = null;
    this.upgradePreview = null;
    this.upgradeImpactConfirmed = false;
    this.preserveExistingService = false;
    this.terraformApi
      .terraformUpgradePreviewGet({
        componentId: component.ComponentId,
        projectId: this.selectedProject.ProjectId,
        name: this.template.Name,
        version: this.template.Version
      })
      .subscribe({
        next: preview => {
          this.upgradePreview = preview;
          this.submitting = false;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ?? 'Could not load upgrade impact.';
          this.submitting = false;
        }
      });
  }

  private upgradeComponent() {
    const preview = this.upgradePreview;
    if (
      !preview?.ComponentId ||
      !this.template ||
      !this.selectedProject?.ProjectId ||
      this.submitting ||
      !this.upgradeImpactConfirmed
    )
      return;
    this.submitting = true;
    this.error = null;
    this.terraformApi
      .terraformUpgradePost({
        componentId: preview.ComponentId,
        projectId: this.selectedProject.ProjectId,
        name: this.template.Name,
        version: this.template.Version,
        catalogUpgradeRequestApiModel: {
          ExpectedRevision: preview.Revision,
          ExpectedVersion: preview.CurrentVersion,
          EnvironmentIds: (preview.Environments ?? []).map(
            environment => environment.EnvironmentId!
          ),
          ConfigurationRevisions: preview.ConfigurationRevisions,
          ConfirmImpact: this.upgradeImpactConfirmed,
          PreserveExistingServiceOwnership: this.preserveExistingService
        }
      })
      .subscribe({
        next: () => {
          this.components = this.components.map(component =>
            component.ComponentId === preview.ComponentId
              ? {
                  ...component,
                  TerraformTemplateVersion: preview.TargetVersion
                }
              : component
          );
          this.selectedComponent =
            this.components.find(
              component => component.ComponentId === preview.ComponentId
            ) ?? null;
          this.createNew = false;
          this.upgradePreview = null;
          this.submitting = false;
          this.clearResolution();
          this.submissionIdentity = null;
        },
        error: error => {
          this.error =
            retrieveErrorMessage(error) ??
            'Upgrade refused. Reload impact and review active requests.';
          this.submitting = false;
        }
      });
  }

  private saveConfiguration() {
    if (
      !this.template ||
      !this.selectedProject?.ProjectId ||
      !this.selectedComponent?.ComponentId ||
      !this.savedConfiguration ||
      this.submitting
    )
      return;
    this.submitting = true;
    this.error = null;
    const inputs = Object.fromEntries(
      this.parameters
        .filter(
          parameter =>
            !parameter.Sensitive && parameter.Name in this.paramValues
        )
        .map(parameter => [parameter.Name, this.paramValues[parameter.Name]])
    );
    this.terraformApi
      .terraformTemplateConfigurationPut({
        name: this.template.Name,
        version: this.template.Version,
        projectId: this.selectedProject.ProjectId,
        componentId: this.selectedComponent.ComponentId,
        environmentName: this.selectedEnvironmentName,
        saveCatalogEnvironmentInputsApiModel: {
          ExpectedRevision: this.savedConfiguration.Revision ?? 0,
          Inputs: inputs
        }
      })
      .subscribe({
        next: () => {
          this.submitting = false;
          this.submissionIdentity = null;
          this.loadResolution();
        },
        error: error => {
          this.submitting = false;
          this.error =
            retrieveErrorMessage(error) ??
            'Could not save inputs. Reload and review the current configuration.';
        }
      });
  }

  private submit() {
    if (!this.template || !this.selectedProject?.ProjectId || this.submitting)
      return;
    this.submitting = true;
    this.error = null;
    const componentName = this.targetComponentName;
    const environmentName = this.selectedEnvironmentName;
    const payload = JSON.stringify([
      this.template.Name,
      this.template.Version,
      this.selectedProject.ProjectId,
      componentName,
      environmentName,
      this.createNew ? null : this.selectedComponent?.ParentId || null,
      Object.entries(this.paramValues).sort(([a], [b]) => a.localeCompare(b))
    ]);
    if (this.submissionIdentity?.payload !== payload)
      this.submissionIdentity = { payload, key: crypto.randomUUID() };
    this.terraformApi
      .terraformTemplateInstantiatePost({
        name: this.template.Name,
        version: this.template.Version,
        body: {
          SubmissionKey: this.submissionIdentity.key,
          ProjectId: this.selectedProject.ProjectId,
          ComponentName: componentName,
          ParentComponentId: this.createNew
            ? null
            : this.selectedComponent?.ParentId || null,
          EnvironmentName: environmentName,
          Parameters: { ...this.paramValues }
        }
      })
      .subscribe({
        next: response => {
          this.submitting = false;
          if (!response.requestId || response.requestId <= 0) {
            this.error =
              'No deployment request ID was returned. Check deployment requests before retrying.';
            return;
          }
          this.close();
          const notification = Notification.show(
            `Plan request #${response.requestId} submitted for ${componentName} in ${environmentName}. Review the plan before applying.`,
            { duration: 8000, position: 'bottom-end' }
          );
          notification.setAttribute('theme', 'success');
          navigate(`/monitor-result/${response.requestId}`);
        },
        error: err => {
          this.submitting = false;
          this.error =
            retrieveErrorMessage(err) ??
            'Could not submit the plan request. Review the target and inputs.';
        }
      });
  }
}
