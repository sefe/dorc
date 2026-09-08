import '@vaadin/button';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import '@vaadin/number-field';
import '@vaadin/password-field';
import '@vaadin/checkbox';
import '@vaadin/dialog';
import { dialogRenderer, dialogFooterRenderer } from '@vaadin/dialog/lit';
import { Notification } from '@vaadin/notification';
import { css, html, LitElement } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { forkJoin } from 'rxjs';
import { navigate } from '../router/router';
import {
  ComponentApiModel,
  ComponentType,
  EnvironmentApiModel,
  ProjectApiModel,
  RefDataComponentsApi,
  RefDataProjectsApi,
  RefDataProjectEnvironmentMappingsApi,
  TerraformApi,
  TerraformSourceType,
  TerraformTemplateManifest,
  TerraformTemplateParameter
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

export interface TemplateDeploymentContext {
  projectName?: string;
  environmentName?: string;
}

@customElement('deploy-from-template-dialog')
export class DeployFromTemplateDialog extends LitElement {
  static styles = css`
    :host {
      display: contents;
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

  private projectsApi = new RefDataProjectsApi(dorcApiConfiguration);
  private componentsApi = new RefDataComponentsApi(dorcApiConfiguration);
  private envMappingsApi = new RefDataProjectEnvironmentMappingsApi(
    dorcApiConfiguration
  );
  private terraformApi = new TerraformApi(dorcApiConfiguration);
  private requestToken = 0;
  private context: TemplateDeploymentContext = {};

  open(
    template: TerraformTemplateManifest,
    context: TemplateDeploymentContext = {}
  ) {
    if (this.submitting) return;
    const token = ++this.requestToken;
    this.template = template;
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
    this.selectedComponent = null;
    this.selectedEnvironmentName = '';
    this.environments = [];
    this.components = [];
    this.paramValues = {};
    this.createNew = true;
    this.error = null;
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

  private get targetComponentName() {
    return this.createNew
      ? this.componentName.trim()
      : (this.selectedComponent?.ComponentName ?? '');
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
    for (const p of this.template?.Parameters ?? []) {
      // Omitted inputs are resolved on the server, never fetched into the browser.
      if (!(p.Name in this.paramValues)) continue;
      const value = this.paramValues[p.Name];
      if (p.Required && value === '')
        return `Enter an override for ${p.Name}, or use its environment value.`;
      if (value === '') continue;
      if (p.AllowedValues?.length && !p.AllowedValues.includes(value))
        return `Choose an allowed value for ${p.Name}.`;
      if (p.Type === 'Number') {
        const number = Number(value);
        if (!Number.isFinite(number))
          return `${p.Name} must be a finite number.`;
        if (p.Min != null && number < p.Min)
          return `${p.Name} must be at least ${p.Min}.`;
        if (p.Max != null && number > p.Max)
          return `${p.Name} must be at most ${p.Max}.`;
      }
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
        header-title="Plan infrastructure${this.template ? `: ${this.template.Name} ${this.template.Version}` : ''}"
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
          this.error
        ])}
        ${dialogFooterRenderer(this.footerRenderer, [
          this.step,
          this.submitting,
          this.projectsLoading,
          this.targetLoading
        ])}
      ></vaadin-dialog>
    `;
  }

  private bodyRenderer = () => html`
    <div
      style="display:flex;flex-direction:column;gap:var(--lumo-space-m);min-width:0;overflow-wrap:anywhere;"
      aria-busy=${this.submitting}
    >
      <div role="status" aria-live="polite">
        Step ${this.step + 1} of 3:
        <strong>${['Target', 'Inputs', 'Review'][this.step]}</strong>
      </div>
      ${this.error ? html`<div role="alert" style="color:var(--lumo-error-text-color)">${this.error}</div>` : ''}
      ${this.step === 0 ? this.targetRenderer() : this.step === 1 ? this.inputsRenderer() : this.reviewRenderer()}
    </div>
  `;

  private targetRenderer = () => html`
    <p style="margin:0">
      A template defines a reusable project component, not a new DOrc
      environment. Select an existing mapped environment. Reuse the same
      component to update its infrastructure there; a new component represents
      separate infrastructure.
    </p>
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
      helper-text="Creating or reusing a template here requires project ownership or administrator access."
      required
    ></vaadin-combo-box>
    ${this.projectsLoading || this.targetLoading ? html`<div role="status">Loading project targets...</div>` : ''}
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
        }
      }}
      helper-text="Only mapped environments with deployment access are listed. Inputs and state belong to this target."
      required
    ></vaadin-combo-box>
    ${
      this.selectedProject && !this.targetLoading && !this.environments.length
        ? html`<p role="status">
            No deployable environments. Map the project to an environment and
            obtain deployment access first.
          </p>`
        : ''
    }
    ${
      this.reusableComponents.length
        ? html`
            <vaadin-checkbox
              label="Create a separate component instead of reusing one"
              .checked=${this.createNew}
              @checked-changed=${(e: CustomEvent<{ value: boolean }>) => (this.createNew = e.detail.value)}
            ></vaadin-checkbox>
          `
        : ''
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
  `;

  private inputsRenderer = () => html`
    <p style="margin:0">
      Inputs use DOrc properties for
      <strong>${this.selectedEnvironmentName}</strong>, falling back to module
      defaults. Override only values that should differ for this request.
      Overrides do not update environment variables and are cleared if you
      change the target.
    </p>
    <p style="margin:0">
      Required values are checked on submission. Inherited values, including
      secrets, are not loaded into this form.
      <a
        href="/environment/${encodeURIComponent(this.selectedEnvironmentName)}/variables"
        target="_blank"
        rel="noopener"
      >
        Manage environment variables
      </a>
    </p>
    ${(this.template?.Parameters ?? []).map(
      p => html`
        <div
          style="display:flex;flex-direction:column;gap:var(--lumo-space-xs);border-top:1px solid var(--lumo-contrast-10pct);padding-top:var(--lumo-space-s)"
        >
          <strong
            >${p.Name}${p.Required ? ' (required)' : ''}${p.Sensitive ? ' (sensitive)' : ''}</strong
          >
          <span>${p.Description ?? ''}</span>
          <span
            style="font-size:var(--lumo-font-size-s);color:var(--lumo-secondary-text-color)"
          >
            ${p.Name in this.paramValues ? 'Request override' : 'Environment property, otherwise module default'}
            ${!p.Sensitive && p.Default != null ? ` (default: ${p.Default})` : ''}
          </span>
          <vaadin-checkbox
            .label=${`Override ${p.Name} for this request`}
            .checked=${p.Name in this.paramValues}
            @checked-changed=${(e: CustomEvent<{ value: boolean }>) =>
            this.setOverride(p, e.detail.value)}
          ></vaadin-checkbox>
          ${p.Name in this.paramValues ? this.paramRenderer(p) : ''}
        </div>
      `
    )}
  `;

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
    ></vaadin-text-field>`;
  }

  private reviewRenderer = () => html`
    <dl
      style="margin:0;display:grid;grid-template-columns:auto minmax(0,1fr);gap:var(--lumo-space-s)"
    >
      <dt>Project</dt>
      <dd style="margin:0">${this.selectedProject?.ProjectName}</dd>
      <dt>Environment</dt>
      <dd style="margin:0">${this.selectedEnvironmentName}</dd>
      <dt>Component</dt>
      <dd style="margin:0">
        ${this.targetComponentName} (${this.createNew ? 'create' : 'reuse'})
      </dd>
      <dt>Template</dt>
      <dd style="margin:0">${this.template?.Name} ${this.template?.Version}</dd>
    </dl>
    <p style="margin:0">
      The project, component and environment identify the Terraform state.
      Reusing this component in this environment updates the same
      infrastructure; deploying it to another environment uses separate state.
    </p>
    <div style="display:flex;flex-direction:column;gap:var(--lumo-space-xs)">
      ${(this.template?.Parameters ?? []).map(
        p => html`
          <div>
            <strong>${p.Name}:</strong> ${
          p.Name in this.paramValues
            ? p.Sensitive
              ? 'Sensitive override (hidden)'
              : this.paramValues[p.Name] || '(empty override)'
            : 'Environment property / module default'
        }
          </div>
        `
      )}
    </div>
    <p style="margin:0">
      Submit creates a deployment request and generates a plan. It does
      <strong>not</strong> apply changes. Review the plan in deployment results
      and explicitly confirm it before Terraform applies it.
    </p>
    ${this.template?.Deprecated ? html`<p role="alert">This template version is deprecated. Review its suitability before proceeding.</p>` : ''}
  `;

  private footerRenderer = () => html`
    <vaadin-button
      theme="tertiary"
      @click=${() => this.close()}
      .disabled=${this.submitting}
      >Cancel</vaadin-button
    >
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
        : ''
    }
    <vaadin-button
      theme="primary"
      @click=${() => this.advance()}
      .disabled=${this.submitting || this.projectsLoading || this.targetLoading}
    >
      ${this.submitting ? 'Submitting...' : this.step === 2 ? 'Submit plan request' : 'Continue'}
    </vaadin-button>
  `;

  private advance() {
    if (this.submitting) return;
    this.error =
      this.targetError() ?? (this.step > 0 ? this.inputError() : null);
    if (this.error) return;
    if (this.step < 2) this.step += 1;
    else this.submit();
  }

  private close() {
    if (this.submitting) return;
    this.opened = false;
    this.requestToken += 1;
    this.paramValues = {};
    this.error = null;
  }

  private submit() {
    if (!this.template || !this.selectedProject?.ProjectId || this.submitting)
      return;
    this.submitting = true;
    this.error = null;
    const componentName = this.targetComponentName;
    const environmentName = this.selectedEnvironmentName;
    this.terraformApi
      .terraformTemplateInstantiatePost({
        name: this.template.Name,
        version: this.template.Version,
        body: {
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
